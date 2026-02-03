using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace MyFinder;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly MyFinder.Services.MetadataStore _store;
    private readonly MyFinder.Services.FileScanner _scanner;
    private readonly MyFinder.Services.ConfigService _config;
    private bool _isListView = false;

    public MainWindow()
    {
        InitializeComponent();
        
        // Initialize services (in a real app, use Dependency Injection)
        // Use CommonApplicationData (C:\ProgramData) for persistent storage across updates
        string appData = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MyFinder");
        if (!System.IO.Directory.Exists(appData)) System.IO.Directory.CreateDirectory(appData);
        
        _config = new MyFinder.Services.ConfigService(appData);
        _store = new MyFinder.Services.MetadataStore(appData);
        _scanner = new MyFinder.Services.FileScanner(_store);
        
        _scanner.OnFileFound += (path) => Dispatcher.Invoke(() => TxtStatus.Text = $"Found: {path}");
        _scanner.OnScanComplete += async (count) => 
        {
            await _store.SaveAsync();
            Dispatcher.Invoke(() => 
            {
                TxtStatus.Text = $"Scan Complete. Found {count} files.";
                RefreshList();
                BtnScan.IsEnabled = true;
            });
        };
        
        Loaded += async (s, e) => 
        {
            await _config.LoadAsync();
            await _store.LoadAsync();
            RefreshList();
            
            // Apply settings
            _scanner.SkipSystemFolders = _config.SkipSystemFolders;
            
            // Check Dependencies (FFmpeg)
            await MyFinder.Services.DependencyService.EnsureFFmpegAsync();

            // Auto-Download AI Models
            var progress = new Progress<string>(status => Dispatcher.Invoke(() => TxtStatus.Text = status));
            await MyFinder.Services.ModelManager.EnsureAllModelsAsync(progress);
        };
    }
    
    // Helper to find model in CommonData or fall back to local bin
    private string GetModelPath(string fileName)
    {
        string commonData = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MyFinder");
        string commonPath = System.IO.Path.Combine(commonData, fileName);
        if (System.IO.File.Exists(commonPath)) return commonPath;
        
        // Fallback to bin folder (current directory)
        return fileName;
    }

    private void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
        // Simple Folder Browser using WinForms (easiest in simple WPF app without extra packages)
        // Alternatively, use Ookii.Dialogs for a modern one, but we stick to native/simple for now.
        // For pure WPF .NET Core, we often just use the string or a hacked OpenFileDialog.
        // Let's use OpenFileDialog with "CheckFileExists = false" hack or just asking user to paste.
        // BETTER: Use System.Windows.Forms dependency (add-on) OR just a simple hack for now?
        // Actually, WinUI 3 would use FolderPicker. Since this is WPF, let's use a simple approach:
        
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            ValidateNames = false,
            CheckFileExists = false,
            CheckPathExists = true,
            FileName = "Select Folder",
            Filter = "Folders|no.files"
        };
        
        // This is a common hack for folder selection in pure WPF without Ookii
        // Ideally we'd add the Ookii.Dialogs.Wpf NuGet, but let's try to keep deps low.
        // User can also just paste "Z:\" or "D:\"
        
        // Let's just assume the user knows how to type for now to avoid the confusing "Select Folder" file hack,
        // unless I add System.Windows.Forms reference.
        // Wait! The user asked "how can the user select other drives".
        // The text box supports it directly. I will add a "Common Drives" dropdown or just let them type.
        
        // Okay, I will implement a "Smart Drive Selector" popup instead of a broken dialog.
        // NO, let's just use the FolderBrowserDialog from System.Windows.Forms if available,
        // or just list drives in a ContextMenu.
        
        // Let's try ContextMenu for "BtnBrowse" to show available Drives!
        var menu = new ContextMenu();
        foreach (var drive in System.IO.DriveInfo.GetDrives())
        {
            if (drive.IsReady)
            {
                var item = new MenuItem { Header = $"{drive.Name}  ({drive.VolumeLabel}) - {drive.DriveType}" };
                item.Click += (s, args) => TxtPath.Text = drive.Name;
                menu.Items.Add(item);
            }
        }
        BtnBrowse.ContextMenu = menu;
        BtnBrowse.ContextMenu.IsOpen = true;
        BtnBrowse.ContextMenu = menu;
        BtnBrowse.ContextMenu.IsOpen = true;
    }

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        var settingsWin = new SettingsWindow(_config, _store);
        settingsWin.Owner = this;
        if (settingsWin.ShowDialog() == true)
        {
             RefreshList(); // Refresh in case settings changed
        }
    }

    private void CboSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshList();
    }

    private void BtnViewToggle_Click(object sender, RoutedEventArgs e)
    {
        _isListView = !_isListView;
        BtnViewToggle.Content = _isListView ? "Grid View" : "List View";
        
        // Swap Template logic would go here. For now, we will just use the RefreshList to trigger update if we bound ItemTemplate selector,
        // but since we are doing simple code-behind swap:
        if (_isListView)
        {
             // Switch to List
             var factory = new FrameworkElementFactory(typeof(StackPanel));
             var template = new ItemsPanelTemplate { VisualTree = factory };
             LstFiles.ItemsPanel = template;
             
             // In a full implementation, we would swap the ItemTemplate to a Row-based DataTemplate here.
             // For this MVP, we just stack them vertically which looks like a list enough given the current Card template.
             // To do it properly, we'd need a secondary DataTemplate resource.
             // Let's assume we keep the card but stack them.
        }
        else
        {
             // Switch back to Grid (WrapPanel)
             var factory = new FrameworkElementFactory(typeof(WrapPanel));
             factory.SetValue(WrapPanel.ItemWidthProperty, 230.0);
             factory.SetValue(WrapPanel.ItemHeightProperty, 270.0);
             var template = new ItemsPanelTemplate { VisualTree = factory };
             LstFiles.ItemsPanel = template;
        }
    }

    private async void BtnScan_Click(object sender, RoutedEventArgs e)
    {
        string path = TxtPath.Text;
        if (System.IO.Directory.Exists(path))
        {
            BtnScan.IsEnabled = false;
            
            // Settings applied from ConfigService now
            _scanner.SkipSystemFolders = _config.SkipSystemFolders;
            
            TxtStatus.Text = $"Scanning {path}...";
            await _scanner.ScanDriveAsync(path);
        }
        else
        {
            MessageBox.Show($"Directory not found: {path}\n\nFor Network Drives, ensure they are mapped (e.g. Z:\\) or type the UNC path (\\\\Server\\Share).");
        }
    }

    private async void BtnAnalyze_Click(object sender, RoutedEventArgs e)
    {
        string modelPath = GetModelPath("yolov8n.onnx");
        
        var analyzer = new MyFinder.Services.VideoAnalyzer(modelPath);
        await analyzer.InitializeAsync(); // This loads the model (if present)

        var files = _store.GetAll().ToList();
        int processed = 0;
        
        BtnAnalyze.IsEnabled = false;

        await Task.Run(async () =>
        {
            foreach (var file in files)
            {
                // Only analyze videos
                string ext = System.IO.Path.GetExtension(file.FilePath).ToLower();
                if (ext == ".mp4" || ext == ".mkv" || ext == ".avi")
                {
                    Dispatcher.Invoke(() => TxtStatus.Text = $"Analyzing: {file.FileName}...");
                    
                    try 
                    {
                        await analyzer.AnalyzeVideoAsync(file);
                        file.IsAnalyzed = true;
                    }
                    catch (Exception ex)
                    {
                         System.Diagnostics.Debug.WriteLine($"Failed to analyze {file.FileName}: {ex.Message}");
                    }
                    
                    processed++;
                    
                    // Periodically save/refresh
                    if (processed % 5 == 0)
                    {
                        await _store.SaveAsync();
                        Dispatcher.Invoke(RefreshList);
                    }
                }
            }
        });

        await _store.SaveAsync();
        TxtStatus.Text = "Analysis Complete.";
        BtnAnalyze.IsEnabled = true;
        RefreshList();
    }

    private async void BtnTranscribe_Click(object sender, RoutedEventArgs e)
    {
        // Whisper handles download, so point it to CommonData to save it there persistence
        string commonData = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MyFinder");
        string modelPath = System.IO.Path.Combine(commonData, "ggml-base.bin");

        var transcriber = new MyFinder.Services.AudioTranscriber(modelPath);
        
        TxtStatus.Text = "Loading Whisper Model (may download)...";
        await transcriber.InitializeAsync();

        var selected = LstFiles.SelectedItem as MyFinder.Models.MediaFile;
        if (selected != null)
        {
            Dispatcher.Invoke(() => TxtStatus.Text = $"Transcribing {selected.FileName}...");
            selected.TranscriptSnippet = await transcriber.TranscribeAsync(selected);
            System.Windows.MessageBox.Show(selected.TranscriptSnippet, "Transcript");
            await _store.SaveAsync();
        }
        else
        {
            // Batch mode or warning
             System.Windows.MessageBox.Show("Please select a file to transcribe first.");
        }
        
        TxtStatus.Text = "Transcription Ready.";
    }

    private async void BtnFace_Click(object sender, RoutedEventArgs e)
    {
        string recPath = GetModelPath("arcface.onnx");
        var faceService = new MyFinder.Services.FaceRecognitionService(recPath);
        
        TxtStatus.Text = "Initializing Face Service...";
        await faceService.InitializeAsync();
        
        var files = _store.GetAll().ToList();
        foreach(var file in files)
        {
             if (file.FilePath.EndsWith(".mp4"))
             {
                 Dispatcher.Invoke(() => TxtStatus.Text = $"Finding faces in {file.FileName}...");
                 await faceService.ProcessVideoForFacesAsync(file);
             }
        }
        
        await _store.SaveAsync();
        TxtStatus.Text = "Face Scan Complete.";
        RefreshList();
    }

    private async void BtnVoice_Click(object sender, RoutedEventArgs e)
    {
        string modelPath = GetModelPath("voxceleb.onnx");
        var voiceService = new MyFinder.Services.VoiceFingerprintService(modelPath);
        
        TxtStatus.Text = "Initializing Voice Service...";
        await voiceService.InitializeAsync();
        
        var selected = LstFiles.SelectedItem as MyFinder.Models.MediaFile;
        // In this proto, we just generate embedding for selected file
        if (selected != null)
        {
             Dispatcher.Invoke(() => TxtStatus.Text = $"Generating Voice ID for {selected.FileName}...");
             var embedding = await voiceService.GetVoiceEmbeddingAsync(selected.FilePath);
             
             if (embedding.Length > 0)
             {
                 System.Windows.MessageBox.Show($"Generated Vector: [{string.Join(", ", embedding.Take(5))}...]", "Voice ID");
             }
             else
             {
                 System.Windows.MessageBox.Show("Failed to extract voice (file too short or silent?)");
             }
        }
    }

    private void MenuItem_Play_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item && item.Tag is string path)
        {
            PlayFile(path);
        }
    }

    private void MenuItem_OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item && item.Tag is string path)
        {
           try
           {
               // Selects the file in Explorer
               System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
           }
           catch { }
        }
    }

    private void MenuItem_AddTag_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item && item.Tag is string path)
        {
            // Simple InputBox hack for WPF
            string tag = Microsoft.VisualBasic.Interaction.InputBox("Enter Tag Name:", "Add Tag", "");
            if (!string.IsNullOrWhiteSpace(tag))
            {
                _store.AddTag(path, tag);
                _store.SaveAsync();
                RefreshList();
            }
        }
    }

    private void PlayFile(string path)
    {
        try 
        {
            // Update Last Opened
            var file = _store.GetByPath(path);
            if (file != null)
            {
                file.LastOpened = DateTime.Now;
                _store.SaveAsync();
                RefreshList(); 
            }

            // Open with default player
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not play value: {ex.Message}");
        }
    }

    private async void BtnTime_Click(object sender, RoutedEventArgs e)
    {
        string appData = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MyFinder");
        var timeService = new MyFinder.Services.TimestampService(appData);
        
        TxtStatus.Text = "Initializing OCR...";
        await timeService.InitializeAsync();
        
        var files = _store.GetAll().ToList();
        foreach(var file in files)
        {
             if (file.FilePath.EndsWith(".mp4") && file.ExtractedTimestamp == null)
             {
                 Dispatcher.Invoke(() => TxtStatus.Text = $"Checking Date in {file.FileName}...");
                 await timeService.ExtractTimestampAsync(file);
             }
        }
        
        await _store.SaveAsync();
        TxtStatus.Text = "Date Extraction Complete.";
        RefreshList();
    }

    private void RefreshList()
    {
        if (_store == null || _config == null) return;

        // Sort Logic
        IEnumerable<MyFinder.Models.MediaFile> query = _store.GetAll();
        
        if (CboSort.SelectedIndex == 1) // Size
            query = query.OrderByDescending(f => f.FileSizeBytes);
        else // Date (Default)
            query = query.OrderByDescending(f => f.LastModified);

        // Force refresh for UI binding
        LstFiles.ItemsSource = null;
        var list = query.ToList();
        LstFiles.ItemsSource = list;
        
        // Update Recent
        LstRecent.ItemsSource = _store.GetRecentFiles(TimeSpan.FromHours(_config.RecencyDurationHours));
    }
}