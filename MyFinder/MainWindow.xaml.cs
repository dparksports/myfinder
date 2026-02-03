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
    private readonly Dictionary<string, MyFinder.Services.MetadataStore> _stores = new();
    private readonly MyFinder.Services.ConfigService _config;
    private readonly MyFinder.Services.DriveIdentificationService _driveService;
    // AI Services (Shared to avoid reloading models)
    private MyFinder.Services.AudioTranscriber? _transcriber;
    private MyFinder.Services.VoiceFingerprintService? _voiceService;
    private MyFinder.Services.TimestampService? _timestampService;

    private bool _isListView = false;
    private bool _showRecentOnly = false;

    public class DriveViewModel
    {
        public required string Name { get; set; } // "C:\"
        public required string Label { get; set; } // "Windows"
        public required string DriveId { get; set; } // "883999226"
        public string DisplayName => $"{Label} ({Name})";
        public bool IsSelected { get; set; }
        public required MyFinder.Services.FileScanner Scanner { get; set; }
        public required MyFinder.Services.MetadataStore Store { get; set; }
    }

    public List<DriveViewModel> DriveList { get; set; } = new();

    public MainWindow()
    {
        InitializeComponent();
        
        // Initialize services (in a real app, use Dependency Injection)
        // Use CommonApplicationData (C:\ProgramData) for persistent storage across updates
        string appData = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MyFinder");
        if (!System.IO.Directory.Exists(appData)) System.IO.Directory.CreateDirectory(appData);
        
        _driveService = new MyFinder.Services.DriveIdentificationService();
        _config = new MyFinder.Services.ConfigService(appData);
        // _store and _scanner are now per-drive and initialized in LoadDrives()
        
        Loaded += async (s, e) => 
        {
            await _config.LoadAsync();
            LoadDrives(); // Detect drives and init stores
            RefreshList();
            
            // Check Dependencies (FFmpeg)
            await MyFinder.Services.DependencyService.EnsureFFmpegAsync();

            // Auto-Download AI Models
            var progress = new Progress<string>(status => Dispatcher.Invoke(() => TxtStatus.Text = status));
            await MyFinder.Services.ModelManager.EnsureAllModelsAsync(progress);
            
            // Init AI Services
            // Init AI Services
            string commonData = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MyFinder");
            
            // Parse Model Type
            Whisper.net.Ggml.GgmlType modelType = Whisper.net.Ggml.GgmlType.Base;
            if (Enum.TryParse(_config.WhisperModel, true, out Whisper.net.Ggml.GgmlType parsed))
                modelType = parsed;
                
            _transcriber = new MyFinder.Services.AudioTranscriber(System.IO.Path.Combine(commonData, $"ggml-{modelType.ToString().ToLower()}.bin"), modelType);
            _voiceService = new MyFinder.Services.VoiceFingerprintService(System.IO.Path.Combine(commonData, "voxceleb.onnx"));
            _timestampService = new MyFinder.Services.TimestampService(commonData); // Init Service
            
            // Wire up Transcript Back Button
            TranscriptScreen.BackRequested += (s, args) => 
            {
                TranscriptScreen.Visibility = Visibility.Collapsed;
                MainGalleryView.Visibility = Visibility.Visible;
            };

            // Wire up Settings Back Button
            SettingsScreen.BackRequested += (s, args) => 
            {
                SettingsScreen.Visibility = Visibility.Collapsed;
                MainGalleryView.Visibility = Visibility.Visible;
                RefreshList(); // Refresh in case settings changed
            };

            // Background Init
            _ = Task.Run(async () => 
            {
               await _transcriber.InitializeAsync();
               await _voiceService.InitializeAsync();
               await _timestampService.InitializeAsync();
            });
        };
    }

    private void LoadDrives()
    {
        string appData = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MyFinder");
        DriveList.Clear();

        foreach (var drive in System.IO.DriveInfo.GetDrives().Where(d=>d.IsReady))
        {
            string id = _driveService.GetDriveId(drive);
            
            // Init Store for this specific drive ID
            var store = new MyFinder.Services.MetadataStore(appData, id);
            _stores[id] = store; // Keep track globally if needed
            
            // Init Scanner
            var scanner = new MyFinder.Services.FileScanner(store, _timestampService!);
            scanner.SkipSystemFolders = _config.SkipSystemFolders;
            
            // Wire up events
            scanner.OnFileFound += (path) => Dispatcher.Invoke(() => TxtStatus.Text = $"Found: {path}");
            scanner.OnScanComplete += async (count) => 
            {
                await store.SaveAsync();
                Dispatcher.Invoke(() => 
                {
                    TxtStatus.Text = $"Scan Complete for {drive.Name}.";
                    RefreshList();
                    BtnScan.IsEnabled = true;
                });
            };

            // Load existing index if available
            Task.Run(async () => await store.LoadAsync()).Wait(); // Blocking for safely init UI
            
            DriveList.Add(new DriveViewModel 
            { 
                Name = drive.Name, 
                Label = string.IsNullOrEmpty(drive.VolumeLabel) ? drive.DriveType.ToString() : drive.VolumeLabel,
                DriveId = id,
                Scanner = scanner,
                Store = store
            });
        }
        
        // Bind UI
        LstDrives.ItemsSource = DriveList;
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

    /* BtnBrowse removed
    private void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
    }
    */

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        var firstStore = DriveList.FirstOrDefault()?.Store;
        
        // Initialize view
        SettingsScreen.Initialize(_config, firstStore, _timestampService!);

        // Switch View
        MainGalleryView.Visibility = Visibility.Collapsed;
        SettingsScreen.Visibility = Visibility.Visible;
    }

    private void CboSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshList();
    }

    private void BtnViewToggle_Click(object sender, RoutedEventArgs e)
    {
        _isListView = !_isListView;
        BtnViewToggle.Content = _isListView ? "Grid View" : "List View";
        
        if (_isListView)
        {
             // Switch to Details (Grid) View
             LstFiles.View = FindResource("DetailsView") as ViewBase;
             // Clear ItemTemplate because GridView uses columns, conflict causes crash if both set
             LstFiles.ItemTemplate = null;
             // Reset ItemsPanel to default (StackPanel for ListView)
             LstFiles.ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));
        }
        else
        {
             // Switch Back to Icon View (WrapPanel)
             LstFiles.View = null; 
             // Restore Item Template
             LstFiles.ItemTemplate = FindResource("CardViewTemplate") as DataTemplate;
             
             // Restore WrapPanel
             var factory = new FrameworkElementFactory(typeof(WrapPanel));
             factory.SetValue(WrapPanel.ItemWidthProperty, 230.0);
             factory.SetValue(WrapPanel.ItemHeightProperty, 270.0);
             LstFiles.ItemsPanel = new ItemsPanelTemplate { VisualTree = factory };
        }
    }

    private void BtnRecent_Click(object sender, RoutedEventArgs e)
    {
         _showRecentOnly = !_showRecentOnly;
         BtnRecent.Background = _showRecentOnly ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#007ACC")) : Brushes.Transparent;
         
         // The RefreshList() method will now handle aggregating recent files from all stores
         // based on the _showRecentOnly flag.
         RefreshList();
    }

    private async void BtnScan_Click(object sender, RoutedEventArgs e)
    {
        var selectedDrives = DriveList.Where(d => d.IsSelected).ToList();
        
        if (selectedDrives.Any())
        {
            BtnScan.IsEnabled = false;
            TxtStatus.Text = "Starting Parallel Scan...";
            
            // Run all selected scans in parallel
            var tasks = selectedDrives.Select(d => 
            {
                d.Scanner.SkipSystemFolders = _config.SkipSystemFolders;
                return d.Scanner.ScanDriveAsync(d.Name);
            });
            
            await Task.WhenAll(tasks);
            
            // Re-enabled in OnScanComplete but that fires per drive. 
            // We should ensure it stays enabled.
            BtnScan.IsEnabled = true;
            TxtStatus.Text = "All Scans Complete.";
        }
        else
        {
            // Fallback to manual path
            string path = TxtPath.Text;
            if (System.IO.Directory.Exists(path))
            {
                // We need a store for this Manual Path.
                // Ideally we map it to one of the drives.
                // For now, let's just find the best match or alert user to select a drive.
                MessageBox.Show("Please select a Drive from the list to scan, or ensure your manual path corresponds to a selected drive.");
            }
            else
            {
                MessageBox.Show($"Directory not found: {path}");
            }
        }
    }

    private async void BtnAnalyze_Click(object sender, RoutedEventArgs e)
    {
        string modelPath = GetModelPath("yolov8n.onnx");
        
        var analyzer = new MyFinder.Services.VideoAnalyzer(modelPath, _config);
        // Aggregate all for analysis
        var files = _stores.Values.SelectMany(s => s.GetAll()).ToList();
        int processed = 0;
        
        BtnAnalyze.IsEnabled = false;

        // Move heavy init to background thread
        await Task.Run(() => analyzer.InitializeAsync());

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
                        foreach(var store in _stores.Values) await store.SaveAsync();
                        Dispatcher.Invoke(RefreshList);
                    }
                }
            }
        });

        foreach(var store in _stores.Values) await store.SaveAsync();
        TxtStatus.Text = "Analysis Complete.";
        BtnAnalyze.IsEnabled = true;
        RefreshList();
    }

    private async void BtnTranscribe_Click(object sender, RoutedEventArgs e)
    {
        var selected = LstFiles.SelectedItem as MyFinder.Models.MediaFile;
        if (selected == null)
        {
             System.Windows.MessageBox.Show("Please select a file to transcribe first.");
             return;
        }

        try
        {
            TxtStatus.Text = "Opening Transcript View...";
            await _transcriber!.InitializeAsync(); // Ensure loaded
    
            // Switch to Transcript View
            MainGalleryView.Visibility = Visibility.Collapsed;
            TranscriptScreen.Visibility = Visibility.Visible;
            
            TranscriptScreen.LoadFile(selected, _transcriber, async () => 
            {
                foreach(var d in DriveList) await d.Store.SaveAsync();
            });
            
            TxtStatus.Text = "Ready.";
        }
        catch (Exception ex)
        {
            TxtStatus.Text = "Error opening transcript.";
            MessageBox.Show($"Error: {ex.Message}");
        }
    }

    private async void BtnFace_Click(object sender, RoutedEventArgs e)
    {
        string recPath = GetModelPath("arcface.onnx");
        var faceService = new MyFinder.Services.FaceRecognitionService(recPath);
        
        TxtStatus.Text = "Initializing Face Service...";
        await faceService.InitializeAsync();
        
        // Prioritize Selected File
        var targetFiles = new List<Models.MediaFile>();
        if (LstFiles.SelectedItem is Models.MediaFile selectedFile)
        {
            targetFiles.Add(selectedFile);
        }
        else
        {
            targetFiles = _stores.Values.SelectMany(s => s.GetAll()).ToList();
        }

        foreach(var file in targetFiles)
        {
             if (file.FilePath.EndsWith(".mp4") || file.FilePath.EndsWith(".mkv") || file.FilePath.EndsWith(".mov"))
             {
                 Dispatcher.Invoke(() => TxtStatus.Text = $"Finding faces in {file.FileName}...");
                 try
                 {
                    await faceService.ProcessVideoForFacesAsync(file);
                 }
                 catch (Exception ex)
                 {
                    System.Diagnostics.Debug.WriteLine($"Error processing {file.FileName}: {ex.Message}");
                 }
             }
        }
        
        foreach(var store in _stores.Values) await store.SaveAsync();
        TxtStatus.Text = "Face Scan Complete.";
        RefreshList();
    }

    private async void BtnVoice_Click(object sender, RoutedEventArgs e)
    {
        var selected = LstFiles.SelectedItem as MyFinder.Models.MediaFile;
        if (selected == null)
        {
            MessageBox.Show("Please select a file first.");
            return;
        }

        // Initialize scanner with shared services
        await _transcriber!.InitializeAsync();
        await _voiceService!.InitializeAsync();
        var scanner = new MyFinder.Services.VoiceScanningService(_transcriber, _voiceService);
        
        var win = new VoiceAnalysisWindow(selected, scanner);
        win.Owner = this;
        win.ShowDialog();
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

    private async void MenuItem_AddTag_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item && item.Tag is string path)
        {
            // Simple InputBox hack for WPF
            string tag = Microsoft.VisualBasic.Interaction.InputBox("Enter Tag Name:", "Add Tag", "");
            if (!string.IsNullOrWhiteSpace(tag))
            {
                // Find Store
                foreach(var d in DriveList)
                {
                    if (d.Store.GetByPath(path) != null)
                    {
                        d.Store.AddTag(path, tag);
                        await d.Store.SaveAsync();
                        break;
                    }
                }
                RefreshList();
            }
        }
    }

    private void LstFiles_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListViewItem item && item.Content is MyFinder.Models.MediaFile file)
        {
            PlayFile(file.FilePath);
        }
    }

    private async void PlayFile(string path)
    {
        try 
        {
            // Update Last Opened
            MyFinder.Models.MediaFile? file = null;
            foreach(var d in DriveList) {
                file = d.Store.GetByPath(path);
                if (file != null) {
                    file.LastOpened = DateTime.Now;
                    await d.Store.SaveAsync();
                    break;
                }
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
        // Use shared service
        TxtStatus.Text = "Using Shared OCR Service...";
        await _timestampService!.InitializeAsync();
        
        var allFiles = DriveList.SelectMany(d => d.Store.GetAll()).ToList();
        foreach(var file in allFiles)
        {
             if (file.FilePath.EndsWith(".mp4") && file.ExtractedTimestamp == null)
             {
                 Dispatcher.Invoke(() => TxtStatus.Text = $"Checking Date in {file.FileName}...");
                 // Find Store for file to pass context if needed (though method signature changed to just accept store for potential saving, 
                 // actually I added store to signature in previous step but didn't use it. Let's pass null or fix signature usage).
                 // Wait, I updated signature to (file, store).
                 // We need to find the store for this file.
                 
                 var store = DriveList.FirstOrDefault(d => d.Store.GetByPath(file.FilePath) != null)?.Store;
                 if (store != null) await _timestampService.ExtractTimestampAsync(file, store);
             }
        }
        
        foreach(var d in DriveList) await d.Store.SaveAsync();
        TxtStatus.Text = "Date Extraction Complete.";
        RefreshList();
    }

    private void RefreshList()
    {
        if (DriveList == null || _config == null) return;
        
        // Aggregate all files from all loaded drives
        var allFiles = DriveList.SelectMany(d => d.Store.GetAll());

        // Sort Logic
        IEnumerable<MyFinder.Models.MediaFile> query = allFiles;
        
        if (CboSort.SelectedIndex == 1) // Size
            query = query.OrderByDescending(f => f.FileSizeBytes);
        else // Date (Default)
            query = query.OrderByDescending(f => f.LastModified);

        if (_showRecentOnly)
        {
            // Simple recent filter on aggregate
            var cutoff = DateTime.Now - TimeSpan.FromHours(_config.RecencyDurationHours);
            query = query.Where(f => f.LastOpened >= cutoff).OrderByDescending(f => f.LastOpened);
        }

        // Force refresh for UI binding
        LstFiles.ItemsSource = null;
        var list = query.ToList();
        LstFiles.ItemsSource = list;
    }
}