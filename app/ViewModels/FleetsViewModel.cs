using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SailRacing.Data;
using SailRacing.Models;
using SailRacing.Services;

namespace SailRacing.ViewModels;

public partial class FleetsViewModel : BaseViewModel
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static readonly FilePickerFileType JsonFileType = new(new Dictionary<DevicePlatform, IEnumerable<string>>
    {
        { DevicePlatform.iOS, new[] { "public.json" } },
        { DevicePlatform.Android, new[] { "application/json" } },
        { DevicePlatform.WinUI, new[] { ".json" } },
        { DevicePlatform.macOS, new[] { "json" } },
        { DevicePlatform.MacCatalyst, new[] { "public.json" } }
    });

    private readonly IFleetRepository _fleets;
    private readonly IParticipantRepository _participants;

    public ObservableCollection<Fleet> Fleets { get; } = new();

    public ObservableCollection<SelectableParticipant> EditParticipants { get; } = new();

    [ObservableProperty]
    private bool isEditing;

    [ObservableProperty]
    private string editName = string.Empty;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    private string? _editId;

    public FleetsViewModel(IFleetRepository fleets, IParticipantRepository participants)
    {
        _fleets = fleets;
        _participants = participants;
        Title = "Fleets";
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
            var items = await _fleets.GetAllAsync();
            Fleets.Clear();
            foreach (var item in items)
            {
                Fleets.Add(item);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AddNewAsync()
    {
        _editId = null;
        EditName = string.Empty;
        await LoadEditParticipantsAsync(Array.Empty<string>());
        IsEditing = true;
    }

    [RelayCommand]
    private async Task EditAsync(Fleet fleet)
    {
        _editId = fleet.Id;
        EditName = fleet.Name;
        var selectedIds = await _fleets.GetParticipantIdsAsync(fleet.Id);
        await LoadEditParticipantsAsync(selectedIds);
        IsEditing = true;
    }

    private async Task LoadEditParticipantsAsync(IEnumerable<string> selectedIds)
    {
        var selectedSet = selectedIds.ToHashSet();
        var all = await _participants.GetAllAsync();
        EditParticipants.Clear();
        foreach (var participant in all)
        {
            EditParticipants.Add(new SelectableParticipant(participant, selectedSet.Contains(participant.Id)));
        }
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(EditName))
        {
            return;
        }

        var fleet = _editId is null
            ? new Fleet()
            : Fleets.First(f => f.Id == _editId);

        fleet.Name = EditName.Trim();

        var selectedIds = EditParticipants.Where(p => p.IsSelected).Select(p => p.Participant.Id);
        await _fleets.SaveAsync(fleet, selectedIds);
        IsEditing = false;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteAsync(Fleet fleet)
    {
        await _fleets.DeleteAsync(fleet);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task ExportAllAsync()
    {
        try
        {
            var export = new List<FleetExportDto>();
            foreach (var fleet in Fleets)
            {
                var participantIds = await _fleets.GetParticipantIdsAsync(fleet.Id);
                var participants = await _participants.GetByIdsAsync(participantIds);

                export.Add(new FleetExportDto
                {
                    Name = fleet.Name,
                    Participants = participants
                        .Select(p => new ParticipantExportDto { Name = p.Name, Helm = p.Helm, Tcf = p.Tcf })
                        .ToList()
                });
            }

            var json = JsonSerializer.Serialize(export, JsonOptions);
            var filePath = Path.Combine(FileSystem.CacheDirectory, "sail-racing-fleets-export.json");
            await File.WriteAllTextAsync(filePath, json);

            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Export Fleets",
                File = new ShareFile(filePath)
            });

            StatusMessage = $"Exported {export.Count} fleet(s).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Import fleets",
                FileTypes = JsonFileType
            });

            if (result is null)
            {
                return;
            }

            var json = await File.ReadAllTextAsync(result.FullPath);
            var imported = JsonSerializer.Deserialize<List<FleetExportDto>>(json, JsonOptions);
            if (imported is null || imported.Count == 0)
            {
                StatusMessage = "No fleets found in that file.";
                return;
            }

            foreach (var fleetDto in imported)
            {
                var participantIds = new List<string>();
                foreach (var participantDto in fleetDto.Participants)
                {
                    var participant = new Participant
                    {
                        Name = participantDto.Name,
                        Helm = participantDto.Helm,
                        Tcf = participantDto.Tcf
                    };
                    await _participants.SaveAsync(participant);
                    participantIds.Add(participant.Id);
                }

                var fleet = new Fleet { Name = fleetDto.Name };
                await _fleets.SaveAsync(fleet, participantIds);
            }

            StatusMessage = $"Imported {imported.Count} fleet(s).";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Import failed: {ex.Message}";
        }
    }
}
