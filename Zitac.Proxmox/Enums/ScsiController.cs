namespace Zitac.Proxmox.Steps;

// Proxmox "scsihw": one controller model for all of a VM's SCSI disks
public enum ScsiController
{
    VirtIOScsiPci,
    VirtIOScsiSingle,
    LSI,
    MegaRAID,
    ParaVirtual,
    LSI53C810,
}

public static class ScsiControllerExtensions
{
    public static string ToProxmox(this ScsiController controller) => controller switch
    {
        ScsiController.VirtIOScsiSingle => "virtio-scsi-single",
        ScsiController.LSI              => "lsi",
        ScsiController.MegaRAID         => "megasas",
        ScsiController.ParaVirtual      => "pvscsi",
        ScsiController.LSI53C810        => "lsi53c810",
        _                               => "virtio-scsi-pci",
    };

    // A VM without "scsihw" uses Proxmox's default, lsi. These are all the values the API accepts.
    public static ScsiController FromProxmox(string? scsihw) => scsihw switch
    {
        "virtio-scsi-pci"    => ScsiController.VirtIOScsiPci,
        "virtio-scsi-single" => ScsiController.VirtIOScsiSingle,
        "megasas"            => ScsiController.MegaRAID,
        "pvscsi"             => ScsiController.ParaVirtual,
        "lsi53c810"          => ScsiController.LSI53C810,
        _                    => ScsiController.LSI,
    };
}
