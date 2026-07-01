using System.Runtime.Serialization;
using System.Text.Json;
using DecisionsFramework.ServiceLayer.Utilities;

namespace Zitac.Proxmox.Steps;

[AutoRegisterNativeType]
[DataContract]
public class ProxmoxNode
{
    [DataMember] public string? Node { get; set; }
    [DataMember] public string? Status { get; set; }
    [DataMember] public double? CPU { get; set; }
    [DataMember] public long? Memory { get; set; }
    [DataMember] public long? MaxMemory { get; set; }
    [DataMember] public long? Disk { get; set; }
    [DataMember] public long? MaxDisk { get; set; }
    [DataMember] public long? Uptime { get; set; }

    public ProxmoxNode() { }

    public static ProxmoxNode FromJson(JsonElement element)
    {
        var node = new ProxmoxNode();
        if (element.TryGetProperty("node", out var n)) node.Node = n.GetString();
        if (element.TryGetProperty("status", out var s)) node.Status = s.GetString();
        if (element.TryGetProperty("cpu", out var cpu) && cpu.ValueKind == JsonValueKind.Number) node.CPU = cpu.GetDouble();
        if (element.TryGetProperty("mem", out var mem) && mem.ValueKind == JsonValueKind.Number) node.Memory = mem.GetInt64();
        if (element.TryGetProperty("maxmem", out var maxmem) && maxmem.ValueKind == JsonValueKind.Number) node.MaxMemory = maxmem.GetInt64();
        if (element.TryGetProperty("disk", out var disk) && disk.ValueKind == JsonValueKind.Number) node.Disk = disk.GetInt64();
        if (element.TryGetProperty("maxdisk", out var maxdisk) && maxdisk.ValueKind == JsonValueKind.Number) node.MaxDisk = maxdisk.GetInt64();
        if (element.TryGetProperty("uptime", out var uptime) && uptime.ValueKind == JsonValueKind.Number) node.Uptime = uptime.GetInt64();
        return node;
    }
}
