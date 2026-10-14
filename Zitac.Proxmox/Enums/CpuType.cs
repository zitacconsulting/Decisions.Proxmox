namespace Zitac.Proxmox.Steps;

// Proxmox's generic CPU types. Named models (EPYC-Genoa, Skylake-Server, ...) and
// cluster-defined custom models go in ProxmoxCPUSettings.CustomType instead.
// Default comes first so an unset field in a data type means "not chosen".
public enum CpuType
{
    Default,
    X86_64_v2_AES,
    X86_64_v2,
    X86_64_v3,
    X86_64_v4,
    Host,
    Max,
    KVM64,
    QEMU64,
}

public static class CpuTypeExtensions
{
    // What the Proxmox GUI pre-selects for new VMs. The API itself falls back to kvm64
    // when "cpu" is omitted, so Create VM sends this explicitly.
    public const string GuiDefault = "x86-64-v2-AES";

    // Null for Default
    public static string? ToProxmox(this CpuType type) => type switch
    {
        CpuType.X86_64_v2_AES => "x86-64-v2-AES",
        CpuType.X86_64_v2     => "x86-64-v2",
        CpuType.X86_64_v3     => "x86-64-v3",
        CpuType.X86_64_v4     => "x86-64-v4",
        CpuType.Host          => "host",
        CpuType.Max           => "max",
        CpuType.KVM64         => "kvm64",
        CpuType.QEMU64        => "qemu64",
        _                     => null,
    };
}
