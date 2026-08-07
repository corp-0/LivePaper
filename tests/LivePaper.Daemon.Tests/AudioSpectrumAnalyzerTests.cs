using LivePaper.Protocol;
using Xunit;

namespace LivePaper.Daemon.Tests;

public class AudioSpectrumAnalyzerTests
{
    [Fact]
    public void SeparatesChannelFrequenciesIntoWallpaperEngineBands()
    {
        var analyzer = new AudioSpectrumAnalyzer();
        var samples = new float[AudioSpectrumAnalyzer.WindowSize * 4];
        for (var frame = 0; frame < samples.Length / 2; frame++)
        {
            samples[frame * 2] = (float)(0.8 * Math.Sin(
                2 * Math.PI * 440 * frame / AudioSpectrumAnalyzer.SampleRate));
            samples[frame * 2 + 1] = (float)(0.8 * Math.Sin(
                2 * Math.PI * 4_000 * frame / AudioSpectrumAnalyzer.SampleRate));
        }

        AudioSpectrumChanged? spectrum = null;
        analyzer.AddInterleavedSamples(samples, frame => spectrum = frame);

        Assert.NotNull(spectrum);
        var leftPeak = FindPeak(spectrum.Samples.AsSpan(0, 64));
        var rightPeak = FindPeak(spectrum.Samples.AsSpan(64, 64));
        Assert.True(leftPeak < rightPeak);
        Assert.True(spectrum.Samples[leftPeak] > 0.1f);
        Assert.True(spectrum.Samples[64 + rightPeak] > 0.1f);
    }

    private static int FindPeak(ReadOnlySpan<float> values)
    {
        var peak = 0;
        for (var index = 1; index < values.Length; index++)
        {
            if (values[index] > values[peak])
            {
                peak = index;
            }
        }

        return peak;
    }
}
