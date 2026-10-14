namespace SailRacing.Services;

public static class JoinLinks
{
    public const string NotSyncedText = "Join link: available once the race has synced to the server";

    /// <summary>
    /// The link that opens the competitor tracking page with this boat already selected. The join code is
    /// assigned by the server, so it's null until the race has synced at least once.
    /// </summary>
    public const string MapNotSyncedText = "Live map: available once the race has synced to the server";

    /// <summary>The live map for the whole race, opened by join code (the race's internal id isn't shown anywhere).</summary>
    public static string? BuildMap(string? joinCode)
    {
        if (string.IsNullOrEmpty(joinCode))
        {
            return null;
        }

        return $"{AppConfig.ServerBaseUrl.TrimEnd('/')}/map?code={Uri.EscapeDataString(joinCode)}";
    }

    /// <summary>Replay of the race from its recorded GPS traces.</summary>
    public static string? BuildReplay(string? joinCode)
    {
        if (string.IsNullOrEmpty(joinCode))
        {
            return null;
        }

        return $"{AppConfig.ServerBaseUrl.TrimEnd('/')}/replay?code={Uri.EscapeDataString(joinCode)}";
    }

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
