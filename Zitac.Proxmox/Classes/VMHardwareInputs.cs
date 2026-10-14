using DecisionsFramework;
using DecisionsFramework.Data.DataTypes;
using DecisionsFramework.Design.Flow;
using DecisionsFramework.Design.Flow.Mapping;
using DecisionsFramework.Design.Flow.Mapping.InputImpl;

namespace Zitac.Proxmox.Steps;

// CPU / Memory / Machine Type / Extra Config inputs shared by Create VM and Clone VM.
// An ignored input or field reads as null.
internal static class VMHardwareInputs
{
    public const string CpuInput = "CPU";
    public const string MemoryInput = "Memory";
    public const string MachineTypeInput = "Machine Type";
    public const string MachineVersionInput = "Machine Version";
    public const string ExtraConfigInput = "Extra Config";

    // Each input group gets its own category so its InfoOrWarningEditor note sits right above it.
    // Within a shared category the designer shows step properties (the notes) before all inputs.
    public const string CpuCategory = "CPU";
    public const string MemoryCategory = "Memory";
    public const string MachineCategory = "Machine";
    public const string ExtraConfigCategory = "Extra Config";
    public const string HardwareChangesCategory = "Hardware Changes";
    public const string ProxmoxApiViewer = "https://pve.proxmox.com/pve-docs/api-viewer/";

    public const string CreateCpuNote =
        "CPU: Sockets and Cores are required. Type 'Default' = x86-64-v2-AES, the same as the Proxmox GUI. " +
        "CustomType overrides Type, e.g. EPYC-Genoa or custom-mymodel (list them with Get CPU Types). " +
        "Flags e.g. +aes;-pcid. VCPUs = vCPUs active at boot when fewer than Sockets x Cores. LimitCores 0 = unlimited.";

    public const string CloneCpuNote =
        "CPU: all fields are optional, unset fields keep the source VM's value. " +
        "CustomType overrides Type, e.g. EPYC-Genoa or custom-mymodel (list them with Get CPU Types). " +
        "Changing Type or Flags keeps the source's other CPU options. Flags e.g. +aes;-pcid.";

    public const string CreateMemoryNote =
        "Memory: MemoryMB is required. BalloonMinMB = minimum memory with ballooning (0 = ballooning off). " +
        "Shares = auto-ballooning weight (default 1000, 0 = no auto-ballooning).";

    public const string CloneMemoryNote =
        "Memory: all fields are optional, unset fields keep the source VM's value. " +
        "BalloonMinMB 0 = ballooning off. Shares 0 = no auto-ballooning.";

    public const string MachineNote =
        "Machine Type picks the chipset, Machine Version the QEMU version. Combine them: " +
        "Q35 + (empty) = q35 (newest version on the node). Q35 + 11.0 = pc-q35-11.0. I440fx + 10.1 = pc-i440fx-10.1. " +
        "A version without a type uses i440fx. A full ID from Get Machine Types (e.g. pc-q35-10.0+pve1) is used as-is and overrides Machine Type.";

    public const string ExtraConfigNote =
        "Any Proxmox VM parameter as key/value, e.g. numa = 1 or ciuser = admin. Applied last, so it overrides every other input " +
        "(a 'cpu' key replaces the whole CPU type and flags). Unknown keys or invalid values make Proxmox return an error. " +
        "Full list: Proxmox API Viewer, nodes/{node}/qemu.";

    public const string CloneApplyNote =
        "Setting any CPU, Memory, Machine or Extra Config value makes the step wait for the clone to finish, " +
        "even when Wait For Task Completion is off, and then applies it to the new VM.";

    // Create VM: CPU and Memory start as "Build Data" with the required fields set to the
    // Proxmox GUI defaults and the optional fields ignored.
    public static IEnumerable<IInputMapping> CreateDefaultInputs() => new IInputMapping[]
    {
        BuildData(CpuInput,
            new ConstantInputMapping { InputDataName = nameof(ProxmoxCPUSettings.Sockets), Value = 1 },
            new ConstantInputMapping { InputDataName = nameof(ProxmoxCPUSettings.Cores), Value = 1 },
            Ignore(nameof(ProxmoxCPUSettings.Type)),
            Ignore(nameof(ProxmoxCPUSettings.CustomType)),
            Ignore(nameof(ProxmoxCPUSettings.Flags)),
            Ignore(nameof(ProxmoxCPUSettings.VCPUs)),
            Ignore(nameof(ProxmoxCPUSettings.LimitCores)),
            Ignore(nameof(ProxmoxCPUSettings.CPUUnits)),
            Ignore(nameof(ProxmoxCPUSettings.Affinity)),
            Ignore(nameof(ProxmoxCPUSettings.NUMA))),
        BuildData(MemoryInput,
            new ConstantInputMapping { InputDataName = nameof(ProxmoxMemorySettings.MemoryMB), Value = 2048L },
            Ignore(nameof(ProxmoxMemorySettings.BalloonMinMB)),
            Ignore(nameof(ProxmoxMemorySettings.Shares))),
    }.Concat(OptionalDefaultInputs());

    // Clone VM: everything is optional, unset means "keep the source VM's value"
    public static IEnumerable<IInputMapping> CloneDefaultInputs() => new IInputMapping[]
    {
        Ignore(CpuInput),
        Ignore(MemoryInput),
    }.Concat(OptionalDefaultInputs());

    private static IEnumerable<IInputMapping> OptionalDefaultInputs() => new IInputMapping[]
    {
        Ignore(MachineTypeInput),
        Ignore(MachineVersionInput),
        Ignore(ExtraConfigInput),
    };

    public static IEnumerable<DataDescription> InputData() => new[]
    {
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(ProxmoxCPUSettings)), CpuInput) { Categories = new[] { CpuCategory } },
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(ProxmoxMemorySettings)), MemoryInput) { Categories = new[] { MemoryCategory } },
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(MachineType)), MachineTypeInput) { Categories = new[] { MachineCategory } },
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(string)), MachineVersionInput) { Categories = new[] { MachineCategory } },
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(SimpleKeyValuePair)), ExtraConfigInput, true, true, false) { Categories = new[] { ExtraConfigCategory } },
    };

    public static ProxmoxCPUSettings? Cpu(StepStartData data) => Input(data, CpuInput) as ProxmoxCPUSettings;

    public static ProxmoxMemorySettings? Memory(StepStartData data) => Input(data, MemoryInput) as ProxmoxMemorySettings;

    // Machine Version accepts "11.0" or a full id from Get Machine Types ("pc-q35-11.0+pve1").
    // A version without a type means i440fx, Proxmox's default. Null when neither is given.
    public static string? Machine(StepStartData data)
    {
        var type = Input(data, MachineTypeInput) as MachineType?;
        var version = (Input(data, MachineVersionInput) as string)?.Trim();

        if (!string.IsNullOrEmpty(version))
        {
            if (version.StartsWith("pc", StringComparison.OrdinalIgnoreCase) || version.StartsWith("q35", StringComparison.OrdinalIgnoreCase))
                return version;
            return type == MachineType.Q35 ? $"pc-q35-{version}" : $"pc-i440fx-{version}";
        }
        return type switch
        {
            MachineType.Q35    => "q35",
            MachineType.I440fx => "pc",
            _                  => null,
        };
    }

    // Applied last so it can override anything the step set itself
    public static void ApplyExtraConfig(StepStartData data, Dictionary<string, string> target)
    {
        if (Input(data, ExtraConfigInput) is not SimpleKeyValuePair[] pairs) return;
        foreach (var pair in pairs)
        {
            if (pair == null || string.IsNullOrWhiteSpace(pair.Key)) continue;
            target[pair.Key.Trim()] = pair.Value ?? string.Empty;
        }
    }

    // Create VM inputs and fields that can't be ignored or null. The field names don't clash
    // with any top-level input, so the name alone identifies them.
    private static readonly HashSet<string> CreateRequired = new()
    {
        CpuInput,
        MemoryInput,
        nameof(ProxmoxCPUSettings.Sockets),
        nameof(ProxmoxCPUSettings.Cores),
        nameof(ProxmoxMemorySettings.MemoryMB),
    };

    // Design-time check for Create VM (IInputMappingValidationProvider); flags the step in the designer
    public static ValidationIssue[] CreateMappingIssues(IInputMapping mapping)
    {
        if ((mapping is IgnoreInputMapping || mapping is NullInputMapping) && CreateRequired.Contains(mapping.InputDataName))
            return new[] { new ValidationIssue(mapping, $"{mapping.InputDataName} is required and cannot be ignored or null", null, BreakLevel.Fatal) };
        return Array.Empty<ValidationIssue>();
    }

    private static IInputMapping BuildData(string name, params IInputMapping[] fields) =>
        new BuildDataInputMapping { InputDataName = name, SubMappings = fields };

    private static IInputMapping Ignore(string name) => new IgnoreInputMapping { InputDataName = name };

    // Ignored inputs may be absent or null; both mean "not provided"
    private static object? Input(StepStartData data, string name) =>
        data.Data.TryGetValue(name, out var value) ? value : null;
}
