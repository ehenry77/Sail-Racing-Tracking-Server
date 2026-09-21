using SailRacing.ViewModels;

namespace SailRacing.Views.Race;

public partial class RaceSetupPage : ContentPage
{
    public RaceSetupPage(RaceSetupViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
