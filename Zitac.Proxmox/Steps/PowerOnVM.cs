using DecisionsFramework.Design.Flow;
using DecisionsFramework.Design.Properties;
using DecisionsFramework.Design.ConfigurationStorage.Attributes;
using DecisionsFramework.Design.Flow.Mapping;
using DecisionsFramework.Design.Flow.CoreSteps;

namespace Zitac.Proxmox.Steps;

[AutoRegisterStep("Power On VM", "Integration", "Proxmox", "VMs")]
[Writable]
public class PowerOnVM : BaseFlowAwareStep, ISyncStep, IDataConsumer, IDataProducer
{
    [WritableValue]
    private bool ignoreSSLErrors;

    [WritableValue]
    private int port = 8006;

    [WritableValue]
    private bool waitForTask = true;

    [WritableValue]
    private bool showAlreadyRunning;

    [WritableValue]
    private bool useApiToken;

    [PropertyClassification(0, "Use API Token", new string[] { "Authentication" })]
    public bool UseApiToken
    {
        get { return useApiToken; }
        set { useApiToken = value; this.OnPropertyChanged("InputData"); }
    }

    [PropertyClassification(0, "Ignore SSL Errors", new string[] { "Settings" })]
    public bool IgnoreSSLErrors { get { return ignoreSSLErrors; } set { ignoreSSLErrors = value; } }

    [PropertyClassification(1, "Port", new string[] { "Settings" })]
    public int Port { get { return port; } set { port = value; } }

    [PropertyClassification(2, "Wait For Task Completion", new string[] { "Settings" })]
    public bool WaitForTask { get { return waitForTask; } set { waitForTask = value; } }

    [PropertyClassification(3, "Show Outcome for 'Already Running'", new string[] { "Outcomes" })]
    public bool ShowAlreadyRunning
    {
        get { return showAlreadyRunning; }
        set { showAlreadyRunning = value; this.OnPropertyChanged("OutcomeScenarios"); }
    }

    public DataDescription[] InputData => new[]
    {
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(string)), "Hostname") { Categories = new string[] { "Connection" } },
        useApiToken
            ? new DataDescription((DecisionsType)new DecisionsNativeType(typeof(ApiTokenCredentials)), "Credentials") { Categories = new string[] { "Connection" } }
            : new DataDescription((DecisionsType)new DecisionsNativeType(typeof(UsernamePasswordCredentials)), "Credentials") { Categories = new string[] { "Connection" } },
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(string)), "Node"),
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(int)), "VM ID"),
    };

    public override OutcomeScenarioData[] OutcomeScenarios
    {
        get
        {
            var outcomes = new List<OutcomeScenarioData> { new OutcomeScenarioData("Done") };
            if (showAlreadyRunning) outcomes.Add(new OutcomeScenarioData("Already Running"));
            outcomes.Add(new OutcomeScenarioData("Error", new DataDescription(typeof(string), "Error Message")));
            return outcomes.ToArray();
        }
    }

    public ResultData Run(StepStartData data)
    {
        var hostname = data.Data["Hostname"] as string;
        var credentials = data.Data["Credentials"];
        var node = data.Data["Node"] as string;
        var vmId = (int)data.Data["VM ID"];

        try
        {
            using var client = new ProxmoxClient(hostname!, port, ignoreSSLErrors);
            client.Authenticate(credentials);

            if (showAlreadyRunning)
            {
                var status = client.Get($"/nodes/{node}/qemu/{vmId}/status/current");
                if (status.TryGetProperty("status", out var st) && st.GetString() == "running")
                    return new ResultData("Already Running");
            }

            var upid = client.Post($"/nodes/{node}/qemu/{vmId}/status/start");
            if (waitForTask && !string.IsNullOrEmpty(upid))
                client.WaitForTask(node!, upid);

            return new ResultData("Done");
        }
        catch (Exception e)
        {
            return new ResultData("Error", new Dictionary<string, object> { { "Error Message", e.ToString() } });
        }
    }
}
