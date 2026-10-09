using CommunityToolkit.Mvvm.ComponentModel;

namespace SailRacing.ViewModels;

public partial class BoatLinkItem : ObservableObject
{
    public string ParticipantId { get; init; } = string.Empty;

    public string ParticipantName { get; init; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasJoinUrl))]
    [NotifyPropertyChangedFor(nameof(JoinLinkText))]
    private string? joinUrl;

    public bool HasJoinUrl => !string.IsNullOrEmpty(JoinUrl);

    public string JoinLinkText => JoinUrl ?? Services.JoinLinks.NotSyncedText;
}
