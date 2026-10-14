using DecisionsFramework.Design.Flow;
using DecisionsFramework.Design.Properties;
using DecisionsFramework.Design.ConfigurationStorage.Attributes;
using DecisionsFramework.Design.Flow.Mapping;
using DecisionsFramework.Design.Flow.CoreSteps;
using DecisionsFramework.Design.Flow.Mapping.InputImpl;

namespace Zitac.Proxmox.Steps;

[CategoryClassification(StepCategories.AuthenticationSortKey, StepCategories.Authentication)]
[CategoryClassification(StepCategories.SettingsSortKey, StepCategories.Settings)]
[AutoRegisterStep("Get All VMs", "Integration", "Proxmox", "VMs")]
[Writable]
public class GetAllVMs : BaseFlowAwareStep, ISyncStep, IDataConsumer, IDataProducer, IDefaultInputMappingStep
{
    [WritableValue]
    private bool ignoreSSLErrors;

    [WritableValue]
    private int port = 8006;

    [WritableValue]
    private bool showOutcomeForNoResults;

    [WritableValue]
    private bool fetchExtendedInfo;

    [WritableValue]
    private bool useApiToken;

    [WritableValue]
    private bool excludeTemplates;

    [PropertyClassification(0, "Ignore SSL Errors", new string[] { "Settings" })]
    public bool IgnoreSSLErrors { get { return ignoreSSLErrors; } set { ignoreSSLErrors = value; } }

    [PropertyClassification(1, "Port", new string[] { "Settings" })]
    public int Port { get { return port; } set { port = value; } }

    [PropertyClassification(2, "Show Outcome for No Results", new string[] { "Outcomes" })]
    public bool ShowOutcomeForNoResults
    {
        get { return showOutcomeForNoResults; }
        set { showOutcomeForNoResults = value; this.OnPropertyChanged("OutcomeScenarios"); }
    }

    // Off: one API call for the whole cluster. On: three or more calls per VM (status, config, guest agent).
    [PropertyClassification(3, "Fetch Extended Info", new string[] { "Settings" })]
    public bool FetchExtendedInfo { get { return fetchExtendedInfo; } set { fetchExtendedInfo = value; } }

    [PropertyClassification(4, "Exclude Templates", new string[] { "Settings" })]
    public bool ExcludeTemplates { get { return excludeTemplates; } set { excludeTemplates = value; } }

    [PropertyClassification(0, "Use API Token", new string[] { "Authentication" })]
    public bool UseApiToken
    {
        get { return useApiToken; }
        set { useApiToken = value; this.OnPropertyChanged("InputData"); }
    }

    public IInputMapping[] DefaultInputs => new IInputMapping[]
    {
        new IgnoreInputMapping { InputDataName = "Node" }
    };

    public DataDescription[] InputData
    {
        get
        {
            return new[]
            {
                new DataDescription((DecisionsType)new DecisionsNativeType(typeof(string)), "Hostname") { Categories = new string[] { "Connection" } },
                useApiToken
                    ? new DataDescription((DecisionsType)new DecisionsNativeType(typeof(ApiTokenCredentials)), "Credentials") { Categories = new string[] { "Connection" } }
                    : new DataDescription((DecisionsType)new DecisionsNativeType(typeof(UsernamePasswordCredentials)), "Credentials") { Categories = new string[] { "Connection" } },
                new DataDescription((DecisionsType)new DecisionsNativeType(typeof(string)), "Node"),
            };
        }
    }

    public override OutcomeScenarioData[] OutcomeScenarios
    {
        get
        {
            var outcomes = new List<OutcomeScenarioData>
            {
                new OutcomeScenarioData("Done", new DataDescription(typeof(ProxmoxVM), "VMs", true))
            };
            if (showOutcomeForNoResults) outcomes.Add(new OutcomeScenarioData("No Results"));
            outcomes.Add(new OutcomeScenarioData("Error", new DataDescription(typeof(string), "Error Message")));
            return outcomes.ToArray();
        }
    }

    public ResultData Run(StepStartData data)
    {
        var hostname = data.Data["Hostname"] as string;
        var credentials = data.Data["Credentials"];
        var node = data.Data["Node"] as string;

        try
        {
            using var client = new ProxmoxClient(hostname!, port, ignoreSSLErrors);
            client.Authenticate(credentials);

            var vms = client.GetClusterGuests("qemu")
                .Where(res => string.IsNullOrEmpty(node) || string.Equals(ProxmoxJson.Str(res, "node"), node, StringComparison.OrdinalIgnoreCase))
                .Select(ProxmoxVM.FromResource)
                .Where(vm => !excludeTemplates || !vm.Template)
                .OrderBy(vm => vm.VMID)
                .ToList();

            // VMs on an offline node report status "unknown" and their config can't be read; keep the summary
            if (fetchExtendedInfo)
                vms = vms.Select(vm => vm.PowerState == PowerState.Unknown ? vm : ProxmoxVM.Load(client, vm.Node!, vm.VMID)).ToList();

            if (vms.Count == 0 && showOutcomeForNoResults)
                return new ResultData("No Results");

            return new ResultData("Done", new Dictionary<string, object> { { "VMs", vms.ToArray() } });
        }
        catch (Exception e)
        {
            return new ResultData("Error", new Dictionary<string, object> { { "Error Message", e.ToString() } });
        }
    }
}
