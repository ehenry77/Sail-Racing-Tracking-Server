using System.Diagnostics;
using Avalonia;
using Avalonia.Logging;
using SailRacing.Desktop.Platform;

namespace SailRacing.Desktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // SR_DEBUG=1 prints Avalonia's own warnings and errors to the terminal.
        if (Environment.GetEnvironmentVariable("SR_DEBUG") == "1")
        {
            Trace.Listeners.Add(new ConsoleTraceListener(useErrorStream: true));
        }

        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            // A desktop user gets no console, so a failure to start would otherwise be silent. Leave a note
            // next to the data, and on the terminal for anyone who launched it from one.
            try
            {
                var folder = new DesktopAppPaths().DataDirectory;
                Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder, "crash.log"), $"{DateTimeOffset.Now:u}\n{ex}\n\n");
            }
            catch
            {
                // nowhere to write it
            }

            Console.Error.WriteLine($"SailRacing could not start: {ex}");
            return 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace(LogEventLevel.Warning);
}
