using DecisionsFramework.Design.Flow;
using DecisionsFramework.Design.Properties;
using DecisionsFramework.Design.ConfigurationStorage.Attributes;
using DecisionsFramework.Design.Flow.Mapping;
using DecisionsFramework.Design.Flow.CoreSteps;
using DecisionsFramework.Design.Flow.Mapping.InputImpl;

namespace Zitac.Proxmox.Steps;

[AutoRegisterStep("Create VM", "Integration", "Proxmox", "VMs")]
[Writable]
public class CreateVM : BaseFlowAwareStep, ISyncStep, IDataConsumer, IDataProducer, IDefaultInputMappingStep
{
    [WritableValue]
    private bool ignoreSSLErrors;

    [WritableValue]
    private int port = 8006;

    [WritableValue]
    private bool waitForTask = true;

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

    [PropertyClassification(2, "Wait For Task Completion", new string[] { "Settings" })]
    public bool WaitForTask { get { return waitForTask; } set { waitForTask = value; } }

    public IInputMapping[] DefaultInputs => new IInputMapping[]
    {
        new IgnoreInputMapping { InputDataName = "VM ID" },
        new IgnoreInputMapping { InputDataName = "ISO Images" },
        new IgnoreInputMapping { InputDataName = "Description" },
        new IgnoreInputMapping { InputDataName = "Tags" },
        new ConstantInputMapping { InputDataName = "CPU Sockets", Value = 1 },
        new ConstantInputMapping { InputDataName = "OS Type", Value = OsType.Linux_6x_2_6_Kernel },
        new ConstantInputMapping { InputDataName = "BIOS Type", Value = BiosType.SeaBIOS },
        new ConstantInputMapping { InputDataName = "Network Model", Value = NetworkModel.VirtIO },
        new ConstantInputMapping { InputDataName = "SCSI Controller", Value = ScsiController.VirtIOScsiPci },
        new ConstantInputMapping { InputDataName = "Network Bridge", Value = "vmbr0" },
        new ConstantInputMapping { InputDataName = "Start On Boot", Value = false },
        new ConstantInputMapping { InputDataName = "Enable QEMU Agent", Value = false },
    };

    public DataDescription[] InputData => new[]
    {
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(string)), "Hostname") { Categories = new string[] { "Connection" } },
        useApiToken
            ? new DataDescription((DecisionsType)new DecisionsNativeType(typeof(ApiTokenCredentials)), "Credentials") { Categories = new string[] { "Connection" } }
            : new DataDescription((DecisionsType)new DecisionsNativeType(typeof(UsernamePasswordCredentials)), "Credentials") { Categories = new string[] { "Connection" } },
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(string)), "Node"),
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(int)), "VM ID"),
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(string)), "VM Name"),
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(int)), "Memory (MB)"),
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(int)), "CPU Cores"),
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(int)), "CPU Sockets"),
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(OsType)), "OS Type"),
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(BiosType)), "BIOS Type"),
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(string)), "Storage"),
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(int)), "Disk Size (GB)"),
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(ScsiController)), "SCSI Controller"),
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(string)), "Network Bridge"),
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(NetworkModel)), "Network Model"),
        new DataDescription(typeof(ProxmoxISOImage), "ISO Images", true),
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(bool)), "Start On Boot"),
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(bool)), "Enable QEMU Agent"),
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(string)), "Description"),
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(string)), "Tags"),
    };

    public override OutcomeScenarioData[] OutcomeScenarios => new[]
    {
        new OutcomeScenarioData("Done", new DataDescription(typeof(ProxmoxVM), "VM", false)),
        new OutcomeScenarioData("Error", new DataDescription(typeof(string), "Error Message")),
    };

    public ResultData Run(StepStartData data)
    {
        var hostname = data.Data["Hostname"] as string;
        var credentials = data.Data["Credentials"];
        var node = data.Data["Node"] as string;
        var vmId = data.Data["VM ID"] as int? ?? 0;
        var vmName = data.Data["VM Name"] as string;
        var memoryMb = data.Data["Memory (MB)"] as int? ?? 1024;
        var cpuCores = data.Data["CPU Cores"] as int? ?? 1;
        var cpuSockets = data.Data["CPU Sockets"] as int? ?? 1;
        var osType = (OsType)(data.Data["OS Type"] ?? OsType.Linux_6x_2_6_Kernel);
        var biosType = (BiosType)(data.Data["BIOS Type"] ?? BiosType.SeaBIOS);
        var storage = data.Data["Storage"] as string;
        var diskSizeGb = data.Data["Disk Size (GB)"] as int? ?? 32;
        var scsiController = (ScsiController)(data.Data["SCSI Controller"] ?? ScsiController.VirtIOScsiPci);
        var networkBridge = data.Data["Network Bridge"] as string ?? "vmbr0";
        var networkModel = (NetworkModel)(data.Data["Network Model"] ?? NetworkModel.VirtIO);
        var isoImages = data.Data["ISO Images"] as ProxmoxISOImage[];
        var startOnBoot = data.Data["Start On Boot"] as bool? ?? false;
        var enableAgent = data.Data["Enable QEMU Agent"] as bool? ?? false;
        var description = data.Data["Description"] as string;
        var tags = data.Data["Tags"] as string;

        try
        {
            using var client = new ProxmoxClient(hostname!, port, ignoreSSLErrors);
            client.Authenticate(credentials);

            if (vmId <= 0)
            {
                var nextId = client.Get("/cluster/nextid");
                vmId = int.Parse(nextId.GetString()!);
            }

            var postData = new Dictionary<string, string>
            {
                { "vmid",    vmId.ToString() },
                { "name",    vmName! },
                { "memory",  memoryMb.ToString() },
                { "cores",   cpuCores.ToString() },
                { "sockets", cpuSockets.ToString() },
                { "ostype",  MapOsType(osType) },
                { "bios",    biosType == BiosType.OVMF_UEFI ? "ovmf" : "seabios" },
                { "scsihw",  MapScsiController(scsiController) },
                { "scsi0",   $"{storage}:{diskSizeGb}" },
                { "net0",    $"{MapNetworkModel(networkModel)},bridge={networkBridge}" },
                { "onboot",  startOnBoot ? "1" : "0" },
                { "agent",   enableAgent ? "enabled=1" : "enabled=0" },
            };

            if (biosType == BiosType.OVMF_UEFI)
                postData["efidisk0"] = $"{storage}:1,efitype=4m,pre-enrolled-keys=1";

            var validISOs = isoImages?
                .Where(i => i != null && !string.IsNullOrEmpty(i.Datastore) && !string.IsNullOrEmpty(i.FileName))
                .ToList();

            if (validISOs?.Count > 0)
            {
                var ideSlots = new List<string>();
                for (int i = 0; i < validISOs.Count && i < 4; i++)
                {
                    var slot = $"ide{i}";
                    postData[slot] = $"{validISOs[i].ToVolumeRef()},media=cdrom";
                    ideSlots.Add(slot);
                }
                postData["boot"] = "order=scsi0;" + string.Join(";", ideSlots);
            }
            else
            {
                postData["boot"] = "order=scsi0";
            }

            if (!string.IsNullOrEmpty(description))
                postData["description"] = description;

            if (!string.IsNullOrEmpty(tags))
                postData["tags"] = tags;

            var upid = client.Post($"/nodes/{node}/qemu", postData);
            if (waitForTask && !string.IsNullOrEmpty(upid))
                client.WaitForTask(node!, upid);

            var status = client.Get($"/nodes/{node}/qemu/{vmId}/status/current");
            var vm = ProxmoxVM.FromJson(status, node!);

            return new ResultData("Done", new Dictionary<string, object> { { "VM", vm } });
        }
        catch (Exception e)
        {
            return new ResultData("Error", new Dictionary<string, object> { { "Error Message", e.ToString() } });
        }
    }

    private static string MapOsType(OsType osType) => osType switch
    {
        OsType.Linux_6x_2_6_Kernel    => "l26",
        OsType.Linux_2_4_Kernel       => "l24",
        OsType.Windows_11_2022_2025   => "win11",
        OsType.Windows_10_2016_2019   => "win10",
        OsType.Windows_8x_2012_2012R2 => "win8",
        OsType.Windows_7_2008R2       => "win7",
        OsType.Windows_Vista_2008     => "w2k8",
        OsType.Windows_XP_2003        => "wxp",
        OsType.Windows_2000           => "w2k",
        OsType.Solaris_Kernel         => "solaris",
        _                             => "other",
    };

    private static string MapScsiController(ScsiController controller) => controller switch
    {
        ScsiController.VirtIOScsiPci    => "virtio-scsi-pci",
        ScsiController.VirtIOScsiSingle => "virtio-scsi-single",
        ScsiController.LSI              => "lsi",
        ScsiController.MegaRAID         => "megasas",
        ScsiController.ParaVirtual      => "pvscsi",
        _                               => "virtio-scsi-pci",
    };

    private static string MapNetworkModel(NetworkModel model) => model switch
    {
        NetworkModel.VirtIO  => "virtio",
        NetworkModel.E1000   => "e1000",
        NetworkModel.E1000e  => "e1000e",
        NetworkModel.RTL8139 => "rtl8139",
        _                    => "virtio",
    };
}
