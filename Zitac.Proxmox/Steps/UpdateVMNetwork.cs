using DecisionsFramework.Design.Flow;
using DecisionsFramework.Design.Properties;
using DecisionsFramework.Design.ConfigurationStorage.Attributes;
using DecisionsFramework.Design.Flow.Mapping;
using DecisionsFramework.Design.Flow.CoreSteps;
using DecisionsFramework.Design.Flow.Mapping.InputImpl;

namespace Zitac.Proxmox.Steps;

// Changes only the settings that are provided; everything else on the NIC (MAC, model,
// firewall, VLAN, queues, mtu, rate, ...) is read from the VM and written back unchanged.
[AutoRegisterStep("Update VM Network", "Integration", "Proxmox", "VMs")]
[Writable]
public class UpdateVMNetwork : BaseFlowAwareStep, ISyncStep, IDataConsumer, IDataProducer, IDefaultInputMappingStep
{
    [WritableValue] private bool ignoreSSLErrors;
    [WritableValue] private int port = 8006;
    [WritableValue] private bool useApiToken;

    [PropertyClassification(0, "Use API Token", new string[] { "Authentication" })]
    public bool UseApiToken { get { return useApiToken; } set { useApiToken = value; this.OnPropertyChanged("InputData"); } }

    [PropertyClassification(0, "Ignore SSL Errors", new string[] { "Settings" })]
    public bool IgnoreSSLErrors { get { return ignoreSSLErrors; } set { ignoreSSLErrors = value; } }

    [PropertyClassification(1, "Port", new string[] { "Settings" })]
    public int Port { get { return port; } set { port = value; } }

    // Ignored inputs mean "keep the current value"
    public IInputMapping[] DefaultInputs => new IInputMapping[]
    {
        new IgnoreInputMapping { InputDataName = "Node" },
        new ConstantInputMapping { InputDataName = "Interface", Value = "net0" },
        new IgnoreInputMapping { InputDataName = "VLAN Tag (0 = None)" },
        new IgnoreInputMapping { InputDataName = "Network Model" },
        new IgnoreInputMapping { InputDataName = "Firewall" },
    };

    public DataDescription[] InputData => new[]
    {
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(string)), "Hostname") { Categories = new string[] { "Connection" } },
        useApiToken
            ? new DataDescription((DecisionsType)new DecisionsNativeType(typeof(ApiTokenCredentials)), "Credentials") { Categories = new string[] { "Connection" } }
            : new DataDescription((DecisionsType)new DecisionsNativeType(typeof(UsernamePasswordCredentials)), "Credentials") { Categories = new string[] { "Connection" } },
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(string)), "Node"),
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(int)), "VM ID"),
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(string)), "Interface"),
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(string)), "Bridge"),
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(int)), "VLAN Tag (0 = None)") { Categories = new string[] { "Optional" } },
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(NetworkModel)), "Network Model") { Categories = new string[] { "Optional" } },
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(bool)), "Firewall") { Categories = new string[] { "Optional" } },
    };

    public override OutcomeScenarioData[] OutcomeScenarios => new[]
    {
        new OutcomeScenarioData("Done", new DataDescription(typeof(ProxmoxNetworkInterface), "Network", false)),
        new OutcomeScenarioData("Interface Not Found"),
        new OutcomeScenarioData("Error", new DataDescription(typeof(string), "Error Message")),
    };

    public ResultData Run(StepStartData data)
    {
        var hostname = Input(data, "Hostname") as string;
        var credentials = Input(data, "Credentials");
        var node = Input(data, "Node") as string;
        var vmId = (int)data.Data["VM ID"];
        var ifaceName = Input(data, "Interface") as string;
        var bridge = Input(data, "Bridge") as string;
        var vlanTag = Input(data, "VLAN Tag (0 = None)") as int?;
        var model = Input(data, "Network Model") as NetworkModel?;
        var firewall = Input(data, "Firewall") as bool?;

        if (string.IsNullOrWhiteSpace(ifaceName)) ifaceName = "net0";

        try
        {
            using var client = new ProxmoxClient(hostname!, port, ignoreSSLErrors);
            client.Authenticate(credentials);
            if (string.IsNullOrEmpty(node)) node = client.FindNodeForVM(vmId);

            var config = client.Get($"/nodes/{node}/qemu/{vmId}/config");
            if (!config.TryGetProperty(ifaceName, out var raw))
                return new ResultData("Interface Not Found");

            var nic = ProxmoxNetworkInterface.Parse(ifaceName, raw.GetString()!);
            if (!string.IsNullOrWhiteSpace(bridge)) nic.Bridge = bridge;
            if (vlanTag.HasValue) nic.VlanTag = vlanTag.Value > 0 ? vlanTag.Value : null;
            if (model.HasValue) nic.Model = model.Value.ToProxmox();
            if (firewall.HasValue) nic.Firewall = firewall.Value;

            client.Put($"/nodes/{node}/qemu/{vmId}/config", new Dictionary<string, string>
            {
                { ifaceName, nic.ToConfigString() }
            });

            // Return what Proxmox actually stored
            var updated = client.Get($"/nodes/{node}/qemu/{vmId}/config");
            if (updated.TryGetProperty(ifaceName, out var newRaw))
                nic = ProxmoxNetworkInterface.Parse(ifaceName, newRaw.GetString()!);

            return new ResultData("Done", new Dictionary<string, object> { { "Network", nic } });
        }
        catch (Exception e)
        {
            return new ResultData("Error", new Dictionary<string, object> { { "Error Message", e.ToString() } });
        }
    }

    // Ignored inputs may be absent or null; both mean "not provided"
    private static object? Input(StepStartData data, string name) =>
        data.Data.TryGetValue(name, out var value) ? value : null;
}
