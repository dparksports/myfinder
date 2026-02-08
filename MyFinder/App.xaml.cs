using System.Configuration;
using System.Data;
using System.Windows;

namespace MyFinder;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private Services.FirebaseAnalyticsService? _analytics;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Initialize Analytics
        _analytics = new Services.FirebaseAnalyticsService();
        
        // Send app_start event (fire and forget)
        _ = _analytics.LogEventAsync("app_start");
    }
}


