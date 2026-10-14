using CommunityToolkit.Mvvm.ComponentModel;

namespace SailRacing.ViewModels;

public partial class RaceParticipantEditItem : ObservableObject
{
    public string ParticipantId { get; set; } = string.Empty;

    public string ParticipantName { get; set; } = string.Empty;

    [ObservableProperty]
    private string lapsText = "3";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasJoinUrl))]
    [NotifyPropertyChangedFor(nameof(JoinLinkText))]
    private string? joinUrl;

    public bool HasJoinUrl => !string.IsNullOrEmpty(JoinUrl);

    public string JoinLinkText => JoinUrl ?? Services.JoinLinks.NotSyncedText;
}
