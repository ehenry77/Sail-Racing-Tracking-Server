using System.Diagnostics;
using SailRacing.Services;

namespace SailRacing.Desktop.Platform;

/// <summary>
/// Start-sequence announcements through the desktop's own speech tool, run as a short-lived process per phrase:
/// on Linux <c>espeak-ng</c> (or <c>espeak</c>, or speech-dispatcher's <c>spd-say</c>) — install with
/// <c>sudo apt install espeak-ng</c> — on macOS <c>say</c>, and on Windows the built-in SAPI voices.
/// These start speaking within a few tens of milliseconds, so there is nothing to prepare ahead of time.
/// If no tool is installed the announcements are skipped and the countdown on screen carries on regardless.
/// </summary>
public sealed class ProcessSpeechService : ISpeechService
{
    private sealed record Engine(string File, string[] Args, Dictionary<string, string>? Environment = null, bool TextFromEnvironment = false);

    private static readonly Lazy<Engine?> Resolved = new(Resolve);

    /// <summary>Whether a speech tool was found — for diagnostics.</summary>
    public static bool IsAvailable => Resolved.Value is not null;

    public Task PrepareAsync(IEnumerable<string> phrases) => Task.CompletedTask;

    public async Task SpeakAsync(string text, CancellationToken token)
    {
        var engine = Resolved.Value;
        if (engine is null)
        {
            return;
        }

        token.ThrowIfCancellationRequested();

        var info = new ProcessStartInfo(engine.File)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var arg in engine.Args)
        {
            info.ArgumentList.Add(arg);
        }

        if (engine.TextFromEnvironment)
        {
            info.Environment["SR_SPEECH_TEXT"] = text;
        }
        else
        {
            info.ArgumentList.Add(text);
        }

        Process process;
        try
        {
            process = Process.Start(info)!;
        }
        catch
        {
            return; // the tool vanished since it was found; stay silent rather than interrupt the timer
        }

        using (process)
        using (token.Register(() =>
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch
            {
                // already gone
            }
        }))
        {
            // Drain the pipes so a chatty tool can't block on a full buffer.
            _ = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(CancellationToken.None);
        }

        token.ThrowIfCancellationRequested();
    }

    private static Engine? Resolve()
    {
        if (OperatingSystem.IsLinux())
        {
            foreach (var (tool, args) in new (string, string[])[]
            {
                ("espeak-ng", Array.Empty<string>()),
                ("espeak", Array.Empty<string>()),
                ("spd-say", new[] { "--wait" })
            })
            {
                if (OnPath(tool)) return new Engine(tool, args);
            }

            return null;
        }

        if (OperatingSystem.IsMacOS())
        {
            return OnPath("say") ? new Engine("say", Array.Empty<string>()) : null;
        }

        if (OperatingSystem.IsWindows())
        {
            return new Engine("powershell.exe", new[]
            {
                "-NoProfile", "-NonInteractive", "-Command",
                "Add-Type -AssemblyName System.Speech; (New-Object System.Speech.Synthesis.SpeechSynthesizer).Speak($env:SR_SPEECH_TEXT)"
            }, TextFromEnvironment: true);
        }

        return null;
    }

    private static bool OnPath(string tool)
    {
        var path = System.Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(dir => File.Exists(Path.Combine(dir, tool)));
    }
}
