using System.Runtime.Serialization;
using System.Text.Json;
using DecisionsFramework.ServiceLayer.Utilities;

namespace Zitac.Proxmox.Steps;

[AutoRegisterNativeType]
[DataContract]
public class ProxmoxISO
{
    [DataMember] public string? FileName { get; set; }
    [DataMember] public string? Datastore { get; set; }
    [DataMember] public string? Node { get; set; }
    [DataMember] public long? Size { get; set; }

    public ProxmoxISO() { }

    public static ProxmoxISO FromJson(JsonElement element, string datastore, string node)
    {
        var iso = new ProxmoxISO { Datastore = datastore, Node = node };

        if (element.TryGetProperty("volid", out var volid))
        {
            var volStr = volid.GetString() ?? string.Empty;
            var slash = volStr.LastIndexOf('/');
            iso.FileName = slash >= 0 ? volStr[(slash + 1)..] : volStr;
        }

        if (element.TryGetProperty("size", out var size) && size.ValueKind == JsonValueKind.Number)
            iso.Size = size.GetInt64();

        return iso;
    }
}
