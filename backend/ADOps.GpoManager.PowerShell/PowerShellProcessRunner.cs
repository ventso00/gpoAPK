using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using ADOps.GpoManager.PowerShell.Host;

namespace ADOps.GpoManager.PowerShell;

/// <summary>
/// Lance powershell.exe (Windows PowerShell 5.1) avec un script encodé en Base64/UTF-16,
/// capture stdout/stderr en UTF-8, applique un délai d'attente par commande,
/// et peut transiter un secret (blob DPAPI) par l'entrée standard du processus.
/// </summary>
internal static class PowerShellProcessRunner
{
    public static async Task<PowerShellExecutionResult> RunAsync(
        string script,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        string? stdinPayload = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encodedCommand,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdinPayload is not null,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using var process = new Process { StartInfo = startInfo };
        var output = new StringBuilder();
        var error = new StringBuilder();

        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                output.AppendLine(args.Data);
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                error.AppendLine(args.Data);
            }
        };

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            process.Start();

            if (stdinPayload is not null)
            {
                using (var writer = process.StandardInput)
                {
                    writer.Write(stdinPayload);
                    writer.Flush();
                }
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            KillProcessTree(process);
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return new PowerShellExecutionResult(-1, output.ToString(), error.ToString(), TimedOut: true, stopwatch.Elapsed);
        }

        // Le stderr natif peut être une enveloppe CLIXML (#< CLIXML <Objs …) :
        // sans décodage, l'interface n'affiche que l'en-tête brut du sérialiseur.
        var stderr = ClixmlErrorDecoder.Decode(error.ToString());
        var rawStderr = ClixmlErrorDecoder.LooksLikeClixml(error.ToString()) ? error.ToString() : null;

        return new PowerShellExecutionResult(process.ExitCode, output.ToString(), stderr, TimedOut: false, stopwatch.Elapsed)
        {
            RawStandardError = rawStderr,
        };
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Le processus était déjà terminé.
        }
        catch (Win32Exception)
        {
            // Accès refusé au moment de la terminaison : sans conséquence pour l'appelant.
        }
    }
}
