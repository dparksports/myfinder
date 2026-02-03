using System.Windows;

namespace MyFinder;

public partial class SettingsWindow : Window
{
    private readonly Services.ConfigService _config;
    private readonly Services.MetadataStore _store;

    public SettingsWindow(Services.ConfigService config, Services.MetadataStore store)
    {
        InitializeComponent();
        _config = config;
        _store = store;

        // Load values
        ChkSkipSystem.IsChecked = _config.SkipSystemFolders;
        TxtRecency.Text = _config.RecencyDurationHours.ToString();
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        // Validate inputs
        if (int.TryParse(TxtRecency.Text, out int hours))
        {
            _config.RecencyDurationHours = hours;
        }
        else
        {
            MessageBox.Show("Please enter a valid number for recent hours.");
            return;
        }

        _config.SkipSystemFolders = ChkSkipSystem.IsChecked ?? true;

        await _config.SaveAsync();
        DialogResult = true;
        Close();
    }

    private void BtnWipe_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show("Are you sure you want to wipe myfinder_index? This cannot be undone.", "Confirm Wipe", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result == MessageBoxResult.Yes)
        {
            _store.Clear();
            _store.SaveAsync().Wait(); // Sync wait for simplicity here
            MessageBox.Show("Database wiped. Please restart scan.");
        }
    }
}
