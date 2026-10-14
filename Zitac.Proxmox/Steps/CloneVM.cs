using DecisionsFramework.Design.Flow;
using DecisionsFramework.Design.Properties;
using DecisionsFramework.Design.Properties.Attributes;
using DecisionsFramework.Design.ConfigurationStorage.Attributes;
using DecisionsFramework.Design.Flow.Mapping;
using DecisionsFramework.Design.Flow.CoreSteps;
using DecisionsFramework.Design.Flow.Mapping.InputImpl;

namespace Zitac.Proxmox.Steps;

[CategoryClassification(StepCategories.AuthenticationSortKey, StepCategories.Authentication)]
[CategoryClassification(StepCategories.SettingsSortKey, StepCategories.Settings)]
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

    // Read-only help texts, shown next to the inputs they describe
    [InfoOrWarningEditor(false, null, true)]
    [PropertyClassification(new string[] { VMHardwareInputs.CpuCategory }, "CPU Info", 0)]
    public string CpuNote { get => VMHardwareInputs.CloneCpuNote; set { } }

    [InfoOrWarningEditor(false, null, true)]
    [PropertyClassification(new string[] { VMHardwareInputs.MemoryCategory }, "Memory Info", 0)]
    public string MemoryNote { get => VMHardwareInputs.CloneMemoryNote; set { } }

    [InfoOrWarningEditor(false, null, true)]
    [PropertyClassification(new string[] { VMHardwareInputs.MachineCategory }, "Machine Type Info", 0)]
    public string MachineNote { get => VMHardwareInputs.MachineNote; set { } }

    [InfoOrWarningEditor(false, VMHardwareInputs.ProxmoxApiViewer, true)]
    [PropertyClassification(new string[] { VMHardwareInputs.ExtraConfigCategory }, "Extra Config Info", 0)]
    public string ExtraConfigNote { get => VMHardwareInputs.ExtraConfigNote; set { } }

    [InfoOrWarningEditor(false, null, true)]
    [PropertyClassification(new string[] { VMHardwareInputs.HardwareChangesCategory }, "Applying Changes Info", 0)]
    public string ApplyNote { get => VMHardwareInputs.CloneApplyNote; set { } }

    public IInputMapping[] DefaultInputs => new IInputMapping[]
    {
        new IgnoreInputMapping { InputDataName = "Node" },
        new IgnoreInputMapping { InputDataName = "New VM ID" },
        new IgnoreInputMapping { InputDataName = "Target Node" },
        new IgnoreInputMapping { InputDataName = "Target Storage" },
    }.Concat(VMHardwareInputs.CloneDefaultInputs()).ToArray();

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
    }.Concat(VMHardwareInputs.InputData()).ToArray();

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

            // Proxmox requires newid, so pick the next free ID when none is given
            if (newVmId <= 0)
                newVmId = int.Parse(client.Get("/cluster/nextid").GetString()!);

            var postData = new Dictionary<string, string>
            {
                { "newid", newVmId.ToString() },
                { "full", fullClone ? "1" : "0" },
            };
            if (!string.IsNullOrEmpty(newVmName)) postData["name"] = newVmName;
            if (!string.IsNullOrEmpty(targetNode)) postData["target"] = targetNode;
            if (!string.IsNullOrEmpty(targetStorage)) postData["storage"] = targetStorage;

            // Hardware changes are applied to the clone afterwards. Unset inputs and fields keep
            // the source VM's values (no GUI default is forced here, unlike Create VM).
            var cpuSettings = VMHardwareInputs.Cpu(data);
            var config = cpuSettings?.ToConfig(isCreate: false) ?? new Dictionary<string, string>();

            // A CPU type/flags change rewrites the whole "cpu" line; it's rebuilt from the
            // clone's current line below so its other CPU options are kept
            config.Remove("cpu");

            foreach (var kv in VMHardwareInputs.Memory(data)?.ToConfig() ?? new Dictionary<string, string>())
                config[kv.Key] = kv.Value;

            var machine = VMHardwareInputs.Machine(data);
            if (machine != null) config["machine"] = machine;
            VMHardwareInputs.ApplyExtraConfig(data, config);

            // A "cpu" key in Extra Config wins over the CPU input
            var mergeCpu = cpuSettings?.ChangesCpu == true && !config.ContainsKey("cpu");
            var updateConfig = config.Count > 0 || mergeCpu;

            var upid = client.Post($"/nodes/{node}/qemu/{vmId}/clone", postData);
            var warnings = Array.Empty<string>();

            // The clone is locked until its task ends, so config changes force a wait
            if ((waitForTask || updateConfig) && !string.IsNullOrEmpty(upid))
                warnings = client.WaitForTask(node!, upid, taskTimeoutSeconds);

            if (updateConfig)
            {
                var cloneNode = string.IsNullOrEmpty(targetNode) ? node : targetNode;
                if (mergeCpu)
                {
                    var current = client.Get($"/nodes/{cloneNode}/qemu/{newVmId}/config");
                    config["cpu"] = cpuSettings!.ToConfig(isCreate: false, ProxmoxJson.Str(current, "cpu"))["cpu"];
                }
                client.Put($"/nodes/{cloneNode}/qemu/{newVmId}/config", config);
            }

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
