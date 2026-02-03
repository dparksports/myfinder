using System;
using System.Windows;
using System.Windows.Controls;

namespace MyFinder.Views;

public partial class SettingsView : UserControl
{
    private Services.ConfigService? _config;
    private Services.MetadataStore? _store;
    private Services.TimestampService? _timestampService;

    public event EventHandler? BackRequested;

    public SettingsView()
    {
        InitializeComponent();
    }

    public void Initialize(Services.ConfigService config, Services.MetadataStore? store, Services.TimestampService timestampService)
    {
        _config = config;
        _store = store;
        _timestampService = timestampService;

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
    
    private async void BtnDebugTime_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Video Files|*.mp4;*.mov;*.avi;*.mkv",
            Title = "Select Video to Test Timestamp Extraction"
        };

        if (dialog.ShowDialog() == true)
        {
            string path = dialog.FileName;
            BtnDebugTime.Content = "Running Test...";
            BtnDebugTime.IsEnabled = false;
            
            string result = await _timestampService!.TestExtractionAsync(path);
            
            BtnDebugTime.Content = "Debug Timestamp Extraction (Select File)";
            BtnDebugTime.IsEnabled = true;
            
            // Show result with ScrollViewer in MessageBox if possible, but MessageBox can be small.
            // Let's just output to MessageBox, user can screenshot it.
            MessageBox.Show(result, "Timestamp Extraction Debug Log");
        }
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
