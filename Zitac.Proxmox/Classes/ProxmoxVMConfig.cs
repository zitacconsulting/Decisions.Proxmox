using System.Text.Json;
using System.Text.RegularExpressions;

namespace Zitac.Proxmox.Steps;

// Extracts the device lists from a /nodes/{node}/qemu/{vmid}/config response.
internal static class ProxmoxVMConfig
{
    private static readonly Regex NetKey = new(@"^net\d+$", RegexOptions.Compiled);
    // efidisk0/tpmstate0 are left out on purpose: they're firmware storage, not data disks, and
    // resizing or removing them breaks UEFI boot or the virtual TPM
    private static readonly Regex DriveKey = new(@"^(scsi|ide|virtio|sata)\d+$", RegexOptions.Compiled);

    public static ProxmoxDisk[] Disks(JsonElement config) =>
        Drives(config).Where(d => !IsCDROM(d.raw))
            .Select(d => ProxmoxDisk.Parse(d.key, d.raw))
            .ToArray();

    // "agent" is "1", or "enabled=1,..." with extra options
    public static bool AgentEnabled(JsonElement config)
    {
        var agent = ProxmoxJson.Str(config, "agent");
        if (agent == null) return false;
        var (positional, options) = ProxmoxJson.ParsePropertyString(agent);
        return positional.FirstOrDefault() == "1" || options.GetValueOrDefault("enabled") == "1";
    }

    // A VM has a virtual TPM exactly when tpmstate0 exists; there's no separate on/off setting
    public static bool HasTPM(JsonElement config) => config.TryGetProperty("tpmstate0", out _);

    public static ProxmoxCDROM[] CDROMs(JsonElement config) =>
        Drives(config).Where(d => IsCDROM(d.raw))
            .Select(d => ProxmoxCDROM.Parse(d.key, d.raw))
            .ToArray();

    public static ProxmoxNetworkInterface[] NICs(JsonElement config) =>
        StringProperties(config).Where(p => NetKey.IsMatch(p.key))
            .Select(p => ProxmoxNetworkInterface.Parse(p.key, p.raw))
            .ToArray();

    public static bool IsDataDiskInterface(string interfaceName) => DriveKey.IsMatch(interfaceName);

    // The volume reference of a drive line, e.g. "local-lvm:vm-100-disk-1" (null for passthrough devices)
    public static string? VolumeRef(string raw)
    {
        var (positional, options) = ProxmoxJson.ParsePropertyString(raw);
        var volume = positional.FirstOrDefault() ?? options.GetValueOrDefault("file");
        return volume != null && volume.Contains(':') ? volume : null;
    }

    // A disk removed from the config is kept as unusedN; this finds the entry for a volume
    public static string? UnusedKeyFor(JsonElement config, string volumeRef) =>
        StringProperties(config)
            .Where(p => Regex.IsMatch(p.key, @"^unused\d+$") && VolumeRef(p.raw) == volumeRef)
            .Select(p => p.key)
            .FirstOrDefault();

    // Buses Proxmox allows a CD drive on (not virtio)
    private static readonly Regex CDROMSlot = new(@"^(ide[0-3]|sata[0-5]|scsi([0-9]|[12][0-9]|30))$", RegexOptions.Compiled);

    public static bool IsValidCDROMInterface(string interfaceName) => CDROMSlot.IsMatch(interfaceName);

    // The raw config line for an interface, or null when it isn't in use
    public static string? Drive(JsonElement config, string interfaceName) =>
        config.TryGetProperty(interfaceName, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    public static bool IsCDROM(string raw) =>
        ProxmoxJson.ParsePropertyString(raw).options.GetValueOrDefault("media") == "cdrom";

    private static IEnumerable<(string key, string raw)> Drives(JsonElement config) =>
        StringProperties(config).Where(p => DriveKey.IsMatch(p.key));

    private static IEnumerable<(string key, string raw)> StringProperties(JsonElement config) =>
        config.EnumerateObject()
            .Where(p => p.Value.ValueKind == JsonValueKind.String)
            .Select(p => (key: p.Name, raw: p.Value.GetString()!))
            .OrderBy(p => p.key, Comparer<string>.Create(ProxmoxJson.CompareInterfaces));
}
