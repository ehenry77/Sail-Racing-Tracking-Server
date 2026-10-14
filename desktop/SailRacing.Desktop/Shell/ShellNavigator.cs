using Avalonia.Threading;
using SailRacing.Services;

namespace SailRacing.Desktop.Shell;

/// <summary>
/// The desktop app's <see cref="INavigator"/>. The view models navigate with the same route strings as under
/// MAUI Shell — "raceSetup?raceId=…", "startSequence?raceId=…", "timingSheet?…", "results?…" and "//races" for the
/// race list — and the main window shows the matching screen.
/// </summary>
public sealed class ShellNavigator : INavigator
{
    private Action<string, IReadOnlyDictionary<string, string>>? _show;

    /// <summary>Called by the main window: how to display a screen by name.</summary>
    public void Attach(Action<string, IReadOnlyDictionary<string, string>> show) => _show = show;

    public Task GoToAsync(string route)
    {
        var (name, query) = Parse(route);

        // View models can reach here from a background continuation; screens must be changed on the UI thread.
        return Dispatcher.UIThread.InvokeAsync(() => _show?.Invoke(name, query)).GetTask();
    }

    public static (string Name, IReadOnlyDictionary<string, string> Query) Parse(string route)
    {
        var path = route;
        var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var q = route.IndexOf('?');
        if (q >= 0)
        {
            path = route[..q];
            foreach (var pair in route[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = pair.Split('=', 2);
                query[Uri.UnescapeDataString(kv[0])] = kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : string.Empty;
            }
        }

        return (path.TrimStart('/'), query);
    }
}
