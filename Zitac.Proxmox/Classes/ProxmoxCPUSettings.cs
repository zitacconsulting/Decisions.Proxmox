using System.Globalization;
using System.Runtime.Serialization;
using DecisionsFramework.ServiceLayer.Utilities;

namespace Zitac.Proxmox.Steps;

// CPU input for Create VM and Clone VM, matching the Processor dialog in the Proxmox GUI.
// Unset fields mean the Proxmox default on Create VM, and "keep the source VM's value" on Clone VM.
[AutoRegisterNativeType]
[DataContract]
public class ProxmoxCPUSettings
{
    [DataMember] public int? Sockets { get; set; }
    [DataMember] public int? Cores { get; set; }
    [DataMember] public CpuType Type { get; set; }

    // Any other model, e.g. "EPYC-Genoa" or "custom-mymodel". Overrides Type.
    [DataMember] public string? CustomType { get; set; }

    // Extra CPU flags, e.g. "+aes;-pcid"
    [DataMember] public string? Flags { get; set; }

    // vCPUs active at boot when fewer than Sockets x Cores (CPU hotplug)
    [DataMember] public int? VCPUs { get; set; }

    // Max CPU time in whole cores, e.g. 2.5. 0 = unlimited.
    [DataMember] public double? LimitCores { get; set; }

    // Scheduler weight relative to other VMs (cgroup v2 default 100)
    [DataMember] public int? CPUUnits { get; set; }

    // Host cores the VM may run on, e.g. "0,5,8-11"
    [DataMember] public string? Affinity { get; set; }
    [DataMember] public bool? NUMA { get; set; }

    public ProxmoxCPUSettings() { }

    // Config entries to send. existingCpu is the VM's current "cpu" value (Clone VM);
    // its other options (hidden, phys-bits, ...) are kept when type or flags change.
    internal Dictionary<string, string> ToConfig(bool isCreate, string? existingCpu = null)
    {
        var config = new Dictionary<string, string>();
        if (Sockets.HasValue) config["sockets"] = Sockets.Value.ToString(CultureInfo.InvariantCulture);
        if (Cores.HasValue) config["cores"] = Cores.Value.ToString(CultureInfo.InvariantCulture);
        if (VCPUs.HasValue) config["vcpus"] = VCPUs.Value.ToString(CultureInfo.InvariantCulture);
        if (LimitCores.HasValue) config["cpulimit"] = LimitCores.Value.ToString(CultureInfo.InvariantCulture);
        if (CPUUnits.HasValue) config["cpuunits"] = CPUUnits.Value.ToString(CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(Affinity)) config["affinity"] = Affinity.Trim();
        if (NUMA.HasValue) config["numa"] = NUMA.Value ? "1" : "0";

        var cpu = BuildCpu(isCreate, existingCpu);
        if (cpu != null) config["cpu"] = cpu;
        return config;
    }

    // True when the "cpu" value has to change, i.e. Clone VM needs the current value to merge into
    internal bool ChangesCpu => ChosenType() != null || !string.IsNullOrWhiteSpace(Flags);

    private string? ChosenType() =>
        !string.IsNullOrWhiteSpace(CustomType) ? CustomType.Trim() : Type.ToProxmox();

    private string? BuildCpu(bool isCreate, string? existingCpu)
    {
        if (!ChangesCpu)
            return isCreate ? CpuTypeExtensions.GuiDefault : null;

        string? existingType = null;
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(existingCpu))
        {
            var (positional, existingOptions) = ProxmoxJson.ParsePropertyString(existingCpu);
            existingType = positional.FirstOrDefault() ?? existingOptions.GetValueOrDefault("cputype");
            foreach (var kv in existingOptions.Where(kv => !kv.Key.Equals("cputype", StringComparison.OrdinalIgnoreCase)))
                options[kv.Key] = kv.Value;
        }

        // A VM without a "cpu" line runs kvm64, so that's what flags-only changes keep on clones
        var type = ChosenType() ?? existingType ?? (isCreate ? CpuTypeExtensions.GuiDefault : "kvm64");
        if (!string.IsNullOrWhiteSpace(Flags)) options["flags"] = Flags.Trim();

        return options.Count == 0 ? type : $"{type},{string.Join(",", options.Select(kv => $"{kv.Key}={kv.Value}"))}";
    }
}
