namespace Zitac.Proxmox.Steps;

// Mirrors the Proxmox GUI "Guest OS" Type + Version choices.
// Mapped to the REST API "ostype" value by OsTypeExtensions.ToProxmox.
public enum OsType
{
    Linux_6x_2_6_Kernel,
    Linux_2_4_Kernel,
    Windows_11_2022_2025,
    Windows_10_2016_2019,
    Windows_8x_2012_2012R2,
    Windows_7_2008R2,
    Windows_Vista_2008,
    Windows_XP_2003,
    Windows_2000,
    Solaris_Kernel,
    Other,
}

public static class OsTypeExtensions
{
    public static string ToProxmox(this OsType osType) => osType switch
    {
        OsType.Linux_6x_2_6_Kernel    => "l26",
        OsType.Linux_2_4_Kernel       => "l24",
        OsType.Windows_11_2022_2025   => "win11",
        OsType.Windows_10_2016_2019   => "win10",
        OsType.Windows_8x_2012_2012R2 => "win8",
        OsType.Windows_7_2008R2       => "win7",
        OsType.Windows_Vista_2008     => "w2k8",
        OsType.Windows_XP_2003        => "wxp",
        OsType.Windows_2000           => "w2k",
        OsType.Solaris_Kernel         => "solaris",
        _                             => "other",
    };
}
