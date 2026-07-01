using DecisionsFramework.Design.Flow;
using DecisionsFramework.Design.Properties;
using DecisionsFramework.Design.ConfigurationStorage.Attributes;
using DecisionsFramework.Design.Flow.Mapping;
using DecisionsFramework.Design.Flow.CoreSteps;
using DecisionsFramework.Design.Flow.Mapping.InputImpl;

namespace Zitac.Proxmox.Steps;

[AutoRegisterStep("Get All Network Bridges", "Integration", "Proxmox", "Network")]
[Writable]
public class GetAllNetworkBridges : BaseFlowAwareStep, ISyncStep, IDataConsumer, IDataProducer, IDefaultInputMappingStep
{
    [WritableValue] private bool ignoreSSLErrors;
    [WritableValue] private int port = 8006;
    [WritableValue] private bool showOutcomeForNoResults;
    [WritableValue] private bool useApiToken;

    [PropertyClassification(0, "Use API Token", new string[] { "Authentication" })]
    public bool UseApiToken { get { return useApiToken; } set { useApiToken = value; this.OnPropertyChanged("InputData"); } }

    [PropertyClassification(0, "Ignore SSL Errors", new string[] { "Settings" })]
    public bool IgnoreSSLErrors { get { return ignoreSSLErrors; } set { ignoreSSLErrors = value; } }

    [PropertyClassification(1, "Port", new string[] { "Settings" })]
    public int Port { get { return port; } set { port = value; } }

    [PropertyClassification(2, "Show Outcome for No Results", new string[] { "Outcomes" })]
    public bool ShowOutcomeForNoResults { get { return showOutcomeForNoResults; } set { showOutcomeForNoResults = value; this.OnPropertyChanged("OutcomeScenarios"); } }

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
    };

    public override OutcomeScenarioData[] OutcomeScenarios
    {
        get
        {
            var outcomes = new List<OutcomeScenarioData>
            {
                new OutcomeScenarioData("Done", new DataDescription(typeof(ProxmoxNetworkBridge), "Bridges", true))
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

            var nodeNames = new List<string>();
            if (!string.IsNullOrEmpty(node)) nodeNames.Add(node);
            else
            {
                var nodes = client.Get("/nodes");
                foreach (var n in nodes.EnumerateArray())
                    nodeNames.Add(n.GetProperty("node").GetString()!);
            }

            var bridges = new List<ProxmoxNetworkBridge>();
            foreach (var nodeName in nodeNames)
            {
                var network = client.Get($"/nodes/{nodeName}/network?type=bridge");
                foreach (var el in network.EnumerateArray())
                    bridges.Add(ProxmoxNetworkBridge.FromJson(el, nodeName));
            }

            if (bridges.Count == 0 && showOutcomeForNoResults)
                return new ResultData("No Results");

            return new ResultData("Done", new Dictionary<string, object> { { "Bridges", bridges.ToArray() } });
        }
        catch (Exception e)
        {
            return new ResultData("Error", new Dictionary<string, object> { { "Error Message", e.ToString() } });
        }
    }
}
