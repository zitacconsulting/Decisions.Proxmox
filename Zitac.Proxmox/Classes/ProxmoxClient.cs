using System.Text.Json;

namespace Zitac.Proxmox.Steps;

public class ProxmoxClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    public ProxmoxClient(string hostname, int port, bool ignoreSSLErrors)
    {
        _baseUrl = $"https://{hostname}:{port}/api2/json";
        var handler = new HttpClientHandler();
        if (ignoreSSLErrors)
        {
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        }
        _httpClient = new HttpClient(handler);
        _httpClient.Timeout = TimeSpan.FromSeconds(60);
    }

    public void Authenticate(object? credentials)
    {
        switch (credentials)
        {
            case ApiTokenCredentials tokenCreds:
                _httpClient.DefaultRequestHeaders.Remove("Authorization");
                _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Authorization",
                    $"PVEAPIToken={tokenCreds.ApiTokenId}={tokenCreds.ApiTokenSecret}");
                break;
            case UsernamePasswordCredentials userCreds:
                var (ticket, csrfToken) = Login(userCreds.Username!, userCreds.Password!);
                _httpClient.DefaultRequestHeaders.Remove("Cookie");
                _httpClient.DefaultRequestHeaders.Remove("CSRFPreventionToken");
                _httpClient.DefaultRequestHeaders.Add("Cookie", $"PVEAuthCookie={Uri.EscapeDataString(ticket)}");
                _httpClient.DefaultRequestHeaders.Add("CSRFPreventionToken", csrfToken);
                break;
            default:
                throw new Exception("Unsupported credentials type.");
        }
    }

    private (string ticket, string csrfToken) Login(string username, string password)
    {
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("username", username),
            new KeyValuePair<string, string>("password", password),
        });

        var response = _httpClient.PostAsync($"{_baseUrl}/access/ticket", content).GetAwaiter().GetResult();
        var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Authentication failed ({response.StatusCode}): {json}");

        using var doc = JsonDocument.Parse(json);
        var data = doc.RootElement.GetProperty("data");
        return (data.GetProperty("ticket").GetString()!, data.GetProperty("CSRFPreventionToken").GetString()!);
    }

    public JsonElement Get(string path)
    {
        var response = _httpClient.GetAsync($"{_baseUrl}{path}").GetAwaiter().GetResult();
        var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

        if (!response.IsSuccessStatusCode)
            throw new Exception($"GET {path} failed ({response.StatusCode}): {json}");

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("data").Clone();
    }

    public string Post(string path, Dictionary<string, string>? data = null)
    {
        HttpContent content = data != null
            ? new FormUrlEncodedContent(data)
            : new StringContent(string.Empty);

        var response = _httpClient.PostAsync($"{_baseUrl}{path}", content).GetAwaiter().GetResult();
        var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

        if (!response.IsSuccessStatusCode)
            throw new Exception($"POST {path} failed ({response.StatusCode}): {json}");

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("data", out var dataEl))
        {
            return dataEl.ValueKind == JsonValueKind.String ? dataEl.GetString()! : dataEl.ToString();
        }
        return string.Empty;
    }

    public void Put(string path, Dictionary<string, string>? data = null)
    {
        HttpContent content = data != null
            ? new FormUrlEncodedContent(data)
            : new StringContent(string.Empty);

        var response = _httpClient.PutAsync($"{_baseUrl}{path}", content).GetAwaiter().GetResult();
        var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

        if (!response.IsSuccessStatusCode)
            throw new Exception($"PUT {path} failed ({response.StatusCode}): {json}");
    }

    // Returns the "data" value (a task UPID for async operations), or empty.
    public string Delete(string path)
    {
        var response = _httpClient.DeleteAsync($"{_baseUrl}{path}").GetAwaiter().GetResult();
        var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

        if (!response.IsSuccessStatusCode)
            throw new Exception($"DELETE {path} failed ({response.StatusCode}): {json}");

        if (string.IsNullOrWhiteSpace(json)) return string.Empty;
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.String)
            return dataEl.GetString()!;
        return string.Empty;
    }

    // Returns the node name that hosts the given VM ID, or throws if not found.
    public string FindNodeForVM(int vmId) =>
        TryFindNodeForVM(vmId) ?? throw new Exception(NotFoundMessage("VM", vmId));

    // Returns the node name that hosts the given container ID, or throws if not found.
    public string FindNodeForContainer(int containerId) =>
        TryFindNodeForContainer(containerId) ?? throw new Exception(NotFoundMessage("Container", containerId));

    // Return null when the guest isn't in the cluster; API/auth/network errors still throw.
    public string? TryFindNodeForVM(int vmId) => FindNodeInCluster("qemu", vmId);

    public string? TryFindNodeForContainer(int containerId) => FindNodeInCluster("lxc", containerId);

    // One call lists every guest in the cluster with its node. Proxmox only includes
    // guests the credentials have VM.Audit on, so "missing" can also mean "no access".
    private string? FindNodeInCluster(string guestType, int id)
    {
        var resources = Get("/cluster/resources?type=vm");
        foreach (var res in resources.EnumerateArray())
        {
            if (!res.TryGetProperty("type", out var type) || type.GetString() != guestType) continue;
            if (!res.TryGetProperty("vmid", out var vmid)) continue;

            // vmid is normally a number, but tolerate it arriving as a string
            var resId = vmid.ValueKind == JsonValueKind.Number ? vmid.GetInt32()
                      : int.TryParse(vmid.GetString(), out var parsed) ? parsed : -1;

            if (resId == id && res.TryGetProperty("node", out var node))
                return node.GetString();
        }
        return null;
    }

    private static string NotFoundMessage(string kind, int id) =>
        $"{kind} {id} not found in the cluster, or the credentials lack VM.Audit permission on it.";

    // Polls a task until it stops. Returns its warning lines (empty if it finished cleanly).
    // A task ending in "WARNINGS: n" still succeeded, so it is not treated as a failure.
    // Throws if the task fails, or if it is still running after timeoutSeconds (0 = no limit).
    public string[] WaitForTask(string node, string upid, int timeoutSeconds)
    {
        var encodedUpid = Uri.EscapeDataString(upid);
        var deadline = timeoutSeconds > 0 ? DateTime.UtcNow.AddSeconds(timeoutSeconds) : DateTime.MaxValue;
        while (true)
        {
            System.Threading.Thread.Sleep(2000);
            var status = Get($"/nodes/{node}/tasks/{encodedUpid}/status");
            if (status.GetProperty("status").GetString() == "stopped")
            {
                var exitStatus = status.TryGetProperty("exitstatus", out var exitEl) ? exitEl.GetString() ?? "OK" : "OK";
                if (exitStatus == "OK")
                    return Array.Empty<string>();
                if (exitStatus.StartsWith("WARNINGS", StringComparison.OrdinalIgnoreCase))
                    return GetTaskWarnings(node, encodedUpid, exitStatus);
                throw new Exception($"Task failed: {exitStatus}");
            }
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException($"Task did not complete within {timeoutSeconds} seconds. It may still be running in Proxmox: {upid}");
        }
    }

    // The exit status only carries a count ("WARNINGS: 2"); the text is in the task log as "WARN: ..." lines.
    private string[] GetTaskWarnings(string node, string encodedUpid, string exitStatus)
    {
        try
        {
            var log = Get($"/nodes/{node}/tasks/{encodedUpid}/log?limit=10000");
            var warnings = log.EnumerateArray()
                .Select(l => l.TryGetProperty("t", out var t) ? t.GetString() ?? string.Empty : string.Empty)
                .Where(t => t.StartsWith("WARN:", StringComparison.OrdinalIgnoreCase))
                .Select(t => t[5..].Trim())
                .ToArray();
            if (warnings.Length > 0) return warnings;
        }
        catch { }
        return new[] { exitStatus };
    }

    public void Dispose() => _httpClient.Dispose();
}
