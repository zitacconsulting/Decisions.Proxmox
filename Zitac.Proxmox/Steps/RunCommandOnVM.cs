using System.Collections;
using DecisionsFramework.Design.Flow;
using DecisionsFramework.Design.Properties;
using DecisionsFramework.Design.Properties.Attributes;
using DecisionsFramework.Design.ConfigurationStorage.Attributes;
using DecisionsFramework.Design.Flow.Mapping;
using DecisionsFramework.Design.Flow.Mapping.InputImpl;

namespace Zitac.Proxmox.Steps;

// Runs a program in the guest through the QEMU Guest Agent (as SYSTEM / root)
[AutoRegisterStep("Run Command On VM", "Integration", "Proxmox", "VMs")]
[Writable]
public class RunCommandOnVM : GuestExecStepBase
{
    [InfoOrWarningEditor(false, null, true)]
    [PropertyClassification(new string[] { "Inputs" }, "Command Info", 0)]
    public string CommandNote
    {
        get => "Command is the program to start, e.g. ipconfig.exe or /usr/bin/systemctl. Each item in Arguments is passed as one " +
               "argument, so no quoting is needed (e.g. restart, nginx). No shell is involved: pipes, redirects and variables only " +
               "work through a shell, e.g. Command /bin/sh with Arguments -c, \"ls | wc -l\". Input Data is sent to the program's stdin.";
        set { }
    }

    protected override IEnumerable<IInputMapping> StepDefaultInputs() => new IInputMapping[]
    {
        new IgnoreInputMapping { InputDataName = "Arguments" },
        new IgnoreInputMapping { InputDataName = "Input Data" },
    };

    protected override IEnumerable<DataDescription> StepInputData() => new[]
    {
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(string)), "Command"),
        new DataDescription(typeof(string), "Arguments", true),
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(string)), "Input Data"),
    };

    protected override (IEnumerable<string> command, string? inputData) BuildCommand(StepStartData data)
    {
        var command = (data.Data["Command"] as string)?.Trim();
        if (string.IsNullOrEmpty(command))
            throw new ArgumentException("Command is empty.");

        // Ignored inputs may be absent or null
        var arguments = data.Data.TryGetValue("Arguments", out var a) && a is IEnumerable list && a is not string
            ? list.Cast<object?>().Where(x => x != null).Select(x => x!.ToString()!)
            : Enumerable.Empty<string>();
        var inputData = data.Data.TryGetValue("Input Data", out var i) ? i as string : null;

        return (new[] { command }.Concat(arguments).ToArray(), inputData);
    }
}
