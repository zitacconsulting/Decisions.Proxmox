using DecisionsFramework.Design.Flow;
using DecisionsFramework.Design.Properties;
using DecisionsFramework.Design.ConfigurationStorage.Attributes;
using DecisionsFramework.Design.Flow.Mapping;
using DecisionsFramework.Design.Flow.CoreSteps;
using DecisionsFramework.Design.Flow.Mapping.InputImpl;

namespace Zitac.Proxmox.Steps;

[AutoRegisterStep("Get VM By ID", "Integration", "Proxmox", "VMs")]
[Writable]
public class GetVMByID : BaseFlowAwareStep, ISyncStep, IDataConsumer, IDataProducer, IDefaultInputMappingStep
{
    [WritableValue]
    private bool ignoreSSLErrors;

    [WritableValue]
    private int port = 8006;

    [WritableValue]
    private bool fetchExtendedInfo;

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

    [PropertyClassification(2, "Fetch Extended Info", new string[] { "Settings" })]
    public bool FetchExtendedInfo
    {
        get { return fetchExtendedInfo; }
        set { fetchExtendedInfo = value; this.OnPropertyChanged("OutcomeScenarios"); }
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
            var vmType = fetchExtendedInfo ? typeof(ProxmoxVMExtended) : typeof(ProxmoxVM);
            return new[]
            {
                new OutcomeScenarioData("Done", new DataDescription(vmType, "VM", false)),
                new OutcomeScenarioData("Not Found"),
                new OutcomeScenarioData("Error", new DataDescription(typeof(string), "Error Message")),
            };
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

            if (string.IsNullOrEmpty(node))
            {
                node = client.TryFindNodeForVM(vmId);
                if (node == null) return new ResultData("Not Found");
            }

            try
            {
                var vm = BuildVM(client, node, vmId);
                return new ResultData("Done", new Dictionary<string, object> { { "VM", vm } });
            }
            catch
            {
                return new ResultData("Not Found");
            }
        }
        catch (Exception e)
        {
            return new ResultData("Error", new Dictionary<string, object> { { "Error Message", e.ToString() } });
        }
    }

    private ProxmoxVM BuildVM(ProxmoxClient client, string node, int vmId)
    {
        var status = client.Get($"/nodes/{node}/qemu/{vmId}/status/current");
        if (fetchExtendedInfo)
        {
            var vm = ProxmoxVMExtended.FromJson(status, node);
            vm.ApplyConfig(client.Get($"/nodes/{node}/qemu/{vmId}/config"));
            return vm;
        }
        return ProxmoxVM.FromJson(status, node);
    }
}
