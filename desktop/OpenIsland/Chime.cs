using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Resources;

namespace OpenIsland;

/// <summary>
/// Короткий звук завершения задачи. Источник — <c>Assets/staplebops-01.mp3</c>
/// (звук OpenCode, лицензия MIT). Файл встроен в exe как WPF-ресурс и при первом
/// использовании выгружается в кэш: так звук доезжает и в portable-exe, и после
/// автообновления, а <see cref="MediaPlayer"/> умеет играть только с диска.
/// </summary>
sealed class Chime
{
    static readonly string CachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenIsland", "staplebops-01.mp3");

    readonly MediaPlayer _player = new() { Volume = 0.8 };
    bool _opened;

    public Chime()
    {
        _player.MediaFailed += (_, e) => App.LogDebug($"Chime: media failed: {e.ErrorException?.Message}");
        Preload();
    }

    public void Play()
    {
        try
        {
            Preload();
            if (_player.NaturalDuration.HasTimeSpan) _player.Position = TimeSpan.Zero;
            _player.Play();
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }

    void Preload()
    {
        if (_opened) return;

        string? source = Materialize();
        if (source == null) return;

        try
        {
            _player.Open(new Uri(source, UriKind.Absolute));
            _opened = true;
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }

    static string? Materialize()
    {
        try
        {
            if (File.Exists(CachePath)) return CachePath;

            StreamResourceInfo? resource = Application.GetResourceStream(
                new Uri("Assets/staplebops-01.mp3", UriKind.Relative));
            if (resource == null)
            {
                App.LogDebug("Chime: embedded sound not found.");
                return null;
            }

            string? folder = Path.GetDirectoryName(CachePath);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

            using (Stream source = resource.Stream)
            using (FileStream target = File.Create(CachePath))
            {
                source.CopyTo(target);
            }

            App.LogDebug("Chime: embedded sound materialized.");
            return CachePath;
        }
        catch (Exception ex)
        {
            App.Log(ex);
            return null;
        }
    }
}
