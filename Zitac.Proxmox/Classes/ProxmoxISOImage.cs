using System.Runtime.Serialization;
using DecisionsFramework.ServiceLayer.Utilities;

namespace Zitac.Proxmox.Steps;

[AutoRegisterNativeType]
[DataContract]
public class ProxmoxISOImage
{
    [DataMember] public string? Datastore { get; set; }
    [DataMember] public string? FileName { get; set; }

    public ProxmoxISOImage() { }

    // Returns the Proxmox volume reference: "datastore:iso/filename.iso"
    public string ToVolumeRef() => $"{Datastore}:iso/{FileName}";
}
