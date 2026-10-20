using Microsoft.Extensions.DependencyInjection;
using SailRacing.Services;
using SailRacing.Services.Devices;
using SailRacing.ViewModels;
using Xunit;

// Several tests install their own AppConfig settings store (it is static), so tests must not overlap.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace SailRacing.Core.Tests;

/// <summary>Stand-ins for the platform services each app provides.</summary>
internal sealed class StubPlatform : INavigator, IDialogService, IClipboardService, ILauncherService, IFileService,
    IGeolocationService, IAppPaths, IConnectivityService, IUiDispatcher, ISpeechService, IDeviceSettingsService, ICameraService
{
    public StubPlatform(string dataDirectory) => DataDirectory = dataDirectory;

    public string DataDirectory { get; }
    public string DocumentsDirectory => DataDirectory;
    public string CacheDirectory => DataDirectory;
    public bool IsAvailable => false;
    public event EventHandler<bool>? ConnectivityChanged { add { } remove { } }

    public Task GoToAsync(string route) => Task.CompletedTask;
    public Task<bool> ConfirmAsync(string title, string message, string accept, string cancel) => Task.FromResult(true);
    public Task<string?> PromptAsync(string title, string message, string accept, string cancel, string? placeholder = null) => Task.FromResult<string?>(null);
    public Task SetTextAsync(string text) => Task.CompletedTask;
    public Task OpenAsync(Uri uri) => Task.CompletedTask;
    public Task OpenFileAsync(string path) => Task.CompletedTask;
    public Task WarmUpAsync(string? deviceId) => Task.CompletedTask;
    public Task<byte[]> CaptureJpegAsync(string? deviceId) => Task.FromResult(Array.Empty<byte>());
    public Task ReleaseAsync() => Task.CompletedTask;
    public Task ShareFileAsync(string title, string path) => Task.CompletedTask;
    public Task<PickedTextFile?> PickTextFileAsync(string title, params string[] extensions) => Task.FromResult<PickedTextFile?>(null);
    public Task<GeoFix?> GetLocationAsync(TimeSpan timeout) => Task.FromResult<GeoFix?>(null);
    public void Post(Action action) => action();
    public IUiTimer CreateTimer() => throw new NotSupportedException();
    public Task PrepareAsync(IEnumerable<string> phrases) => Task.CompletedTask;
    public Task SpeakAsync(string text, CancellationToken token) => Task.CompletedTask;
    public Task<List<MediaDeviceInfo>> GetAudioOutputDevicesAsync() => Task.FromResult(new List<MediaDeviceInfo>());
    public Task<List<MediaDeviceInfo>> GetAudioInputDevicesAsync() => Task.FromResult(new List<MediaDeviceInfo>());
    public Task<List<MediaDeviceInfo>> GetVideoInputDevicesAsync() => Task.FromResult(new List<MediaDeviceInfo>());
    public Task<List<MediaDeviceInfo>> GetGpsSensorsAsync() => Task.FromResult(new List<MediaDeviceInfo>());
}

public class CompositionTests
{
    /// <summary>Both apps register the core with AddSailRacingCore() and add the platform pieces; a missing
    /// registration would otherwise only show when someone opens the screen that needs it.</summary>
    [Fact]
    public void Every_view_model_and_service_can_be_built()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sr-core-tests-" + Guid.NewGuid());
        var platform = new StubPlatform(dir);

        var services = new ServiceCollection();
        services.AddSailRacingCore();
        services.AddSingleton<INavigator>(platform);
        services.AddSingleton<IDialogService>(platform);
        services.AddSingleton<IClipboardService>(platform);
        services.AddSingleton<ILauncherService>(platform);
        services.AddSingleton<IFileService>(platform);
        services.AddSingleton<IGeolocationService>(platform);
        services.AddSingleton<IUiDispatcher>(platform);
        services.AddSingleton<IAppPaths>(platform);
        services.AddSingleton<IConnectivityService>(platform);
        services.AddSingleton<ISpeechService>(platform);
        services.AddSingleton<IDeviceSettingsService>(platform);
        services.AddSingleton<ICameraService>(platform);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        foreach (var type in new[]
        {
            typeof(CompetitorsViewModel), typeof(FleetsViewModel), typeof(RaceListViewModel), typeof(RaceSetupViewModel),
            typeof(StartSequenceViewModel), typeof(TimingSheetViewModel), typeof(ResultsViewModel), typeof(SettingsViewModel),
            typeof(IRaceSyncService), typeof(IRaceTakeOverService), typeof(IStartSequenceService), typeof(IFinishPhotoService)
        })
        {
            Assert.NotNull(provider.GetRequiredService(type));
        }

        try { Directory.Delete(dir, true); } catch { /* temp folder */ }
    }
}

public class SettingsStoreTests
{
    [Fact]
    public void Json_file_store_round_trips_and_removes_keys()
    {
        var path = Path.Combine(Path.GetTempPath(), "sr-settings-" + Guid.NewGuid(), "settings.json");
        var store = new JsonFileSettingsStore(path);
        Assert.Null(store.Get("ServerBaseUrl"));

        store.Set("ServerBaseUrl", "https://tracking.example");
        store.Set("SequenceAlarms", "-10,-6");
        Assert.Equal("https://tracking.example", new JsonFileSettingsStore(path).Get("ServerBaseUrl"));

        store.Set("SequenceAlarms", null);
        Assert.Null(new JsonFileSettingsStore(path).Get("SequenceAlarms"));
        Assert.Equal("https://tracking.example", new JsonFileSettingsStore(path).Get("ServerBaseUrl"));

        Directory.Delete(Path.GetDirectoryName(path)!, true);
    }

    [Fact]
    public void Damaged_settings_file_means_defaults_not_a_crash()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sr-settings-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "settings.json");
        File.WriteAllText(path, "{ this is not json");

        Assert.Null(new JsonFileSettingsStore(path).Get("anything"));

        Directory.Delete(dir, true);
    }

    [Fact]
    public void AppConfig_reads_through_the_installed_store_and_falls_back_to_defaults()
    {
        var previous = AppConfig.Store;
        try
        {
            AppConfig.Store = new InMemorySettingsStore();
            Assert.Equal(StartSequenceSettings.DefaultAlarmsText, AppConfig.SequenceAlarmsText);

            AppConfig.SequenceAlarmsText = "-12,-8";
            Assert.Equal("-12,-8", AppConfig.SequenceAlarmsText);
            Assert.Equal(new[] { -12.0, -8.0 }, AppConfig.LoadSequenceSettings().AlarmMinutes);

            AppConfig.PreferredGpsSensor = ("id1", "Sensor 1");
            Assert.Equal(("id1", "Sensor 1"), AppConfig.PreferredGpsSensor);
        }
        finally
        {
            AppConfig.Store = previous;
        }
    }
}

public class StartSequenceTests
{
    [Fact]
    public void Standard_sequence_has_the_5_4_1_0_signals_and_a_ten_second_countdown_before_each()
    {
        var timeline = StartSequenceTimeline.Build(StartSequenceSettings.Default);

        Assert.Equal(TimeSpan.FromMinutes(-10), timeline.First(e => e.Type == SequenceEventType.GetReadyAlarm).Offset);
        Assert.Equal(TimeSpan.FromMinutes(-5), timeline.Single(e => e.Type == SequenceEventType.ClassFlagUp).Offset);
        Assert.Equal(TimeSpan.FromMinutes(-4), timeline.Single(e => e.Type == SequenceEventType.PFlagUp).Offset);
        Assert.Equal(TimeSpan.FromMinutes(-1), timeline.Single(e => e.Type == SequenceEventType.PFlagDown).Offset);
        Assert.Equal(TimeSpan.Zero, timeline.Single(e => e.Type == SequenceEventType.ClassFlagDown).Offset);
        Assert.Equal(40, timeline.Count(e => e.Type == SequenceEventType.CountdownTick));
        Assert.Equal(timeline.OrderBy(e => e.Offset).ToList(), timeline.ToList());
    }

    [Fact]
    public void Countdown_numbers_are_spoken_ten_seconds_out_down_to_one()
    {
        var timeline = StartSequenceTimeline.Build(StartSequenceSettings.Default);
        var beforeStart = timeline.Where(e => e.Type == SequenceEventType.CountdownTick && e.Offset > TimeSpan.FromSeconds(-11)).ToList();

        Assert.Equal(Enumerable.Range(1, 10).Reverse().Select(n => n.ToString()), beforeStart.Select(e => e.Text));
        Assert.Equal(TimeSpan.FromSeconds(-10), beforeStart[0].Offset);
        Assert.Equal(TimeSpan.FromSeconds(-1), beforeStart[^1].Offset);
    }

    [Theory]
    [InlineData("-10,-6", "10", "-5,-4,-1,0", true)]
    [InlineData("10, 6", "10", "5,4,1,0", true)]        // minus signs optional
    [InlineData("", "0", "-5,-4,-1,0", true)]            // no alarms, no spoken countdown
    [InlineData("-10,-6", "10", "-5,-4,-1", false)]      // a signal missing
    [InlineData("-10,-6", "10", "-4,-5,-1,0", false)]    // out of order
    [InlineData("-10,-6", "31", "-5,-4,-1,0", false)]    // countdown too long
    [InlineData("-10,-6", "abc", "-5,-4,-1,0", false)]
    public void Settings_are_validated(string alarms, string countdown, string signals, bool valid)
    {
        Assert.Equal(valid, StartSequenceSettings.TryParse(alarms, countdown, signals, out _, out var error));
        Assert.Equal(valid, string.IsNullOrEmpty(error));
    }
}

public class JoinCodeTests
{
    [Theory]
    [InlineData("K7P2M", "K7P2M")]
    [InlineData("k7p2m", "K7P2M")]
    [InlineData(" K7-P2M ", "K7P2M")]
    [InlineData("https://tracking.example/race/K7P2M?boat=abc", "K7P2M")]
    [InlineData("https://tracking.example/map/?code=k7p2m", "K7P2M")]
    [InlineData("https://tracking.example/replay/?code=K7P2M&x=1", "K7P2M")]
    [InlineData("https://tracking.example/", "")]
    public void Join_code_is_found_in_a_code_or_any_of_the_race_links(string input, string expected) =>
        Assert.Equal(expected, RaceTakeOverService.ExtractJoinCode(input));
}
