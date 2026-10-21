using SailRacing.Data;

namespace SailRacing.Services;

public static class RaceLocation
{
    /// <summary>
    /// Where the race is, for the GPS stamp on photos and video when this PC has no location of its own: the finish point
    /// if it is separate from the start line, otherwise the middle of the start/finish line. Null if neither has been set.
    /// </summary>
    public static (GeoFix? Location, string Label) Of(RaceAggregate aggregate)
    {
        var race = aggregate.Race;
        if (!race.FinishSameAsStart && race.FinishLatitude is { } fLat && race.FinishLongitude is { } fLon)
        {
            return (new GeoFix(fLat, fLon), "finish point");
        }

        var line = aggregate.StartLine;
        if (line.CommitteeLatitude is { } cLat && line.CommitteeLongitude is { } cLon
            && line.PinLatitude is { } pLat && line.PinLongitude is { } pLon)
        {
            return (new GeoFix((cLat + pLat) / 2, (cLon + pLon) / 2), "finish line");
        }

        return (null, string.Empty);
    }
}
