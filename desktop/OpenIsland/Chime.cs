using System.Windows.Media;

namespace DynamicIsland;

/// <summary>
/// Короткий звук завершения задачи. Источник — встроенный ресурс
/// <c>Assets/staplebops-01.mp3</c> (OpenCode, лицензия MIT).
/// </summary>
sealed class Chime
{
    static readonly Uri Source = new("pack://application:,,,/Assets/staplebops-01.mp3", UriKind.Absolute);

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
        try
        {
            _player.Open(Source);
            _opened = true;
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }
}
