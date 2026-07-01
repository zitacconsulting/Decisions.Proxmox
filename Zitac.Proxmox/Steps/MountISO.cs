using System.Text.Json;
using DecisionsFramework.Design.Flow;
using DecisionsFramework.Design.Properties;
using DecisionsFramework.Design.ConfigurationStorage.Attributes;
using DecisionsFramework.Design.Flow.Mapping;
using DecisionsFramework.Design.Flow.CoreSteps;
using DecisionsFramework.Design.Flow.Mapping.InputImpl;

namespace Zitac.Proxmox.Steps;

[AutoRegisterStep("Mount ISO", "Integration", "Proxmox", "VMs")]
[Writable]
public class MountISO : BaseFlowAwareStep, ISyncStep, IDataConsumer, IDataProducer, IDefaultInputMappingStep
{
    private static readonly string[] IdeSlots = { "ide0", "ide1", "ide2", "ide3" };

    [WritableValue]
    private bool ignoreSSLErrors;

    [WritableValue]
    private int port = 8006;

    [WritableValue]
    private bool useApiToken;

    [PropertyClassification(0, "Use API Token", new string[] { "Authentication" })]
    public bool UseApiToken
    {
        get { return useApiToken; }
        set { useApiToken = value; this.OnPropertyChanged("InputData"); }
    }

    [PropertyClassification(0, "Ignore SSL Errors", new string[] { "Settings" })]
    public bool IgnoreSSLErrors { get { return ignoreSSLErrors; } set { ignoreSSLErrors = value; } }

    [PropertyClassification(1, "Port", new string[] { "Settings" })]
    public int Port { get { return port; } set { port = value; } }

    public IInputMapping[] DefaultInputs => new IInputMapping[]
    {
        new IgnoreInputMapping { InputDataName = "Node" },
        new IgnoreInputMapping { InputDataName = "ISO Image" },
    };

    public DataDescription[] InputData => new[]
    {
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(string)), "Hostname") { Categories = new string[] { "Connection" } },
        useApiToken
            ? new DataDescription((DecisionsType)new DecisionsNativeType(typeof(ApiTokenCredentials)), "Credentials") { Categories = new string[] { "Connection" } }
            : new DataDescription((DecisionsType)new DecisionsNativeType(typeof(UsernamePasswordCredentials)), "Credentials") { Categories = new string[] { "Connection" } },
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(string)), "Node"),
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(int)), "VM ID"),
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(ProxmoxISOImage)), "ISO Image"),
    };

    public override OutcomeScenarioData[] OutcomeScenarios => new[]
    {
        new OutcomeScenarioData("Done", new DataDescription(typeof(ProxmoxMountedISO), "Mounted ISO", false)),
        new OutcomeScenarioData("No Free Slot"),
        new OutcomeScenarioData("Error", new DataDescription(typeof(string), "Error Message")),
    };

    public ResultData Run(StepStartData data)
    {
        var hostname = data.Data["Hostname"] as string;
        var credentials = data.Data["Credentials"];
        var node = data.Data["Node"] as string;
        var vmId = (int)data.Data["VM ID"];
        var isoImage = data.Data["ISO Image"] as ProxmoxISOImage;

        try
        {
            using var client = new ProxmoxClient(hostname!, port, ignoreSSLErrors);
            client.Authenticate(credentials);

            if (string.IsNullOrEmpty(node))
                node = client.FindNodeForVM(vmId);

            var config = client.Get($"/nodes/{node}/qemu/{vmId}/config");
            var freeSlot = IdeSlots.FirstOrDefault(s => !config.TryGetProperty(s, out _));

            if (freeSlot == null)
                return new ResultData("No Free Slot");

            var volumeRef = isoImage!.ToVolumeRef();
            client.Put($"/nodes/{node}/qemu/{vmId}/config", new Dictionary<string, string>
            {
                { freeSlot, $"{volumeRef},media=cdrom" }
            });

            var mounted = ProxmoxMountedISO.FromVolumeRef(freeSlot, volumeRef);
            return new ResultData("Done", new Dictionary<string, object> { { "Mounted ISO", mounted } });
        }
        catch (Exception e)
        {
            return new ResultData("Error", new Dictionary<string, object> { { "Error Message", e.ToString() } });
        }
    }
}
