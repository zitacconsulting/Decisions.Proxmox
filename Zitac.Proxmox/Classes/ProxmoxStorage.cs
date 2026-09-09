using System.Runtime.Serialization;
using System.Text.Json;
using DecisionsFramework.ServiceLayer.Utilities;

namespace Zitac.Proxmox.Steps;

[AutoRegisterNativeType]
[DataContract]
public class ProxmoxStorage
{
    [DataMember] public string? Storage { get; set; }
    [DataMember] public string? Node { get; set; }
    [DataMember] public string? Type { get; set; }
    [DataMember] public bool? Active { get; set; }
    [DataMember] public bool? Enabled { get; set; }
    [DataMember] public long? Total { get; set; }
    [DataMember] public long? Used { get; set; }
    [DataMember] public long? Available { get; set; }
    [DataMember] public bool? Shared { get; set; }

    public ProxmoxStorage() { }

    public static ProxmoxStorage FromJson(JsonElement element, string node)
    {
        var s = new ProxmoxStorage { Node = node };
        if (element.TryGetProperty("storage", out var storage)) s.Storage = storage.GetString();
        if (element.TryGetProperty("type", out var type)) s.Type = type.GetString();
        if (element.TryGetProperty("active", out var active)) s.Active = active.ValueKind == JsonValueKind.True || (active.ValueKind == JsonValueKind.Number && active.GetInt32() == 1);
        if (element.TryGetProperty("enabled", out var enabled)) s.Enabled = enabled.ValueKind == JsonValueKind.True || (enabled.ValueKind == JsonValueKind.Number && enabled.GetInt32() == 1);
        if (element.TryGetProperty("total", out var total) && total.ValueKind == JsonValueKind.Number) s.Total = total.GetInt64();
        if (element.TryGetProperty("used", out var used) && used.ValueKind == JsonValueKind.Number) s.Used = used.GetInt64();
        if (element.TryGetProperty("avail", out var avail) && avail.ValueKind == JsonValueKind.Number) s.Available = avail.GetInt64();
        if (element.TryGetProperty("shared", out var shared)) s.Shared = shared.ValueKind == JsonValueKind.True || (shared.ValueKind == JsonValueKind.Number && shared.GetInt32() == 1);
        return s;
    }
}
