using System.Runtime.Serialization;
using DecisionsFramework.ServiceLayer.Utilities;

namespace Zitac.Proxmox.Steps;

[AutoRegisterNativeType]
[DataContract]
public class ProxmoxDisk
{
    [DataMember] public string? Interface { get; set; }

    // From the interface name (scsi0 = scsi). SCSI disks use the VM's SCSIController.
    [DataMember] public DiskBus Bus { get; set; }
    [DataMember] public string? Storage { get; set; }
    [DataMember] public string? VolumeName { get; set; }
    [DataMember] public double? SizeGB { get; set; }
    [DataMember] public string? Format { get; set; }
    [DataMember] public string? Cache { get; set; }
    [DataMember] public bool SSD { get; set; }
    [DataMember] public bool Discard { get; set; }
    [DataMember] public bool IOThread { get; set; }
    [DataMember] public bool Backup { get; set; } = true;

    public ProxmoxDisk() { }

    public static ProxmoxDisk Parse(string interfaceName, string raw)
    {
        var (positional, options) = ProxmoxJson.ParsePropertyString(raw);
        var result = new ProxmoxDisk
        {
            Interface = interfaceName,
            Bus = Enum.TryParse<DiskBus>(interfaceName.TrimEnd("0123456789".ToCharArray()), out var bus) ? bus : DiskBus.scsi,
        };

        // The volume may be positional or given as file=/volume=
        var volume = positional.FirstOrDefault()
                     ?? (options.TryGetValue("file", out var f) ? f : options.GetValueOrDefault("volume"));
        if (volume != null)
        {
            var colon = volume.IndexOf(':');
            if (colon >= 0)
            {
                result.Storage = volume[..colon];
                result.VolumeName = volume[(colon + 1)..];
            }
            else
            {
                // Passthrough of a host device, e.g. /dev/sdb
                result.VolumeName = volume;
            }
        }

        result.SizeGB = ProxmoxJson.SizeToGB(options.GetValueOrDefault("size"));
        result.Format = options.GetValueOrDefault("format");
        result.Cache = options.GetValueOrDefault("cache");
        result.SSD = options.GetValueOrDefault("ssd") == "1";
        result.Discard = options.GetValueOrDefault("discard") == "on";
        result.IOThread = options.GetValueOrDefault("iothread") == "1";
        result.Backup = options.GetValueOrDefault("backup") != "0";
        return result;
    }
}
