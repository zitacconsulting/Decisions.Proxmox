using System.Text.Json;
using System.Text.RegularExpressions;
using DecisionsFramework.Design.Flow;
using DecisionsFramework.Design.Properties;
using DecisionsFramework.Design.ConfigurationStorage.Attributes;
using DecisionsFramework.Design.Flow.Mapping;
using DecisionsFramework.Design.Flow.CoreSteps;
using DecisionsFramework.Design.Flow.Mapping.InputImpl;

namespace Zitac.Proxmox.Steps;

[AutoRegisterStep("Get VM Disks", "Integration", "Proxmox", "VMs")]
[Writable]
public class GetVMDisks : BaseFlowAwareStep, ISyncStep, IDataConsumer, IDataProducer, IDefaultInputMappingStep
{
    private static readonly Regex DiskKey = new(@"^(scsi|ide|virtio|sata)\d+$", RegexOptions.Compiled);

    [WritableValue] private bool ignoreSSLErrors;
    [WritableValue] private int port = 8006;
    [WritableValue] private bool useApiToken;

    [PropertyClassification(0, "Use API Token", new string[] { "Authentication" })]
    public bool UseApiToken { get { return useApiToken; } set { useApiToken = value; this.OnPropertyChanged("InputData"); } }

    [PropertyClassification(0, "Ignore SSL Errors", new string[] { "Settings" })]
    public bool IgnoreSSLErrors { get { return ignoreSSLErrors; } set { ignoreSSLErrors = value; } }

    [PropertyClassification(1, "Port", new string[] { "Settings" })]
    public int Port { get { return port; } set { port = value; } }

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

    public override OutcomeScenarioData[] OutcomeScenarios => new[]
    {
        new OutcomeScenarioData("Done", new DataDescription(typeof(ProxmoxDisk), "Disks", true)),
        new OutcomeScenarioData("Error", new DataDescription(typeof(string), "Error Message")),
    };

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

            var config = client.Get($"/nodes/{node}/qemu/{vmId}/config");
            var disks = new List<ProxmoxDisk>();
            foreach (var prop in config.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.String) continue;
                if (!DiskKey.IsMatch(prop.Name)) continue;
                var raw = prop.Value.GetString()!;
                if (raw.Contains("media=cdrom")) continue;
                var disk = ProxmoxDisk.Parse(prop.Name, raw);
                if (disk != null) disks.Add(disk);
            }
            return new ResultData("Done", new Dictionary<string, object> { { "Disks", disks.ToArray() } });
        }
        catch (Exception e)
        {
            return new ResultData("Error", new Dictionary<string, object> { { "Error Message", e.ToString() } });
        }
    }
}
