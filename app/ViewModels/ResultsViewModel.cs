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
                Status = rp.Status,
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
