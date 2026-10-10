using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SailRacing.Data;
using SailRacing.Models;
using SailRacing.Services;

namespace SailRacing.ViewModels;

[QueryProperty(nameof(RaceId), "raceId")]
public partial class ResultsViewModel : BaseViewModel
{
    private readonly IRaceRepository _races;
    private readonly IParticipantRepository _participants;
    private readonly ISailRacingApiClient _api;
    private readonly IResultPublicationRepository _publications;

    private Race? _race;

    public string? RaceId { get; set; }

    public ObservableCollection<ResultsEntry> Results { get; } = new();

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReplayUrl))]
    [NotifyPropertyChangedFor(nameof(ReplayLinkText))]
    private string? replayUrl;

    public bool HasReplayUrl => !string.IsNullOrEmpty(ReplayUrl);

    public string ReplayLinkText => ReplayUrl is null
        ? "Replay: available once the race has synced to the server"
        : $"Replay: {ReplayUrl}";

    [RelayCommand]
    private async Task OpenReplayAsync()
    {
        if (ReplayUrl is not null)
        {
            await Launcher.Default.OpenAsync(new Uri(ReplayUrl));
        }
    }

    [RelayCommand]
    private async Task CopyReplayLinkAsync()
    {
        if (ReplayUrl is null)
        {
            return;
        }

        await Clipboard.Default.SetTextAsync(ReplayUrl);
        StatusMessage = "Copied the replay link.";
    }

    public ResultsViewModel(
        IRaceRepository races,
        IParticipantRepository participants,
        ISailRacingApiClient api,
        IResultPublicationRepository publications)
    {
        _races = races;
        _participants = participants;
        _api = api;
        _publications = publications;
        Title = "Results";
    }

    [RelayCommand]
    private async Task EditRaceAsync()
    {
        if (!string.IsNullOrEmpty(RaceId))
        {
            await Shell.Current.GoToAsync($"raceSetup?raceId={RaceId}");
        }
    }

    public async Task OnAppearingAsync()
    {
        if (string.IsNullOrEmpty(RaceId))
        {
            return;
        }

        var aggregate = await _races.GetAggregateAsync(RaceId);
        if (aggregate is null)
        {
            return;
        }

        _race = aggregate.Race;
        ReplayUrl = JoinLinks.BuildReplay(_race.JoinCode);

        var participantIds = aggregate.RaceParticipants.Select(rp => rp.ParticipantId);
        var participants = await _participants.GetByIdsAsync(participantIds);

        // Corrected time = elapsed time x TCF (standard club handicap convention).
        var computed = aggregate.RaceParticipants.Select(rp =>
        {
            var participant = participants.FirstOrDefault(p => p.Id == rp.ParticipantId);
            var tcf = participant?.Tcf ?? 1.0;
            var corrected = rp.Status == RaceParticipantStatus.Finished && rp.ElapsedSeconds.HasValue
                ? rp.ElapsedSeconds.Value * tcf
                : (double?)null;

            return new ResultsEntry
            {
                ParticipantId = rp.ParticipantId,
                ParticipantName = participant?.Name ?? "(unknown)",
                Helm = participant?.Helm ?? string.Empty,
                Tcf = tcf,
                Status = rp.Status,
                FinishTime = rp.FinishTime,
                ElapsedSeconds = rp.ElapsedSeconds,
                CorrectedSeconds = corrected
            };
        }).ToList();

        // Finishers ranked ascending by corrected time; non-finishers (DNF/DNS/RET/OCS) after, unranked.
        var finishers = computed.Where(e => e.Status == RaceParticipantStatus.Finished)
            .OrderBy(e => e.CorrectedSeconds)
            .ToList();
        var nonFinishers = computed.Where(e => e.Status != RaceParticipantStatus.Finished).ToList();

        for (var i = 0; i < finishers.Count; i++)
        {
            finishers[i].Rank = i + 1;
        }

        Results.Clear();
        foreach (var entry in finishers.Concat(nonFinishers))
        {
            Results.Add(entry);
        }
    }

    /// <summary>
    /// Writes the results as a CSV that opens straight in Excel (UTF-8 with BOM; the column separator
    /// follows the PC's list separator, so a Swiss/German Excel gets ';' and an English one ','), saves it
    /// under Documents, and offers the platform share sheet on top.
    /// </summary>
    [RelayCommand]
    private async Task ExportAsync()
    {
        if (_race is null || Results.Count == 0)
        {
            StatusMessage = "Nothing to export yet.";
            return;
        }

        try
        {
            var sep = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ListSeparator;
            string Field(string? value)
            {
                var v = value ?? string.Empty;
                return v.Contains(sep) || v.Contains('"') || v.Contains('\n') ? $"\"{v.Replace("\"", "\"\"")}\"" : v;
            }

            string Row(params string?[] fields) => string.Join(sep, fields.Select(Field));

            var lines = new List<string>
            {
                Row("Race", _race.Name),
                Row("Start", _race.StartAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")),
                Row("Corrected time = real (elapsed) time x TCF"),
                string.Empty,
                Row("Rank", "Boat", "Helm", "TCF", "Status", "Finish time", "Real time (elapsed)", "Corrected time (TCF)")
            };

            foreach (var r in Results)
            {
                lines.Add(Row(r.Rank?.ToString(), r.ParticipantName, r.Helm, r.TcfText, r.Status.ToString(),
                    r.FinishClockText, r.ElapsedText, r.CorrectedText));
            }

            var safeName = string.Concat((string.IsNullOrWhiteSpace(_race.Name) ? "race" : _race.Name.Trim())
                .Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Sail-Racing results");
            Directory.CreateDirectory(folder);
            var filePath = Path.Combine(folder, $"{safeName} - results.csv");

            await File.WriteAllTextAsync(filePath, string.Join("\r\n", lines) + "\r\n",
                new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            StatusMessage = $"Exported to {filePath}";

            try
            {
                await Share.Default.RequestAsync(new ShareFileRequest
                {
                    Title = "Export results",
                    File = new ShareFile(filePath)
                });
            }
            catch
            {
                // The share sheet is a convenience; the file is already saved where the message says.
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task PublishAsync()
    {
        if (_race is null)
        {
            return;
        }

        var payload = new ResultPublicationDto
        {
            RaceId = _race.Id,
            PublishedAt = DateTimeOffset.UtcNow,
            Results = Results.Select(r => new ResultEntryDto
            {
                ParticipantId = r.ParticipantId,
                Status = r.Status.ToString(),
                ElapsedSeconds = r.ElapsedSeconds,
                CorrectedSeconds = r.CorrectedSeconds,
                Rank = r.Rank
            }).ToList()
        };

        var publication = new ResultPublication
        {
            RaceId = _race.Id,
            PublishedAt = payload.PublishedAt,
            PayloadJson = JsonSerializer.Serialize(payload)
        };

        try
        {
            await _api.PublishResultsAsync(payload);
            publication.ServerAckStatus = PublishAckStatus.Success;
            StatusMessage = "Results published.";
        }
        catch
        {
            publication.ServerAckStatus = PublishAckStatus.Failed;
            StatusMessage = "Publish failed — will retry when connectivity is restored.";
        }

        await _publications.SaveAsync(publication);
    }
}
