using System.Runtime.Serialization;
using System.Text.Json;
using DecisionsFramework.ServiceLayer.Utilities;

namespace Zitac.Proxmox.Steps;

[AutoRegisterNativeType]
[DataContract]
public class ProxmoxCPUType
{
    // Value to use in the CustomType field of Create/Clone VM's CPU input
    [DataMember] public string? Name { get; set; }

    // CPU vendor the guest sees, e.g. GenuineIntel, AuthenticAMD
    [DataMember] public string? Vendor { get; set; }

    // Defined by the cluster admin (name starts with "custom-")
    [DataMember] public bool Custom { get; set; }

    // Proxmox's generic profiles (x86-64-v2, -v3, -v4)
    [DataMember] public bool Generic { get; set; }

    public ProxmoxCPUType() { }

    public static ProxmoxCPUType FromJson(JsonElement el) => new()
    {
        Name = ProxmoxJson.Str(el, "name"),
        Vendor = ProxmoxJson.Str(el, "vendor"),
        Custom = ProxmoxJson.Bool(el, "custom") ?? false,
        Generic = ProxmoxJson.Bool(el, "abstract") ?? false,
    };
}
