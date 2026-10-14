using Avalonia.Controls;

namespace SailRacing.Desktop.Views;

/// <summary>
/// A screen of the app. Does what MAUI's Page.OnAppearing/OnDisappearing did: the view model loads when the
/// screen is shown and lets go of its live connections and timers when it is replaced.
/// </summary>
public abstract class ScreenView : UserControl
{
    protected ScreenView()
    {
        AttachedToVisualTree += async (_, _) =>
        {
            try
            {
                await OnAppearingAsync();
            }
            catch (Exception ex)
            {
                // A failure while loading must never take the app down; the screen just stays as it is.
                System.Diagnostics.Trace.TraceError($"{GetType().Name} failed to load: {ex}");
            }
        };

        DetachedFromVisualTree += (_, _) => OnDisappearing();
    }

    protected virtual Task OnAppearingAsync() => Task.CompletedTask;

    protected virtual void OnDisappearing()
    {
    }
}
