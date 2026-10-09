using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SailRacing.Services;
using SailRacing.Services.Devices;

namespace SailRacing.ViewModels;

public partial class SettingsViewModel : BaseViewModel
{
    private readonly IDeviceSettingsService _devices;

    public ObservableCollection<MediaDeviceInfo> AudioOutputDevices { get; } = new();

    public ObservableCollection<MediaDeviceInfo> AudioInputDevices { get; } = new();

    public ObservableCollection<MediaDeviceInfo> VideoInputDevices { get; } = new();

    public ObservableCollection<MediaDeviceInfo> GpsSensors { get; } = new();

    [ObservableProperty]
    private string serverBaseUrlText = string.Empty;

    [ObservableProperty]
    private MediaDeviceInfo? selectedAudioOutputDevice;

    [ObservableProperty]
    private MediaDeviceInfo? selectedAudioInputDevice;

    [ObservableProperty]
    private MediaDeviceInfo? selectedVideoInputDevice;

    [ObservableProperty]
    private MediaDeviceInfo? selectedGpsSensor;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    // Start sequence parameters (see StartSequenceSettings): alarms, spoken countdown, and the four signals.
    [ObservableProperty]
    private string sequenceAlarmsText = StartSequenceSettings.DefaultAlarmsText;

    [ObservableProperty]
    private string sequenceCountdownText = StartSequenceSettings.DefaultCountdownText;

    [ObservableProperty]
    private string sequenceSignalsText = StartSequenceSettings.DefaultSignalsText;

    [ObservableProperty]
    private string sequencePreview = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSequenceError))]
    private string sequenceError = string.Empty;

    public bool HasSequenceError => !string.IsNullOrEmpty(SequenceError);

    partial void OnSequenceAlarmsTextChanged(string value) => UpdateSequencePreview();

    partial void OnSequenceCountdownTextChanged(string value) => UpdateSequencePreview();

    partial void OnSequenceSignalsTextChanged(string value) => UpdateSequencePreview();

    private void UpdateSequencePreview()
    {
        if (StartSequenceSettings.TryParse(SequenceAlarmsText, SequenceCountdownText, SequenceSignalsText, out var settings, out var error))
        {
            SequencePreview = settings.Describe();
            SequenceError = string.Empty;
        }
        else
        {
            SequencePreview = string.Empty;
            SequenceError = error;
        }
    }

    [RelayCommand]
    private void ResetSequence()
    {
        SequenceAlarmsText = StartSequenceSettings.DefaultAlarmsText;
        SequenceCountdownText = StartSequenceSettings.DefaultCountdownText;
        SequenceSignalsText = StartSequenceSettings.DefaultSignalsText;
    }

    public SettingsViewModel(IDeviceSettingsService devices)
    {
        _devices = devices;
        Title = "Settings";
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            ServerBaseUrlText = AppConfig.ServerBaseUrl;
            SequenceAlarmsText = AppConfig.SequenceAlarmsText;
            SequenceCountdownText = AppConfig.SequenceCountdownText;
            SequenceSignalsText = AppConfig.SequenceSignalsText;
            UpdateSequencePreview();

            await LoadCategoryAsync(AudioOutputDevices, _devices.GetAudioOutputDevicesAsync, AppConfig.PreferredAudioOutputDevice,
                d => SelectedAudioOutputDevice = d);
            await LoadCategoryAsync(AudioInputDevices, _devices.GetAudioInputDevicesAsync, AppConfig.PreferredAudioInputDevice,
                d => SelectedAudioInputDevice = d);
            await LoadCategoryAsync(VideoInputDevices, _devices.GetVideoInputDevicesAsync, AppConfig.PreferredVideoInputDevice,
                d => SelectedVideoInputDevice = d);
            await LoadCategoryAsync(GpsSensors, _devices.GetGpsSensorsAsync, AppConfig.PreferredGpsSensor,
                d => SelectedGpsSensor = d);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not list devices: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static async Task LoadCategoryAsync(
        ObservableCollection<MediaDeviceInfo> target,
        Func<Task<List<MediaDeviceInfo>>> fetch,
        (string? Id, string? Name) preferred,
        Action<MediaDeviceInfo?> select)
    {
        target.Clear();
        var devices = await fetch();
        foreach (var device in devices)
        {
            target.Add(device);
        }

        select(preferred.Id is null ? null : devices.FirstOrDefault(d => d.Id == preferred.Id));
    }

    [RelayCommand]
    private void Save()
    {
        // Validate the start sequence first and save nothing if it's wrong — a half-saved settings page
        // that silently kept the old sequence would only be discovered at the start of a race.
        if (!StartSequenceSettings.TryParse(SequenceAlarmsText, SequenceCountdownText, SequenceSignalsText, out _, out var sequenceError))
        {
            StatusMessage = $"Not saved — start sequence: {sequenceError}";
            return;
        }

        AppConfig.SequenceAlarmsText = SequenceAlarmsText.Trim();
        AppConfig.SequenceCountdownText = SequenceCountdownText.Trim();
        AppConfig.SequenceSignalsText = SequenceSignalsText.Trim();

        AppConfig.ServerBaseUrl = string.IsNullOrWhiteSpace(ServerBaseUrlText)
            ? AppConfig.ServerBaseUrl
            : ServerBaseUrlText.Trim();

        AppConfig.PreferredAudioOutputDevice = (SelectedAudioOutputDevice?.Id, SelectedAudioOutputDevice?.Name);
        AppConfig.PreferredAudioInputDevice = (SelectedAudioInputDevice?.Id, SelectedAudioInputDevice?.Name);
        AppConfig.PreferredVideoInputDevice = (SelectedVideoInputDevice?.Id, SelectedVideoInputDevice?.Name);
        AppConfig.PreferredGpsSensor = (SelectedGpsSensor?.Id, SelectedGpsSensor?.Name);

        StatusMessage = "Settings saved.";
    }
}
