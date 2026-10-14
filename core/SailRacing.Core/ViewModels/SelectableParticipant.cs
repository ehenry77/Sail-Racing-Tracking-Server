using CommunityToolkit.Mvvm.ComponentModel;
using SailRacing.Models;

namespace SailRacing.ViewModels;

public partial class SelectableParticipant : ObservableObject
{
    public Participant Participant { get; }

    [ObservableProperty]
    private bool isSelected;

    public string Name => Participant.Name;

    public SelectableParticipant(Participant participant, bool isSelected)
    {
        Participant = participant;
        this.isSelected = isSelected;
    }
}
