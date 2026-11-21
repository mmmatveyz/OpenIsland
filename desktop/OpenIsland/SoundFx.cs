using System;
using System.IO;
using System.Media;

namespace OpenIsland;

public enum SoundCue
{
    Click,
    Pop,
    Tuck,
}

/// <summary>
/// Звуковые микро-эффекты интерфейса (UI Sound FX).
/// Звуки синтезируются в памяти без внешних файлов и воспроизводятся с нулевой задержкой через SoundPlayer.
/// </summary>
public static class SoundFx
{
    const int SampleRate = 44100;

    static readonly SoundPlayer? ClickPlayer;
    static readonly SoundPlayer? PopPlayer;
    static readonly SoundPlayer? TuckPlayer;

    static SoundFx()
    {
        try
        {
            ClickPlayer = CreatePlayer(GenerateClickSamples());
            PopPlayer = CreatePlayer(GeneratePopSamples());
            TuckPlayer = CreatePlayer(GenerateTuckSamples());
        }
        catch (Exception ex)
        {
            App.LogDebug($"SoundFx: init error: {ex.Message}");
        }
    }

    /// <summary>
    /// Воспроизводит короткий микрозвук интерфейса, если включено в настройках.
    /// </summary>
    public static void Play(SoundCue cue)
    {
        if (!Settings.UiSoundFx) return;

        try
        {
            SoundPlayer? player = cue switch
            {
                SoundCue.Click => ClickPlayer,
                SoundCue.Pop => PopPlayer,
                SoundCue.Tuck => TuckPlayer,
                _ => null,
            };

            player?.Play();
        }
        catch
        {
            // Аудиоустройство недоступно или звук отключен в системе
        }
    }

    static SoundPlayer CreatePlayer(short[] samples)
    {
        byte[] wavBytes = CreateWavBytes(samples, SampleRate);
        var ms = new MemoryStream(wavBytes);
        var player = new SoundPlayer(ms);
        player.Load();
        return player;
    }

    /// <summary>
    /// Создаёт стандартный 16-битный моно WAV-поток в памяти.
    /// </summary>
    public static byte[] CreateWavBytes(short[] samples, int sampleRate)
    {
        int dataSize = samples.Length * sizeof(short);
        byte[] wav = new byte[44 + dataSize];
        using var ms = new MemoryStream(wav);
        using var bw = new BinaryWriter(ms);

        // RIFF header
        bw.Write(new char[] { 'R', 'I', 'F', 'F' });
        bw.Write(36 + dataSize);
        bw.Write(new char[] { 'W', 'A', 'V', 'E' });

        // fmt chunk
        bw.Write(new char[] { 'f', 'm', 't', ' ' });
        bw.Write(16); // Subchunk1Size
        bw.Write((short)1); // PCM
        bw.Write((short)1); // Mono
        bw.Write(sampleRate);
        bw.Write(sampleRate * 2); // ByteRate
        bw.Write((short)2); // BlockAlign
        bw.Write((short)16); // BitsPerSample

        // data chunk
        bw.Write(new char[] { 'd', 'a', 't', 'a' });
        bw.Write(dataSize);

        for (int i = 0; i < samples.Length; i++)
        {
            bw.Write(samples[i]);
        }

        return wav;
    }

    /// <summary>
    /// Короткий щелчок для переключателей и кнопок (12 мс, ~1400 Гц с экспоненциальным затуханием).
    /// </summary>
    public static short[] GenerateClickSamples()
    {
        int count = (int)(SampleRate * 0.012);
        short[] samples = new short[count];
        for (int i = 0; i < count; i++)
        {
            double t = (double)i / SampleRate;
            double decay = Math.Exp(-i * 14.0 / count);
            double wave = Math.Sin(2 * Math.PI * 1400 * t);
            samples[i] = (short)(wave * decay * 11000);
        }
        return samples;
    }

    /// <summary>
    /// Мягкий округлый «поп» для раскрытия карточек и добавления файлов (30 мс, слайд 460 -> 240 Гц).
    /// </summary>
    public static short[] GeneratePopSamples()
    {
        int count = (int)(SampleRate * 0.030);
        short[] samples = new short[count];
        double phase = 0;
        for (int i = 0; i < count; i++)
        {
            double progress = (double)i / count;
            double freq = 460 - (220 * progress);
            phase += 2 * Math.PI * freq / SampleRate;
            double envelope = Math.Sin(Math.PI * Math.Pow(progress, 0.45)) * Math.Exp(-progress * 4.5);
            samples[i] = (short)(Math.Sin(phase) * envelope * 14000);
        }
        return samples;
    }

    /// <summary>
    /// Мягкий тихий «тык» при сворачивании карточек (22 мс, слайд 280 -> 160 Гц).
    /// </summary>
    public static short[] GenerateTuckSamples()
    {
        int count = (int)(SampleRate * 0.022);
        short[] samples = new short[count];
        double phase = 0;
        for (int i = 0; i < count; i++)
        {
            double progress = (double)i / count;
            double freq = 280 - (120 * progress);
            phase += 2 * Math.PI * freq / SampleRate;
            double envelope = Math.Sin(Math.PI * Math.Pow(progress, 0.5)) * Math.Exp(-progress * 5.0);
            samples[i] = (short)(Math.Sin(phase) * envelope * 12000);
        }
        return samples;
    }
}
