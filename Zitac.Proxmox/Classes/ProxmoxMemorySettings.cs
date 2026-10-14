using System.Globalization;
using System.Runtime.Serialization;
using DecisionsFramework.ServiceLayer.Utilities;

namespace Zitac.Proxmox.Steps;

// Memory input for Create VM and Clone VM, matching the Memory dialog in the Proxmox GUI.
// MemoryMB is required on Create VM. Unset fields keep the source VM's value on Clone VM.
[AutoRegisterNativeType]
[DataContract]
public class ProxmoxMemorySettings
{
    [DataMember] public long? MemoryMB { get; set; }

    // Minimum memory with ballooning. 0 = ballooning off. Unset = ballooning on with no minimum below MemoryMB.
    [DataMember] public long? BalloonMinMB { get; set; }

    // Auto-ballooning weight relative to other VMs (default 1000). 0 = no auto-ballooning.
    [DataMember] public int? Shares { get; set; }

    public ProxmoxMemorySettings() { }

    internal Dictionary<string, string> ToConfig()
    {
        var config = new Dictionary<string, string>();
        if (MemoryMB.HasValue) config["memory"] = MemoryMB.Value.ToString(CultureInfo.InvariantCulture);
        if (BalloonMinMB.HasValue) config["balloon"] = BalloonMinMB.Value.ToString(CultureInfo.InvariantCulture);
        if (Shares.HasValue) config["shares"] = Shares.Value.ToString(CultureInfo.InvariantCulture);
        return config;
    }
}
