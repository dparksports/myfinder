using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MyFinder.Models;
using MyFinder.Services;

namespace MyFinder;

public partial class VoiceAnalysisWindow : Window
{
    private readonly MediaFile _file;
    private readonly VoiceScanningService _service;
    private List<Services.SpeakerCluster> _clusters = new();

    public VoiceAnalysisWindow(MediaFile file, VoiceScanningService service)
    {
        InitializeComponent();
        _file = file;
        _service = service;
        
        Loaded += VoiceAnalysisWindow_Loaded;
    }

    private async void VoiceAnalysisWindow_Loaded(object sender, RoutedEventArgs e)
    {
        LoadingOverlay.Visibility = Visibility.Visible;
        try
        {
            _clusters = await _service.ScanVideoForVoicesAsync(_file);
            LstClusters.ItemsSource = _clusters;
            
            if (_clusters.Any())
            {
                LstClusters.SelectedIndex = 0;
            }
            else
            {
                MessageBox.Show("No voice activity detected in this file.");
                Close();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error analyzing video: {ex.Message}");
            Close();
        }
        finally
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void LstClusters_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LstClusters.SelectedItem is Services.SpeakerCluster cluster)
        {
            LstSegments.ItemsSource = cluster.Segments;
            TxtSpeakerName.Text = cluster.Id;
            TxtStatus.Text = "Select a segment below to play";
            MediaPlayer.Stop();
            MediaPlayer.Source = null; // Unload
        }
    }

    private void BtnRename_Click(object sender, RoutedEventArgs e)
    {
        if (LstClusters.SelectedItem is Services.SpeakerCluster cluster && !string.IsNullOrWhiteSpace(TxtSpeakerName.Text))
        {
            cluster.Id = TxtSpeakerName.Text;
            LstClusters.Items.Refresh(); // Refresh UI to show new name
        }
    }

    private void BtnPlaySegment_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Services.AudioTranscriber.TranscriptionSegment seg)
        {
             PlaySegment(seg);
        }
    }

    private async void PlaySegment(Services.AudioTranscriber.TranscriptionSegment seg)
    {
        // Simple approach: Play original file, seek to start
        // A standard MediaElement might struggle with perfectly seamless loops, but good enough for review.
        
        try 
        {
            TxtStatus.Text = $"Playing: {seg.Start:mm\\:ss} - {seg.End:mm\\:ss}";
            MediaPlayer.Source = new Uri(_file.FilePath);
            MediaPlayer.Position = seg.Start;
            MediaPlayer.Play();

            // Auto-stop is tricky without a timer/event loop.
            // For now, let user manually stop or it just plays on.
            // Improve: Start a delay task to stop?
            
            var duration = seg.End - seg.Start;
            await System.Threading.Tasks.Task.Delay(duration);
            MediaPlayer.Pause();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Playback error: {ex.Message}");
        }
    }
}
