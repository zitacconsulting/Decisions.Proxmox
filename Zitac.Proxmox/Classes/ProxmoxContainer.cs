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
    [DataMember] public string? Status { get; set; }
    [DataMember] public long? Memory { get; set; }
    [DataMember] public long? MaxMemory { get; set; }
    [DataMember] public double? CPU { get; set; }
    [DataMember] public int? CPUs { get; set; }
    [DataMember] public long? Disk { get; set; }
    [DataMember] public long? MaxDisk { get; set; }
    [DataMember] public long? Uptime { get; set; }

    public ProxmoxContainer() { }

    public static ProxmoxContainer FromJson(JsonElement element, string node)
    {
        var ct = new ProxmoxContainer { Node = node };
        if (element.TryGetProperty("vmid", out var vmid)) ct.VMID = vmid.GetInt32();
        if (element.TryGetProperty("name", out var name)) ct.Name = name.GetString();
        if (element.TryGetProperty("status", out var status)) ct.Status = status.GetString();
        if (element.TryGetProperty("mem", out var mem) && mem.ValueKind == JsonValueKind.Number) ct.Memory = mem.GetInt64();
        if (element.TryGetProperty("maxmem", out var maxmem) && maxmem.ValueKind == JsonValueKind.Number) ct.MaxMemory = maxmem.GetInt64();
        if (element.TryGetProperty("cpu", out var cpu) && cpu.ValueKind == JsonValueKind.Number) ct.CPU = cpu.GetDouble();
        if (element.TryGetProperty("cpus", out var cpus) && cpus.ValueKind == JsonValueKind.Number) ct.CPUs = cpus.GetInt32();
        if (element.TryGetProperty("disk", out var disk) && disk.ValueKind == JsonValueKind.Number) ct.Disk = disk.GetInt64();
        if (element.TryGetProperty("maxdisk", out var maxdisk) && maxdisk.ValueKind == JsonValueKind.Number) ct.MaxDisk = maxdisk.GetInt64();
        if (element.TryGetProperty("uptime", out var uptime) && uptime.ValueKind == JsonValueKind.Number) ct.Uptime = uptime.GetInt64();
        return ct;
    }
}
