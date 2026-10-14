using System.Globalization;
using System.Runtime.Serialization;
using System.Text;
using DecisionsFramework.ServiceLayer.Utilities;

namespace Zitac.Proxmox.Steps;

[AutoRegisterNativeType]
[DataContract]
public class ProxmoxNetworkInterface
{
    // Every NIC model Proxmox accepts as the "<model>=<mac>" key of a netX line
    private static readonly HashSet<string> KnownModels = new(StringComparer.OrdinalIgnoreCase)
    {
        "virtio", "e1000", "e1000e", "rtl8139", "vmxnet3",
        "e1000-82540em", "e1000-82544gc", "e1000-82545em",
        "i82551", "i82557b", "i82559er", "ne2k_isa", "ne2k_pci", "pcnet",
    };

    [DataMember] public string? Interface { get; set; }
    [DataMember] public string? Model { get; set; }
    [DataMember] public string? MacAddress { get; set; }
    [DataMember] public string? Bridge { get; set; }
    [DataMember] public int? VlanTag { get; set; }
    [DataMember] public bool Firewall { get; set; }

    // False when the link is set down ("Disconnect" in the Proxmox GUI)
    [DataMember] public bool Connected { get; set; } = true;
    [DataMember] public int? MTU { get; set; }
    [DataMember] public double? RateLimitMBps { get; set; }
    [DataMember] public int? Queues { get; set; }

    // Reported by the guest agent, matched on MAC address. Empty if the agent isn't responding.
    [DataMember] public string[] IPAddresses { get; set; } = Array.Empty<string>();

    // Any other options on the line (e.g. "trunks=10;20"), kept as-is so updates don't drop them
    [DataMember] public string? OtherOptions { get; set; }

    public ProxmoxNetworkInterface() { }

    public static ProxmoxNetworkInterface Parse(string interfaceName, string raw)
    {
        var result = new ProxmoxNetworkInterface { Interface = interfaceName };
        var others = new List<string>();
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            var key = eq < 0 ? part : part[..eq];
            var val = eq < 0 ? string.Empty : part[(eq + 1)..];

            if (key == "bridge") result.Bridge = val;
            else if (key == "tag" && int.TryParse(val, out var tag)) result.VlanTag = tag;
            else if (key == "firewall") result.Firewall = val == "1";
            else if (key == "link_down") result.Connected = val != "1";
            else if (key == "mtu" && int.TryParse(val, out var mtu)) result.MTU = mtu;
            else if (key == "queues" && int.TryParse(val, out var queues)) result.Queues = queues;
            else if (key == "rate" && double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var rate)) result.RateLimitMBps = rate;
            else if (key == "model") result.Model = val;
            else if (key == "macaddr") result.MacAddress = val;
            else if (KnownModels.Contains(key)) { result.Model = key; result.MacAddress = string.IsNullOrEmpty(val) ? null : val; }
            else others.Add(part);
        }
        result.OtherOptions = others.Count > 0 ? string.Join(",", others) : null;
        return result;
    }

    // Builds the Proxmox config string, preserving MAC and any other options.
    public string ToConfigString()
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(MacAddress))
            sb.Append($"{Model}={MacAddress}");
        else
            sb.Append(Model);
        if (!string.IsNullOrEmpty(Bridge)) sb.Append($",bridge={Bridge}");
        if (Firewall) sb.Append(",firewall=1");
        if (VlanTag.HasValue && VlanTag.Value > 0) sb.Append($",tag={VlanTag}");
        if (!Connected) sb.Append(",link_down=1");
        if (MTU.HasValue) sb.Append($",mtu={MTU}");
        if (Queues.HasValue) sb.Append($",queues={Queues}");
        if (RateLimitMBps.HasValue) sb.Append(",rate=").Append(RateLimitMBps.Value.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrEmpty(OtherOptions)) sb.Append($",{OtherOptions}");
        return sb.ToString();
    }
}
