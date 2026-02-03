using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MyFinder.Models;
using OpenCvSharp;
using Tesseract;

namespace MyFinder.Services;

public class TimestampService
{
    private readonly string _tessDataPath;
    
    public TimestampService(string appDataPath)
    {
        _tessDataPath = Path.Combine(appDataPath, "tessdata");
    }

    public async Task InitializeAsync()
    {
        // Ensure tessdata exists
        if (!Directory.Exists(_tessDataPath)) Directory.CreateDirectory(_tessDataPath);
        
        string engPath = Path.Combine(_tessDataPath, "eng.traineddata");
        if (!File.Exists(engPath))
        {
            // Auto-download eng.traineddata (fast variant)
            using var client = new System.Net.Http.HttpClient();
            var data = await client.GetByteArrayAsync("https://github.com/tesseract-ocr/tessdata_fast/raw/main/eng.traineddata");
            await File.WriteAllBytesAsync(engPath, data);
        }
    }

    public async Task ExtractTimestampAsync(MediaFile file)
    {
        await Task.Run(() => 
        {
            using var capture = new VideoCapture(file.FilePath);
            if (!capture.IsOpened()) return;

            // Check first few seconds
            using var frame = new Mat();
            if (!capture.Read(frame) || frame.Empty()) return;

            // OCR Engine
            using var engine = new TesseractEngine(_tessDataPath, "eng", EngineMode.Default);
            
            // Define ROIs (Top-Right, Bottom-Right often have stamps)
            // Logic: Crop 20% of width/height from corners
            var rois = GetRegionsOfInterest(frame.Width, frame.Height);

            foreach (var roi in rois)
            {
                using var crop = new Mat(frame, roi);
                
                // Preprocess: Grayscale + Threshold for better OCR
                using var gray = new Mat();
                Cv2.CvtColor(crop, gray, ColorConversionCodes.BGR2GRAY);
                Cv2.Threshold(gray, gray, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

                // Convert OpenCV Mat to Pix (Tesseract format)
                var imageBytes = gray.ToBytes(); // This gives encoded bmp/jpg
                // Tesseract load from memory 
                // Note: Efficient way is raw bytes, but simple way is memory stream
                
                using var pix = Pix.LoadFromMemory(imageBytes);
                using var page = engine.Process(pix);
                
                string text = page.GetText().Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    // Try parse date
                    if (TryParseTimestamp(text, out DateTime result))
                    {
                        file.ExtractedTimestamp = result;
                        System.Diagnostics.Debug.WriteLine($"Found Timestamp: {result} in {file.FileName}");
                        return; // Found it
                    }
                }
            }
        });
    }

    private List<OpenCvSharp.Rect> GetRegionsOfInterest(int w, int h)
    {
        // Return Top-Right and Top-Left and Bottom-Right crops
        int cropW = w / 3;
        int cropH = h / 5;
        
        return new List<OpenCvSharp.Rect>
        {
            new OpenCvSharp.Rect(0, 0, cropW, cropH), // Top-Left
            new OpenCvSharp.Rect(w - cropW, 0, cropW, cropH), // Top-Right
            new OpenCvSharp.Rect(w - cropW, h - cropH, cropW, cropH) // Bottom-Right
        };
    }

    private bool TryParseTimestamp(string text, out DateTime date)
    {
        // Clean text: "2024-01-01 12:00:00" or "01/01/2024"
        // Replace common OCR errors like 'O' -> '0', 'l' -> '1'
        var cleaned = text.Replace('O', '0').Replace('l', '1').Replace(',', '.');
        
        // Simple heuristics
        return DateTime.TryParse(cleaned, out date);
    }
}
