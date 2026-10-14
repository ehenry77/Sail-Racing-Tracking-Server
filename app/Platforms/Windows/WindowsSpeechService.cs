using System.Collections.Concurrent;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;

namespace SailRacing.Services;

/// <summary>
/// Start-sequence speech on Windows with no start-up delay. The stock text-to-speech call builds a
/// synthesizer and a media player for every phrase, which takes a quarter of a second warm and more than a
/// second the first time — enough for "nine" to be heard while the counter already reads 8. Here every
/// phrase is rendered once, ahead of time, into a media player that is already loaded; speaking it is then
/// a single Play() that starts in about 10 ms.
/// </summary>
public class WindowsSpeechService : ISpeechService
{
    private readonly SpeechSynthesizer _synth = new();
    private readonly ConcurrentDictionary<string, MediaPlayer> _prepared = new();
    private readonly SemaphoreSlim _prepareLock = new(1, 1);
    private readonly SailRacing.Platform.MauiTextSpeechService _fallback = new();

    public async Task PrepareAsync(IEnumerable<string> phrases)
    {
        await _prepareLock.WaitAsync();
        try
        {
            foreach (var phrase in phrases.Distinct())
            {
                if (string.IsNullOrWhiteSpace(phrase) || _prepared.ContainsKey(phrase))
                {
                    continue;
                }

                try
                {
                    var stream = await _synth.SynthesizeTextToStreamAsync(phrase);
                    var player = new MediaPlayer { AutoPlay = false };
                    player.Source = MediaSource.CreateFromStream(stream, stream.ContentType);
                    _prepared[phrase] = player;
                }
                catch
                {
                    // That phrase will use the normal text-to-speech when its turn comes.
                }
            }
        }
        finally
        {
            _prepareLock.Release();
        }
    }

    public async Task SpeakAsync(string text, CancellationToken token)
    {
        if (!_prepared.TryGetValue(text, out var player))
        {
            await _fallback.SpeakAsync(text, token);
            return;
        }

        token.ThrowIfCancellationRequested();

        var finished = new TaskCompletionSource();
        void OnEnded(MediaPlayer sender, object args) => finished.TrySetResult();
        void OnFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args) => finished.TrySetResult();

        player.MediaEnded += OnEnded;
        player.MediaFailed += OnFailed;
        using var cancellation = token.Register(() =>
        {
            player.Pause();
            finished.TrySetCanceled(token);
        });

        try
        {
            player.PlaybackSession.Position = TimeSpan.Zero;
            player.Play();
            await finished.Task;
        }
        finally
        {
            player.MediaEnded -= OnEnded;
            player.MediaFailed -= OnFailed;
        }
    }
}
