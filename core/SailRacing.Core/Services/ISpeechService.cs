namespace SailRacing.Services;

/// <summary>Speaks the start-sequence announcements.</summary>
public interface ISpeechService
{
    /// <summary>
    /// Gets the given phrases ready ahead of time, in the order they will be needed, so speaking one later
    /// starts without delay. Safe to call repeatedly — phrases already prepared are skipped. A platform
    /// that can't prepare ignores it.
    /// </summary>
    Task PrepareAsync(IEnumerable<string> phrases);

    /// <summary>Speaks the phrase and completes when it has been spoken (or <paramref name="token"/> cancels it).</summary>
    Task SpeakAsync(string text, CancellationToken token);
}
