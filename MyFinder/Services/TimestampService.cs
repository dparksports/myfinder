using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MyFinder.Models;
using OpenCvSharp;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace MyFinder.Services;

public class TimestampService
{
    private OcrEngine? _engine;
    
    public TimestampService(string appDataPath)
    {
        // No tessdata needed
    }

    public async Task InitializeAsync()
    {
        // Init Windows OCR
        await Task.Run(() => 
        {
            try 
            {
                _engine = OcrEngine.TryCreateFromUserProfileLanguages();
                if (_engine == null)
                {
                    // Fallback
                    var lang = new Windows.Globalization.Language("en-US");
                    _engine = OcrEngine.TryCreateFromLanguage(lang);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OCR Init Error: {ex.Message}");
            }
        });
    }

    public async Task ExtractTimestampAsync(MediaFile file, MetadataStore store)
    {
        if (_engine == null) return;
        
        await Task.Run(async () => 
        {
            using var capture = new VideoCapture(file.FilePath);
            if (!capture.IsOpened()) return;

            // 1. Get Start Timestamp (Frame 0)
            var startResult = await ExtractDateAndTextFromFrameAsync(capture, 0);
            
            // 2. Get End Timestamp
            int totalFrames = (int)capture.Get(VideoCaptureProperties.FrameCount);
            if (totalFrames < 10) return;
            var endResult = await ExtractDateAndTextFromFrameAsync(capture, totalFrames - 5);

            bool foundAny = false;

            if (startResult.Date != null)
            {
                file.TimestampStart = startResult.Date;
                file.ExtractedTimestamp = startResult.Date;
                file.TimestampRaw = startResult.Text; // Store confirmed text
                System.Diagnostics.Debug.WriteLine($"Found Start: {startResult.Date} in {file.FileName}");
                foundAny = true;
            }
            // Fallback: If no date parsed, but we found text that looks like a date via Regex in ExtractDateAndTextFromFrameAsync
            else if (!string.IsNullOrEmpty(startResult.Text))
            {
                // We'll store it as Raw if it passed a logic check (which we'll add in Extract)
                file.TimestampRaw = startResult.Text;
            }

            if (endResult.Date != null)
            {
                file.TimestampEnd = endResult.Date;
                System.Diagnostics.Debug.WriteLine($"Found End: {endResult.Date} in {file.FileName}");
                foundAny = true;
            }

            if (file.TimestampStart != null && file.TimestampEnd != null && file.TimestampEnd > file.TimestampStart)
            {
                file.ComputedDuration = file.TimestampEnd - file.TimestampStart;
            }
            
            if (!foundAny && string.IsNullOrEmpty(file.TimestampRaw))
            {
                if (!file.Tags.Contains("NO TIMESTAMP")) file.Tags.Add("NO TIMESTAMP");
            }
            else
            {
                if (file.Tags.Contains("NO TIMESTAMP")) file.Tags.Remove("NO TIMESTAMP");
            }
        });
    }

    private async Task<(string Text, DateTime? Date)> ExtractDateAndTextFromFrameAsync(VideoCapture capture, int framePos)
    {
        capture.Set(VideoCaptureProperties.PosFrames, framePos);
        using var frame = new Mat();
        if (!capture.Read(frame) || frame.Empty()) return (string.Empty, null);

        int w = frame.Width;
        int h = frame.Height;
        int cropHeight = Math.Min(300, h);
        using var topStrip = new Mat(frame, new OpenCvSharp.Rect(0, 0, w, cropHeight));
        
        int cropW = w / 3; // Keep side ROIs at 1/3
        int centerW = w / 2; // Widen center to 1/2 (50%)
        int centerX = (w - centerW) / 2;
        
        var rois = new OpenCvSharp.Rect[]
        {
            new OpenCvSharp.Rect(0, 0, cropW, cropHeight), // Top-Left (33%)
            new OpenCvSharp.Rect(w - cropW, 0, cropW, cropHeight), // Top-Right (33%)
            new OpenCvSharp.Rect(centerX, 0, centerW, cropHeight), // Top-Center (50%)
        };

        // We want to find the BEST result.
        // Priority: 1. Valid Date. 2. Regex Match (Raw Fallback).
        string bestRaw = string.Empty;

        foreach (var roi in rois)
        {
            using var crop = new Mat(topStrip, roi);
            
            // Define strategies to run (Lazy execution?)
            // We'll just run them linearly.
            
            var strategies = new List<Func<Task<(string, DateTime?)>>>();
            strategies.Add(async () => await GetOcrResultAsync(crop)); // Standard
            
            strategies.Add(async () => {
                 using var scaled = new Mat();
                 Cv2.Resize(crop, scaled, new OpenCvSharp.Size(crop.Width * 3, crop.Height * 3));
                 return await GetOcrResultAsync(scaled);
            });
            
            strategies.Add(async () => {
                 using var inverted = new Mat();
                 Cv2.BitwiseNot(crop, inverted);
                 return await GetOcrResultAsync(inverted);
            });
            
            strategies.Add(async () => {
                 using var gray = new Mat();
                 Cv2.CvtColor(crop, gray, ColorConversionCodes.BGR2GRAY);
                 using var binary = new Mat();
                 Cv2.Threshold(gray, binary, 0, 255, ThresholdTypes.Otsu | ThresholdTypes.Binary);
                 using var binaryColor = new Mat();
                 Cv2.CvtColor(binary, binaryColor, ColorConversionCodes.GRAY2BGRA);
                 return await GetOcrResultAsync(binaryColor);
            });

            foreach(var strategy in strategies)
            {
                var result = await strategy();
                if (result.Item2 != null) return result; // Found Date!
                
                // Fallback: Just capture the text if we haven't found a date yet.
                // We keep the longest string found, assuming timestamps are longer than random noise/artifacts.
                if (!string.IsNullOrWhiteSpace(result.Item1))
                {
                    if (result.Item1.Length > bestRaw.Length)
                    {
                        bestRaw = result.Item1;
                    }
                }
            }
        }

        return (bestRaw, null);
    }

    private async Task<(string Text, DateTime? Date)> GetOcrResultAsync(Mat mat)
    {
        if (_engine == null) return ("Engine Not Init", null);
        try 
        {
            // Mat -> SoftwareBitmap
            using var temp = new Mat();
            Cv2.CvtColor(mat, temp, ColorConversionCodes.BGR2BGRA);
            
            // Encode to PNG buffer
            Cv2.ImEncode(".png", temp, out byte[] buf);
            
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(buf.AsBuffer());
            stream.Seek(0);
            
            var decoder = await BitmapDecoder.CreateAsync(stream);
            using var softwareBitmap = await decoder.GetSoftwareBitmapAsync();
            
            var ocrResult = await _engine.RecognizeAsync(softwareBitmap);
            string text = ocrResult.Text;
            
            if (string.IsNullOrWhiteSpace(text)) return ("[Empty]", null);
            
            if (TryParseTimestamp(text, out DateTime date))
            {
                return (text, date);
            }
            return (text, null);
        }
        catch (Exception ex)
        {
            return ($"Error: {ex.Message}", null);
        }
    }
    
    // Wrapper for Main Logic (keeps existing signature helper)
    private async Task<DateTime?> TryOcrStrategyAsync(Mat mat)
    {
        var result = await GetOcrResultAsync(mat);
        return result.Date;
    }

    // Helper extension
    public async Task<string> TestExtractionAsync(string filePath)
    {
         if (_engine == null) return "OCR Engine not initialized.";
         var sb = new System.Text.StringBuilder();
         
         try 
         {
             using var capture = new VideoCapture(filePath);
             if (!capture.IsOpened()) return "Could not open video file.";
             
             // Test Frame 0
             sb.AppendLine($"Testing Frame 0 of {System.IO.Path.GetFileName(filePath)}...");
             capture.Set(VideoCaptureProperties.PosFrames, 0);
             using var frame = new Mat();
             capture.Read(frame);
             if (frame.Empty()) return "Frame 0 is empty.";
             
             int w = frame.Width;
             int h = frame.Height;
             int cropHeight = Math.Min(300, h);
             using var topStrip = new Mat(frame, new OpenCvSharp.Rect(0, 0, w, cropHeight));
             
             int cropW = w / 3;
             int centerW = w / 2; // Widen center to 1/2 (50%)
             int centerX = (w - centerW) / 2;
             
             var rois = new OpenCvSharp.Rect[]
             {
                new OpenCvSharp.Rect(0, 0, cropW, cropHeight),
                new OpenCvSharp.Rect(w - cropW, 0, cropW, cropHeight),
                new OpenCvSharp.Rect(centerX, 0, centerW, cropHeight),
             };
             
             string[] roiNames = { "Top-Left", "Top-Right", "Top-Center" };
             
             for(int i=0; i<rois.Length; i++)
             {
                sb.AppendLine($"Checking ROI: {roiNames[i]}");
                 using var crop = new Mat(topStrip, rois[i]);
                 
                 // Strategy 1: Standard
                 var res1 = await GetOcrResultAsync(crop);
                 sb.AppendLine($" - Standard: '{res1.Text}' -> {(res1.Date.HasValue ? res1.Date.Value.ToString() : "No Date")}");

                 // Strategy 2: Scale 3x
                 using var scaled = new Mat();
                 Cv2.Resize(crop, scaled, new OpenCvSharp.Size(crop.Width * 3, crop.Height * 3));
                 var res2 = await GetOcrResultAsync(scaled);
                 sb.AppendLine($" - Scaled 3x: '{res2.Text}' -> {(res2.Date.HasValue ? res2.Date.Value.ToString() : "No Date")}");
                 
                 // Strategy 3: Invert
                 using var inverted = new Mat();
                 Cv2.BitwiseNot(crop, inverted);
                 var res3 = await GetOcrResultAsync(inverted);
                 sb.AppendLine($" - Inverted: '{res3.Text}' -> {(res3.Date.HasValue ? res3.Date.Value.ToString() : "No Date")}");

                 // Strategy 4: Binarization (Otsu)
                 using var gray = new Mat();
                 Cv2.CvtColor(crop, gray, ColorConversionCodes.BGR2GRAY);
                 using var binary = new Mat();
                 Cv2.Threshold(gray, binary, 0, 255, ThresholdTypes.Otsu | ThresholdTypes.Binary);
                 using var binaryColor = new Mat();
                 Cv2.CvtColor(binary, binaryColor, ColorConversionCodes.GRAY2BGRA); // Convert back for OCR
                 var res4 = await GetOcrResultAsync(binaryColor);
                 sb.AppendLine($" - Binary (Otsu): '{res4.Text}' -> {(res4.Date.HasValue ? res4.Date.Value.ToString() : "No Date")}");
             }
             
             return sb.ToString();
         }
         catch (Exception ex)
         {
             return $"Error: {ex.Message}\n{ex.StackTrace}";
         }
    }

    private bool TryParseTimestamp(string text, out DateTime date)
    {
        // Simplify text
        var cleaned = text.Replace('O', '0').Replace('l', '1').Replace(',', '.')
                          .Replace("|", "").Replace("\n", " ").Replace("\r", " ").Trim();
        
        // Remove day names (SUN, MON, etc) - checking both ends
        string[] days = { "SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT" };
        
        // Loop purely for cleaning end strings
        bool keptCleaning = true;
        while(keptCleaning)
        {
            keptCleaning = false;
            foreach (var day in days)
            {
                if (cleaned.ToUpper().EndsWith(day))
                {
                    cleaned = cleaned.Substring(0, cleaned.Length - 3).Trim();
                    keptCleaning = true; 
                    break;
                }
            }
        }
        
        // Also clean "am"/"pm" if attached to day? No formats handle am/pm.
        
        // Formats
        string[] formats = 
        { 
            "yyyy/MM/dd hh:mm:ss tt", 
            "yyyy/MM/dd HH:mm:ss", 
            "yyyy-MM-dd HH:mm:ss", 
            "dd/MM/yyyy HH:mm:ss",
            "MM/dd/yyyy hh:mm:ss tt",
            
            // Loose variants
            "yyyy/MM/dd hh:mm:ss",
            "yyyy.MM.dd hh:mm:ss tt",
            
            // Format seen: "2026/01/13 10:00:06 am TUE" -> cleaned to "2026/01/13 10:00:06 am"
        };
        
        if (DateTime.TryParseExact(cleaned, formats, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeLocal, out date))
        {
            return true;
        }

        return DateTime.TryParse(cleaned, out date);
    }
}

public static class BufferExtensions
{
    public static Windows.Storage.Streams.IBuffer AsBuffer(this byte[] bytes)
    {
         using (var writer = new Windows.Storage.Streams.DataWriter())
         {
             writer.WriteBytes(bytes);
             return writer.DetachBuffer();
         }
    }
}
