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

    public void Delete(string path)
    {
        var response = _httpClient.DeleteAsync($"{_baseUrl}{path}").GetAwaiter().GetResult();
        var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

        if (!response.IsSuccessStatusCode)
            throw new Exception($"DELETE {path} failed ({response.StatusCode}): {json}");
    }

    // Returns the node name that hosts the given VM ID, or throws if not found.
    public string FindNodeForVM(int vmId)
    {
        var nodes = Get("/nodes");
        foreach (var nodeEl in nodes.EnumerateArray())
        {
            var nodeName = nodeEl.GetProperty("node").GetString()!;
            try
            {
                Get($"/nodes/{nodeName}/qemu/{vmId}/status/current");
                return nodeName;
            }
            catch { }
        }
        throw new Exception($"VM {vmId} not found on any node.");
    }

    public void WaitForTask(string node, string upid)
    {
        var encodedUpid = Uri.EscapeDataString(upid);
        while (true)
        {
            System.Threading.Thread.Sleep(2000);
            var status = Get($"/nodes/{node}/tasks/{encodedUpid}/status");
            var taskStatus = status.GetProperty("status").GetString();
            if (taskStatus == "stopped")
            {
                if (status.TryGetProperty("exitstatus", out var exitEl))
                {
                    var exitStatus = exitEl.GetString();
                    if (exitStatus != "OK")
                        throw new Exception($"Task failed: {exitStatus}");
                }
                return;
            }
        }
    }

    public void WaitForTaskWithTimeout(string node, string upid, int timeoutSeconds)
    {
        var encodedUpid = Uri.EscapeDataString(upid);
        int elapsed = 0;
        while (elapsed < timeoutSeconds)
        {
            System.Threading.Thread.Sleep(2000);
            elapsed += 2;
            var status = Get($"/nodes/{node}/tasks/{encodedUpid}/status");
            var taskStatus = status.GetProperty("status").GetString();
            if (taskStatus == "stopped")
            {
                if (status.TryGetProperty("exitstatus", out var exitEl))
                {
                    var exitStatus = exitEl.GetString();
                    if (exitStatus != "OK")
                        throw new Exception($"Task failed: {exitStatus}");
                }
                return;
            }
        }
        throw new TimeoutException($"Task did not complete within {timeoutSeconds} seconds");
    }

    public void Dispose() => _httpClient.Dispose();
}
