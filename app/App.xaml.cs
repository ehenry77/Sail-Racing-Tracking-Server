using SailRacing.Services;
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

		// Closing the app while a start video is being recorded: finish the file properly rather than cut it off.
		window.Destroying += (_, _) =>
		{
			try
			{
				var video = IPlatformApplication.Current?.Services.GetService<IStartVideoService>();
				if (video is not null)
				{
					// On a pool thread: waiting for it here, on the UI thread, must not wait for the UI thread.
					Task.Run(() => video.StopAsync()).Wait(TimeSpan.FromSeconds(8));
				}
			}
			catch
			{
				// closing anyway
			}
		};

		return window;
	}
}
