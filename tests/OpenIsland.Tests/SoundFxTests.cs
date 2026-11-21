using System.Text;
using OpenIsland;
using Xunit;

namespace OpenIsland.Tests;

public class SoundFxTests
{
    [Fact]
    public void CreateWavBytes_ValidHeaderAndLength()
    {
        short[] samples = [0, 1000, -1000, 500, -500];
        byte[] wav = SoundFx.CreateWavBytes(samples, 44100);

        Assert.Equal(44 + (samples.Length * 2), wav.Length);

        // RIFF header
        string riff = Encoding.ASCII.GetString(wav, 0, 4);
        Assert.Equal("RIFF", riff);

        string wave = Encoding.ASCII.GetString(wav, 8, 4);
        Assert.Equal("WAVE", wave);

        // fmt chunk
        string fmt = Encoding.ASCII.GetString(wav, 12, 4);
        Assert.Equal("fmt ", fmt);

        // data chunk
        string data = Encoding.ASCII.GetString(wav, 36, 4);
        Assert.Equal("data", data);

        int dataLength = BitConverter.ToInt32(wav, 40);
        Assert.Equal(samples.Length * 2, dataLength);
    }

    [Fact]
    public void GenerateSamples_ReturnsNonEmptyBuffers()
    {
        short[] click = SoundFx.GenerateClickSamples();
        Assert.True(click.Length > 0);

        short[] pop = SoundFx.GeneratePopSamples();
        Assert.True(pop.Length > 0);

        short[] tuck = SoundFx.GenerateTuckSamples();
        Assert.True(tuck.Length > 0);
    }
}
