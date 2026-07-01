using System.Text.Json;
using DecisionsFramework.Design.Flow;
using DecisionsFramework.Design.Properties;
using DecisionsFramework.Design.ConfigurationStorage.Attributes;
using DecisionsFramework.Design.Flow.Mapping;
using DecisionsFramework.Design.Flow.CoreSteps;
using DecisionsFramework.Design.Flow.Mapping.InputImpl;

namespace Zitac.Proxmox.Steps;

[AutoRegisterStep("Add VM Disk", "Integration", "Proxmox", "VMs")]
[Writable]
public class AddVMDisk : BaseFlowAwareStep, ISyncStep, IDataConsumer, IDataProducer, IDefaultInputMappingStep
{
    private static readonly Dictionary<DiskBus, (string prefix, int max)> SlotLimits = new()
    {
        { DiskBus.scsi,   ("scsi",   31) },
        { DiskBus.virtio, ("virtio", 16) },
        { DiskBus.ide,    ("ide",     4) },
        { DiskBus.sata,   ("sata",    6) },
    };

    [WritableValue] private bool ignoreSSLErrors;
    [WritableValue] private int port = 8006;
    [WritableValue] private bool useApiToken;
    [WritableValue] private DiskBus diskBus = DiskBus.scsi;

    [PropertyClassification(0, "Use API Token", new string[] { "Authentication" })]
    public bool UseApiToken { get { return useApiToken; } set { useApiToken = value; this.OnPropertyChanged("InputData"); } }

    [PropertyClassification(0, "Ignore SSL Errors", new string[] { "Settings" })]
    public bool IgnoreSSLErrors { get { return ignoreSSLErrors; } set { ignoreSSLErrors = value; } }

    [PropertyClassification(1, "Port", new string[] { "Settings" })]
    public int Port { get { return port; } set { port = value; } }

    [PropertyClassification(2, "Disk Bus", new string[] { "Settings" })]
    public DiskBus DiskBus { get { return diskBus; } set { diskBus = value; } }

    public IInputMapping[] DefaultInputs => new IInputMapping[]
    {
        new IgnoreInputMapping { InputDataName = "Node" },
    };

    public DataDescription[] InputData => new[]
    {
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(string)), "Hostname") { Categories = new string[] { "Connection" } },
        useApiToken
            ? new DataDescription((DecisionsType)new DecisionsNativeType(typeof(ApiTokenCredentials)), "Credentials") { Categories = new string[] { "Connection" } }
            : new DataDescription((DecisionsType)new DecisionsNativeType(typeof(UsernamePasswordCredentials)), "Credentials") { Categories = new string[] { "Connection" } },
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(string)), "Node"),
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(int)), "VM ID"),
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(string)), "Storage"),
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(int)), "Size (GB)"),
    };

    public override OutcomeScenarioData[] OutcomeScenarios => new[]
    {
        new OutcomeScenarioData("Done", new DataDescription(typeof(ProxmoxDisk), "Disk", false)),
        new OutcomeScenarioData("No Free Slot"),
        new OutcomeScenarioData("Error", new DataDescription(typeof(string), "Error Message")),
    };

    public ResultData Run(StepStartData data)
    {
        var hostname = data.Data["Hostname"] as string;
        var credentials = data.Data["Credentials"];
        var node = data.Data["Node"] as string;
        var vmId = (int)data.Data["VM ID"];
        var storage = data.Data["Storage"] as string;
        var sizeGb = (int)data.Data["Size (GB)"];

        try
        {
            using var client = new ProxmoxClient(hostname!, port, ignoreSSLErrors);
            client.Authenticate(credentials);
            if (string.IsNullOrEmpty(node)) node = client.FindNodeForVM(vmId);

            var config = client.Get($"/nodes/{node}/qemu/{vmId}/config");
            var usedSlots = new HashSet<string>(config.EnumerateObject().Select(p => p.Name));
            var (prefix, max) = SlotLimits[diskBus];
            var freeSlot = Enumerable.Range(0, max).Select(i => $"{prefix}{i}").FirstOrDefault(s => !usedSlots.Contains(s));

            if (freeSlot == null) return new ResultData("No Free Slot");

            client.Put($"/nodes/{node}/qemu/{vmId}/config", new Dictionary<string, string>
            {
                { freeSlot, $"{storage}:{sizeGb}" }
            });

            // Re-fetch to get the volume name Proxmox assigned
            var updated = client.Get($"/nodes/{node}/qemu/{vmId}/config");
            ProxmoxDisk? disk = null;
            if (updated.TryGetProperty(freeSlot, out var raw))
                disk = ProxmoxDisk.Parse(freeSlot, raw.GetString()!);

            disk ??= new ProxmoxDisk { Interface = freeSlot, Storage = storage, Size = $"{sizeGb}G" };

            return new ResultData("Done", new Dictionary<string, object> { { "Disk", disk } });
        }
        catch (Exception e)
        {
            return new ResultData("Error", new Dictionary<string, object> { { "Error Message", e.ToString() } });
        }
    }
}
