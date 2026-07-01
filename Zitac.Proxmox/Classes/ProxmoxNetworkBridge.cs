using System.Runtime.Serialization;
using System.Text.Json;
using DecisionsFramework.ServiceLayer.Utilities;

namespace Zitac.Proxmox.Steps;

[AutoRegisterNativeType]
[DataContract]
public class ProxmoxNetworkBridge
{
    [DataMember] public string? Name { get; set; }
    [DataMember] public string? Node { get; set; }
    [DataMember] public string? Address { get; set; }
    [DataMember] public bool Active { get; set; }
    [DataMember] public string? Comments { get; set; }
    [DataMember] public string? Ports { get; set; }

    public ProxmoxNetworkBridge() { }

    public static ProxmoxNetworkBridge FromJson(JsonElement element, string node)
    {
        var b = new ProxmoxNetworkBridge { Node = node };
        if (element.TryGetProperty("iface", out var iface)) b.Name = iface.GetString();
        if (element.TryGetProperty("address", out var addr)) b.Address = addr.GetString();
        if (element.TryGetProperty("active", out var active)) b.Active = active.GetInt32() == 1;
        if (element.TryGetProperty("comments", out var comments)) b.Comments = comments.GetString();
        if (element.TryGetProperty("bridge_ports", out var ports)) b.Ports = ports.GetString();
        return b;
    }
}
