using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using OpenCvSharp;


class Program
{
    static async Task Main(string[] args)
    {
        // Initialize OCR Engine
        var available = OcrEngine.AvailableRecognizerLanguages;
        System.Console.WriteLine($"Available Languages: {available.Count}");
        foreach(var l in available) System.Console.WriteLine($"- {l.LanguageTag}");

        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine == null)
        {
             System.Console.WriteLine("TryCreateFromUserProfileLanguages returned null. Trying en-US explicit.");
             var lang = new Windows.Globalization.Language("en-US");
             engine = OcrEngine.TryCreateFromLanguage(lang);
        }
        
        if (engine == null)
        {
             System.Console.WriteLine("OCR Not supported on this device or language.");
             return;
        }

        string imagePath = @"C:\Users\k2\.gemini\antigravity\brain\9fb8f5b7-cf86-4f43-b311-eb22a1e9a85e\uploaded_media_2_1770093963058.jpg";
        
        System.Console.WriteLine($"Testing Windows OCR on: {imagePath}");

        if (!File.Exists(imagePath))
        {
             System.Console.WriteLine("Image file not found.");
             return;
        }

        using var src = new Mat(imagePath);
        int w = src.Width;
        int h = src.Height;

        // Crop top 200 pixels (or less if image is smaller)
        int cropHeight = Math.Min(200, h);
        using var topStrip = new Mat(src, new OpenCvSharp.Rect(0, 0, w, cropHeight));
        
        // Define ROIs within the top strip
        int cropW = w / 3;
        int stripH = cropHeight;
        int centerX = (w - cropW) / 2;
        
        var rois = new OpenCvSharp.Rect[]
        {
            new OpenCvSharp.Rect(0, 0, cropW, stripH), // Top-Left
            new OpenCvSharp.Rect(w - cropW, 0, cropW, stripH), // Top-Right
            new OpenCvSharp.Rect(centerX, 0, cropW, stripH), // Top-Center
        };

        for (int i = 0; i < rois.Length; i++)
        {
             System.Console.WriteLine($"Scanning ROI {i}...");
             using var crop = new Mat(topStrip, rois[i]);
             
             // Windows OCR works best on original images, but scaling up small text helps.
             // Let's try raw crop first, then scaled if needed.
             // Actually, simplest is just to try OCR on the crop converted to SoftwareBitmap
             
             try 
             {
                 // Attempt 1: Raw
                 var text = await OcrMatAsync(engine, crop);
                 if (!string.IsNullOrWhiteSpace(text))
                 {
                      System.Console.WriteLine($"ROI {i} Text: {text.Replace("\n", " ")}");
                      continue;
                 }

                 // Attempt 2: Scale 3x
                 using var scaled = new Mat();
                 Cv2.Resize(crop, scaled, new OpenCvSharp.Size(crop.Width * 3, crop.Height * 3));
                 var text2 = await OcrMatAsync(engine, scaled);
                 if (!string.IsNullOrWhiteSpace(text2))
                 {
                      System.Console.WriteLine($"ROI {i} [Scaled 3x] Text: {text2.Replace("\n", " ")}");
                      continue;
                 }
                 
                 // Attempt 3: Invert (Text might be white)
                 using var inverted = new Mat();
                 Cv2.BitwiseNot(crop, inverted);
                 var text3 = await OcrMatAsync(engine, inverted);
                 if (!string.IsNullOrWhiteSpace(text3))
                 {
                      System.Console.WriteLine($"ROI {i} [Inverted] Text: {text3.Replace("\n", " ")}");
                      continue; 
                 }
             }
             catch (Exception ex)
             {
                  System.Console.WriteLine($"ROI {i} Error: {ex.Message}");
             }
        }
    }

    static async Task<string> OcrMatAsync(OcrEngine engine, Mat mat)
    {
        System.Console.WriteLine($"Processing Mat: {mat.Width}x{mat.Height}");
        
        using var temp = new Mat();
        Cv2.CvtColor(mat, temp, ColorConversionCodes.BGR2BGRA);
        
        Cv2.ImEncode(".png", temp, out byte[] buf);
        using var stream = new MemoryStream(buf);
        var randomAccessStream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        await randomAccessStream.WriteAsync(buf.AsBuffer());
        randomAccessStream.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(randomAccessStream);
        using var softwareBitmap = await decoder.GetSoftwareBitmapAsync();
        
        System.Console.WriteLine($"SoftwareBitmap: {softwareBitmap.PixelWidth}x{softwareBitmap.PixelHeight}");
        
        var result = await engine.RecognizeAsync(softwareBitmap);
        return result.Text;
    }
}

// Helper to allow AsBuffer
public static class Extensions
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
