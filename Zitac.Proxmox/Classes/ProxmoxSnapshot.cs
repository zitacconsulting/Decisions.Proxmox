using System.Runtime.Serialization;
using System.Text.Json;
using DecisionsFramework.ServiceLayer.Utilities;

namespace Zitac.Proxmox.Steps;

[AutoRegisterNativeType]
[DataContract]
public class ProxmoxSnapshot
{
    [DataMember] public string? Name { get; set; }
    [DataMember] public string? Description { get; set; }
    [DataMember] public string? Parent { get; set; }
    [DataMember] public long? SnapTime { get; set; }

    public ProxmoxSnapshot() { }

    public static ProxmoxSnapshot FromJson(JsonElement element)
    {
        var snap = new ProxmoxSnapshot();
        if (element.TryGetProperty("name", out var name)) snap.Name = name.GetString();
        if (element.TryGetProperty("description", out var desc)) snap.Description = desc.GetString();
        if (element.TryGetProperty("parent", out var parent)) snap.Parent = parent.GetString();
        if (element.TryGetProperty("snaptime", out var time) && time.ValueKind == JsonValueKind.Number) snap.SnapTime = time.GetInt64();
        return snap;
    }
}
