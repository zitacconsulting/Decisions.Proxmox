using System.Runtime.Serialization;
using System.Text.Json;
using DecisionsFramework.ServiceLayer.Utilities;

namespace Zitac.Proxmox.Steps;

[AutoRegisterNativeType]
[DataContract]
public class ProxmoxContainer
{
    [DataMember] public int VMID { get; set; }
    [DataMember] public string? Name { get; set; }
    [DataMember] public string? Node { get; set; }
    [DataMember] public string[] Tags { get; set; } = Array.Empty<string>();

    // Running, Stopped or Unknown (node offline)
    [DataMember] public PowerState PowerState { get; set; }
    [DataMember] public string? PowerStateDetail { get; set; }
    [DataMember] public bool Template { get; set; }
    [DataMember] public int? CPUs { get; set; }
    [DataMember] public double? CPUUsagePercent { get; set; }
    [DataMember] public long? MemoryMB { get; set; }
    [DataMember] public long? MemoryUsedMB { get; set; }
    [DataMember] public double? DiskSizeGB { get; set; }
    [DataMember] public double? DiskUsedGB { get; set; }
    [DataMember] public long? UptimeSeconds { get; set; }
    // Locked while an operation holds the container (backup, migration, snapshot, disk operation, ...);
    // config changes are refused until it ends
    [DataMember] public bool Locked { get; set; }
    [DataMember] public LockType LockedBy { get; set; }

    // The raw lock name from Proxmox, e.g. "backup", "suspended"
    [DataMember] public string? LockedByDetail { get; set; }

    public ProxmoxContainer() { }

    // From a /cluster/resources entry
    public static ProxmoxContainer FromResource(JsonElement res)
    {
        var tags = ProxmoxJson.Str(res, "tags");
        var lockName = ProxmoxJson.Str(res, "lock");
        var lockType = LockTypeExtensions.FromProxmox(lockName);
        return new ProxmoxContainer
        {
            VMID = ProxmoxJson.Int(res, "vmid") ?? 0,
            Name = ProxmoxJson.Str(res, "name"),
            Node = ProxmoxJson.Str(res, "node"),
            Tags = string.IsNullOrWhiteSpace(tags)
                ? Array.Empty<string>()
                : tags.Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries),
            PowerState = PowerStateExtensions.FromProxmox(ProxmoxJson.Str(res, "status"), lockName),
            PowerStateDetail = ProxmoxJson.Str(res, "status"),
            Template = ProxmoxJson.Bool(res, "template") ?? false,
            CPUs = ProxmoxJson.Int(res, "maxcpu"),
            CPUUsagePercent = ProxmoxJson.ToPercent(ProxmoxJson.Double(res, "cpu")),
            MemoryMB = ProxmoxJson.BytesToMB(ProxmoxJson.Long(res, "maxmem")),
            MemoryUsedMB = ProxmoxJson.BytesToMB(ProxmoxJson.Long(res, "mem")),
            DiskSizeGB = ProxmoxJson.BytesToGB(ProxmoxJson.Long(res, "maxdisk")),
            DiskUsedGB = ProxmoxJson.BytesToGB(ProxmoxJson.Long(res, "disk")),
            UptimeSeconds = ProxmoxJson.Long(res, "uptime"),
            Locked = lockType != LockType.None,
            LockedBy = lockType,
            LockedByDetail = lockName,
        };
    }
}
