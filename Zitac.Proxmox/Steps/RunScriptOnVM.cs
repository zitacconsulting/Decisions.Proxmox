using System.Text;
using DecisionsFramework.Design.Flow;
using DecisionsFramework.Design.Properties;
using DecisionsFramework.Design.Properties.Attributes;
using DecisionsFramework.Design.ConfigurationStorage.Attributes;
using DecisionsFramework.Design.Flow.Mapping;
using DecisionsFramework.Design.Flow.Mapping.InputImpl;

namespace Zitac.Proxmox.Steps;

// Runs a script in the guest through the QEMU Guest Agent, like the VMware module's
// Run PowerShell Script On VM but without guest credentials (runs as SYSTEM / root).
[AutoRegisterStep("Run Script On VM", "Integration", "Proxmox", "VMs")]
[Writable]
public class RunScriptOnVM : GuestExecStepBase
{
    // Windows' command line limit is 32,767 characters; leave room for the PowerShell arguments
    private const int MaxEncodedLength = 32000;

    [InfoOrWarningEditor(false, null, true)]
    [PropertyClassification(new string[] { "Inputs" }, "Shell Info", 0)]
    public string ShellNote
    {
        get => "PowerShell: sent with -EncodedCommand, so quotes and line breaks are kept (about 12,000 characters max). " +
               "Cmd: written to a temporary .cmd file (with @echo off) and run, via PowerShell (about 9,000 characters max). " +
               "Bash / Sh: passed on stdin, no length limit. The exit code is the script's own (exit N / exit /b N).";
        set { }
    }

    protected override IEnumerable<IInputMapping> StepDefaultInputs() => new IInputMapping[]
    {
        new ConstantInputMapping { InputDataName = "Shell", Value = GuestShell.PowerShell },
    };

    protected override IEnumerable<DataDescription> StepInputData() => new[]
    {
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(GuestShell)), "Shell"),
        new DataDescription((DecisionsType)new DecisionsNativeType(typeof(string)), "Script"),
    };

    protected override (IEnumerable<string> command, string? inputData) BuildCommand(StepStartData data)
    {
        var shell = (GuestShell)(data.Data["Shell"] ?? GuestShell.PowerShell);
        var script = data.Data["Script"] as string;
        if (string.IsNullOrWhiteSpace(script))
            throw new ArgumentException("Script is empty.");

        return shell switch
        {
            GuestShell.Cmd => (PowerShell(CmdWrapper(script)), null),
            GuestShell.Bash => (new[] { "/bin/bash", "-s" }, ToUnixLineEndings(script)),
            GuestShell.Sh => (new[] { "/bin/sh", "-s" }, ToUnixLineEndings(script)),
            _ => (PowerShell(script), null),
        };
    }

    private static string[] PowerShell(string script)
    {
        // Progress bars would otherwise end up in stderr as CLIXML
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes("$ProgressPreference = 'SilentlyContinue'\n" + script));
        if (encoded.Length > MaxEncodedLength)
            throw new ArgumentException("The script is too long to pass on the Windows command line. Shorten it, or split it into several steps.");
        return new[] { "powershell.exe", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", encoded };
    }

    // cmd.exe can't take a multi-line script on its command line, so PowerShell writes it to a
    // temporary .cmd file, runs it and passes its exit code on. The script travels as base64 so
    // no quoting can break it.
    private static string CmdWrapper(string script)
    {
        var batch = "@echo off\r\n" + script.Replace("\r\n", "\n").Replace("\n", "\r\n");
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(batch));
        return
            "$f = Join-Path $env:TEMP ('decisions-' + [guid]::NewGuid() + '.cmd')\n" +
            $"[IO.File]::WriteAllText($f, [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{encoded}')), [Text.Encoding]::Default)\n" +
            "try { & cmd.exe /d /c $f; $code = $LASTEXITCODE } finally { Remove-Item $f -Force -ErrorAction SilentlyContinue }\n" +
            "exit $code";
    }

    // A script pasted from Windows would otherwise fail on the \r
    private static string ToUnixLineEndings(string script) => script.Replace("\r\n", "\n");
}
