using System.Text;

namespace Zitac.Proxmox.Steps;

// Proxmox snapshot names ("pve-configid"): a letter first, then letters, digits, '_' and '-',
// at most 40 characters. Create/Delete/Revert Snapshot all convert names the same way, so the
// same input (e.g. "Snapshot_2026-10-14 19:28") always refers to the same snapshot.
internal static class SnapshotName
{
    private const int MaxLength = 40;

    public static string ToProxmox(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name.Trim())
        {
            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-')
                sb.Append(c);
            else if (c == ':' || c == '.')
                sb.Append('-');
            else
                sb.Append('_');
        }

        var result = sb.ToString();
        if (result.Length == 0 || !char.IsAsciiLetter(result[0]))
            result = "s" + result;
        if (result.Length < 2)
            result += "_";
        return result.Length > MaxLength ? result[..MaxLength] : result;
    }
}
