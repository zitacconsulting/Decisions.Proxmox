using System.Runtime.Serialization;
using System.Text.Json;
using DecisionsFramework.ServiceLayer.Utilities;

namespace Zitac.Proxmox.Steps;

[AutoRegisterNativeType]
[DataContract]
public class ProxmoxVM
{
    [DataMember] public int VMID { get; set; }
    [DataMember] public string? Name { get; set; }
    [DataMember] public string? Node { get; set; }
    [DataMember] public string? Notes { get; set; }
    [DataMember] public string[] Tags { get; set; } = Array.Empty<string>();

    [DataMember] public PowerState PowerState { get; set; }

    // The raw state from Proxmox (QEMU run state when known), e.g. "running", "paused", "save-vm"
    [DataMember] public string? PowerStateDetail { get; set; }
    [DataMember] public bool Template { get; set; }

    // Proxmox ostype code, e.g. "l26", "win11"
    [DataMember] public string? OSType { get; set; }
    [DataMember] public string? BIOS { get; set; }

    // BIOS is "ovmf". The EFI disk is optional, so it isn't used for this.
    [DataMember] public bool UEFI { get; set; }
    [DataMember] public bool TPMEnabled { get; set; }

    // Controller model for all SCSI disks (Disks with Bus = scsi); Proxmox has no per-disk controller
    [DataMember] public ScsiController SCSIController { get; set; }
    [DataMember] public string? MachineType { get; set; }
    [DataMember] public string? UUID { get; set; }

    [DataMember] public int? CPUSockets { get; set; }
    [DataMember] public int? CPUCores { get; set; }

    // Total vCPUs presented to the guest
    [DataMember] public int? CPUs { get; set; }
    [DataMember] public string? CPUType { get; set; }
    [DataMember] public double? CPUUsagePercent { get; set; }

    [DataMember] public long? MemoryMB { get; set; }
    [DataMember] public long? MemoryUsedMB { get; set; }

    // Minimum memory with ballooning; 0 = ballooning disabled, empty = not configured
    [DataMember] public long? BalloonMinMB { get; set; }

    [DataMember] public long? UptimeSeconds { get; set; }
    [DataMember] public bool StartOnBoot { get; set; }

    // Locked while an operation holds the VM (backup, clone, migration, snapshot, hibernation, ...);
    // config changes are refused until it ends
    [DataMember] public bool Locked { get; set; }
    [DataMember] public LockType LockedBy { get; set; }

    // The raw lock name from Proxmox, e.g. "backup", "suspended"
    [DataMember] public string? LockedByDetail { get; set; }

    [DataMember] public ProxmoxGuestAgent? GuestAgent { get; set; }
    [DataMember] public ProxmoxDisk[] Disks { get; set; } = Array.Empty<ProxmoxDisk>();
    [DataMember] public ProxmoxCDROM[] CDROMs { get; set; } = Array.Empty<ProxmoxCDROM>();
    [DataMember] public ProxmoxNetworkInterface[] NICs { get; set; } = Array.Empty<ProxmoxNetworkInterface>();

    public ProxmoxVM() { }

    // Full VM: runtime status, config (hardware, disks, CD-ROMs, NICs) and, if running, guest agent info.
    public static ProxmoxVM Load(ProxmoxClient client, string node, int vmId)
    {
        var vm = new ProxmoxVM { VMID = vmId, Node = node };
        vm.ApplyStatus(client.Get($"/nodes/{node}/qemu/{vmId}/status/current"));
        vm.ApplyConfig(client.Get($"/nodes/{node}/qemu/{vmId}/config"));
        vm.UpdateStates();
        if (vm.GuestAgent!.Enabled && vm.PowerState == PowerState.Running)
            vm.ApplyGuestAgent(client);
        return vm;
    }

    // Summary VM from a /cluster/resources entry. No config details or device lists.
    public static ProxmoxVM FromResource(JsonElement res)
    {
        var vm = new ProxmoxVM
        {
            VMID = ProxmoxJson.Int(res, "vmid") ?? 0,
            Node = ProxmoxJson.Str(res, "node"),
            Template = ProxmoxJson.Bool(res, "template") ?? false,
            CPUs = ProxmoxJson.Int(res, "maxcpu"),
        };
        vm.ApplyStatus(res);
        vm.UpdateStates();
        return vm;
    }

    // PowerState and the lock fields are derived once status and config are both read
    private void UpdateStates()
    {
        PowerState = PowerStateExtensions.FromProxmox(PowerStateDetail, LockedByDetail);
        LockedBy = LockTypeExtensions.FromProxmox(LockedByDetail);
        Locked = LockedBy != LockType.None;
    }

    // Fields common to /status/current and /cluster/resources
    private void ApplyStatus(JsonElement status)
    {
        Name = ProxmoxJson.Str(status, "name") ?? Name;
        PowerStateDetail = ProxmoxJson.Str(status, "qmpstatus") ?? ProxmoxJson.Str(status, "status");
        CPUs ??= ProxmoxJson.Int(status, "cpus");
        CPUUsagePercent = ProxmoxJson.ToPercent(ProxmoxJson.Double(status, "cpu"));
        MemoryMB = ProxmoxJson.BytesToMB(ProxmoxJson.Long(status, "maxmem"));
        MemoryUsedMB = ProxmoxJson.BytesToMB(ProxmoxJson.Long(status, "mem"));
        UptimeSeconds = ProxmoxJson.Long(status, "uptime");
        LockedByDetail = ProxmoxJson.Str(status, "lock");
        Tags = SplitTags(ProxmoxJson.Str(status, "tags"));
    }

    private void ApplyConfig(JsonElement config)
    {
        Name = ProxmoxJson.Str(config, "name") ?? Name;
        Notes = ProxmoxJson.Str(config, "description");
        Tags = SplitTags(ProxmoxJson.Str(config, "tags"));
        Template = ProxmoxJson.Bool(config, "template") ?? false;
        OSType = ProxmoxJson.Str(config, "ostype") ?? "other";
        BIOS = ProxmoxJson.Str(config, "bios") ?? "seabios";
        UEFI = BIOS == "ovmf";
        TPMEnabled = ProxmoxVMConfig.HasTPM(config);
        SCSIController = ScsiControllerExtensions.FromProxmox(ProxmoxJson.Str(config, "scsihw"));
        MachineType = ProxmoxJson.Str(config, "machine") ?? "i440fx";
        StartOnBoot = ProxmoxJson.Bool(config, "onboot") ?? false;
        LockedByDetail = ProxmoxJson.Str(config, "lock") ?? LockedByDetail;

        var smbios = ProxmoxJson.Str(config, "smbios1");
        if (smbios != null) UUID = ProxmoxJson.ParsePropertyString(smbios).options.GetValueOrDefault("uuid");

        CPUSockets = ProxmoxJson.Int(config, "sockets") ?? 1;
        CPUCores = ProxmoxJson.Int(config, "cores") ?? 1;
        CPUs = ProxmoxJson.Int(config, "vcpus") ?? CPUSockets * CPUCores;
        var cpu = ProxmoxJson.Str(config, "cpu");
        if (cpu != null)
        {
            var (positional, options) = ProxmoxJson.ParsePropertyString(cpu);
            CPUType = positional.FirstOrDefault() ?? options.GetValueOrDefault("cputype");
        }
        CPUType ??= "kvm64";

        // "memory" is a plain number, or "current=<MB>" on newer versions
        var memory = ProxmoxJson.Str(config, "memory");
        if (memory != null)
        {
            var (positional, options) = ProxmoxJson.ParsePropertyString(memory);
            MemoryMB = ProxmoxJson.ParseLong(positional.FirstOrDefault() ?? options.GetValueOrDefault("current")) ?? MemoryMB;
        }
        BalloonMinMB = ProxmoxJson.Long(config, "balloon");

        GuestAgent = new ProxmoxGuestAgent { Enabled = ProxmoxVMConfig.AgentEnabled(config) };

        Disks = ProxmoxVMConfig.Disks(config);
        CDROMs = ProxmoxVMConfig.CDROMs(config);
        NICs = ProxmoxVMConfig.NICs(config);
    }

    // Best effort: a failing agent call just leaves the info empty.
    private void ApplyGuestAgent(ProxmoxClient client)
    {
        var basePath = $"/nodes/{Node}/qemu/{VMID}/agent";
        JsonElement interfaces;
        try
        {
            interfaces = client.Get($"{basePath}/network-get-interfaces").GetProperty("result");
        }
        catch
        {
            return;
        }
        GuestAgent!.Responding = true;

        var allIps = new List<string>();
        foreach (var iface in interfaces.EnumerateArray())
        {
            var mac = ProxmoxJson.Str(iface, "hardware-address");
            var ips = new List<string>();
            if (iface.TryGetProperty("ip-addresses", out var addrs))
            {
                foreach (var addr in addrs.EnumerateArray())
                {
                    var ip = ProxmoxJson.Str(addr, "ip-address");
                    if (ip != null && !IsLoopbackOrLinkLocal(ip)) ips.Add(ip);
                }
            }
            allIps.AddRange(ips);

            var nic = NICs.FirstOrDefault(n => mac != null && string.Equals(n.MacAddress, mac, StringComparison.OrdinalIgnoreCase));
            if (nic != null) nic.IPAddresses = ips.ToArray();
        }
        GuestAgent.IPAddresses = allIps.Distinct().ToArray();

        try
        {
            var os = client.Get($"{basePath}/get-osinfo").GetProperty("result");
            GuestAgent.OSFullName = ProxmoxJson.Str(os, "pretty-name") ?? ProxmoxJson.Str(os, "name");
            GuestAgent.OSVersion = ProxmoxJson.Str(os, "version");
            GuestAgent.KernelRelease = ProxmoxJson.Str(os, "kernel-release");
        }
        catch { }

        try
        {
            GuestAgent.Hostname = ProxmoxJson.Str(client.Get($"{basePath}/get-host-name").GetProperty("result"), "host-name");
        }
        catch { }
    }

    private static bool IsLoopbackOrLinkLocal(string ip) =>
        ip.StartsWith("127.") || ip == "::1" || ip.StartsWith("169.254.") ||
        ip.StartsWith("fe80:", StringComparison.OrdinalIgnoreCase);

    // Proxmox separates tags with ';' (also accepts ',' and spaces on input)
    private static string[] SplitTags(string? tags) =>
        string.IsNullOrWhiteSpace(tags)
            ? Array.Empty<string>()
            : tags.Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
}
