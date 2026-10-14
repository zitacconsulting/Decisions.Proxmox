using System.Runtime.Serialization;
using DecisionsFramework.ServiceLayer.Utilities;

namespace Zitac.Proxmox.Steps;

[AutoRegisterNativeType]
[DataContract]
public class ProxmoxGuestAgent
{
    // Agent is enabled in the VM's config
    [DataMember] public bool Enabled { get; set; }

    // Agent answered. False when the VM is stopped, the agent isn't installed/running,
    // or the credentials lack permission to query it (VM.Monitor / VM.GuestAgent.Audit).
    [DataMember] public bool Responding { get; set; }

    [DataMember] public string? OSFullName { get; set; }
    [DataMember] public string? OSVersion { get; set; }
    [DataMember] public string? KernelRelease { get; set; }
    [DataMember] public string? Hostname { get; set; }

    // All non-loopback, non-link-local addresses reported by the guest
    [DataMember] public string[] IPAddresses { get; set; } = Array.Empty<string>();

    public ProxmoxGuestAgent() { }
}
