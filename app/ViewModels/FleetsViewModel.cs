using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SailRacing.Data;
using SailRacing.Models;

namespace SailRacing.ViewModels;

public partial class FleetsViewModel : BaseViewModel
{
    private readonly IFleetRepository _fleets;
    private readonly IParticipantRepository _participants;

    public ObservableCollection<Fleet> Fleets { get; } = new();

    public ObservableCollection<SelectableParticipant> EditParticipants { get; } = new();

    [ObservableProperty]
    private bool isEditing;

    [ObservableProperty]
    private string editName = string.Empty;

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
}
