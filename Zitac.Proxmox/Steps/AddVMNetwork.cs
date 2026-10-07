using System.Text.Json;
using System.Text.RegularExpressions;
using DecisionsFramework.Design.Flow;
using DecisionsFramework.Design.Properties;
using DecisionsFramework.Design.ConfigurationStorage.Attributes;
using DecisionsFramework.Design.Flow.Mapping;
using DecisionsFramework.Design.Flow.CoreSteps;
using DecisionsFramework.Design.Flow.Mapping.InputImpl;

namespace Zitac.Proxmox.Steps;

[AutoRegisterStep("Add VM Network", "Integration", "Proxmox", "VMs")]
[Writable]
public class AddVMNetwork : BaseFlowAwareStep, ISyncStep, IDataConsumer, IDataProducer, IDefaultInputMappingStep
{
    private static readonly Regex NetKey = new(@"^net\d+$", RegexOptions.Compiled);

    [WritableValue] private bool ignoreSSLErrors;
    [WritableValue] private int port = 8006;
    [WritableValue] private bool useApiToken;

    [PropertyClassification(0, "Use API Token", new string[] { "Authentication" })]
    public bool UseApiToken { get { return useApiToken; } set { useApiToken = value; this.OnPropertyChanged("InputData"); } }

    [PropertyClassification(0, "Ignore SSL Errors", new string[] { "Settings" })]
    public bool IgnoreSSLErrors { get { return ignoreSSLErrors; } set { ignoreSSLErrors = value; } }

    [PropertyClassification(1, "Port", new string[] { "Settings" })]
    public int Port { get { return port; } set { port = value; } }

    public IInputMapping[] DefaultInputs => new IInputMapping[]
    {
        new IgnoreInputMapping { InputDataName = "Node" },
        new ConstantInputMapping { InputDataName = "Network Model", Value = NetworkModel.VirtIO },
        new ConstantInputMapping { InputDataName = "VLAN Tag", Value = 0 },
        new ConstantInputMapping { InputDataName = "Firewall", Value = false },
    };

    public DataDescription[] InputData => new[]
    {
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(string)), "Hostname") { Categories = new string[] { "Connection" } },
        useApiToken
            ? new DataDescription((DecisionsType)new DecisionsNativeType(typeof(ApiTokenCredentials)), "Credentials") { Categories = new string[] { "Connection" } }
            : new DataDescription((DecisionsType)new DecisionsNativeType(typeof(UsernamePasswordCredentials)), "Credentials") { Categories = new string[] { "Connection" } },
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(string)), "Node"),
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(int)), "VM ID"),
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(string)), "Bridge"),
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(NetworkModel)), "Network Model"),
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(int)), "VLAN Tag"),
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(bool)), "Firewall"),
    };

    public override OutcomeScenarioData[] OutcomeScenarios => new[]
    {
        new OutcomeScenarioData("Done", new DataDescription(typeof(ProxmoxNetworkInterface), "Network", false)),
        new OutcomeScenarioData("No Free Slot"),
        new OutcomeScenarioData("Error", new DataDescription(typeof(string), "Error Message")),
    };

    public ResultData Run(StepStartData data)
    {
        var hostname = data.Data["Hostname"] as string;
        var credentials = data.Data["Credentials"];
        var node = data.Data["Node"] as string;
        var vmId = (int)data.Data["VM ID"];
        var bridge = data.Data["Bridge"] as string;
        var model = (NetworkModel)(data.Data["Network Model"] ?? NetworkModel.VirtIO);
        var vlanTag = data.Data["VLAN Tag"] as int? ?? 0;
        var firewall = data.Data["Firewall"] as bool? ?? false;

        try
        {
            using var client = new ProxmoxClient(hostname!, port, ignoreSSLErrors);
            client.Authenticate(credentials);
            if (string.IsNullOrEmpty(node)) node = client.FindNodeForVM(vmId);

            var config = client.Get($"/nodes/{node}/qemu/{vmId}/config");
            var usedSlots = new HashSet<string>(config.EnumerateObject().Select(p => p.Name));
            var freeSlot = Enumerable.Range(0, 32).Select(i => $"net{i}").FirstOrDefault(s => !usedSlots.Contains(s));

            if (freeSlot == null) return new ResultData("No Free Slot");

            var iface = new ProxmoxNetworkInterface
            {
                Interface = freeSlot,
                Model = model.ToProxmox(),
                Bridge = bridge,
                VlanTag = vlanTag > 0 ? vlanTag : null,
                Firewall = firewall,
            };

            client.Put($"/nodes/{node}/qemu/{vmId}/config", new Dictionary<string, string>
            {
                { freeSlot, iface.ToConfigString() }
            });

            // Re-fetch to get the MAC Proxmox assigned
            var updated = client.Get($"/nodes/{node}/qemu/{vmId}/config");
            if (updated.TryGetProperty(freeSlot, out var raw))
                iface = ProxmoxNetworkInterface.Parse(freeSlot, raw.GetString()!);

            return new ResultData("Done", new Dictionary<string, object> { { "Network", iface } });
        }
        catch (Exception e)
        {
            return new ResultData("Error", new Dictionary<string, object> { { "Error Message", e.ToString() } });
        }
    }

}
