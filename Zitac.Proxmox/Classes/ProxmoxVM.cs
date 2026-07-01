using System.Runtime.Serialization;
using System.Text.Json;
using DecisionsFramework.ServiceLayer.Utilities;

namespace Zitac.Proxmox.Steps;

[AutoRegisterNativeType]
[DataContract]
public class ProxmoxVM
{
    [DataMember] public int VMID { get; set; }
    [DataMember] public string? Name { get; set; }
    [DataMember] public string? Node { get; set; }
    [DataMember] public string? Status { get; set; }
    [DataMember] public long? Memory { get; set; }
    [DataMember] public long? MaxMemory { get; set; }
    [DataMember] public double? CPU { get; set; }
    [DataMember] public int? CPUs { get; set; }
    [DataMember] public long? MaxDisk { get; set; }
    [DataMember] public long? Uptime { get; set; }
    [DataMember] public string? QMPStatus { get; set; }

    public ProxmoxVM() { }

    public static ProxmoxVM FromJson(JsonElement element, string node)
    {
        var vm = new ProxmoxVM();
        FillFromJson(vm, element, node);
        return vm;
    }

    protected static void FillFromJson(ProxmoxVM vm, JsonElement element, string node)
    {
        vm.Node = node;
        if (element.TryGetProperty("vmid", out var vmid)) vm.VMID = vmid.GetInt32();
        if (element.TryGetProperty("name", out var name)) vm.Name = name.GetString();
        if (element.TryGetProperty("status", out var status)) vm.Status = status.GetString();
        if (element.TryGetProperty("mem", out var mem) && mem.ValueKind == JsonValueKind.Number) vm.Memory = mem.GetInt64();
        if (element.TryGetProperty("maxmem", out var maxmem) && maxmem.ValueKind == JsonValueKind.Number) vm.MaxMemory = maxmem.GetInt64();
        if (element.TryGetProperty("cpu", out var cpu) && cpu.ValueKind == JsonValueKind.Number) vm.CPU = cpu.GetDouble();
        if (element.TryGetProperty("cpus", out var cpus) && cpus.ValueKind == JsonValueKind.Number) vm.CPUs = cpus.GetInt32();
        if (element.TryGetProperty("maxdisk", out var maxdisk) && maxdisk.ValueKind == JsonValueKind.Number) vm.MaxDisk = maxdisk.GetInt64();
        if (element.TryGetProperty("uptime", out var uptime) && uptime.ValueKind == JsonValueKind.Number) vm.Uptime = uptime.GetInt64();
        if (element.TryGetProperty("qmpstatus", out var qmp)) vm.QMPStatus = qmp.GetString();
    }
}
