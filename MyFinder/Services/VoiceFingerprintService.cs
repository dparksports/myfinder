using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using MyFinder.Models;
using NAudio.Wave;
using NWaves.FeatureExtractors;
using NWaves.FeatureExtractors.Options;

namespace MyFinder.Services;

public class VoiceFingerprintService
{
    private readonly string _modelPath;
    private InferenceSession? _session;

    public VoiceFingerprintService(string modelPath)
    {
        _modelPath = modelPath;
    }

    public async Task InitializeAsync()
    {
        await Task.Run(() => 
        {
            if (File.Exists(_modelPath) && _session == null)
            {
                _session = new InferenceSession(_modelPath);
            }
        });
    }

    public async Task<float[]> GetVoiceEmbeddingAsync(string audioPath)
    {
        if (_session == null || !File.Exists(audioPath)) return new float[0];

        return await Task.Run(() =>
        {
            // 1. Read Audio (Force 16kHz Mono)
            // Use NAudio to read via MediaFoundation (supports mp3, mp4 extraction)
            float[] samples;
            try 
            {
                using var reader = new AudioFileReader(audioPath);
                var resampler = new MediaFoundationResampler(reader, new WaveFormat(16000, 1)); // Resample to 16k
                var buffer = new byte[resampler.WaveFormat.AverageBytesPerSecond * 3]; // Read 3 seconds
                int read = resampler.Read(buffer, 0, buffer.Length);
                
                // Convert byte to float
                int sampleCount = read / 2; // 16bit = 2 bytes
                samples = new float[sampleCount];
                for (int i = 0; i < sampleCount; i++)
                {
                     short val = BitConverter.ToInt16(buffer, i * 2);
                     samples[i] = val / 32768f;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Audio read failed: {ex.Message}");
                return new float[0];
            }

            if (samples.Length < 1600) return new float[0]; // Too short

            // 2. Extract MFCC (Mel-Frequency Cepstral Coefficients)
            // VoxCeleb models usually take Spectrograms or MFCCs.
            // Let's assume a simplified ResNet that takes raw MFCC blocks (80 dim).
            // Configuration depends heavily on the specific ONNX model used.
            // WE ASSUME standard configuration: 80 Mel bands.
            
            var options = new MfccOptions
            {
                SamplingRate = 16000,
                FeatureCount = 80, 
                FrameDuration = 0.025, // 25ms
                HopDuration = 0.010,   // 10ms
            };
            
            var extractor = new MfccExtractor(options);
            var mfccs = extractor.ComputeFrom(samples);

            // 3. Create Tensor [1, 1, Time, 80] or [1, 80, Time]
            // Standard layout is often [Batch, Channel, Height, Width] -> [1, 1, Time, Freq]
            // We need to crop/pad to fixed size (e.g., 200 frames = 2 seconds)
            
            int timeSteps = 200;
            if (mfccs.Count < timeSteps) return new float[0]; // Need at least 2 secs
            
            // Build Tensor
            var tensor = new DenseTensor<float>(new[] { 1, 1, 80, timeSteps }); 
            // Warning: Dimension order varies by model (BxCxHxW vs BxTxF)
            
            for (int t = 0; t < timeSteps; t++)
            {
                for (int f = 0; f < 80; f++)
                {
                     // MfccExtractor returns [Time][Freq]
                     tensor[0, 0, f, t] = (float)mfccs[t][f];
                }
            }

            // 4. Run Inference
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input", tensor) 
            };

            using var results = _session.Run(inputs);
            return results.First().AsEnumerable<float>().ToArray();
        });
    }
}
