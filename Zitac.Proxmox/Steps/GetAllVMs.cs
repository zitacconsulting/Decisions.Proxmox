using DecisionsFramework.Design.Flow;
using DecisionsFramework.Design.Properties;
using DecisionsFramework.Design.ConfigurationStorage.Attributes;
using DecisionsFramework.Design.Flow.Mapping;
using DecisionsFramework.Design.Flow.CoreSteps;
using DecisionsFramework.Design.Flow.Mapping.InputImpl;

namespace Zitac.Proxmox.Steps;

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

    [PropertyClassification(3, "Fetch Extended Info", new string[] { "Settings" })]
    public bool FetchExtendedInfo
    {
        get { return fetchExtendedInfo; }
        set { fetchExtendedInfo = value; this.OnPropertyChanged("OutcomeScenarios"); }
    }

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
            var vmType = fetchExtendedInfo ? typeof(ProxmoxVMExtended) : typeof(ProxmoxVM);
            var outcomes = new List<OutcomeScenarioData>
            {
                new OutcomeScenarioData("Done", new DataDescription(vmType, "VMs", true))
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

            var vms = new List<ProxmoxVM>();

            if (string.IsNullOrEmpty(node))
            {
                var nodes = client.Get("/nodes");
                foreach (var nodeEl in nodes.EnumerateArray())
                {
                    var nodeName = nodeEl.GetProperty("node").GetString()!;
                    var nodeVms = client.Get($"/nodes/{nodeName}/qemu");
                    foreach (var vmEl in nodeVms.EnumerateArray())
                        vms.Add(BuildVM(client, vmEl, nodeName));
                }
            }
            else
            {
                var nodeVms = client.Get($"/nodes/{node}/qemu");
                foreach (var vmEl in nodeVms.EnumerateArray())
                    vms.Add(BuildVM(client, vmEl, node));
            }

            if (vms.Count == 0 && showOutcomeForNoResults)
                return new ResultData("No Results");

            return new ResultData("Done", new Dictionary<string, object> { { "VMs", vms.ToArray() } });
        }
        catch (Exception e)
        {
            return new ResultData("Error", new Dictionary<string, object> { { "Error Message", e.ToString() } });
        }
    }

    private ProxmoxVM BuildVM(ProxmoxClient client, System.Text.Json.JsonElement element, string node)
    {
        if (fetchExtendedInfo)
        {
            var vm = ProxmoxVMExtended.FromJson(element, node);
            vm.ApplyConfig(client.Get($"/nodes/{node}/qemu/{vm.VMID}/config"));
            return vm;
        }
        return ProxmoxVM.FromJson(element, node);
    }
}
