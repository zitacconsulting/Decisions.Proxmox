using System.Runtime.Serialization;
using System.Text.Json;
using DecisionsFramework.ServiceLayer.Utilities;

namespace Zitac.Proxmox.Steps;

[AutoRegisterNativeType]
[DataContract]
public class ProxmoxMachineType
{
    // Full id, e.g. "pc-q35-10.0+pve1". Can be passed as-is to the "Machine Version" input.
    [DataMember] public string? ID { get; set; }

    // "q35" or "i440fx"
    [DataMember] public string? Type { get; set; }
    [DataMember] public string? Version { get; set; }

    // Notable changes, only set for +pveX versions
    [DataMember] public string? Changes { get; set; }

    public ProxmoxMachineType() { }

    public static ProxmoxMachineType FromJson(JsonElement el) => new()
    {
        ID = ProxmoxJson.Str(el, "id"),
        Type = ProxmoxJson.Str(el, "type"),
        Version = ProxmoxJson.Str(el, "version"),
        Changes = ProxmoxJson.Str(el, "changes"),
    };

    // "10.0+pve1" sorts as 10.0.1, "10.0" as 10.0.0
    internal Version SortKey()
    {
        var parts = (Version ?? string.Empty).Split("+pve");
        var numbers = parts[0].Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToList();
        while (numbers.Count < 2) numbers.Add(0);
        var pve = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : 0;
        return new Version(numbers[0], numbers[1], pve);
    }
}
