using System.Globalization;
using System.Text.Json;

namespace Zitac.Proxmox.Steps;

// Proxmox returns many values as either numbers or numeric strings depending on the
// endpoint and version (e.g. config "memory" is a string on PVE 8+), so read leniently.
internal static class ProxmoxJson
{
    public static string? Str(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null,
        };
    }

    public static long? Long(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var l)) return l;
        if (v.ValueKind == JsonValueKind.Number) return (long)v.GetDouble();
        if (v.ValueKind == JsonValueKind.String) return ParseLong(v.GetString());
        return null;
    }

    public static int? Int(JsonElement el, string name) => (int?)Long(el, name);

    public static double? Double(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number) return v.GetDouble();
        if (v.ValueKind == JsonValueKind.String &&
            double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
        return null;
    }

    public static bool? Bool(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => v.GetDouble() != 0,
            JsonValueKind.String => v.GetString() == "1",
            _ => null,
        };
    }

    public static long? ParseLong(string? s) =>
        long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : null;

    public static long? BytesToMB(long? bytes) => bytes.HasValue ? bytes.Value / (1024 * 1024) : null;

    public static double? BytesToGB(long? bytes) =>
        bytes.HasValue ? Math.Round(bytes.Value / (1024.0 * 1024 * 1024), 2) : null;

    // Fraction (0.0-1.0) to percent with two decimals
    public static double? ToPercent(double? fraction) =>
        fraction.HasValue ? Math.Round(fraction.Value * 100, 2) : null;

    // Proxmox size strings: "32G", "512M", "1T", "528K" or plain bytes
    public static double? SizeToGB(string? size)
    {
        if (string.IsNullOrEmpty(size)) return null;
        var unit = char.ToUpperInvariant(size[^1]);
        var numberPart = char.IsDigit(unit) ? size : size[..^1];
        if (!double.TryParse(numberPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)) return null;
        var gb = unit switch
        {
            'T' => n * 1024,
            'G' => n,
            'M' => n / 1024,
            'K' => n / (1024 * 1024),
            _ => n / (1024.0 * 1024 * 1024),
        };
        // Three decimals so small volumes (a few MB) don't round to 0
        return Math.Round(gb, 3);
    }

    // Splits a Proxmox property string like "local-lvm:vm-100-disk-0,size=32G,ssd=1".
    // Entries without '=' are returned in order as positional values.
    public static (List<string> positional, Dictionary<string, string> options) ParsePropertyString(string raw)
    {
        var positional = new List<string>();
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq < 0) positional.Add(part);
            else options[part[..eq]] = part[(eq + 1)..];
        }
        return (positional, options);
    }

    // Sorts interface names naturally (net2 before net10)
    public static int CompareInterfaces(string? a, string? b)
    {
        static (string prefix, int number) Split(string? s)
        {
            s ??= string.Empty;
            var i = s.Length;
            while (i > 0 && char.IsDigit(s[i - 1])) i--;
            return (s[..i], i < s.Length ? int.Parse(s[i..], CultureInfo.InvariantCulture) : -1);
        }
        var (pa, na) = Split(a);
        var (pb, nb) = Split(b);
        var c = string.CompareOrdinal(pa, pb);
        return c != 0 ? c : na.CompareTo(nb);
    }
}
