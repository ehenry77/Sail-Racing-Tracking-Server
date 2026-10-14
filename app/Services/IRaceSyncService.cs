using SailRacing.Models;

namespace SailRacing.Services;

public interface IRaceSyncService
{
    /// <summary>Pushes one race (create or full update) to the server. Never throws; returns success.</summary>
    Task<bool> PushAsync(string raceId, CancellationToken ct = default);

    /// <summary>Retries every locally-stored race that isn't marked Synced, then sends any queued timing entries.</summary>
    Task RetryPendingAsync(CancellationToken ct = default);

    /// <summary>
    /// Queues a manual Timing Sheet entry for the server and tries to send it straight away. The entry is
    /// stored first, so it is never lost: if the server can't be reached it goes out when connectivity returns.
    /// </summary>
    Task QueueTimingAsync(string raceId, RaceParticipant boat, long updatedAtMs);

    /// <summary>Sends every queued timing entry. Never throws; entries the server didn't take stay queued.</summary>
    Task FlushTimingAsync(CancellationToken ct = default);

    /// <summary>The boats of this race that have a manual entry not yet delivered to the server.</summary>
    Task<HashSet<string>> GetPendingTimingParticipantIdsAsync(string raceId);

    /// <summary>Subscribes to connectivity-restored events to auto-retry the outbox. Call once at startup.</summary>
    void StartConnectivityWatcher();
}
