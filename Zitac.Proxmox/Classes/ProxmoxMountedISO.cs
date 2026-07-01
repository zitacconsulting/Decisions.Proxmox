using System.Runtime.Serialization;
using DecisionsFramework.ServiceLayer.Utilities;

namespace Zitac.Proxmox.Steps;

[AutoRegisterNativeType]
[DataContract]
public class ProxmoxMountedISO
{
    [DataMember] public string? Slot { get; set; }
    [DataMember] public string? Datastore { get; set; }
    [DataMember] public string? FileName { get; set; }

    public ProxmoxMountedISO() { }

    // Parses a volume reference like "local:iso/virtio-win.iso" into its parts
    public static ProxmoxMountedISO FromVolumeRef(string slot, string volumeRef)
    {
        var result = new ProxmoxMountedISO { Slot = slot };
        var colon = volumeRef.IndexOf(':');
        if (colon >= 0)
        {
            result.Datastore = volumeRef[..colon];
            var path = volumeRef[(colon + 1)..];
            // Strip leading "iso/" subdirectory if present
            result.FileName = path.StartsWith("iso/", StringComparison.OrdinalIgnoreCase)
                ? path[4..]
                : path;
        }
        else
        {
            result.FileName = volumeRef;
        }
        return result;
    }
}
