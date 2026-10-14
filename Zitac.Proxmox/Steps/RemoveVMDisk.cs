using DecisionsFramework.Design.Flow;
using DecisionsFramework.Design.Properties;
using DecisionsFramework.Design.ConfigurationStorage.Attributes;
using DecisionsFramework.Design.Flow.Mapping;
using DecisionsFramework.Design.Flow.CoreSteps;
using DecisionsFramework.Design.Flow.Mapping.InputImpl;

namespace Zitac.Proxmox.Steps;

[CategoryClassification(StepCategories.AuthenticationSortKey, StepCategories.Authentication)]
[CategoryClassification(StepCategories.SettingsSortKey, StepCategories.Settings)]
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

    // Permanently deletes the volume, the same way removing an unused disk does in the Proxmox GUI
    [PropertyClassification(2, "Delete Disk Data", new string[] { "Settings" })]
    public bool DeleteDiskData
    {
        get { return deleteDiskData; }
        set { deleteDiskData = value; this.OnPropertyChanged("OutcomeScenarios"); }
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
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(string)), "Interface"),
    };

    public override OutcomeScenarioData[] OutcomeScenarios
    {
        get
        {
            var done = new List<DataDescription> { new DataDescription(typeof(bool), "Pending Restart") };
            if (deleteDiskData) done.Add(new DataDescription(typeof(bool), "Disk Data Deleted"));
            return new[]
            {
                new OutcomeScenarioData("Done", done.ToArray()),
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
        var iface = (data.Data["Interface"] as string)?.Trim().ToLowerInvariant();

        try
        {
            using var client = new ProxmoxClient(hostname!, port, ignoreSSLErrors);
            client.Authenticate(credentials);
            if (string.IsNullOrEmpty(node)) node = client.FindNodeForVM(vmId);

            if (string.IsNullOrEmpty(iface))
                return new ResultData("Not Found");

            // A disk detached earlier (e.g. after a pending removal was applied) is listed as unusedN.
            // Removing that entry is what deletes the volume, so it's only allowed with Delete Disk Data.
            if (System.Text.RegularExpressions.Regex.IsMatch(iface, @"^unused\d+$"))
            {
                if (!deleteDiskData)
                    return Error($"Removing {iface} permanently deletes its data. Turn on Delete Disk Data to do that.");
                var unused = client.GetPendingEntry(node!, vmId, iface)?.CurrentValue;
                if (unused == null)
                    return new ResultData("Not Found");
                return new ResultData("Done", new Dictionary<string, object>
                {
                    { "Pending Restart", false },
                    { "Disk Data Deleted", DeleteDetachedVolume(client, node!, vmId, ProxmoxVMConfig.VolumeRef(unused)) },
                });
            }

            if (!ProxmoxVMConfig.IsDataDiskInterface(iface))
                return Error($"{iface} is not a data disk (scsi, virtio, sata or ide). EFI disk and TPM state can't be removed with this step.");

            // Current and pending values: a removal waiting for a restart isn't "not found"
            var entry = client.GetPendingEntry(node!, vmId, iface);
            var drive = entry?.CurrentValue ?? entry?.PendingValue;
            if (entry == null || drive == null)
                return new ResultData("Not Found");
            if (ProxmoxVMConfig.IsCDROM(drive))
                return Error($"{iface} is a CD drive. Use Remove CD-ROM instead.");

            if (!entry.PendingDelete)
            {
                client.Put($"/nodes/{node}/qemu/{vmId}/config", new Dictionary<string, string>
                {
                    { "delete", iface }
                });
            }

            // Disks that can't be hot-removed from a running VM stay attached until it's stopped and started
            var pendingRestart = client.GetPendingEntry(node!, vmId, iface)?.CurrentValue != null;
            var outputs = new Dictionary<string, object> { { "Pending Restart", pendingRestart } };

            if (deleteDiskData)
                outputs["Disk Data Deleted"] = !pendingRestart && DeleteDetachedVolume(client, node!, vmId, ProxmoxVMConfig.VolumeRef(drive));

            return new ResultData("Done", outputs);
        }
        catch (Exception e)
        {
            return new ResultData("Error", new Dictionary<string, object> { { "Error Message", e.ToString() } });
        }
    }

    // Never runs while the disk is still attached. Deleting the unusedN entry Proxmox created for it
    // removes the volume and the config entry together. Passthrough devices (no volume) are skipped.
    private static bool DeleteDetachedVolume(ProxmoxClient client, string node, int vmId, string? volumeRef)
    {
        if (volumeRef == null)
            return false;

        var config = client.Get($"/nodes/{node}/qemu/{vmId}/config?current=1");
        var unusedKey = ProxmoxVMConfig.UnusedKeyFor(config, volumeRef);
        if (unusedKey != null)
        {
            client.Put($"/nodes/{node}/qemu/{vmId}/config", new Dictionary<string, string> { { "delete", unusedKey } });

            // Only report it deleted once the entry is really gone, not just marked for deletion
            return client.GetPendingEntry(node, vmId, unusedKey)?.CurrentValue == null;
        }
        else
        {
            // Not kept as unused (shouldn't normally happen); the volume is detached, so delete it directly
            var storage = volumeRef[..volumeRef.IndexOf(':')];
            client.Delete($"/nodes/{node}/storage/{storage}/content/{Uri.EscapeDataString(volumeRef)}");
        }
        return true;
    }

    private static ResultData Error(string message) =>
        new ResultData("Error", new Dictionary<string, object> { { "Error Message", message } });
}
