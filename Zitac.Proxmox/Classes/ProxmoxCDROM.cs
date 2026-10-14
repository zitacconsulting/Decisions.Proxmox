using System.Runtime.Serialization;
using DecisionsFramework.ServiceLayer.Utilities;

namespace Zitac.Proxmox.Steps;

[AutoRegisterNativeType]
[DataContract]
public class ProxmoxCDROM
{
    [DataMember] public string? Interface { get; set; }

    // "ISO", "Physical" (host CD/DVD drive) or "Empty"
    [DataMember] public string? Type { get; set; }
    [DataMember] public string? Storage { get; set; }
    [DataMember] public string? FileName { get; set; }

    // Full Proxmox volume reference, e.g. "local:iso/virtio-win.iso"
    [DataMember] public string? VolumeID { get; set; }

    public ProxmoxCDROM() { }

    public static ProxmoxCDROM Parse(string interfaceName, string raw)
    {
        var (positional, options) = ProxmoxJson.ParsePropertyString(raw);
        var volume = positional.FirstOrDefault() ?? options.GetValueOrDefault("file");
        return FromVolumeRef(interfaceName, volume);
    }

    public static ProxmoxCDROM FromVolumeRef(string interfaceName, string? volumeRef)
    {
        var result = new ProxmoxCDROM { Interface = interfaceName };
        if (string.IsNullOrEmpty(volumeRef) || volumeRef == "none")
        {
            result.Type = "Empty";
            return result;
        }
        if (volumeRef == "cdrom")
        {
            result.Type = "Physical";
            return result;
        }

        result.Type = "ISO";
        result.VolumeID = volumeRef;
        var colon = volumeRef.IndexOf(':');
        if (colon >= 0)
        {
            result.Storage = volumeRef[..colon];
            var path = volumeRef[(colon + 1)..];
            result.FileName = path.StartsWith("iso/", StringComparison.OrdinalIgnoreCase) ? path[4..] : path;
        }
        else
        {
            result.FileName = volumeRef;
        }
        return result;
    }
}
