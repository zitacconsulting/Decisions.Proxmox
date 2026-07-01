using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.RegularExpressions;
using DecisionsFramework.ServiceLayer.Utilities;

namespace Zitac.Proxmox.Steps;

[AutoRegisterNativeType]
[DataContract]
public class ProxmoxVMExtended : ProxmoxVM
{
    [DataMember] public ProxmoxNetworkInterface[]? NetworkInterfaces { get; set; }
    [DataMember] public ProxmoxDisk[]? Disks { get; set; }
    [DataMember] public ProxmoxMountedISO[]? MountedISOs { get; set; }

    public ProxmoxVMExtended() { }

    public static new ProxmoxVMExtended FromJson(JsonElement element, string node)
    {
        var vm = new ProxmoxVMExtended();
        FillFromJson(vm, element, node);
        return vm;
    }

    private static readonly Regex NetKey = new(@"^net\d+$", RegexOptions.Compiled);
    private static readonly Regex DiskKey = new(@"^(scsi|ide|virtio|sata)\d+$", RegexOptions.Compiled);

    public void ApplyConfig(JsonElement config)
    {
        var nets = new List<ProxmoxNetworkInterface>();
        var disks = new List<ProxmoxDisk>();
        var isos = new List<ProxmoxMountedISO>();

        foreach (var prop in config.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.String)
                continue;
            var raw = prop.Value.GetString() ?? string.Empty;
            if (NetKey.IsMatch(prop.Name))
                nets.Add(ProxmoxNetworkInterface.Parse(prop.Name, raw));
            else if (DiskKey.IsMatch(prop.Name))
            {
                if (raw.Contains("media=cdrom"))
                {
                    var volumePart = raw.Split(',')[0];
                    if (!string.IsNullOrEmpty(volumePart) && volumePart != "none")
                        isos.Add(ProxmoxMountedISO.FromVolumeRef(prop.Name, volumePart));
                }
                else
                {
                    var disk = ProxmoxDisk.Parse(prop.Name, raw);
                    if (disk != null) disks.Add(disk);
                }
            }
        }

        NetworkInterfaces = nets.ToArray();
        Disks = disks.ToArray();
        MountedISOs = isos.ToArray();
    }
}
