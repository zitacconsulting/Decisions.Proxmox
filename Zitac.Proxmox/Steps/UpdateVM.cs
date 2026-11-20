using DecisionsFramework.Design.Flow;
using DecisionsFramework.Design.Properties;
using DecisionsFramework.Design.Properties.Attributes;
using DecisionsFramework.Design.ConfigurationStorage.Attributes;
using DecisionsFramework.Design.Flow.Mapping;
using DecisionsFramework.Design.Flow.CoreSteps;
using DecisionsFramework.Design.Flow.Mapping.InputImpl;

namespace Zitac.Proxmox.Steps;

// Changes the VM-level settings Create VM sets. Disks, NICs and ISOs have their own steps.
// Every input is ignored by default, and ignored means "keep the current value".
[CategoryClassification(StepCategories.AuthenticationSortKey, StepCategories.Authentication)]
[CategoryClassification(StepCategories.SettingsSortKey, StepCategories.Settings)]
[AutoRegisterStep("Update VM", "Integration", "Proxmox", "VMs")]
[Writable]
public class UpdateVM : BaseFlowAwareStep, ISyncStep, IDataConsumer, IDataProducer, IDefaultInputMappingStep
{
    private const string VmNameInput = "VM Name";
    private const string OsTypeInput = "OS Type";
    private const string BiosTypeInput = "BIOS Type";
    private const string PreEnrollKeysInput = "Pre-Enroll Keys";
    private const string EfiStorageInput = "EFI Disk Storage";
    private const string ScsiControllerInput = "SCSI Controller";
    private const string StartOnBootInput = "Start On Boot";
    private const string AgentInput = "Enable QEMU Agent";
    private const string DescriptionInput = "Description";
    private const string TagsInput = "Tags";
    private const string EfiCategory = "EFI Disk";

    [WritableValue] private bool ignoreSSLErrors;
    [WritableValue] private int port = 8006;
    [WritableValue] private int taskTimeoutSeconds = 600;
    [WritableValue] private bool showWarningsOutcome;
    [WritableValue] private bool useApiToken;

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

    [PropertyClassification(3, "Task Timeout (Seconds, 0 = No Limit)", new string[] { "Settings" })]
    public int TaskTimeoutSeconds { get { return taskTimeoutSeconds; } set { taskTimeoutSeconds = value; } }

    [PropertyClassification(5, "Show Outcome for 'Done With Warnings'", new string[] { "Settings" })]
    public bool ShowWarningsOutcome
    {
        get { return showWarningsOutcome; }
        set { showWarningsOutcome = value; this.OnPropertyChanged("OutcomeScenarios"); }
    }

    // Read-only help texts, shown next to the inputs they describe
    [InfoOrWarningEditor(false, null, true)]
    [PropertyClassification(new string[] { VMHardwareInputs.HardwareChangesCategory }, "Applying Changes Info", 0)]
    public string ApplyNote { get => VMHardwareInputs.UpdateApplyNote; set { } }

    [InfoOrWarningEditor(false, null, true)]
    [PropertyClassification(new string[] { EfiCategory }, "EFI Disk Info", 0)]
    public string EfiNote
    {
        get => "Only used when BIOS Type is set to OVMF_UEFI and the VM has no EFI disk yet: the step creates one. " +
               "EFI Disk Storage ignored = the boot disk's storage. Pre-Enroll Keys ignored = keys enrolled (Secure Boot on), like the Proxmox GUI. " +
               "An existing EFI disk is never changed.";
        set { }
    }

    [InfoOrWarningEditor(false, null, true)]
    [PropertyClassification(new string[] { VMHardwareInputs.CpuCategory }, "CPU Info", 0)]
    public string CpuNote { get => VMHardwareInputs.UpdateCpuNote; set { } }

    [InfoOrWarningEditor(false, null, true)]
    [PropertyClassification(new string[] { VMHardwareInputs.MemoryCategory }, "Memory Info", 0)]
    public string MemoryNote { get => VMHardwareInputs.UpdateMemoryNote; set { } }

    [InfoOrWarningEditor(false, null, true)]
    [PropertyClassification(new string[] { VMHardwareInputs.MachineCategory }, "Machine Type Info", 0)]
    public string MachineNote { get => VMHardwareInputs.MachineNote; set { } }

    [InfoOrWarningEditor(false, VMHardwareInputs.ProxmoxApiViewer, true)]
    [PropertyClassification(new string[] { VMHardwareInputs.ExtraConfigCategory }, "Extra Config Info", 0)]
    public string ExtraConfigNote { get => VMHardwareInputs.ExtraConfigNote; set { } }

    public IInputMapping[] DefaultInputs => new IInputMapping[]
    {
        new IgnoreInputMapping { InputDataName = "Node" },
        new IgnoreInputMapping { InputDataName = VmNameInput },
        new IgnoreInputMapping { InputDataName = OsTypeInput },
        new IgnoreInputMapping { InputDataName = BiosTypeInput },
        new IgnoreInputMapping { InputDataName = PreEnrollKeysInput },
        new IgnoreInputMapping { InputDataName = EfiStorageInput },
        new IgnoreInputMapping { InputDataName = ScsiControllerInput },
        new IgnoreInputMapping { InputDataName = StartOnBootInput },
        new IgnoreInputMapping { InputDataName = AgentInput },
        new IgnoreInputMapping { InputDataName = DescriptionInput },
        new IgnoreInputMapping { InputDataName = TagsInput },
    }.Concat(VMHardwareInputs.OptionalHardwareDefaultInputs()).ToArray();

    public DataDescription[] InputData => new[]
    {
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(string)), "Hostname") { Categories = new string[] { "Connection" } },
        useApiToken
            ? new DataDescription((DecisionsType)new DecisionsNativeType(typeof(ApiTokenCredentials)), "Credentials") { Categories = new string[] { "Connection" } }
            : new DataDescription((DecisionsType)new DecisionsNativeType(typeof(UsernamePasswordCredentials)), "Credentials") { Categories = new string[] { "Connection" } },
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(string)), "Node"),
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(int)), "VM ID"),
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(string)), VmNameInput),
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(OsType)), OsTypeInput),
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(BiosType)), BiosTypeInput),
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(ScsiController)), ScsiControllerInput),
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(bool)), StartOnBootInput),
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(bool)), AgentInput),
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(string)), DescriptionInput),
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(string)), TagsInput),
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(string)), EfiStorageInput) { Categories = new string[] { EfiCategory } },
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(bool)), PreEnrollKeysInput) { Categories = new string[] { EfiCategory } },
    }.Concat(VMHardwareInputs.InputData()).ToArray();

    public override OutcomeScenarioData[] OutcomeScenarios
    {
        get
        {
            var done = new[]
            {
                new DataDescription(typeof(ProxmoxVM), "VM", false),
                new DataDescription(typeof(bool), "Restart Required"),
                new DataDescription(typeof(string), "Pending Changes", true),
            };
            var outcomes = new List<OutcomeScenarioData> { new OutcomeScenarioData("Done", done) };
            if (showWarningsOutcome)
                outcomes.Add(new OutcomeScenarioData("Done With Warnings", done.Append(new DataDescription(typeof(string), "Warnings", true)).ToArray()));
            outcomes.Add(new OutcomeScenarioData("Error", new DataDescription(typeof(string), "Error Message")));
            return outcomes.ToArray();
        }
    }

    public ResultData Run(StepStartData data)
    {
        var hostname = Input(data, "Hostname") as string;
        var credentials = Input(data, "Credentials");
        var node = Input(data, "Node") as string;
        var vmId = (int)data.Data["VM ID"];
        var vmName = (Input(data, VmNameInput) as string)?.Trim();
        var osType = Input(data, OsTypeInput) as OsType?;
        var biosType = Input(data, BiosTypeInput) as BiosType?;
        var preEnrollKeys = Input(data, PreEnrollKeysInput) as bool?;
        var efiStorage = (Input(data, EfiStorageInput) as string)?.Trim();
        var scsiController = Input(data, ScsiControllerInput) as ScsiController?;
        var startOnBoot = Input(data, StartOnBootInput) as bool?;
        var enableAgent = Input(data, AgentInput) as bool?;
        var description = Input(data, DescriptionInput) as string;
        var tags = Input(data, TagsInput) as string;

        try
        {
            using var client = new ProxmoxClient(hostname!, port, ignoreSSLErrors);
            client.Authenticate(credentials);
            if (string.IsNullOrEmpty(node)) node = client.FindNodeForVM(vmId);

            var current = client.Get($"/nodes/{node}/qemu/{vmId}/config");
            var config = new Dictionary<string, string>();
            var delete = new List<string>();
            var warnings = new List<string>();

            // A blank name is skipped: Proxmox would fall back to "VM <id>"
            if (!string.IsNullOrEmpty(vmName)) config["name"] = vmName;
            if (osType.HasValue) config["ostype"] = osType.Value.ToProxmox();
            if (scsiController.HasValue) config["scsihw"] = scsiController.Value.ToProxmox();
            if (startOnBoot.HasValue) config["onboot"] = startOnBoot.Value ? "1" : "0";
            if (enableAgent.HasValue) config["agent"] = AgentConfig(ProxmoxJson.Str(current, "agent"), enableAgent.Value);

            // Mapped but empty clears the value; ignored keeps it
            if (description != null)
            {
                if (description.Length > 0) config["description"] = description;
                else delete.Add("description");
            }
            if (tags != null)
            {
                if (!string.IsNullOrWhiteSpace(tags)) config["tags"] = tags.Trim();
                else delete.Add("tags");
            }

            if (biosType.HasValue)
            {
                config["bios"] = biosType == BiosType.OVMF_UEFI ? "ovmf" : "seabios";

                // Without an EFI disk OVMF can't keep its settings (boot order, Secure Boot) across restarts.
                // Switching back to SeaBIOS leaves the EFI disk alone, like the Proxmox GUI.
                if (biosType == BiosType.OVMF_UEFI && !current.TryGetProperty("efidisk0", out _))
                {
                    var storage = !string.IsNullOrEmpty(efiStorage) ? efiStorage : BootDiskStorage(current);
                    if (storage != null)
                        config["efidisk0"] = $"{storage}:1,efitype=4m,pre-enrolled-keys={(preEnrollKeys ?? true ? 1 : 0)}";
                    else
                        warnings.Add("No EFI disk was created: set EFI Disk Storage, the VM has no disk on a storage to put it on.");
                }
            }

            var cpuSettings = VMHardwareInputs.Cpu(data);
            if (cpuSettings != null)
                foreach (var kv in cpuSettings.ToConfig(isCreate: false, ProxmoxJson.Str(current, "cpu")))
                    config[kv.Key] = kv.Value;
            foreach (var kv in VMHardwareInputs.Memory(data)?.ToConfig() ?? new Dictionary<string, string>())
                config[kv.Key] = kv.Value;

            var machine = VMHardwareInputs.Machine(data);
            if (machine != null) config["machine"] = machine;
            VMHardwareInputs.ApplyExtraConfig(data, config);

            // Proxmox rejects a key that is both set and deleted, e.g. description from Extra Config
            delete.RemoveAll(config.ContainsKey);
            if (delete.Count > 0) config["delete"] = string.Join(",", delete);

            if (config.Count > 0)
            {
                // POST (async) rather than PUT: creating an EFI disk allocates storage
                var upid = client.Post($"/nodes/{node}/qemu/{vmId}/config", config);
                if (!string.IsNullOrEmpty(upid))
                    warnings.AddRange(client.WaitForTask(node!, upid, taskTimeoutSeconds));
            }

            var pending = PendingKeys(client, node!, vmId);
            var vm = ProxmoxVM.Load(client, node!, vmId);
            var result = new Dictionary<string, object>
            {
                { "VM", vm },
                { "Restart Required", pending.Length > 0 },
                { "Pending Changes", pending },
            };

            if (showWarningsOutcome && warnings.Count > 0)
            {
                result["Warnings"] = warnings.ToArray();
                return new ResultData("Done With Warnings", result);
            }
            return new ResultData("Done", result);
        }
        catch (Exception e)
        {
            return new ResultData("Error", new Dictionary<string, object> { { "Error Message", e.ToString() } });
        }
    }

    // Keeps the agent's other options (type, fstrim_cloned_disks, ...); only "enabled" changes
    private static string AgentConfig(string? existing, bool enabled)
    {
        var parts = new List<string> { $"enabled={(enabled ? 1 : 0)}" };
        if (existing != null)
        {
            var (_, options) = ProxmoxJson.ParsePropertyString(existing);
            parts.AddRange(options.Where(kv => !kv.Key.Equals("enabled", StringComparison.OrdinalIgnoreCase))
                                  .Select(kv => $"{kv.Key}={kv.Value}"));
        }
        return string.Join(",", parts);
    }

    // Storage of the first disk in the boot order, else of the first disk with a storage
    private static string? BootDiskStorage(System.Text.Json.JsonElement config)
    {
        var disks = ProxmoxVMConfig.Disks(config).Where(d => !string.IsNullOrEmpty(d.Storage)).ToList();
        var boot = ProxmoxJson.Str(config, "boot");
        var order = boot == null
            ? Array.Empty<string>()
            : ProxmoxJson.ParsePropertyString(boot).options.GetValueOrDefault("order")?.Split(';') ?? Array.Empty<string>();
        foreach (var iface in order)
        {
            var disk = disks.FirstOrDefault(d => d.Interface == iface);
            if (disk != null) return disk.Storage;
        }
        return disks.FirstOrDefault()?.Storage;
    }

    // Settings waiting for the next full stop and start (changed or removed)
    private static string[] PendingKeys(ProxmoxClient client, string node, int vmId) =>
        client.Get($"/nodes/{node}/qemu/{vmId}/pending").EnumerateArray()
            .Where(e => e.TryGetProperty("pending", out _) || (ProxmoxJson.Int(e, "delete") ?? 0) != 0)
            .Select(e => ProxmoxJson.Str(e, "key")!)
            .Where(k => k != null)
            .ToArray();

    // Ignored inputs may be absent or null; both mean "not provided"
    private static object? Input(StepStartData data, string name) =>
        data.Data.TryGetValue(name, out var value) ? value : null;
}
