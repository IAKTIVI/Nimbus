using System.Windows.Media;
using Nimbus.Core;

namespace Nimbus.UI;

/// <summary>Plays a death sound on the Mods page, so it can be heard before picking it.</summary>
internal static class SoundPreview
{
    private const string LogIdent = "SoundPreview";

    private static readonly string Folder = Path.Combine(Path.GetTempPath(), "nimbus-sound-preview");
    private static MediaPlayer? _player; // kept so it isn't collected mid-sound

    /// <summary>Plays the sound from a temporary copy (Windows plays .ogg, .mp3 and .wav). Silently does nothing on failure.</summary>
    public static void Play(byte[]? data, string extension)
    {
        if (data is null)
            return;

        try
        {
            _player?.Close();
            Directory.CreateDirectory(Folder);
            foreach (string old in Directory.EnumerateFiles(Folder))
            {
                try { File.Delete(old); }
                catch (IOException) { /* still playing in another Nimbus window */ }
            }

            string file = Path.Combine(Folder, Guid.NewGuid().ToString("N") + extension);
            File.WriteAllBytes(file, data);
            _player = new MediaPlayer { Volume = 0.8 };
            _player.MediaFailed += (_, e) => Logger.Warn(LogIdent, e.ErrorException.Message);
            _player.Open(new Uri(file));
            _player.Play();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Logger.Warn(LogIdent, ex.Message);
        }
    }
}
