using System;
using System.Windows;
using System.Windows.Controls;

namespace MyFinder.Views;

public partial class SettingsView : UserControl
{
    private Services.ConfigService? _config;
    private Services.MetadataStore? _store;
    private Services.TimestampService? _timestampService;
    private Services.FaceRecognitionService? _faceService;
    private Services.VoiceFingerprintService? _voiceService;

    public event EventHandler? BackRequested;

    public SettingsView()
    {
        InitializeComponent();
    }

    public void Initialize(Services.ConfigService config, Services.MetadataStore? store, Services.TimestampService timestampService)
    {
        Initialize(config, store, timestampService, null, null);
    }
    
    public void Initialize(Services.ConfigService config, Services.MetadataStore? store, Services.TimestampService timestampService, Services.FaceRecognitionService? faceService, Services.VoiceFingerprintService? voiceService)
    {
        _config = config;
        _store = store;
        _timestampService = timestampService;
        _faceService = faceService;
        _voiceService = voiceService;

        // Load values
        if (_config != null)
        {
            ChkSkipSystem.IsChecked = _config.SkipSystemFolders;
            TxtRecency.Text = _config.RecencyDurationHours.ToString();
            
            ChkPerson.IsChecked = _config.DetectPersons;
            ChkVehicle.IsChecked = _config.DetectVehicles;
            ChkAnimal.IsChecked = _config.DetectAnimals;
            
            // Load Whisper Model
            string currentModel = _config.WhisperModel ?? "Base";
            for(int i=0; i<CboWhisperModel.Items.Count; i++)
            {
                 if (CboWhisperModel.Items[i] is ComboBoxItem item && 
                     (item.Content?.ToString()?.StartsWith(currentModel) == true))
                 {
                     CboWhisperModel.SelectedIndex = i;
                     break;
                 }
            }
        }
    }
    
    private void ShowDebug(string text)
    {
        TxtDebugOutput.Text = text;
        TxtDebugOutput.Visibility = Visibility.Visible;
    }

    private async void BtnDebugTime_Click(object sender, RoutedEventArgs e)
    {
        string? path = SelectFile("Video Files|*.mp4;*.mov;*.avi;*.mkv");
        if (path == null) return;
        
        BtnDebugTime.Content = "Testing...";
        BtnDebugTime.IsEnabled = false;
        
        string result = await _timestampService!.TestExtractionAsync(path);
        
        BtnDebugTime.Content = "Debug Timestamp";
        BtnDebugTime.IsEnabled = true;
        
        ShowDebug(result);
    }

    private async void BtnDebugFace_Click(object sender, RoutedEventArgs e)
    {
         if (_faceService == null) { ShowDebug("Face Service not available."); return; }
         string? path = SelectFile("Video Files|*.mp4;*.mov;*.avi;*.mkv");
         if (path == null) return;

         BtnDebugFace.Content = "Testing...";
         BtnDebugFace.IsEnabled = false;
         
         string result = await _faceService.TestFaceDetectionAsync(path);
         
         BtnDebugFace.Content = "Debug Face";
         BtnDebugFace.IsEnabled = true;
         
         ShowDebug(result);
    }

    private async void BtnDebugVoice_Click(object sender, RoutedEventArgs e)
    {
         if (_voiceService == null) { ShowDebug("Voice Service not available."); return; }
         string? path = SelectFile("Video Files|*.mp4;*.mov;*.avi;*.mkv;*.mp3;*.wav");
         if (path == null) return;

         BtnDebugVoice.Content = "Testing...";
         BtnDebugVoice.IsEnabled = false;
         
         string result = "Extracting Voice Embedding...";
         ShowDebug(result);
         
         try 
         {
             var embedding = await _voiceService.GetVoiceEmbeddingAsync(path);
             result = $"Voice Debug for: {System.IO.Path.GetFileName(path)}\n";
             result += $"Embedding Found: {(embedding != null && embedding.Length > 0 ? "YES" : "NO")}\n";
             if (embedding != null) result += $"Length: {embedding.Length}\n";
             if (embedding != null && embedding.Length > 0) result += $"First 5: {string.Join(", ", embedding.Take(5))}";
         }
         catch (Exception ex)
         {
             result = $"Error: {ex.Message}";
         }
         
         BtnDebugVoice.Content = "Debug Voice";
         BtnDebugVoice.IsEnabled = true;
         
         ShowDebug(result);
    }

    private string? SelectFile(string filter)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = filter };
        if (dialog.ShowDialog() == true) return dialog.FileName;
        return null;
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        // Validate inputs
        if (int.TryParse(TxtRecency.Text, out int hours))
        {
            _config!.RecencyDurationHours = hours;
        }
        else
        {
            MessageBox.Show("Please enter a valid number for recent hours.");
            return;
        }

        _config!.SkipSystemFolders = ChkSkipSystem.IsChecked ?? true;
        
        _config!.DetectPersons = ChkPerson.IsChecked ?? false;
        _config!.DetectVehicles = ChkVehicle.IsChecked ?? false;
        _config!.DetectAnimals = ChkAnimal.IsChecked ?? false;
        
        // Save Whisper Model
        if (CboWhisperModel.SelectedItem is ComboBoxItem selectedItem)
        {
             string content = selectedItem.Content?.ToString() ?? "Base";
             string modelName = content.Split(' ')[0]; // "Tiny", "Base" etc.
             _config!.WhisperModel = modelName;
        }

        await _config!.SaveAsync();
        
        // Notify back
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private void BtnWipe_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show("Are you sure you want to wipe myfinder_index? This cannot be undone.", "Confirm Wipe", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result == MessageBoxResult.Yes)
        {
            if (_store != null)
            {
                _store.Clear();
                _store.SaveAsync().Wait(); // Sync wait for simplicity here
            }
            MessageBox.Show("Database wiped. Please restart scan.");
        }
    }
}
