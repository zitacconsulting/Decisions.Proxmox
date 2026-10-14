using DecisionsFramework.Design.Flow;
using DecisionsFramework.Design.Properties;
using DecisionsFramework.Design.Properties.Attributes;
using DecisionsFramework.Design.ConfigurationStorage.Attributes;
using DecisionsFramework.Design.Flow.Mapping;
using DecisionsFramework.Design.Flow.CoreSteps;
using DecisionsFramework.Design.Flow.Mapping.InputImpl;

namespace Zitac.Proxmox.Steps;

// Shared settings, outcomes and run logic for Run Script On VM and Run Command On VM.
// Runs through the QEMU Guest Agent (as SYSTEM on Windows, root on Linux).
// Fields are protected, not private, so Writable serialization of the derived steps sees them.
[CategoryClassification(StepCategories.AuthenticationSortKey, StepCategories.Authentication)]
[CategoryClassification(StepCategories.SettingsSortKey, StepCategories.Settings)]
[Writable]
public abstract class GuestExecStepBase : BaseFlowAwareStep, ISyncStep, IDataConsumer, IDataProducer, IDefaultInputMappingStep
{
    [WritableValue] protected bool ignoreSSLErrors;
    [WritableValue] protected int port = 8006;
    [WritableValue] protected bool useApiToken;
    [WritableValue] protected bool waitForExecution = true;
    [WritableValue] protected int timeoutSeconds = 300;
    [WritableValue] protected bool captureOutput = true;
    [WritableValue] protected bool showNotRunning;

    [PropertyClassification(0, "Use API Token", new string[] { "Authentication" })]
    public bool UseApiToken { get { return useApiToken; } set { useApiToken = value; this.OnPropertyChanged("InputData"); } }

    [PropertyClassification(0, "Ignore SSL Errors", new string[] { "Settings" })]
    public bool IgnoreSSLErrors { get { return ignoreSSLErrors; } set { ignoreSSLErrors = value; } }

    [PropertyClassification(1, "Port", new string[] { "Settings" })]
    public int Port { get { return port; } set { port = value; } }

    [PropertyClassification(2, "Wait For Execution", new string[] { "Settings" })]
    public bool WaitForExecution
    {
        get { return waitForExecution; }
        set { waitForExecution = value; this.OnPropertyChanged(nameof(WaitForExecution)); this.OnPropertyChanged("OutcomeScenarios"); }
    }

    [BooleanPropertyHidden(nameof(WaitForExecution), false)]
    [PropertyClassification(3, "Timeout (Seconds, 0 = No Limit)", new string[] { "Settings" })]
    public int TimeoutSeconds { get { return timeoutSeconds; } set { timeoutSeconds = value; } }

    [BooleanPropertyHidden(nameof(WaitForExecution), false)]
    [PropertyClassification(4, "Capture Output", new string[] { "Settings" })]
    public bool CaptureOutput
    {
        get { return captureOutput; }
        set { captureOutput = value; this.OnPropertyChanged("OutcomeScenarios"); }
    }

    [PropertyClassification(5, "Show Outcome for 'Not Running'", new string[] { "Settings" })]
    public bool ShowNotRunning
    {
        get { return showNotRunning; }
        set { showNotRunning = value; this.OnPropertyChanged("OutcomeScenarios"); }
    }

    [InfoOrWarningEditor(false, null, true)]
    [PropertyClassification(new string[] { "Settings" }, "Guest Execution Info", 6)]
    public string ExecutionNote
    {
        get => "Runs through the QEMU Guest Agent as SYSTEM (Windows) or root (Linux); no guest login is used. " +
               "Needs the agent running in the guest and the VM.GuestAgent.Unrestricted permission (VM.Monitor on PVE 8). " +
               "On Timeout the process keeps running in the guest.";
        set { }
    }

    public IInputMapping[] DefaultInputs => new IInputMapping[]
    {
        new IgnoreInputMapping { InputDataName = "Node" },
    }.Concat(StepDefaultInputs()).ToArray();

    public DataDescription[] InputData => new[]
    {
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(string)), "Hostname") { Categories = new string[] { "Connection" } },
        useApiToken
            ? new DataDescription((DecisionsType)new DecisionsNativeType(typeof(ApiTokenCredentials)), "Credentials") { Categories = new string[] { "Connection" } }
            : new DataDescription((DecisionsType)new DecisionsNativeType(typeof(UsernamePasswordCredentials)), "Credentials") { Categories = new string[] { "Connection" } },
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(string)), "Node"),
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(int)), "VM ID"),
    }.Concat(StepInputData()).ToArray();

    public override OutcomeScenarioData[] OutcomeScenarios
    {
        get
        {
            var done = new List<DataDescription> { new DataDescription(typeof(int), "PID") };
            if (waitForExecution)
            {
                done.Add(new DataDescription(typeof(int), "Exit Code"));
                if (captureOutput)
                {
                    done.Add(new DataDescription(typeof(string), "Output"));
                    done.Add(new DataDescription(typeof(string), "Error Output"));
                    done.Add(new DataDescription(typeof(bool), "Output Truncated"));
                }
            }

            var outcomes = new List<OutcomeScenarioData> { new OutcomeScenarioData("Done", done.ToArray()) };
            if (showNotRunning) outcomes.Add(new OutcomeScenarioData("Not Running"));
            if (waitForExecution) outcomes.Add(new OutcomeScenarioData("Timeout", new DataDescription(typeof(int), "PID")));
            outcomes.Add(new OutcomeScenarioData("Error", new DataDescription(typeof(string), "Error Message")));
            return outcomes.ToArray();
        }
    }

    protected abstract IEnumerable<IInputMapping> StepDefaultInputs();

    protected abstract IEnumerable<DataDescription> StepInputData();

    // The program + arguments to run, and optional stdin. Throw for invalid input.
    protected abstract (IEnumerable<string> command, string? inputData) BuildCommand(StepStartData data);

    public ResultData Run(StepStartData data)
    {
        var hostname = data.Data["Hostname"] as string;
        var credentials = data.Data["Credentials"];
        var node = data.Data["Node"] as string;
        var vmId = (int)data.Data["VM ID"];

        try
        {
            var (command, inputData) = BuildCommand(data);

            using var client = new ProxmoxClient(hostname!, port, ignoreSSLErrors);
            client.Authenticate(credentials);
            if (string.IsNullOrEmpty(node)) node = client.FindNodeForVM(vmId);

            var notRunning = NotRunningReason(client, node!, vmId);
            if (notRunning != null)
            {
                return showNotRunning
                    ? new ResultData("Not Running")
                    : new ResultData("Error", new Dictionary<string, object> { { "Error Message", notRunning } });
            }

            var pid = client.GuestExec(node!, vmId, command, inputData);
            if (!waitForExecution)
                return new ResultData("Done", new Dictionary<string, object> { { "PID", pid } });

            var result = client.WaitForGuestExec(node!, vmId, pid, timeoutSeconds);
            if (result == null)
                return new ResultData("Timeout", new Dictionary<string, object> { { "PID", pid } });

            var outputs = new Dictionary<string, object> { { "PID", pid }, { "Exit Code", result.ExitCode } };
            if (captureOutput)
            {
                outputs["Output"] = result.Output;
                outputs["Error Output"] = result.ErrorOutput;
                outputs["Output Truncated"] = result.Truncated;
            }
            return new ResultData("Done", outputs);
        }
        catch (Exception e)
        {
            return new ResultData("Error", new Dictionary<string, object> { { "Error Message", e.ToString() } });
        }
    }

    // Null when the guest can run commands, otherwise why not
    private static string? NotRunningReason(ProxmoxClient client, string node, int vmId)
    {
        var status = client.Get($"/nodes/{node}/qemu/{vmId}/status/current");
        if (ProxmoxJson.Str(status, "status") != "running")
            return $"VM {vmId} is not running.";
        if (!ProxmoxVMConfig.AgentEnabled(client.Get($"/nodes/{node}/qemu/{vmId}/config")))
            return $"The QEMU Guest Agent is not enabled on VM {vmId}.";
        if (!client.GuestAgentResponds(node, vmId))
            return $"The QEMU Guest Agent on VM {vmId} is not responding (not installed, not started yet, or the VM is paused).";
        return null;
    }
}
