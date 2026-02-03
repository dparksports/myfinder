using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace MyFinder.Services;

public static class DependencyService
{
    public static async Task EnsureFFmpegAsync()
    {
        if (IsFFmpegAvailable()) return;

        // Try WinGet
        var result = MessageBox.Show(
            "FFmpeg is required for Audio/Video processing but was not found.\n\nDo you want to install it automatically via WinGet?", 
            "Dependency Missing", 
            MessageBoxButton.YesNo);

        if (result == MessageBoxResult.Yes)
        {
            await InstallViaWinGet();
        }
    }

    private static bool IsFFmpegAvailable()
    {
        try 
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = "-version",
                CreateNoWindow = true,
                UseShellExecute = false
            });
            proc?.WaitForExit();
            return proc?.ExitCode == 0;
        }
        catch 
        {
            return false;
        }
    }

    private static async Task InstallViaWinGet()
    {
        await Task.Run(() => 
        {
            try
            {
                // winget install Gyan.FFmpeg
                var info = new ProcessStartInfo
                {
                    FileName = "winget",
                    Arguments = "install Gyan.FFmpeg --accept-source-agreements --accept-package-agreements",
                    UseShellExecute = true, // Show the window so user sees progress
                    Verb = "runas" // Admin might be needed
                };
                
                var proc = Process.Start(info);
                proc?.WaitForExit();
                
                if (proc?.ExitCode == 0)
                {
                    MessageBox.Show("FFmpeg installed! You may need to restart the app.");
                }
                else
                {
                    MessageBox.Show("WinGet install failed. Please install FFmpeg manually.");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error running WinGet: {ex.Message}");
            }
        });
    }
}
