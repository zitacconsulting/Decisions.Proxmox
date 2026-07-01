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

    [PropertyClassification(3, "Full Clone (vs Linked Clone)", new string[] { "Settings" })]
    public bool FullClone { get { return fullClone; } set { fullClone = value; } }

    public IInputMapping[] DefaultInputs => new IInputMapping[]
    {
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

    public override OutcomeScenarioData[] OutcomeScenarios => new[]
    {
        new OutcomeScenarioData("Done", new DataDescription(typeof(int), "New VM ID")),
        new OutcomeScenarioData("Error", new DataDescription(typeof(string), "Error Message")),
    };

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

            var postData = new Dictionary<string, string>
            {
                { "newid", newVmId > 0 ? newVmId.ToString() : "0" },
                { "full", fullClone ? "1" : "0" },
            };
            if (!string.IsNullOrEmpty(newVmName)) postData["name"] = newVmName;
            if (!string.IsNullOrEmpty(targetNode)) postData["target"] = targetNode;
            if (!string.IsNullOrEmpty(targetStorage)) postData["storage"] = targetStorage;

            var upid = client.Post($"/nodes/{node}/qemu/{vmId}/clone", postData);
            if (waitForTask && !string.IsNullOrEmpty(upid))
                client.WaitForTask(node!, upid);

            return new ResultData("Done", new Dictionary<string, object> { { "New VM ID", newVmId } });
        }
        catch (Exception e)
        {
            return new ResultData("Error", new Dictionary<string, object> { { "Error Message", e.ToString() } });
        }
    }
}
