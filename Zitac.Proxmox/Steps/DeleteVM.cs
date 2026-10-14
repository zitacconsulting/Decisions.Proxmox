using DecisionsFramework.Design.Flow;
using DecisionsFramework.Design.Properties;
using DecisionsFramework.Design.ConfigurationStorage.Attributes;
using DecisionsFramework.Design.Flow.Mapping;
using DecisionsFramework.Design.Flow.CoreSteps;
using DecisionsFramework.Design.Flow.Mapping.InputImpl;

namespace Zitac.Proxmox.Steps;

// Destroys the VM and all its volumes (attached, unused, EFI disk, TPM state)
[CategoryClassification(StepCategories.AuthenticationSortKey, StepCategories.Authentication)]
[CategoryClassification(StepCategories.SettingsSortKey, StepCategories.Settings)]
[AutoRegisterStep("Delete VM", "Integration", "Proxmox", "VMs")]
[Writable]
public class DeleteVM : BaseFlowAwareStep, ISyncStep, IDataConsumer, IDataProducer, IDefaultInputMappingStep
{
    [WritableValue]
    private bool ignoreSSLErrors;

    [WritableValue]
    private int port = 8006;

    [WritableValue]
    private bool useApiToken;

    [WritableValue]
    private bool waitForTask = true;

    [WritableValue]
    private int taskTimeoutSeconds = 600;

    [WritableValue]
    private bool showWarningsOutcome;

    [WritableValue]
    private bool purgeFromJobs = true;

    [WritableValue]
    private bool destroyUnreferencedDisks;

    [WritableValue]
    private bool showNotFound;

    [WritableValue]
    private bool showStillRunning;

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

    // Proxmox "purge": a deleted VM left in a backup job only produces failing backups
    [PropertyClassification(4, "Purge From Backup, Replication and HA Jobs", new string[] { "Settings" })]
    public bool PurgeFromJobs { get { return purgeFromJobs; } set { purgeFromJobs = value; } }

    [PropertyClassification(5, "Show Outcome for 'Done With Warnings'", new string[] { "Settings" })]
    public bool ShowWarningsOutcome
    {
        get { return showWarningsOutcome; }
        set { showWarningsOutcome = value; this.OnPropertyChanged("OutcomeScenarios"); }
    }

    // Proxmox "destroy-unreferenced-disks": also volumes with this VM ID that aren't in its config
    [PropertyClassification(6, "Destroy Unreferenced Disks", new string[] { "Settings" })]
    public bool DestroyUnreferencedDisks { get { return destroyUnreferencedDisks; } set { destroyUnreferencedDisks = value; } }

    [PropertyClassification(7, "Show Outcome for 'Not Found'", new string[] { "Settings" })]
    public bool ShowNotFound
    {
        get { return showNotFound; }
        set { showNotFound = value; this.OnPropertyChanged("OutcomeScenarios"); }
    }

    [PropertyClassification(8, "Show Outcome for 'Still Running'", new string[] { "Settings" })]
    public bool ShowStillRunning
    {
        get { return showStillRunning; }
        set { showStillRunning = value; this.OnPropertyChanged("OutcomeScenarios"); }
    }

    public IInputMapping[] DefaultInputs => new IInputMapping[]
    {
        new IgnoreInputMapping { InputDataName = "Node" },
    };

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
            if (showWarningsOutcome) outcomes.Add(new OutcomeScenarioData("Done With Warnings", new DataDescription(typeof(string), "Warnings", true)));
            if (showNotFound) outcomes.Add(new OutcomeScenarioData("Not Found"));
            if (showStillRunning) outcomes.Add(new OutcomeScenarioData("Still Running"));
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

            // Same rule as Get VM By ID: missing from the cluster, or on another node than the one given
            var actualNode = client.TryFindNodeForVM(vmId);
            if (actualNode == null || (!string.IsNullOrEmpty(node) && !string.Equals(actualNode, node, StringComparison.OrdinalIgnoreCase)))
                return showNotFound ? new ResultData("Not Found") : Error($"VM {vmId} not found in the cluster.");
            node = actualNode;

            // Proxmox refuses to destroy a running VM; say so clearly instead of passing on its error
            var status = client.Get($"/nodes/{node}/qemu/{vmId}/status/current");
            if (ProxmoxJson.Str(status, "status") == "running")
                return showStillRunning ? new ResultData("Still Running") : Error($"VM {vmId} is running. Power it off before deleting it.");

            var options = new List<string>();
            if (purgeFromJobs) options.Add("purge=1");
            if (destroyUnreferencedDisks) options.Add("destroy-unreferenced-disks=1");
            var query = options.Count > 0 ? "?" + string.Join("&", options) : string.Empty;

            var upid = client.Delete($"/nodes/{node}/qemu/{vmId}{query}");
            var warnings = Array.Empty<string>();
            if (waitForTask && !string.IsNullOrEmpty(upid))
                warnings = client.WaitForTask(node, upid, taskTimeoutSeconds);

            if (showWarningsOutcome && warnings.Length > 0)
                return new ResultData("Done With Warnings", new Dictionary<string, object> { { "Warnings", warnings } });
            return new ResultData("Done");
        }
        catch (Exception e)
        {
            return new ResultData("Error", new Dictionary<string, object> { { "Error Message", e.ToString() } });
        }
    }

    private static ResultData Error(string message) =>
        new ResultData("Error", new Dictionary<string, object> { { "Error Message", message } });
}
