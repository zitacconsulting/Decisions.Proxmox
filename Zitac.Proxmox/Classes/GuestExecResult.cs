namespace Zitac.Proxmox.Steps;

// Result of a guest agent exec, from /agent/exec-status
public class GuestExecResult
{
    public int ExitCode { get; set; }
    public string Output { get; set; } = string.Empty;
    public string ErrorOutput { get; set; } = string.Empty;

    // The agent only captures a limited amount of stdout/stderr
    public bool Truncated { get; set; }
}
