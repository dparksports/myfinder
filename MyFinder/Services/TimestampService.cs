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

        // Run on UI thread? No, OCR is thread safe mostly, but VideoCapture might need care.
        // Actually, pure async method is better.
        
        await Task.Run(async () => 
        {
            using var capture = new VideoCapture(file.FilePath);
            if (!capture.IsOpened()) return;

            // 1. Get Start Timestamp (Frame 0)
            DateTime? start = await ExtractDateFromFrameAsync(capture, 0);
            
            // 2. Get End Timestamp
            int totalFrames = (int)capture.Get(VideoCaptureProperties.FrameCount);
            if (totalFrames < 10) return;
            DateTime? end = await ExtractDateFromFrameAsync(capture, totalFrames - 5);

            bool foundAny = false;

            if (start != null)
            {
                file.TimestampStart = start;
                file.ExtractedTimestamp = start;
                System.Diagnostics.Debug.WriteLine($"Found Start: {start} in {file.FileName}");
                foundAny = true;
            }

            if (end != null)
            {
                file.TimestampEnd = end;
                System.Diagnostics.Debug.WriteLine($"Found End: {end} in {file.FileName}");
                foundAny = true;
            }

            if (start != null && end != null && end > start)
            {
                file.ComputedDuration = end - start;
            }
            
            if (!foundAny)
            {
                if (!file.Tags.Contains("NO TIMESTAMP")) file.Tags.Add("NO TIMESTAMP");
            }
            else
            {
                if (file.Tags.Contains("NO TIMESTAMP")) file.Tags.Remove("NO TIMESTAMP");
            }
        });
    }

    private async Task<DateTime?> ExtractDateFromFrameAsync(VideoCapture capture, int framePos)
    {
        capture.Set(VideoCaptureProperties.PosFrames, framePos);
        using var frame = new Mat();
        if (!capture.Read(frame) || frame.Empty()) return null;

        int w = frame.Width;
        int h = frame.Height;
        
        // 1. Crop Top Portion (200px or 25% of height)
        // User requested top portion specifically.
        int cropHeight = Math.Min(300, h); // Safer 300px for 4k
        using var topStrip = new Mat(frame, new OpenCvSharp.Rect(0, 0, w, cropHeight));
        
        // 2. Define ROIs on the strip
        int cropW = w / 3;
        int stripH = cropHeight;
        int centerX = (w - cropW) / 2;
        
        var rois = new OpenCvSharp.Rect[]
        {
            new OpenCvSharp.Rect(0, 0, cropW, stripH), // Top-Left
            new OpenCvSharp.Rect(w - cropW, 0, cropW, stripH), // Top-Right
            new OpenCvSharp.Rect(centerX, 0, cropW, stripH), // Top-Center
        };

        foreach (var roi in rois)
        {
            // Try Standard
            using var crop = new Mat(topStrip, roi);
            var result = await TryOcrStrategyAsync(crop);
            if (result.HasValue) return result;

            // Try Scale 3x (for small text on high res)
            using var scaled = new Mat();
            Cv2.Resize(crop, scaled, new OpenCvSharp.Size(crop.Width * 3, crop.Height * 3));
            result = await TryOcrStrategyAsync(scaled);
            if (result.HasValue) return result;

            // Try Invert (White text)
             using var inverted = new Mat();
             Cv2.BitwiseNot(crop, inverted);
             result = await TryOcrStrategyAsync(inverted);
             if (result.HasValue) return result;
        }

        return null;
    }

    private async Task<DateTime?> TryOcrStrategyAsync(Mat mat)
    {
        if (_engine == null) return null;
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
            
            if (!string.IsNullOrWhiteSpace(text) && TryParseTimestamp(text, out DateTime date))
            {
                return date;
            }
        }
        catch 
        {
            // Ignore OCR errors
        }
        return null;
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
             int centerX = (w - cropW) / 2;
             
             var rois = new OpenCvSharp.Rect[]
             {
                new OpenCvSharp.Rect(0, 0, cropW, cropHeight),
                new OpenCvSharp.Rect(w - cropW, 0, cropW, cropHeight),
                new OpenCvSharp.Rect(centerX, 0, cropW, cropHeight),
             };
             
             string[] roiNames = { "Top-Left", "Top-Right", "Top-Center" };
             
             for(int i=0; i<rois.Length; i++)
             {
                 sb.AppendLine($"Checking ROI: {roiNames[i]}");
                 using var crop = new Mat(topStrip, rois[i]);
                 
                 // Strategy 1: Standard
                 var res1 = await TryOcrStrategyAsync(crop);
                 sb.AppendLine($" - Standard: {(res1.HasValue ? res1.Value.ToString() : "No Date")}");

                 // Strategy 2: Scale 3x
                 using var scaled = new Mat();
                 Cv2.Resize(crop, scaled, new OpenCvSharp.Size(crop.Width * 3, crop.Height * 3));
                 var res2 = await TryOcrStrategyAsync(scaled);
                 sb.AppendLine($" - Scaled 3x: {(res2.HasValue ? res2.Value.ToString() : "No Date")}");
                 
                 // Strategy 3: Invert
                 using var inverted = new Mat();
                 Cv2.BitwiseNot(crop, inverted);
                 var res3 = await TryOcrStrategyAsync(inverted);
                 sb.AppendLine($" - Inverted: {(res3.HasValue ? res3.Value.ToString() : "No Date")}");
             }
             
             return sb.ToString();
         }
         catch (Exception ex)
         {
             return $"Error: {ex.Message}";
         }
    }

    private bool TryParseTimestamp(string text, out DateTime date)
    {
        // Simplify text
        var cleaned = text.Replace('O', '0').Replace('l', '1').Replace(',', '.').Replace("|", "").Replace("\n", " ");
        
        // Remove day names (SUN, MON, etc)
        string[] days = { "SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT" };
        foreach (var day in days)
        {
            if (cleaned.ToUpper().EndsWith(day))
            {
                cleaned = cleaned.Substring(0, cleaned.Length - 3).Trim();
            }
        }
        
        string[] formats = 
        { 
            "yyyy/MM/dd hh:mm:ss tt", 
            "yyyy-MM-dd HH:mm:ss", 
            "dd/MM/yyyy HH:mm:ss",
            "MM/dd/yyyy hh:mm:ss tt",
            "yyyy/MM/dd HH:mm:ss"
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
