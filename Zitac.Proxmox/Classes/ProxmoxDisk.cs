using System.Runtime.Serialization;
using DecisionsFramework.ServiceLayer.Utilities;

namespace Zitac.Proxmox.Steps;

[AutoRegisterNativeType]
[DataContract]
public class ProxmoxDisk
{
    [DataMember] public string? Interface { get; set; }
    [DataMember] public string? Storage { get; set; }
    [DataMember] public string? VolumeName { get; set; }
    [DataMember] public string? Size { get; set; }

    public ProxmoxDisk() { }

    public static ProxmoxDisk? Parse(string interfaceName, string raw)
    {
        if (raw.Contains("media=cdrom") || raw.Contains("media=disk") && raw.Contains("iso"))
            return null;

        var result = new ProxmoxDisk { Interface = interfaceName };
        var parts = raw.Split(',');
        var volumePart = parts[0];
        var colon = volumePart.IndexOf(':');
        if (colon >= 0)
        {
            result.Storage = volumePart[..colon];
            result.VolumeName = volumePart[(colon + 1)..];
        }
        else
        {
            result.Storage = volumePart;
        }

        foreach (var part in parts[1..])
        {
            var eq = part.IndexOf('=');
            if (eq < 0) continue;
            if (part[..eq] == "size")
                result.Size = part[(eq + 1)..];
        }
        return result;
    }
}
