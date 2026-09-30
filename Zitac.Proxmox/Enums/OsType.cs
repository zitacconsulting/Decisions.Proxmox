namespace Zitac.Proxmox.Steps;

// Mirrors the Proxmox GUI "Guest OS" Type + Version choices.
// Mapped to the REST API "ostype" value in CreateVM.MapOsType.
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
