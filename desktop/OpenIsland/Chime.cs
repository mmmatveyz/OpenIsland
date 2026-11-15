using System.IO;
using System.Windows.Media;

namespace OpenIsland;

/// <summary>
/// Короткий звук завершения задачи. Источник — <c>Assets/staplebops-01.mp3</c>
/// (звук OpenCode, лицензия MIT), копируется рядом с исполняемым файлом.
/// </summary>
sealed class Chime
{
    static readonly string Source = Path.Combine(AppContext.BaseDirectory, "Assets", "staplebops-01.mp3");

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
        if (_opened || !File.Exists(Source)) return;
        try
        {
            _player.Open(new Uri(Source, UriKind.Absolute));
            _opened = true;
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }
}
