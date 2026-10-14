using DecisionsFramework.Design.Flow;
using DecisionsFramework.Design.Properties;
using DecisionsFramework.Design.ConfigurationStorage.Attributes;
using DecisionsFramework.Design.Flow.Mapping;
using DecisionsFramework.Design.Flow.CoreSteps;
using DecisionsFramework.Design.Flow.Mapping.InputImpl;

namespace Zitac.Proxmox.Steps;

[CategoryClassification(StepCategories.AuthenticationSortKey, StepCategories.Authentication)]
[CategoryClassification(StepCategories.SettingsSortKey, StepCategories.Settings)]
// Deletes the CD drive itself. Use Unmount ISO to just eject the ISO.
[AutoRegisterStep("Remove CD-ROM", "Integration", "Proxmox", "VMs")]
[Writable]
public class RemoveCDROM : BaseFlowAwareStep, ISyncStep, IDataConsumer, IDataProducer, IDefaultInputMappingStep
{
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
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(string)), "Interface"),
    };

    public override OutcomeScenarioData[] OutcomeScenarios => new[]
    {
        new OutcomeScenarioData("Done", new DataDescription(typeof(bool), "Pending Restart")),
        new OutcomeScenarioData("Not Found"),
        new OutcomeScenarioData("Error", new DataDescription(typeof(string), "Error Message")),
    };

    public ResultData Run(StepStartData data)
    {
        var hostname = data.Data["Hostname"] as string;
        var credentials = data.Data["Credentials"];
        var node = data.Data["Node"] as string;
        var vmId = (int)data.Data["VM ID"];
        var iface = (data.Data["Interface"] as string)?.Trim().ToLowerInvariant();

        try
        {
            using var client = new ProxmoxClient(hostname!, port, ignoreSSLErrors);
            client.Authenticate(credentials);
            if (string.IsNullOrEmpty(node)) node = client.FindNodeForVM(vmId);

            if (string.IsNullOrEmpty(iface))
                return new ResultData("Not Found");

            // Read current and pending values: on a running VM an earlier removal may still be
            // waiting for a restart, and then counts as done rather than not found
            var entry = client.GetPendingEntry(node!, vmId, iface);
            var drive = entry?.CurrentValue ?? entry?.PendingValue;
            if (entry == null || drive == null)
                return new ResultData("Not Found");
            if (!ProxmoxVMConfig.IsCDROM(drive))
                return new ResultData("Error", new Dictionary<string, object> { { "Error Message", $"{iface} is a disk, not a CD drive." } });

            if (!entry.PendingDelete)
            {
                client.Put($"/nodes/{node}/qemu/{vmId}/config", new Dictionary<string, string>
                {
                    { "delete", iface }
                });
            }

            // On a running VM Proxmox keeps the removal pending until the VM is stopped and started
            var pendingRestart = client.GetPendingEntry(node!, vmId, iface)?.HasPendingChange == true;
            return new ResultData("Done", new Dictionary<string, object> { { "Pending Restart", pendingRestart } });
        }
        catch (Exception e)
        {
            return new ResultData("Error", new Dictionary<string, object> { { "Error Message", e.ToString() } });
        }
    }
}
