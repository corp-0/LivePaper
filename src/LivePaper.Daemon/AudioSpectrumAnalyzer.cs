using LivePaper.Protocol;

namespace LivePaper.Daemon;

internal sealed class AudioSpectrumAnalyzer
{
    internal const int SampleRate = 48_000;
    internal const int WindowSize = 2_048;
    internal const int HopSize = 800;
    private const int BandsPerChannel = AudioSpectrumChanged.SampleCount / 2;
    private readonly float[] _left = new float[WindowSize];
    private readonly float[] _right = new float[WindowSize];
    private readonly float[] _smoothed = new float[AudioSpectrumChanged.SampleCount];
    private readonly double[] _real = new double[WindowSize];
    private readonly double[] _imaginary = new double[WindowSize];
    private readonly double[] _window = new double[WindowSize];
    private int _sampleCount;

    public AudioSpectrumAnalyzer()
    {
        for (var index = 0; index < WindowSize; index++)
        {
            _window[index] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * index / (WindowSize - 1));
        }
    }

    public void AddInterleavedSamples(
        ReadOnlySpan<float> samples,
        Action<AudioSpectrumChanged> onSpectrum)
    {
        for (var index = 0; index + 1 < samples.Length; index += 2)
        {
            _left[_sampleCount] = samples[index];
            _right[_sampleCount] = samples[index + 1];
            _sampleCount++;
            if (_sampleCount != WindowSize)
            {
                continue;
            }

            var frame = new float[AudioSpectrumChanged.SampleCount];
            AnalyzeChannel(_left, frame.AsSpan(0, BandsPerChannel), 0);
            AnalyzeChannel(_right, frame.AsSpan(BandsPerChannel, BandsPerChannel), BandsPerChannel);
            onSpectrum(new AudioSpectrumChanged(frame));
            Array.Copy(_left, HopSize, _left, 0, WindowSize - HopSize);
            Array.Copy(_right, HopSize, _right, 0, WindowSize - HopSize);
            _sampleCount = WindowSize - HopSize;
        }

    }

    private void AnalyzeChannel(float[] input, Span<float> output, int smoothingOffset)
    {
        for (var index = 0; index < WindowSize; index++)
        {
            _real[index] = input[index] * _window[index];
            _imaginary[index] = 0;
        }

        Transform();
        const double minimumFrequency = 40;
        var maximumFrequency = SampleRate / 2d;
        for (var band = 0; band < BandsPerChannel; band++)
        {
            var lowFrequency = minimumFrequency * Math.Pow(
                maximumFrequency / minimumFrequency,
                band / (double)BandsPerChannel);
            var highFrequency = minimumFrequency * Math.Pow(
                maximumFrequency / minimumFrequency,
                (band + 1d) / BandsPerChannel);
            var lowBin = Math.Max(1, (int)Math.Floor(lowFrequency * WindowSize / SampleRate));
            var highBin = Math.Min(WindowSize / 2, Math.Max(lowBin + 1,
                (int)Math.Ceiling(highFrequency * WindowSize / SampleRate)));
            double energy = 0;
            for (var bin = lowBin; bin < highBin; bin++)
            {
                energy += _real[bin] * _real[bin] + _imaginary[bin] * _imaginary[bin];
            }

            var magnitude = Math.Sqrt(energy / (highBin - lowBin)) / (WindowSize / 2d);
            var normalized = (float)Math.Clamp(1 - Math.Exp(-magnitude * 24), 0, 1);
            var smoothingIndex = smoothingOffset + band;
            var amount = normalized > _smoothed[smoothingIndex] ? 0.65f : 0.15f;
            _smoothed[smoothingIndex] += (normalized - _smoothed[smoothingIndex]) * amount;
            output[band] = _smoothed[smoothingIndex];
        }
    }

    private void Transform()
    {
        for (int index = 1, reversed = 0; index < WindowSize; index++)
        {
            var bit = WindowSize >> 1;
            while ((reversed & bit) != 0)
            {
                reversed ^= bit;
                bit >>= 1;
            }

            reversed ^= bit;
            if (index >= reversed)
            {
                continue;
            }

            (_real[index], _real[reversed]) = (_real[reversed], _real[index]);
            (_imaginary[index], _imaginary[reversed]) = (_imaginary[reversed], _imaginary[index]);
        }

        for (var length = 2; length <= WindowSize; length <<= 1)
        {
            var angle = -2 * Math.PI / length;
            var stepReal = Math.Cos(angle);
            var stepImaginary = Math.Sin(angle);
            for (var start = 0; start < WindowSize; start += length)
            {
                var twiddleReal = 1d;
                var twiddleImaginary = 0d;
                for (var offset = 0; offset < length / 2; offset++)
                {
                    var even = start + offset;
                    var odd = even + length / 2;
                    var oddReal = _real[odd] * twiddleReal - _imaginary[odd] * twiddleImaginary;
                    var oddImaginary = _real[odd] * twiddleImaginary + _imaginary[odd] * twiddleReal;
                    _real[odd] = _real[even] - oddReal;
                    _imaginary[odd] = _imaginary[even] - oddImaginary;
                    _real[even] += oddReal;
                    _imaginary[even] += oddImaginary;
                    var nextReal = twiddleReal * stepReal - twiddleImaginary * stepImaginary;
                    twiddleImaginary = twiddleReal * stepImaginary + twiddleImaginary * stepReal;
                    twiddleReal = nextReal;
                }
            }
        }
    }
}
