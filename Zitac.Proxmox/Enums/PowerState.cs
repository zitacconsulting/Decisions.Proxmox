namespace Zitac.Proxmox.Steps;

// Unknown comes first so it's also the value of an unset field
public enum PowerState
{
    Unknown,
    Running,
    Stopped,
    Paused,
    Hibernated,
    Starting,
    Migrating,
    Error,

    // A rare QEMU run state (save-vm, restore-vm, shutdown, debug, ...); see PowerStateDetail
    Other,
}

public static class PowerStateExtensions
{
    // state is the QEMU run state (qmpstatus) when known, otherwise the plain running/stopped status
    public static PowerState FromProxmox(string? state, string? lockName) => state switch
    {
        null or "" or "unknown" => PowerState.Unknown,
        "running" => PowerState.Running,

        // Hibernate (suspend to disk) leaves the VM stopped with a "suspended" lock
        "stopped" when lockName == "suspended" => PowerState.Hibernated,
        "stopped" => PowerState.Stopped,
        "paused" => PowerState.Paused,
        "prelaunch" => PowerState.Starting,
        "inmigrate" or "postmigrate" or "finish-migrate" => PowerState.Migrating,
        "io-error" or "internal-error" or "guest-panicked" or "watchdog" => PowerState.Error,
        _ => PowerState.Other,
    };
}
