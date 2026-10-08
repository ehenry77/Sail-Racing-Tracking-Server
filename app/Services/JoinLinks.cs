namespace SailRacing.Services;

public static class JoinLinks
{
    public const string NotSyncedText = "Join link: available once the race has synced to the server";

    /// <summary>
    /// The link that opens the competitor tracking page with this boat already selected. The join code is
    /// assigned by the server, so it's null until the race has synced at least once.
    /// </summary>
    public static string? Build(string? joinCode, string participantId)
    {
        if (string.IsNullOrEmpty(joinCode))
        {
            return null;
        }

        var baseUrl = AppConfig.ServerBaseUrl.TrimEnd('/');
        return $"{baseUrl}/race/{Uri.EscapeDataString(joinCode)}?boat={Uri.EscapeDataString(participantId)}";
    }
}
