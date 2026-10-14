using System.Text.Json;
using DecisionsFramework.Design.Flow;
using DecisionsFramework.Design.Properties;
using DecisionsFramework.Design.ConfigurationStorage.Attributes;
using DecisionsFramework.Design.Flow.Mapping;
using DecisionsFramework.Design.Flow.CoreSteps;
using DecisionsFramework.Design.Flow.Mapping.InputImpl;

namespace Zitac.Proxmox.Steps;

[CategoryClassification(StepCategories.AuthenticationSortKey, StepCategories.Authentication)]
[CategoryClassification(StepCategories.SettingsSortKey, StepCategories.Settings)]
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
        new IgnoreInputMapping { InputDataName = "Interface" },
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
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(string)), "Interface"),
    };

    public override OutcomeScenarioData[] OutcomeScenarios => new[]
    {
        new OutcomeScenarioData("Done", new DataDescription(typeof(ProxmoxCDROM), "CDROM", false)),
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
        var requested = (data.Data.TryGetValue("Interface", out var i) ? i as string : null)?.Trim().ToLowerInvariant();

        try
        {
            using var client = new ProxmoxClient(hostname!, port, ignoreSSLErrors);
            client.Authenticate(credentials);

            if (string.IsNullOrEmpty(node))
                node = client.FindNodeForVM(vmId);

            var config = client.Get($"/nodes/{node}/qemu/{vmId}/config");
            string? target;
            if (!string.IsNullOrEmpty(requested))
            {
                // A given interface gets its ISO replaced, or a new CD drive if it's free
                if (!ProxmoxVMConfig.IsValidCDROMInterface(requested))
                    return Error($"'{requested}' is not a valid CD-ROM interface (ide0-3, sata0-5 or scsi0-30).");
                var existing = ProxmoxVMConfig.Drive(config, requested);
                if (existing != null && !ProxmoxVMConfig.IsCDROM(existing))
                    return Error($"{requested} is a disk, not a CD drive.");
                target = requested;
            }
            else
            {
                // Reuse an empty CD drive first (works on a running VM), then add one in a free IDE slot
                target = ProxmoxVMConfig.CDROMs(config).FirstOrDefault(c => c.Type == "Empty")?.Interface
                         ?? IdeSlots.FirstOrDefault(s => ProxmoxVMConfig.Drive(config, s) == null);
                if (target == null)
                    return new ResultData("No Free Slot");
            }

            var volumeRef = isoImage!.ToVolumeRef();
            client.Put($"/nodes/{node}/qemu/{vmId}/config", new Dictionary<string, string>
            {
                { target, $"{volumeRef},media=cdrom" }
            });

            var mounted = ProxmoxCDROM.FromVolumeRef(target, volumeRef);
            return new ResultData("Done", new Dictionary<string, object> { { "CDROM", mounted } });
        }
        catch (Exception e)
        {
            return new ResultData("Error", new Dictionary<string, object> { { "Error Message", e.ToString() } });
        }
    }

    private static ResultData Error(string message) =>
        new ResultData("Error", new Dictionary<string, object> { { "Error Message", message } });
}
