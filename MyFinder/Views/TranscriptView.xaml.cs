using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MyFinder.Models;
using MyFinder.Services;

namespace MyFinder.Views;

public partial class TranscriptView : UserControl
{
    private MediaFile _file;
    private AudioTranscriber _transcriber;
    private DispatcherTimer _timer;
    private bool _isDraggingSlider;

    public event EventHandler BackRequested;

    public TranscriptView()
    {
        InitializeComponent();
        
        _timer = new DispatcherTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(500);
        _timer.Tick += Timer_Tick;
    }

    public async void LoadFile(MediaFile file, AudioTranscriber transcriber)
    {
        _file = file;
        _transcriber = transcriber;
        
        TxtFileName.Text = file.FileName;
        MediaPlayer.Source = new Uri(file.FilePath);
        MediaPlayer.Play();
        _timer.Start();

        // Check if we need to transcribe
        // We don't store segments in MediaFile yet (only snippet), so we might need to re-transcribe or assume snippet implies done?
        // Actually, we want the FULL segments.
        // Let's re-run transcribe if we don't have cached segments (we don't persist segments in JSON currently).
        // Improving: We should cache segments in JSON or sidecar file.
        // For now, we will re-transcribe if needed, or if we just have snippet. 
        // NOTE: Transcribing again is slow. Ideally we save it. 
        // But for this task, let's just run it.
        
        LoadingOverlay.Visibility = Visibility.Visible;
        try 
        {
            // If we have a way to check if already done...
            // For now, simple re-run.
            var segments = await _transcriber.TranscribeAsync(file);
            LstTranscript.ItemsSource = segments;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Transcription error: {ex.Message}");
        }
        finally
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void Timer_Tick(object sender, EventArgs e)
    {
        if (!_isDraggingSlider && MediaPlayer.NaturalDuration.HasTimeSpan)
        {
            SliderTime.Minimum = 0;
            SliderTime.Maximum = MediaPlayer.NaturalDuration.TimeSpan.TotalSeconds;
            SliderTime.Value = MediaPlayer.Position.TotalSeconds;
            
            TxtTime.Text = $"{MediaPlayer.Position:mm\\:ss} / {MediaPlayer.NaturalDuration.TimeSpan:mm\\:ss}";
        }
    }

    private void BtnPlayPause_Click(object sender, RoutedEventArgs e)
    {
        // Simple toggle state check?
        // MediaElement doesn't have IsPlaying property.
        // We'll toggle based on button content or just Try/Catch
        if (BtnPlayPause.Content.ToString() == "▶")
        {
            MediaPlayer.Play();
            BtnPlayPause.Content = "⏸";
             _timer.Start();
        }
        else
        {
            MediaPlayer.Pause();
            BtnPlayPause.Content = "▶";
             _timer.Stop();
        }
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        MediaPlayer.Stop();
        _timer.Stop();
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SliderTime_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingSlider = true;
    }

    private void SliderTime_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isDraggingSlider = false;
        MediaPlayer.Position = TimeSpan.FromSeconds(SliderTime.Value);
    }

    private void SliderTime_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isDraggingSlider)
        {
             // Optional: Seek while dragging? Can be laggy.
             // TxtTime.Text = ...
        }
    }

    private void MediaPlayer_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (MediaPlayer.NaturalDuration.HasTimeSpan)
        {
            SliderTime.Maximum = MediaPlayer.NaturalDuration.TimeSpan.TotalSeconds;
            TxtTime.Text = $"00:00 / {MediaPlayer.NaturalDuration.TimeSpan:mm\\:ss}";
        }
    }

    private void MediaPlayer_MediaEnded(object sender, RoutedEventArgs e)
    {
        BtnPlayPause.Content = "▶";
        _timer.Stop();
    }

    private void LstTranscript_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LstTranscript.SelectedItem is AudioTranscriber.TranscriptionSegment seg)
        {
             MediaPlayer.Position = seg.Start;
             MediaPlayer.Play();
             BtnPlayPause.Content = "⏸";
             _timer.Start();
        }
    }
}
