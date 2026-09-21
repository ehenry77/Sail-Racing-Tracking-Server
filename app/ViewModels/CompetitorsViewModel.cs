using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SailRacing.Data;
using SailRacing.Models;

namespace SailRacing.ViewModels;

public partial class CompetitorsViewModel : BaseViewModel
{
    private readonly IParticipantRepository _participants;

    public ObservableCollection<Participant> Participants { get; } = new();

    [ObservableProperty]
    private Participant? selected;

    [ObservableProperty]
    private bool isEditing;

    [ObservableProperty]
    private string editName = string.Empty;

    [ObservableProperty]
    private string editHelm = string.Empty;

    [ObservableProperty]
    private string editTcf = "1.0";

    private string? _editId;

    public CompetitorsViewModel(IParticipantRepository participants)
    {
        _participants = participants;
        Title = "Competitors";
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
            var items = await _participants.GetAllAsync();
            Participants.Clear();
            foreach (var item in items)
            {
                Participants.Add(item);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void AddNew()
    {
        _editId = null;
        EditName = string.Empty;
        EditHelm = string.Empty;
        EditTcf = "1.0";
        IsEditing = true;
    }

    [RelayCommand]
    private void Edit(Participant participant)
    {
        _editId = participant.Id;
        EditName = participant.Name;
        EditHelm = participant.Helm;
        EditTcf = participant.Tcf.ToString("0.###");
        IsEditing = true;
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

        if (!double.TryParse(EditTcf, out var tcf))
        {
            tcf = 1.0;
        }

        var participant = _editId is null
            ? new Participant()
            : Participants.First(p => p.Id == _editId);

        participant.Name = EditName.Trim();
        participant.Helm = EditHelm.Trim();
        participant.Tcf = tcf;

        await _participants.SaveAsync(participant);
        IsEditing = false;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteAsync(Participant participant)
    {
        await _participants.DeleteAsync(participant);
        await LoadAsync();
    }
}
