namespace Zitac.Proxmox.Steps;

public enum NetworkModel
{
    VirtIO,
    E1000,
    E1000e,
    RTL8139,
    VMware_vmxnet3,
}

public static class NetworkModelExtensions
{
    // Maps to the model key Proxmox uses in a netX config line
    public static string ToProxmox(this NetworkModel model) => model switch
    {
        NetworkModel.E1000          => "e1000",
        NetworkModel.E1000e         => "e1000e",
        NetworkModel.RTL8139        => "rtl8139",
        NetworkModel.VMware_vmxnet3 => "vmxnet3",
        _                           => "virtio",
    };
}
