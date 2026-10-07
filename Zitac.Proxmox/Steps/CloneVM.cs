using DecisionsFramework.Design.Flow;
using DecisionsFramework.Design.Properties;
using DecisionsFramework.Design.ConfigurationStorage.Attributes;
using DecisionsFramework.Design.Flow.Mapping;
using DecisionsFramework.Design.Flow.CoreSteps;
using DecisionsFramework.Design.Flow.Mapping.InputImpl;

namespace Zitac.Proxmox.Steps;

[AutoRegisterStep("Clone VM", "Integration", "Proxmox", "VMs")]
[Writable]
public class CloneVM : BaseFlowAwareStep, ISyncStep, IDataConsumer, IDataProducer, IDefaultInputMappingStep
{
    [WritableValue]
    private bool ignoreSSLErrors;

    [WritableValue]
    private int port = 8006;

    [WritableValue]
    private bool waitForTask = true;

    [WritableValue]
    private int taskTimeoutSeconds = 3600;

    [WritableValue]
    private bool showWarningsOutcome;

    [WritableValue]
    private bool fullClone = true;

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

    [PropertyClassification(3, "Task Timeout (Seconds, 0 = No Limit)", new string[] { "Settings" })]
    public int TaskTimeoutSeconds { get { return taskTimeoutSeconds; } set { taskTimeoutSeconds = value; } }

    [PropertyClassification(5, "Show Outcome for 'Done With Warnings'", new string[] { "Settings" })]
    public bool ShowWarningsOutcome
    {
        get { return showWarningsOutcome; }
        set { showWarningsOutcome = value; this.OnPropertyChanged("OutcomeScenarios"); }
    }

    [PropertyClassification(4, "Full Clone (vs Linked Clone)", new string[] { "Settings" })]
    public bool FullClone { get { return fullClone; } set { fullClone = value; } }

    public IInputMapping[] DefaultInputs => new IInputMapping[]
    {
        new IgnoreInputMapping { InputDataName = "Node" },
        new IgnoreInputMapping { InputDataName = "New VM ID" },
        new IgnoreInputMapping { InputDataName = "Target Node" },
        new IgnoreInputMapping { InputDataName = "Target Storage" },
    };

    public DataDescription[] InputData => new[]
    {
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(string)), "Hostname") { Categories = new string[] { "Connection" } },
        useApiToken
            ? new DataDescription((DecisionsType)new DecisionsNativeType(typeof(ApiTokenCredentials)), "Credentials") { Categories = new string[] { "Connection" } }
            : new DataDescription((DecisionsType)new DecisionsNativeType(typeof(UsernamePasswordCredentials)), "Credentials") { Categories = new string[] { "Connection" } },
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(string)), "Node"),
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(int)), "VM ID"),
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(int)), "New VM ID"),
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(string)), "New VM Name"),
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(string)), "Target Node"),
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(string)), "Target Storage"),
    };

    public override OutcomeScenarioData[] OutcomeScenarios
    {
        get
        {
            var outcomes = new List<OutcomeScenarioData> { new OutcomeScenarioData("Done", new DataDescription(typeof(int), "New VM ID")) };
            if (showWarningsOutcome) outcomes.Add(new OutcomeScenarioData("Done With Warnings", new DataDescription(typeof(int), "New VM ID"), new DataDescription(typeof(string), "Warnings", true)));
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
        var newVmId = data.Data["New VM ID"] as int? ?? 0;
        var newVmName = data.Data["New VM Name"] as string;
        var targetNode = data.Data["Target Node"] as string;
        var targetStorage = data.Data["Target Storage"] as string;

        try
        {
            using var client = new ProxmoxClient(hostname!, port, ignoreSSLErrors);
            client.Authenticate(credentials);
            if (string.IsNullOrEmpty(node)) node = client.FindNodeForVM(vmId);

            var postData = new Dictionary<string, string>
            {
                { "newid", newVmId > 0 ? newVmId.ToString() : "0" },
                { "full", fullClone ? "1" : "0" },
            };
            if (!string.IsNullOrEmpty(newVmName)) postData["name"] = newVmName;
            if (!string.IsNullOrEmpty(targetNode)) postData["target"] = targetNode;
            if (!string.IsNullOrEmpty(targetStorage)) postData["storage"] = targetStorage;

            var upid = client.Post($"/nodes/{node}/qemu/{vmId}/clone", postData);
            var warnings = Array.Empty<string>();
            if (waitForTask && !string.IsNullOrEmpty(upid))
                warnings = client.WaitForTask(node!, upid, taskTimeoutSeconds);

            if (showWarningsOutcome && warnings.Length > 0)
                return new ResultData("Done With Warnings", new Dictionary<string, object> { { "New VM ID", newVmId }, { "Warnings", warnings } });
            return new ResultData("Done", new Dictionary<string, object> { { "New VM ID", newVmId } });
        }
        catch (Exception e)
        {
            return new ResultData("Error", new Dictionary<string, object> { { "Error Message", e.ToString() } });
        }
    }
}
