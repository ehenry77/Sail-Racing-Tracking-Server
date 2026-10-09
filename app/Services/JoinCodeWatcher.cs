using SailRacing.Data;
using SailRacing.Models;

namespace SailRacing.Services;

/// <summary>
/// Polls the local database until the race has a join code. The server assigns it during the background
/// sync that starts when a race is saved, which often finishes just after the next page has loaded — and
/// when the venue has no connectivity it only arrives later, when the sync is retried. The watcher lets a
/// page show its links as soon as the code lands instead of only on the next visit.
/// </summary>
public sealed class JoinCodeWatcher
{
    private readonly IRaceRepository _races;
    private IDispatcherTimer? _timer;
    private bool _checking;

    public JoinCodeWatcher(IRaceRepository races)
    {
        _races = races;
    }

    /// <summary>Calls <paramref name="onJoinCode"/> on the UI thread once, then stops.</summary>
    public void Start(string raceId, Action<Race> onJoinCode)
    {
        Stop();

        var timer = Application.Current!.Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(3);
        timer.Tick += async (_, _) =>
        {
            if (_checking)
            {
                return;
            }

            _checking = true;
            try
            {
                var fresh = (await _races.GetAggregateAsync(raceId))?.Race;
                if (fresh is not null && !string.IsNullOrEmpty(fresh.JoinCode))
                {
                    Stop();
                    onJoinCode(fresh);
                }
            }
            catch
            {
                // Try again on the next tick.
            }
            finally
            {
                _checking = false;
            }
        };
        timer.Start();
        _timer = timer;
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }
}
