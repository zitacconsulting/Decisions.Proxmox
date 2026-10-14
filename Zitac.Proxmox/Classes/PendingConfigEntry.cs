namespace Zitac.Proxmox.Steps;

public class PendingConfigEntry
{
    public string? CurrentValue { get; set; }
    public string? PendingValue { get; set; }
    public bool PendingDelete { get; set; }

    // Waiting for the next full stop and start
    public bool HasPendingChange => PendingDelete || PendingValue != null;

    // What the setting will be once pending changes are applied (null = removed)
    public string? EffectiveValue => PendingDelete ? null : PendingValue ?? CurrentValue;
}
