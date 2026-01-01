using Microsoft.Extensions.Logging;
using A3ITranslator.Application.DTOs.Audio;
using A3ITranslator.Application.Models;
using A3ITranslator.Application.Services;

namespace A3ITranslator.Infrastructure.Services.Audio;



public class PitchAnalysisService : IPitchAnalysisService
{
    private readonly ILogger<PitchAnalysisService> _logger;

    public PitchAnalysisService(ILogger<PitchAnalysisService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Extract pitch-based characteristics from raw audio data
    /// Returns fundamental frequency, gender estimation, age estimation, etc.
    /// </summary>
    public async Task<PitchAnalysisResult> ExtractPitchCharacteristicsAsync(
        byte[] audioData, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("🎵 Starting pitch analysis for {Size} bytes of audio ({SizeKB:F1} KB)", 
                audioData.Length, audioData.Length / 1024.0);

            // Add detailed audio format diagnostics
            if (audioData.Length > 44)
            {
                var header = audioData.Take(44).ToArray();
                var riffHeader = System.Text.Encoding.ASCII.GetString(header.Take(4).ToArray());
                var waveHeader = System.Text.Encoding.ASCII.GetString(header.Skip(8).Take(4).ToArray());
                _logger.LogDebug("🎵 Audio format: RIFF={RIFF}, WAVE={WAVE}", riffHeader, waveHeader);
                
                if (header.Length >= 24)
                {
                    var sampleRate = BitConverter.ToInt32(header, 24);
                    var bitsPerSample = header.Length >= 34 ? BitConverter.ToInt16(header, 34) : 0;
                    _logger.LogDebug("🎵 Audio specs: SampleRate={SampleRate}Hz, BitsPerSample={BitsPerSample}", 
                        sampleRate, bitsPerSample);
                }
            }

            // Run pitch analysis in background thread to avoid blocking
            return await Task.Run(() => ExtractPitchFromAudio(audioData), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🎵 Pitch analysis failed");
            return CreateDefaultPitchResult();
        }
    }

    /// <summary>
    /// Core pitch extraction logic using autocorrelation
    /// </summary>
    private PitchAnalysisResult ExtractPitchFromAudio(byte[] audioData)
    {
        try
        {
            // PRIORITY 1 FIX: Extract actual sample rate from WAV header
            var (headerSize, sampleRate, channels) = ParseWavHeader(audioData);
            
            // Convert audio to samples for analysis
            var allSamples = ConvertAudioToSamples(audioData, headerSize);
            var totalDuration = allSamples.Length / (float)sampleRate;
            
            _logger.LogInformation("🎵 Converted to {SampleCount} samples ({Duration:F1}s @ {SampleRate}Hz) from {AudioSize} bytes", 
                allSamples.Length, totalDuration, sampleRate, audioData.Length);
            
            if (allSamples.Length < 1024)
            {
                _logger.LogWarning("🎵 Audio too short for pitch analysis ({Samples} samples, need >= 1024)", allSamples.Length);
                return CreateDefaultPitchResult();
            }

            // PRIORITY 2 FIX: Extract first 10 seconds of SPEECH (skip silence)
            var (speechSamples, silenceDuration) = ExtractSpeechSegment(allSamples, sampleRate, targetDuration: 10.0f);
            
            if (speechSamples.Length < sampleRate * 0.5f) // Less than 0.5 seconds of speech
            {
                _logger.LogWarning("🎵 Insufficient speech detected ({Duration:F1}s) - audio might be mostly silent", 
                    speechSamples.Length / (float)sampleRate);
                return CreateDefaultPitchResult();
            }

            var analyzedDuration = speechSamples.Length / (float)sampleRate;
            _logger.LogInformation("🎵 Analyzing {Duration:F1}s of speech (skipped {Silence:F1}s silence)", 
                analyzedDuration, silenceDuration);

            // Extract pitch measurements across the speech
            var pitchMeasurements = ExtractPitchMeasurements(speechSamples, sampleRate);
            
            _logger.LogInformation("🎵 Extracted {MeasurementCount} pitch measurements", pitchMeasurements.Count);
            
            if (!pitchMeasurements.Any())
            {
                _logger.LogWarning("🎵 No pitch measurements found - audio might be non-speech");
                return CreateDefaultPitchResult();
            }

            // Calculate statistics
            var fundamentalFreq = CalculateMedianPitch(pitchMeasurements);
            var pitchVariance = CalculateNormalizedPitchVariance(pitchMeasurements, fundamentalFreq);
            
            // PRIORITY 1 FIX: Probabilistic gender/age estimation with confidence
            var (gender, genderConfidence) = EstimateGenderFromPitch(fundamentalFreq, analyzedDuration);
            var (ageRange, ageConfidence) = EstimateAgeFromPitch(fundamentalFreq, pitchVariance, analyzedDuration);
            var voiceQuality = AssessVoiceQuality(pitchMeasurements, pitchVariance);

            _logger.LogInformation("🎵 Pitch analysis complete: F0={Freq:F1}Hz, Gender={Gender} ({GenderConf:F2}), Age={Age} ({AgeConf:F2})", 
                fundamentalFreq, gender, genderConfidence, ageRange, ageConfidence);

            return new PitchAnalysisResult
            {
                IsSuccess = true,
                FundamentalFrequency = fundamentalFreq,
                PitchVariance = pitchVariance,
                EstimatedGender = gender,
                EstimatedAge = ageRange,
                VoiceQuality = voiceQuality,
                AnalysisConfidence = CalculateConfidence(pitchMeasurements, pitchVariance, analyzedDuration),
                AnalyzedDuration = analyzedDuration,
                SilenceSkipped = silenceDuration,
                GenderConfidence = genderConfidence,
                AgeConfidence = ageConfidence
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Core pitch extraction failed");
            return CreateDefaultPitchResult();
        }
    }

    // PRIORITY 1 FIX: Proper WAV header parsing
    private (int headerSize, int sampleRate, int channels) ParseWavHeader(byte[] audioData)
    {
        try
        {
            if (audioData.Length < 44)
                return (0, 16000, 1); // Fallback defaults
            
            // Check RIFF header
            var riff = System.Text.Encoding.ASCII.GetString(audioData, 0, 4);
            if (riff != "RIFF")
            {
                _logger.LogWarning("🎵 Not a valid WAV file (no RIFF header), using defaults");
                return (0, 16000, 1);
            }
            
            // Extract sample rate and channels from fmt chunk
            var sampleRate = BitConverter.ToInt32(audioData, 24);
            var channels = BitConverter.ToInt16(audioData, 22);
            
            // Find "data" chunk (proper way to handle WAV files with metadata)
            int pos = 12;
            while (pos < audioData.Length - 8)
            {
                var chunkId = System.Text.Encoding.ASCII.GetString(audioData, pos, 4);
                var chunkSize = BitConverter.ToInt32(audioData, pos + 4);
                
                if (chunkId == "data")
                {
                    _logger.LogDebug("🎵 WAV header parsed: {SampleRate}Hz, {Channels} channel(s), data starts at byte {DataStart}",
                        sampleRate, channels, pos + 8);
                    return (pos + 8, sampleRate, channels);
                }
                
                pos += 8 + chunkSize;
            }
            
            // Fallback to standard 44-byte header
            _logger.LogDebug("🎵 Using standard WAV header (44 bytes): {SampleRate}Hz, {Channels} channel(s)",
                sampleRate, channels);
            return (44, sampleRate, channels);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "🎵 Failed to parse WAV header, using defaults");
            return (44, 16000, 1);
        }
    }

    private float[] ConvertAudioToSamples(byte[] audioData, int headerSize)
    {
        if (audioData.Length <= headerSize) return new float[0];
        
        var pcmData = audioData.Skip(headerSize).ToArray();
        var samples = new float[pcmData.Length / 2];
        
        for (int i = 0; i < samples.Length; i++)
        {
            var sample16 = BitConverter.ToInt16(pcmData, i * 2);
            samples[i] = sample16 / 32768.0f;
        }
        
        return samples;
    }

    // PRIORITY 2 FIX: Extract speech segment with Voice Activity Detection
    private (float[] speechSamples, float silenceDuration) ExtractSpeechSegment(
        float[] samples, int sampleRate, float targetDuration = 10.0f)
    {
        var targetSamples = (int)(targetDuration * sampleRate);
        var speechSamples = new List<float>();
        
        var windowSize = sampleRate / 10; // 100ms windows
        var energyThreshold = CalculateAdaptiveEnergyThreshold(samples, sampleRate);
        
        _logger.LogDebug("🎤 Extracting {TargetDuration}s of speech, energy threshold: {Threshold:F6}", 
            targetDuration, energyThreshold);
        
        int silenceWindows = 0;
        int speechWindows = 0;
        
        for (int start = 0; start < samples.Length - windowSize; start += windowSize)
        {
            var window = samples.Skip(start).Take(windowSize).ToArray();
            var energy = window.Select(x => x * x).Average();
            
            // Check if this window contains speech
            if (energy > energyThreshold)
            {
                speechSamples.AddRange(window);
                speechWindows++;
                
                // Stop when we have enough speech
                if (speechSamples.Count >= targetSamples)
                {
                    var silenceDuration = silenceWindows * windowSize / (float)sampleRate;
                    _logger.LogInformation("✅ Extracted {Duration:F1}s of speech (skipped {Silence:F1}s silence)",
                        speechSamples.Count / (float)sampleRate, silenceDuration);
                    return (speechSamples.ToArray(), silenceDuration);
                }
            }
            else
            {
                silenceWindows++;
            }
        }
        
        // Return whatever speech we found
        var finalSilence = silenceWindows * windowSize / (float)sampleRate;
        if (speechSamples.Count < targetSamples * 0.3f)
        {
            _logger.LogWarning("⚠️ Only found {Duration:F1}s of speech (target: {Target}s)",
                speechSamples.Count / (float)sampleRate, targetDuration);
        }
        
        return (speechSamples.ToArray(), finalSilence);
    }

    // PRIORITY 2 FIX: Adaptive energy threshold
    private float CalculateAdaptiveEnergyThreshold(float[] samples, int sampleRate)
    {
        var windowSize = sampleRate / 10; // 100ms
        var energies = new List<float>();
        
        // Sample first 30 seconds max for threshold calculation
        var maxSamples = Math.Min(samples.Length - windowSize, sampleRate * 30);
        
        for (int i = 0; i < maxSamples; i += windowSize)
        {
            var window = samples.Skip(i).Take(windowSize);
            var energy = window.Select(x => x * x).Average();
            energies.Add(energy);
        }
        
        if (!energies.Any()) return 0.00001f;
        
        // Use median energy as baseline (more robust than mean)
        var sortedEnergies = energies.OrderBy(x => x).ToList();
        var medianEnergy = sortedEnergies[sortedEnergies.Count / 2];
        
        // Threshold: 3x median (speech is typically louder than background)
        var threshold = medianEnergy * 3.0f;
        var minimumThreshold = 0.00001f;
        
        return Math.Max(threshold, minimumThreshold);
    }

    private List<float> ExtractPitchMeasurements(float[] samples, int sampleRate)
    {
        var measurements = new List<float>();
        var windowSize = sampleRate / 10; // 100ms windows
        var hopSize = windowSize / 2;

        _logger.LogDebug("🎵 Pitch extraction: samples={SampleCount}, windowSize={WindowSize}, hopSize={HopSize}", 
            samples.Length, windowSize, hopSize);

        var windowCount = 0;
        var validPitchCount = 0;

        for (int start = 0; start + windowSize < samples.Length; start += hopSize)
        {
            windowCount++;
            var window = samples.Skip(start).Take(windowSize).ToArray();
            var pitch = DetectPitchInWindow(window, sampleRate);
            
            _logger.LogDebug("🎵 Window {WindowNum}: pitch={Pitch:F1}Hz", windowCount, pitch);
            
            if (pitch > 0) 
            {
                validPitchCount++;
                measurements.Add(pitch);
            }
        }

        _logger.LogInformation("🎵 Pitch measurements: {ValidCount}/{TotalWindows} windows had valid pitch", 
            validPitchCount, windowCount);

        return measurements;
    }

    private float DetectPitchInWindow(float[] window, int sampleRate)
    {
        var minPitch = 50.0f;
        var maxPitch = 400.0f;
        var minPeriod = (int)(sampleRate / maxPitch);
        var maxPeriod = (int)(sampleRate / minPitch);

        // Check if window has sufficient energy (not silent)
        var energy = window.Select(x => x * x).Average();
        if (energy < 0.00001f) // Much lower threshold - was too high at 0.001f
        {
            _logger.LogDebug("🎵 Window too quiet: energy={Energy:F8}", energy);
            return 0;
        }

        var autocorrelation = CalculateAutocorrelation(window, maxPeriod);
        var bestPeriod = FindBestPitchPeriod(autocorrelation, minPeriod, maxPeriod);

        if (bestPeriod > 0)
        {
            var pitch = (float)sampleRate / bestPeriod;
            var finalPitch = pitch >= minPitch && pitch <= maxPitch ? pitch : 0;
            
            if (finalPitch == 0)
            {
                _logger.LogDebug("🎵 Pitch {Pitch:F1}Hz outside range [{MinPitch}-{MaxPitch}Hz]", 
                    pitch, minPitch, maxPitch);
            }
            
            return finalPitch;
        }

        _logger.LogDebug("🎵 No valid period found in autocorrelation");
        return 0;
    }

    private float[] CalculateAutocorrelation(float[] signal, int maxLag)
    {
        var autocorrelation = new float[maxLag];
        
        for (int lag = 0; lag < maxLag; lag++)
        {
            float sum = 0;
            int count = 0;
            
            for (int i = 0; i < signal.Length - lag; i++)
            {
                sum += signal[i] * signal[i + lag];
                count++;
            }
            
            autocorrelation[lag] = count > 0 ? sum / count : 0;
        }
        
        return autocorrelation;
    }

    private int FindBestPitchPeriod(float[] autocorrelation, int minPeriod, int maxPeriod)
    {
        var bestPeriod = 0;
        var bestCorrelation = 0.0f;
        
        for (int period = minPeriod; period < Math.Min(maxPeriod, autocorrelation.Length); period++)
        {
            if (autocorrelation[period] > bestCorrelation)
            {
                bestCorrelation = autocorrelation[period];
                bestPeriod = period;
            }
        }
        
        // FIX: Use much lower threshold - real voice has much lower correlations than expected
        // Based on testing with real audio, correlations of 0.005-0.02 are typical for human speech
        var threshold = autocorrelation[0] * 0.01f; // 1% of zero-lag correlation
        var absoluteThreshold = 0.005f; // Much lower minimum - real speech can be 0.5-2%
        var finalThreshold = Math.Max(threshold, absoluteThreshold);
        
        var passed = bestCorrelation > finalThreshold;
        
        _logger.LogDebug("🎵 Autocorrelation: best={Best:F4} at period={Period}, zeroLag={ZeroLag:F4}, threshold={Threshold:F4}, passed={Passed}", 
            bestCorrelation, bestPeriod, autocorrelation[0], finalThreshold, passed);
        
        return passed ? bestPeriod : 0;
    }

    private float CalculateMedianPitch(List<float> measurements)
    {
        if (!measurements.Any()) return 150.0f;
        
        var sorted = measurements.OrderBy(x => x).ToList();
        var middle = sorted.Count / 2;
        
        return sorted.Count % 2 == 0 
            ? (sorted[middle - 1] + sorted[middle]) / 2.0f 
            : sorted[middle];
    }

    private float CalculatePitchVariance(List<float> measurements)
    {
        if (measurements.Count < 2) return 10.0f;
        
        var mean = measurements.Average();
        var variance = measurements.Select(x => (x - mean) * (x - mean)).Average();
        
        return (float)Math.Sqrt(variance);
    }

    // PRIORITY 2 FIX: Normalized pitch variance (coefficient of variation)
    private float CalculateNormalizedPitchVariance(List<float> measurements, float fundamentalFreq)
    {
        if (measurements.Count < 3) return 0.1f;
        
        // Remove outliers (beyond 2 standard deviations)
        var mean = measurements.Average();
        var stdDev = (float)Math.Sqrt(measurements.Select(x => (x - mean) * (x - mean)).Average());
        var filtered = measurements.Where(x => Math.Abs(x - mean) < 2 * stdDev).ToList();
        
        if (filtered.Count < 2) return 0.1f;
        
        // Coefficient of variation (normalized variance)
        var filteredMean = filtered.Average();
        var filteredStdDev = (float)Math.Sqrt(filtered.Select(x => (x - filteredMean) * (x - filteredMean)).Average());
        
        // Avoid division by zero
        if (filteredMean < 1.0f) return 0.1f;
        
        return filteredStdDev / filteredMean;
    }

    // PRIORITY 1 FIX: Probabilistic gender estimation with confidence
    private (string gender, float confidence) EstimateGenderFromPitch(float f0, float duration)
    {
        // Base confidence depends on analyzed duration
        var baseConfidence = duration switch
        {
            < 0.5f => 0.5f,   // Very short
            < 1.0f => 0.7f,   // Short
            < 3.0f => 0.85f,  // Good
            _ => 0.95f        // Excellent (10s is optimal)
        };
        
        // Gender estimation with overlap zones
        if (f0 > 250) return ("FEMALE", baseConfidence * 0.95f);
        if (f0 > 200) return ("FEMALE", baseConfidence * 0.85f);
        if (f0 > 165) return ("NEUTRAL", baseConfidence * 0.50f); // Ambiguous overlap zone
        if (f0 > 120) return ("MALE", baseConfidence * 0.85f);
        if (f0 > 85) return ("MALE", baseConfidence * 0.95f);
        
        return ("UNKNOWN", 0.30f);
    }

    // PRIORITY 2 FIX: Duration-aware age estimation with confidence
    private (string age, float confidence) EstimateAgeFromPitch(float f0, float variance, float duration)
    {
        // Age estimation requires more data
        if (duration < 3.0f)
            return ("adult", 0.3f); // Default with low confidence
        
        var baseConfidence = Math.Min(0.75f, duration / 10.0f); // Max 0.75 at 10s (age is harder)
        
        // Children: high pitch + high variance
        if (f0 > 280 && variance > 0.08f) // Using normalized variance
            return ("child", baseConfidence * 0.9f);
        
        // Teens: moderate-high pitch + high variance
        if (f0 > 200 && variance > 0.06f)
            return ("teen", baseConfidence * 0.7f);
        
        // Seniors: low pitch + low variance
        if (f0 < 90 && variance < 0.03f)
            return ("senior", baseConfidence * 0.7f);
        
        // Young adults: moderate variance
        if (variance > 0.05f)
            return ("young_adult", baseConfidence * 0.6f);
        
        // Default to adult
        return ("adult", baseConfidence * 0.6f);
    }

    private string AssessVoiceQuality(List<float> measurements, float variance)
    {
        if (measurements.Count < 3) return "poor";
        if (variance < 5.0f) return "excellent";
        if (variance < 15.0f) return "good";
        if (variance < 30.0f) return "fair";
        return "poor";
    }

    // PRIORITY 2 FIX: Duration-aware confidence calculation
    private float CalculateConfidence(List<float> measurements, float variance, float duration)
    {
        if (measurements.Count < 3) return 0.3f;
        
        // Measurement quality score (more measurements = better)
        var measurementScore = Math.Min(1.0f, measurements.Count / 20.0f);
        
        // Pitch stability score (normalized variance, lower is better)
        var stabilityScore = Math.Max(0.0f, 1.0f - (variance * 10.0f)); // variance is now normalized
        
        // Duration score (10s is optimal)
        var durationScore = Math.Min(1.0f, duration / 10.0f);
        
        // Weighted average: duration is most important for reliability
        return (measurementScore * 0.25f + stabilityScore * 0.35f + durationScore * 0.40f);
    }

    private PitchAnalysisResult CreateDefaultPitchResult()
    {
        return new PitchAnalysisResult
        {
            IsSuccess = false,
            FundamentalFrequency = 150.0f,
            PitchVariance = 15.0f,
            EstimatedGender = "UNKNOWN",
            EstimatedAge = "adult",
            VoiceQuality = "unknown",
            AnalysisConfidence = 0.3f
        };
    }
}

