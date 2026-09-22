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

    [ObservableProperty]
    private string serverBaseUrlText = string.Empty;

    [ObservableProperty]
    private MediaDeviceInfo? selectedAudioOutputDevice;

    [ObservableProperty]
    private MediaDeviceInfo? selectedAudioInputDevice;

    [ObservableProperty]
    private MediaDeviceInfo? selectedVideoInputDevice;

    [ObservableProperty]
    private string statusMessage = string.Empty;

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

            await LoadCategoryAsync(AudioOutputDevices, _devices.GetAudioOutputDevicesAsync, AppConfig.PreferredAudioOutputDevice,
                d => SelectedAudioOutputDevice = d);
            await LoadCategoryAsync(AudioInputDevices, _devices.GetAudioInputDevicesAsync, AppConfig.PreferredAudioInputDevice,
                d => SelectedAudioInputDevice = d);
            await LoadCategoryAsync(VideoInputDevices, _devices.GetVideoInputDevicesAsync, AppConfig.PreferredVideoInputDevice,
                d => SelectedVideoInputDevice = d);
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
        AppConfig.ServerBaseUrl = string.IsNullOrWhiteSpace(ServerBaseUrlText)
            ? AppConfig.ServerBaseUrl
            : ServerBaseUrlText.Trim();

        AppConfig.PreferredAudioOutputDevice = (SelectedAudioOutputDevice?.Id, SelectedAudioOutputDevice?.Name);
        AppConfig.PreferredAudioInputDevice = (SelectedAudioInputDevice?.Id, SelectedAudioInputDevice?.Name);
        AppConfig.PreferredVideoInputDevice = (SelectedVideoInputDevice?.Id, SelectedVideoInputDevice?.Name);

        StatusMessage = "Settings saved.";
    }
}
