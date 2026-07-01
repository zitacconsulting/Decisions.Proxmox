using System.Runtime.Serialization;
using System.Text;
using DecisionsFramework.ServiceLayer.Utilities;

namespace Zitac.Proxmox.Steps;

[AutoRegisterNativeType]
[DataContract]
public class ProxmoxNetworkInterface
{
    [DataMember] public string? Interface { get; set; }
    [DataMember] public string? Model { get; set; }
    [DataMember] public string? MacAddress { get; set; }
    [DataMember] public string? Bridge { get; set; }
    [DataMember] public int? VlanTag { get; set; }
    [DataMember] public bool Firewall { get; set; }

    public ProxmoxNetworkInterface() { }

    public static ProxmoxNetworkInterface Parse(string interfaceName, string raw)
    {
        var result = new ProxmoxNetworkInterface { Interface = interfaceName };
        var parts = raw.Split(',');
        foreach (var part in parts)
        {
            var eq = part.IndexOf('=');
            if (eq < 0) continue;
            var key = part[..eq];
            var val = part[(eq + 1)..];
            if (key == "bridge") result.Bridge = val;
            else if (key == "tag" && int.TryParse(val, out var tag)) result.VlanTag = tag;
            else if (key == "firewall") result.Firewall = val == "1";
            else { result.Model = key; result.MacAddress = val; }
        }
        return result;
    }

    // Builds the Proxmox config string, preserving MAC if present.
    public string ToConfigString()
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(MacAddress))
            sb.Append($"{Model}={MacAddress}");
        else
            sb.Append(Model);
        sb.Append($",bridge={Bridge}");
        if (Firewall) sb.Append(",firewall=1");
        if (VlanTag.HasValue && VlanTag.Value > 0) sb.Append($",tag={VlanTag}");
        return sb.ToString();
    }
}
