using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using MyFinder.Models;
using OpenCvSharp;
using OpenCvSharp.Dnn;

namespace MyFinder.Services;

public class VideoAnalyzer
{
    private readonly string _modelPath;
    private readonly ConfigService _config;
    private InferenceSession? _session;
    
    // Classes we care about (from COCO dataset)
    // 0: person, 1: bicycle, 2: car ... 15: cat, 16: dog
    private readonly int[] _interestingClasses = { 0, 1, 2, 15, 16 };

    public VideoAnalyzer(string modelPath, ConfigService config)
    {
        _modelPath = modelPath;
        _config = config;
    }

    public async Task InitializeAsync()
    {
        if (_session != null) return;
        
        await Task.Run(() => 
        {
            if (File.Exists(_modelPath))
            {
                // Use CPU for now (easiest compatibility)
                var options = new SessionOptions();
                _session = new InferenceSession(_modelPath, options);
            }
        });
    }

    public async Task AnalyzeVideoAsync(MediaFile file)
    {
        if (_session == null) return;

        await Task.Run(() =>
        {
            using var capture = new VideoCapture(file.FilePath);
            if (!capture.IsOpened()) return;

            int totalFrames = (int)capture.Get(VideoCaptureProperties.FrameCount);
            double fps = capture.Get(VideoCaptureProperties.Fps);
            int durationSec = (int)(totalFrames / fps);

            // Strategy: Sample 1 frame every 5 seconds
            int step = (int)fps * 5; 
            int processedFrames = 0;
            int framesWithContent = 0;

            for (int i = 0; i < totalFrames; i += step)
            {
                capture.Set(VideoCaptureProperties.PosFrames, i);
                using var frame = new Mat();
                capture.Read(frame);
                
                if (frame.Empty()) continue;

                if (HasInterestingContent(frame))
                {
                    framesWithContent++;
                }
                processedFrames++;
            }

            // Analysis Result
            if (processedFrames > 0)
            {
                file.ContentScore = (double)framesWithContent / processedFrames;
                file.IsLowContent = framesWithContent == 0; // Strict: True only if absolutely NO objects found
            }
        });
    }

    private bool HasInterestingContent(Mat frame)
    {
        // YOLO Preprocessing: Resize to 640x640, Normalize
        using var blob = CvDnn.BlobFromImage(frame, 1.0 / 255.0, new OpenCvSharp.Size(640, 640), new Scalar(0, 0, 0), true, false);
        
        // This part requires understanding the specific input/output tensor names of the YOLOv8 model
        // For standard YOLOv8n.onnx, input is "images", output is "output0"
        
        var inputMeta = _session.InputMetadata;
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("images", ExtractTensor(blob))
        };

        using var results = _session.Run(inputs);
        var output = results.First().AsTensor<float>();

        // Parsing YOLO output is complex (84x8400 tensor). 
        // For this prototype, we will assume a simplified check:
        // If max confidence for "Person" class > 0.5 in any anchor, return true.
        
        // SIMPLIFICATION FOR PROTOTYPE:
        // Real YOLO parsing takes ~100 lines of code (NMS, box decoding).
        // To keep this file concise for the agent, we will simulate the detection logic 
        // until we have the full NMS utility helper.
        
        // For now, let's use a placeholder heuristic: 
        // If frame has high variance (not a black screen/wall), we count it.
        // Wait, I should not fake it. But implementing full NMS here without a library is risky.
        
        // Let's implement a very basic pixel variance check instead for "Low Content" 
        // if the model isn't running perfectly, 
        // BUT ideally we parse the tensor. 
        
        // Let's try to parse just the class scores which are usually in specific rows/cols.
        // YOLOv8 output: [batch, 4+80, 8400] -> [1, 84, 8400]
        // 0-3: Box, 4-83: Class probabilities
        
        return ParseYoloOutput(output);
    }

    private bool ParseYoloOutput(Tensor<float> output)
    {
        // YOLOv8 output: [1, 84, 8400]
        // Rows 0-3: Box Coordinates
        // Rows 4-83: Class Probabilities
        
        bool detectPerson = _config.DetectPersons;
        bool detectVehicle = _config.DetectVehicles;
        bool detectAnimal = _config.DetectAnimals;
        
        float threshold = 0.45f; // Threshold

        // Loop through 8400 anchors
        for (int i = 0; i < 8400; i++)
        {
            // PERSON (Class 0 -> Index 4)
            if (detectPerson && output[0, 4, i] > threshold) return true;

            // VEHICLES
            if (detectVehicle)
            {
                if (output[0, 6, i] > threshold) return true; // Car (Class 2 -> Index 6)
                if (output[0, 5, i] > threshold) return true; // Bicycle (Class 1 -> Index 5)
                if (output[0, 7, i] > threshold) return true; // Motorcycle (Class 3 -> Index 7)
                if (output[0, 9, i] > threshold) return true; // Bus (Class 5 -> Index 9)
                if (output[0, 11, i] > threshold) return true; // Truck (Class 7 -> Index 11)
            }

            // ANIMALS
            if (detectAnimal)
            {
                // Classes 14-23 (Indices 18-27)
                // Bird, Cat, Dog, Horse, Sheep, Cow, Elephant, Bear, Zebra, Giraffe
                for (int c = 18; c <= 27; c++)
                {
                     if (output[0, c, i] > threshold) return true;
                }
            }
        }
        
        return false; // No specified object found
    }
    
    private DenseTensor<float> ExtractTensor(Mat blob)
    {
        var tensor = new DenseTensor<float>(new[] { 1, 3, 640, 640 });
        
        // Get pointer to the blob data
        // Blob is NCHW (1, 3, 640, 640)
        // We can safely iterate if we know the layout
        
        unsafe 
        {
            float* ptr = (float*)blob.DataPointer;
            for (int i = 0; i < 3 * 640 * 640; i++)
            {
                tensor.SetValue(i, ptr[i]);
            }
        }
        
        return tensor;
    }
}
