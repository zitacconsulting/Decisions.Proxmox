using DecisionsFramework.Design.Flow;
using DecisionsFramework.Design.Properties;
using DecisionsFramework.Design.ConfigurationStorage.Attributes;
using DecisionsFramework.Design.Flow.Mapping;
using DecisionsFramework.Design.Flow.CoreSteps;
using DecisionsFramework.Design.Flow.Mapping.InputImpl;

namespace Zitac.Proxmox.Steps;

[AutoRegisterStep("Remove VM Disk", "Integration", "Proxmox", "VMs")]
[Writable]
public class RemoveVMDisk : BaseFlowAwareStep, ISyncStep, IDataConsumer, IDataProducer, IDefaultInputMappingStep
{
    [WritableValue] private bool ignoreSSLErrors;
    [WritableValue] private int port = 8006;
    [WritableValue] private bool useApiToken;
    [WritableValue] private bool deleteDiskData;

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

    [PropertyClassification(2, "Delete Disk Data", new string[] { "Settings" })]
    public bool DeleteDiskData
    {
        get { return deleteDiskData; }
        set { deleteDiskData = value; this.OnPropertyChanged("InputData"); }
    }

    public IInputMapping[] DefaultInputs => new IInputMapping[]
    {
        new IgnoreInputMapping { InputDataName = "Node" },
    };

    public DataDescription[] InputData
    {
        get
        {
            var inputs = new List<DataDescription>
            {
                new DataDescription((DecisionsType)new DecisionsNativeType(typeof(string)), "Hostname") { Categories = new string[] { "Connection" } },
                useApiToken
                    ? new DataDescription((DecisionsType)new DecisionsNativeType(typeof(ApiTokenCredentials)), "Credentials") { Categories = new string[] { "Connection" } }
                    : new DataDescription((DecisionsType)new DecisionsNativeType(typeof(UsernamePasswordCredentials)), "Credentials") { Categories = new string[] { "Connection" } },
                new DataDescription((DecisionsType)new DecisionsNativeType(typeof(string)), "Node"),
                new DataDescription((DecisionsType)new DecisionsNativeType(typeof(int)), "VM ID"),
                new DataDescription((DecisionsType)new DecisionsNativeType(typeof(string)), "Interface"),
            };

            if (deleteDiskData)
            {
                inputs.Add(new DataDescription((DecisionsType)new DecisionsNativeType(typeof(string)), "Storage"));
                inputs.Add(new DataDescription((DecisionsType)new DecisionsNativeType(typeof(string)), "Volume Name"));
            }

            return inputs.ToArray();
        }
    }

    public override OutcomeScenarioData[] OutcomeScenarios => new[]
    {
        new OutcomeScenarioData("Done"),
        new OutcomeScenarioData("Error", new DataDescription(typeof(string), "Error Message")),
    };

    public ResultData Run(StepStartData data)
    {
        var hostname = data.Data["Hostname"] as string;
        var credentials = data.Data["Credentials"];
        var node = data.Data["Node"] as string;
        var vmId = (int)data.Data["VM ID"];
        var iface = data.Data["Interface"] as string;

        try
        {
            using var client = new ProxmoxClient(hostname!, port, ignoreSSLErrors);
            client.Authenticate(credentials);
            if (string.IsNullOrEmpty(node)) node = client.FindNodeForVM(vmId);

            client.Put($"/nodes/{node}/qemu/{vmId}/config", new Dictionary<string, string>
            {
                { "delete", iface! }
            });

            if (deleteDiskData)
            {
                var storage = data.Data["Storage"] as string;
                var volumeName = data.Data["Volume Name"] as string;
                client.Delete($"/nodes/{node}/storage/{storage}/content/{storage}:{volumeName}");
            }

            return new ResultData("Done");
        }
        catch (Exception e)
        {
            return new ResultData("Error", new Dictionary<string, object> { { "Error Message", e.ToString() } });
        }
    }
}
