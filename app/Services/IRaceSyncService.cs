namespace SailRacing.Services;

public interface IRaceSyncService
{
    /// <summary>Pushes one race (create or full update) to the server. Never throws; returns success.</summary>
    Task<bool> PushAsync(string raceId, CancellationToken ct = default);

    /// <summary>Retries every locally-stored race that isn't marked Synced.</summary>
    Task RetryPendingAsync(CancellationToken ct = default);

    /// <summary>Subscribes to connectivity-restored events to auto-retry the outbox. Call once at startup.</summary>
    void StartConnectivityWatcher();
}
