using System.Diagnostics;

namespace GDI.Core.Services;

public sealed class AudioMeterState
{
    public float PeakLeftDb { get; set; } = -60f;
    public float PeakRightDb { get; set; } = -60f;
    public float RmsLeftDb { get; set; } = -60f;
    public float RmsRightDb { get; set; } = -60f;
    public float PeakHoldLeftDb { get; set; } = -60f;
    public float PeakHoldRightDb { get; set; } = -60f;
    public bool IsClippingLeft { get; set; }
    public bool IsClippingRight { get; set; }
    public bool IsSilent { get; set; } = true;
    public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AudioMeterEngine
{
    private const float MinDb = -60f;
    private const float ClipThreshold = 0.999f;
    private const float SilenceThresholdDb = -58f;
    private const float DecayRatePerSec = 28f; // Standard broadcast VU release ballistics (28 dB/s)

    private float _peakHoldLeft = MinDb;
    private float _peakHoldRight = MinDb;
    private long _peakHoldLeftUntilTicks;
    private long _peakHoldRightUntilTicks;
    private long _lastProcessedTicks = Stopwatch.GetTimestamp();

    public AudioMeterState ProcessPcm16Stereo(ReadOnlySpan<byte> pcmBytes)
    {
        var state = new AudioMeterState();
        int sampleCount = pcmBytes.Length / 2; // 16-bit = 2 bytes per sample
        if (sampleCount < 2)
        {
            state.PeakLeftDb = MinDb;
            state.PeakRightDb = MinDb;
            state.RmsLeftDb = MinDb;
            state.RmsRightDb = MinDb;
            state.PeakHoldLeftDb = MinDb;
            state.PeakHoldRightDb = MinDb;
            state.IsSilent = true;
            return state;
        }

        int frameCount = sampleCount / 2; // stereo pairs
        float peakL = 0f;
        float peakR = 0f;
        double sumSqL = 0.0;
        double sumSqR = 0.0;

        for (int i = 0; i < frameCount; i++)
        {
            int offset = i * 4;
            short sL = (short)(pcmBytes[offset] | (pcmBytes[offset + 1] << 8));
            short sR = (short)(pcmBytes[offset + 2] | (pcmBytes[offset + 3] << 8));

            float nL = Math.Abs(sL / 32768f);
            float nR = Math.Abs(sR / 32768f);

            if (nL > peakL) peakL = nL;
            if (nR > peakR) peakR = nR;

            sumSqL += nL * nL;
            sumSqR += nR * nR;
        }

        // dBFS = 20 * log10(amplitude)
        float currentPeakLeftDb = peakL > 0.000001f ? (float)(20.0 * Math.Log10(peakL)) : MinDb;
        float currentPeakRightDb = peakR > 0.000001f ? (float)(20.0 * Math.Log10(peakR)) : MinDb;

        double rmsL = Math.Sqrt(sumSqL / frameCount);
        double rmsR = Math.Sqrt(sumSqR / frameCount);
        float currentRmsLeftDb = rmsL > 0.000001 ? (float)(20.0 * Math.Log10(rmsL)) : MinDb;
        float currentRmsRightDb = rmsR > 0.000001 ? (float)(20.0 * Math.Log10(rmsR)) : MinDb;

        currentPeakLeftDb = Math.Clamp(currentPeakLeftDb, MinDb, 0f);
        currentPeakRightDb = Math.Clamp(currentPeakRightDb, MinDb, 0f);
        currentRmsLeftDb = Math.Clamp(currentRmsLeftDb, MinDb, 0f);
        currentRmsRightDb = Math.Clamp(currentRmsRightDb, MinDb, 0f);

        // Peak hold management with 1.5s hold time
        long nowTicks = Stopwatch.GetTimestamp();
        double elapsedSec = Math.Max(0.001, (nowTicks - _lastProcessedTicks) / (double)Stopwatch.Frequency);
        _lastProcessedTicks = nowTicks;

        long holdDurationTicks = (long)(1.5 * Stopwatch.Frequency);

        if (currentPeakLeftDb >= _peakHoldLeft)
        {
            _peakHoldLeft = currentPeakLeftDb;
            _peakHoldLeftUntilTicks = nowTicks + holdDurationTicks;
        }
        else if (nowTicks > _peakHoldLeftUntilTicks)
        {
            _peakHoldLeft = Math.Max(MinDb, _peakHoldLeft - (float)(DecayRatePerSec * elapsedSec));
        }

        if (currentPeakRightDb >= _peakHoldRight)
        {
            _peakHoldRight = currentPeakRightDb;
            _peakHoldRightUntilTicks = nowTicks + holdDurationTicks;
        }
        else if (nowTicks > _peakHoldRightUntilTicks)
        {
            _peakHoldRight = Math.Max(MinDb, _peakHoldRight - (float)(DecayRatePerSec * elapsedSec));
        }

        state.PeakLeftDb = currentPeakLeftDb;
        state.PeakRightDb = currentPeakRightDb;
        state.RmsLeftDb = currentRmsLeftDb;
        state.RmsRightDb = currentRmsRightDb;
        state.PeakHoldLeftDb = _peakHoldLeft;
        state.PeakHoldRightDb = _peakHoldRight;
        state.IsClippingLeft = peakL >= ClipThreshold;
        state.IsClippingRight = peakR >= ClipThreshold;
        state.IsSilent = currentPeakLeftDb <= SilenceThresholdDb && currentPeakRightDb <= SilenceThresholdDb;
        state.LastUpdated = DateTimeOffset.UtcNow;

        return state;
    }

    public void Reset()
    {
        _peakHoldLeft = MinDb;
        _peakHoldRight = MinDb;
        _peakHoldLeftUntilTicks = 0;
        _peakHoldRightUntilTicks = 0;
        _lastProcessedTicks = Stopwatch.GetTimestamp();
    }
}
