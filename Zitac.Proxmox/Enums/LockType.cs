namespace Zitac.Proxmox.Steps;

// What a Proxmox config lock is held by. None comes first so it's also the value of an unset field.
public enum LockType
{
    None,
    Backup,
    Clone,
    Create,
    Migrate,
    Snapshot,
    SnapshotDelete,
    Rollback,

    // Saving RAM to disk ("suspending")
    Hibernating,

    // Hibernated; PowerState is Hibernated too ("suspended")
    Hibernated,

    // Container-only or future values (disk, fstrim, mounted, destroyed, ...); see LockedByDetail
    Other,
}

public static class LockTypeExtensions
{
    public static LockType FromProxmox(string? lockName) => lockName switch
    {
        null or "" => LockType.None,
        "backup" => LockType.Backup,
        "clone" => LockType.Clone,
        "create" => LockType.Create,
        "migrate" => LockType.Migrate,
        "snapshot" => LockType.Snapshot,
        "snapshot-delete" => LockType.SnapshotDelete,
        "rollback" => LockType.Rollback,
        "suspending" => LockType.Hibernating,
        "suspended" => LockType.Hibernated,
        _ => LockType.Other,
    };
}
