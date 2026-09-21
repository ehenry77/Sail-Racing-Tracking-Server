using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SailRacing.Data;
using SailRacing.Models;
using SailRacing.Services;

namespace SailRacing.ViewModels;

[QueryProperty(nameof(RaceId), "raceId")]
public partial class StartSequenceViewModel : BaseViewModel, IDisposable
{
    private readonly IRaceRepository _races;
    private readonly IStartSequenceService _sequence;
    private readonly ISailRacingApiClient _api;

    private Race? _race;

    public string? RaceId { get; set; }

    [ObservableProperty]
    private bool classFlagUp;

    [ObservableProperty]
    private bool pFlagUp;

    [ObservableProperty]
    private string countdownText = "--:--";

    [ObservableProperty]
    private bool isSequenceComplete;

    [ObservableProperty]
    private bool isSequenceStarted;

    public StartSequenceViewModel(IRaceRepository races, IStartSequenceService sequence, ISailRacingApiClient api)
    {
        _races = races;
        _sequence = sequence;
        _api = api;
        Title = "Start Sequence";
        _sequence.StatusChanged += OnStatusChanged;
    }

    public async Task OnAppearingAsync()
    {
        if (string.IsNullOrEmpty(RaceId))
        {
            return;
        }

        var aggregate = await _races.GetAggregateAsync(RaceId);
        _race = aggregate?.Race;

        if (_race?.StartAt is not null)
        {
            IsSequenceStarted = true;
            _sequence.Start(_race.StartAt.Value);
        }
    }

    [RelayCommand]
    private async Task BeginSequenceAsync()
    {
        if (_race is null)
        {
            return;
        }

        _race.StartAt = DateTimeOffset.UtcNow;
        _race.Status = RaceStatus.StartSequence;

        var aggregate = await _races.GetAggregateAsync(_race.Id);
        if (aggregate is not null)
        {
            aggregate.Race = _race;
            await _races.SaveAggregateAsync(aggregate);
        }

        IsSequenceStarted = true;
        _sequence.Start(_race.StartAt.Value);

        try
        {
            await _api.StartSequenceAsync(_race.Id, _race.StartAt.Value);
        }
        catch
        {
            // Non-fatal: the sequence itself runs entirely off the locally-persisted StartAt;
            // the server will catch up next time the race syncs.
        }
    }

    [RelayCommand]
    private async Task AllClearAsync()
    {
        if (_race is null)
        {
            return;
        }

        _race.Status = RaceStatus.Racing;
        var aggregate = await _races.GetAggregateAsync(_race.Id);
        if (aggregate is not null)
        {
            aggregate.Race = _race;
            await _races.SaveAggregateAsync(aggregate);
        }

        try
        {
            await _api.AllClearAsync(_race.Id);
        }
        catch
        {
            // Non-fatal: retried implicitly next time the race is pushed/synced.
        }

        await Shell.Current.GoToAsync($"timingSheet?raceId={_race.Id}");
    }

    private void OnStatusChanged(object? sender, StartSequenceStatus status)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ClassFlagUp = status.ClassFlagUp;
            PFlagUp = status.PFlagUp;
            IsSequenceComplete = status.IsComplete;

            var t = status.TimeToStart;
            CountdownText = t >= TimeSpan.Zero
                ? $"-{t:mm\\:ss}"
                : $"+{t.Negate():mm\\:ss}";
        });
    }

    public void Dispose()
    {
        _sequence.StatusChanged -= OnStatusChanged;
        _sequence.Stop();
    }
}
