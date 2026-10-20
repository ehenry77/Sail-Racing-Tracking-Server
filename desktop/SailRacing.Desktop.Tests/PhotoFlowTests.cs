using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using SailRacing.Data;
using SailRacing.Desktop.Platform;
using SailRacing.Desktop.Shell;
using SailRacing.Models;
using SailRacing.Services;
using SailRacing.ViewModels;
using SkiaSharp;
using Xunit;

namespace SailRacing.Desktop.Tests;

internal static class Pictures
{
    public static byte[] Jpeg(int width = 1280, int height = 720)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor(60, 110, 160));
        }

        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Jpeg, 85).ToArray();
    }
}

internal sealed class FakeCamera : ICameraService
{
    public int Captures { get; private set; }
    public bool WarmedUp { get; private set; }
    public bool Released { get; private set; }
    public Exception? Failure { get; set; }

    public Task WarmUpAsync(string? deviceId)
    {
        WarmedUp = true;
        return Task.CompletedTask;
    }

    public Task ReleaseAsync()
    {
        Released = true;
        return Task.CompletedTask;
    }

    public Task<byte[]> CaptureJpegAsync(string? deviceId)
    {
        Captures++;
        return Failure is null ? Task.FromResult(Pictures.Jpeg()) : Task.FromException<byte[]>(Failure);
    }
}

/// <summary>Files in a temporary folder, so a test never writes into the real Documents.</summary>
internal sealed class TempDocuments : IFileService
{
    public string DocumentsDirectory { get; } = Path.Combine(Path.GetTempPath(), "sr-docs-" + Guid.NewGuid());
    public string CacheDirectory => DocumentsDirectory;
    public Task ShareFileAsync(string title, string path) => Task.CompletedTask;
    public Task<PickedTextFile?> PickTextFileAsync(string title, params string[] extensions) => Task.FromResult<PickedTextFile?>(null);
}

public class PhotoFlowTests
{
    private static async Task Settle(int rounds = 25)
    {
        for (var i = 0; i < rounds; i++)
        {
            await Task.Delay(30);
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static (IServiceProvider Services, FakeCamera Camera, TempDocuments Docs) Build(TempPaths paths)
    {
        var camera = new FakeCamera();
        var docs = new TempDocuments();
        var services = App.BuildServices(paths, s =>
        {
            s.AddSingleton<ISpeechService>(new SilentSpeech());
            s.AddSingleton<IDialogService, AlwaysYes>();
            s.AddSingleton<ICameraService>(camera);
            s.AddSingleton<IFileService>(docs);
        });
        AppConfig.ServerBaseUrl = "http://127.0.0.1:9";
        return (services, camera, docs);
    }

    private static async Task<string> SeedRacingAsync(IServiceProvider services, int laps = 2)
    {
        var participants = services.GetRequiredService<IParticipantRepository>();
        var fleets = services.GetRequiredService<IFleetRepository>();
        var races = services.GetRequiredService<IRaceRepository>();

        var p = new Participant { Name = "Lemanja", Helm = "Eric", Tcf = 1.0 };
        await participants.SaveAsync(p);
        var fleet = new Fleet { Name = "Cruisers" };
        await fleets.SaveAsync(fleet, new[] { p.Id });

        var aggregate = new RaceAggregate
        {
            Race = new Race { Name = "Test race", FleetId = fleet.Id, Status = RaceStatus.Racing, LapsDefault = laps, StartAt = DateTimeOffset.UtcNow.AddMinutes(-20) },
            StartLine = new StartLine { CommitteeLatitude = 46.454, CommitteeLongitude = 6.55, PinLatitude = 46.454, PinLongitude = 6.553 },
            RaceParticipants = new() { new RaceParticipant { ParticipantId = p.Id, Laps = laps } }
        };
        await races.SaveAggregateAsync(aggregate);
        return aggregate.Race.Id;
    }

    [AvaloniaFact]
    public async Task With_the_automatic_photo_on_the_boat_is_photographed_when_it_finishes_and_not_before()
    {
        using var paths = new TempPaths();
        var (services, camera, docs) = Build(paths);
        AppConfig.AutoFinishPhoto = true;
        var raceId = await SeedRacingAsync(services);

        var screen = services.GetRequiredService<ScreenFactory>().Create("timingSheet", new Dictionary<string, string> { ["raceId"] = raceId });
        var window = new Window { Width = 1000, Height = 700, Content = screen };
        window.Show();
        await Settle();

        var vm = (TimingSheetViewModel)screen.DataContext!;
        var entry = Assert.Single(vm.Entries);
        Assert.True(camera.WarmedUp, "the camera should be opened when the Timing Sheet opens");

        await vm.RecordLapNowCommand.ExecuteAsync(entry);   // lap 1 of 2: no photo yet
        await Settle();
        Assert.Equal(0, camera.Captures);
        Assert.False(entry.HasPhoto);

        await vm.RecordLapNowCommand.ExecuteAsync(entry);   // the last lap: it finishes
        await Settle(60);
        Assert.Equal(1, camera.Captures);
        Assert.True(entry.HasPhoto, vm.InfoMessage);
        Assert.True(File.Exists(entry.PhotoPath));
        Assert.StartsWith(Path.Combine(docs.DocumentsDirectory, "Sail-Racing photos", "Test race"), entry.PhotoPath);
        Assert.Contains("Photo saved", vm.InfoMessage);

        // The photo is remembered with the race, not lost when the sheet is reopened.
        var stored = (await services.GetRequiredService<IRaceRepository>().GetAggregateAsync(raceId))!;
        Assert.Equal(entry.PhotoPath, stored.RaceParticipants.Single().FinishPhotoPath);

        // A boat that already has its finish photo is not photographed again.
        await vm.SetFinishTimeCommand.ExecuteAsync(entry);
        await Settle();
        Assert.Equal(1, camera.Captures);

        window.Close();
        Assert.True(camera.Released, "the camera should be released when the Timing Sheet is closed");
    }

    [AvaloniaFact]
    public async Task With_the_automatic_photo_off_nothing_is_taken_but_the_button_still_works()
    {
        using var paths = new TempPaths();
        var (services, camera, _) = Build(paths);
        AppConfig.AutoFinishPhoto = false;
        var raceId = await SeedRacingAsync(services);

        var screen = services.GetRequiredService<ScreenFactory>().Create("timingSheet", new Dictionary<string, string> { ["raceId"] = raceId });
        var window = new Window { Width = 1000, Height = 700, Content = screen };
        window.Show();
        await Settle();

        var vm = (TimingSheetViewModel)screen.DataContext!;
        var entry = Assert.Single(vm.Entries);
        Assert.False(camera.WarmedUp, "the camera stays off unless the automatic photo is switched on");

        await vm.RecordLapNowCommand.ExecuteAsync(entry);
        await vm.RecordLapNowCommand.ExecuteAsync(entry);   // finishes
        await Settle(40);
        Assert.Equal(0, camera.Captures);
        Assert.False(entry.HasPhoto);

        await vm.TakePhotoCommand.ExecuteAsync(entry);       // by hand
        await Settle(40);
        Assert.Equal(1, camera.Captures);
        Assert.True(entry.HasPhoto, vm.InfoMessage);

        if ((Environment.GetEnvironmentVariable("SR_SCREENSHOTS") ?? "") is { Length: > 0 } shots)
        {
            Directory.CreateDirectory(shots);
            window.CaptureRenderedFrame()?.Save(Path.Combine(shots, "timingSheet-photo.png"));
        }

        window.Close();
    }

    [AvaloniaFact]
    public async Task A_camera_that_fails_tells_the_committee_and_never_blocks_the_finish()
    {
        using var paths = new TempPaths();
        var (services, camera, _) = Build(paths);
        AppConfig.AutoFinishPhoto = true;
        camera.Failure = new InvalidOperationException("no camera was found");
        var raceId = await SeedRacingAsync(services);

        var screen = services.GetRequiredService<ScreenFactory>().Create("timingSheet", new Dictionary<string, string> { ["raceId"] = raceId });
        var window = new Window { Width = 1000, Height = 700, Content = screen };
        window.Show();
        await Settle();

        var vm = (TimingSheetViewModel)screen.DataContext!;
        var entry = Assert.Single(vm.Entries);

        await vm.RecordLapNowCommand.ExecuteAsync(entry);
        await vm.RecordLapNowCommand.ExecuteAsync(entry);
        await Settle(40);

        Assert.Equal(RaceParticipantStatus.Finished, entry.Status);   // the finish itself is recorded regardless
        Assert.False(entry.HasPhoto);
        Assert.Contains("no camera was found", vm.InfoMessage);

        window.Close();
    }

    [AvaloniaFact]
    public async Task The_settings_screen_remembers_the_automatic_photo_choice()
    {
        using var paths = new TempPaths();
        var (services, _, _) = Build(paths);
        AppConfig.AutoFinishPhoto = false;

        var screen = services.GetRequiredService<ScreenFactory>().Create("settings", new Dictionary<string, string>());
        var window = new Window { Width = 1000, Height = 800, Content = screen };
        window.Show();
        await Settle();

        var vm = (SettingsViewModel)screen.DataContext!;
        Assert.False(vm.AutoFinishPhoto);
        vm.AutoFinishPhoto = true;
        vm.SaveCommand.Execute(null);
        Assert.True(AppConfig.AutoFinishPhoto);

        window.Close();
    }
}

public class DesktopCameraServiceTests
{
    /// <summary>A stand-in "ffmpeg" that writes a picture to the output file (the last argument), as the real one would.</summary>
    private static string FakeToolFolder(string toolName, string body)
    {
        var dir = Path.Combine(Path.GetTempPath(), "sr-faketool-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, toolName);
        File.WriteAllText(path, "#!/bin/sh\n" + body + "\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return dir;
    }

    private static async Task<T> WithPath<T>(string pathValue, Func<Task<T>> action)
    {
        var previous = Environment.GetEnvironmentVariable("PATH");
        Environment.SetEnvironmentVariable("PATH", pathValue);
        try
        {
            return await action();
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", previous);
        }
    }

    [Fact]
    public async Task On_Linux_the_camera_is_read_with_ffmpeg_and_the_temp_file_is_cleaned_up()
    {
        if (!OperatingSystem.IsLinux())
        {
            return; // this path only exists on Linux; it runs there (see README: tests)
        }

        var jpeg = Path.Combine(Path.GetTempPath(), "sr-fake-" + Guid.NewGuid() + ".jpg");
        File.WriteAllBytes(jpeg, Pictures.Jpeg(640, 480));
        var argsLog = Path.Combine(Path.GetTempPath(), "sr-args-" + Guid.NewGuid() + ".txt");
        var folder = FakeToolFolder("ffmpeg", $"echo \"$@\" > \"{argsLog}\"\nfor last; do :; done\ncp \"{jpeg}\" \"$last\"");

        var bytes = await WithPath(folder + Path.PathSeparator + "/usr/bin" + Path.PathSeparator + "/bin",
            () => new DesktopCameraService().CaptureJpegAsync("/dev/video-test"));

        using var picture = SKBitmap.Decode(bytes);
        Assert.Equal((640, 480), (picture.Width, picture.Height));

        var args = await File.ReadAllTextAsync(argsLog);
        Assert.Contains("-f v4l2", args);
        Assert.Contains("-i /dev/video-test", args);
        Assert.Contains("-frames:v 1", args);
        Assert.DoesNotContain(Directory.GetFiles(Path.GetTempPath(), "sailracing-photo-*"), f => f.EndsWith(".jpg"));
    }

    [Fact]
    public async Task On_Linux_a_failing_ffmpeg_gives_its_own_reason()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var folder = FakeToolFolder("ffmpeg", "echo 'Cannot open video device /dev/video-test: No such file or directory' >&2\nexit 1");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => WithPath(folder + Path.PathSeparator + "/usr/bin" + Path.PathSeparator + "/bin",
            () => new DesktopCameraService().CaptureJpegAsync("/dev/video-test")));

        Assert.Contains("ffmpeg failed", ex.Message);
        Assert.Contains("Cannot open video device", ex.Message);
    }

    [Fact]
    public async Task On_Linux_without_ffmpeg_or_fswebcam_it_says_what_to_install()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var empty = Path.Combine(Path.GetTempPath(), "sr-empty-" + Guid.NewGuid());
        Directory.CreateDirectory(empty);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => WithPath(empty,
            () => new DesktopCameraService().CaptureJpegAsync("/dev/video-test")));

        Assert.Contains("sudo apt install ffmpeg", ex.Message);
    }

    [Fact]
    public async Task Off_Linux_this_app_says_to_use_the_windows_version()
    {
        if (OperatingSystem.IsLinux())
        {
            return;
        }

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => new DesktopCameraService().CaptureJpegAsync(null));
        Assert.Contains("Linux", ex.Message);
    }
}
