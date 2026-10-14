using DecisionsFramework.Design.Flow;
using DecisionsFramework.Design.Properties;
using DecisionsFramework.Design.ConfigurationStorage.Attributes;
using DecisionsFramework.Design.Flow.Mapping;
using DecisionsFramework.Design.Flow.CoreSteps;
using DecisionsFramework.Design.Flow.Mapping.InputImpl;

namespace Zitac.Proxmox.Steps;

[CategoryClassification(StepCategories.AuthenticationSortKey, StepCategories.Authentication)]
[CategoryClassification(StepCategories.SettingsSortKey, StepCategories.Settings)]
[AutoRegisterStep("Reboot VM", "Integration", "Proxmox", "VMs")]
[Writable]
public class RebootVM : BaseFlowAwareStep, ISyncStep, IDataConsumer, IDataProducer, IDefaultInputMappingStep
{
    [WritableValue]
    private bool ignoreSSLErrors;

    [WritableValue]
    private int port = 8006;

    [WritableValue]
    private bool waitForTask = true;

    [WritableValue]
    private int taskTimeoutSeconds = 600;

    [WritableValue]
    private bool showWarningsOutcome;

    [WritableValue]
    private bool useApiToken;

    [WritableValue]
    private bool waitForOsBoot;

    [WritableValue]
    private int osBootTimeoutSeconds = 600;

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

    // Like VMware's "Wait For OS Boot": done once the QEMU Guest Agent of the new boot answers
    [PropertyClassification(6, "Wait For OS Boot (Requires QEMU Guest Agent)", new string[] { "Settings" })]
    public bool WaitForOsBoot
    {
        get { return waitForOsBoot; }
        set { waitForOsBoot = value; this.OnPropertyChanged(nameof(WaitForOsBoot)); this.OnPropertyChanged("OutcomeScenarios"); }
    }

    [BooleanPropertyHidden(nameof(WaitForOsBoot), false)]
    [PropertyClassification(7, "OS Boot Timeout (Seconds, 0 = No Limit)", new string[] { "Settings" })]
    public int OsBootTimeoutSeconds { get { return osBootTimeoutSeconds; } set { osBootTimeoutSeconds = value; } }

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
            if (waitForOsBoot) outcomes.Add(new OutcomeScenarioData("Timeout"));
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
            if (string.IsNullOrEmpty(node)) node = client.FindNodeForVM(vmId);
            // Checked before rebooting, so a missing agent doesn't cost a reboot plus the full timeout
            if (waitForOsBoot && !ProxmoxVMConfig.AgentEnabled(client.Get($"/nodes/{node}/qemu/{vmId}/config")))
                return new ResultData("Error", new Dictionary<string, object> { { "Error Message", ProxmoxClient.GuestAgentRequiredMessage } });

            var rebootRequestedUtc = DateTime.UtcNow;
            var upid = client.Post($"/nodes/{node}/qemu/{vmId}/status/reboot");
            var warnings = Array.Empty<string>();

            // Waiting for the OS only makes sense once the reboot task itself has finished
            if ((waitForTask || waitForOsBoot) && !string.IsNullOrEmpty(upid))
                warnings = client.WaitForTask(node!, upid, taskTimeoutSeconds);

            if (waitForOsBoot && !client.WaitForGuestAgent(node!, vmId, osBootTimeoutSeconds, rebootRequestedUtc))
                return new ResultData("Timeout");
            if (showWarningsOutcome && warnings.Length > 0)
                return new ResultData("Done With Warnings", new Dictionary<string, object> { { "Warnings", warnings } });
            return new ResultData("Done");
        }
        catch (Exception e)
        {
            return new ResultData("Error", new Dictionary<string, object> { { "Error Message", e.ToString() } });
        }
    }
}
