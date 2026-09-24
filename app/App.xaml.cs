using SailRacing.Views;

namespace SailRacing;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new SplashPage());

		// Windows runs this app unpackaged, so there's no OS-level splash screen to rely on — show
		// this page briefly ourselves, then swap in the real app shell.
		window.Created += async (_, _) =>
		{
			await Task.Delay(1500);
			window.Page = new AppShell();
		};

		return window;
	}
}
