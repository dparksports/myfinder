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
    private MediaFile? _file;
    private AudioTranscriber? _transcriber;
    private DispatcherTimer _timer;
    private bool _isDraggingSlider;

    public event EventHandler? BackRequested;

    public TranscriptView()
    {
        InitializeComponent();
        
        _timer = new DispatcherTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(500);
        _timer.Tick += Timer_Tick;
    }

    private Action? _saveCallback;

    public async void LoadFile(MediaFile file, AudioTranscriber transcriber, Action saveCallback)
    {
        _file = file;
        _transcriber = transcriber;
        _saveCallback = saveCallback;
        
        TxtFileName.Text = file.FileName;
        MediaPlayer.Source = new Uri(file.FilePath);
        MediaPlayer.Play();
        _timer.Start();

        if (_file.Transcripts.Count > 0)
        {
             LoadVersions();
        }
        else
        {
             await RunTranscription(false);
        }
    }

    private async System.Threading.Tasks.Task RunTranscription(bool force)
    {
        if (_transcriber == null || _file == null) return;

        LoadingOverlay.Visibility = Visibility.Visible;
        try 
        {
            var segments = await _transcriber!.TranscribeAsync(_file!, force: force);
            LstTranscript.ItemsSource = segments;
            
            // If new transcription happened (force or first time), save logic is handled by Transcriber updating MediaFile,
            // but we need to trigger Store Save.
            // Transcriber returns existing if !force and exists.
            // If we are forcing, we definitely need save. If we are not forcing but it ran (first time), we need save.
            // Simplified: Always save after transcribe returns, or only if modified?
            // Since Transcriber updates properties, safe to save.
            
            _saveCallback?.Invoke();
            
            // Refresh Versions
            LoadVersions();
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

    private void LoadVersions()
    {
        if (_file == null) return;
        
        var versions = _file.Transcripts.OrderByDescending(t => t.Created).ToList();
        
        CboPrimary.ItemsSource = versions;
        // Default select latest if not selected
        if (CboPrimary.SelectedItem == null && versions.Any())
             CboPrimary.SelectedIndex = 0;
             
        CboSecondary.ItemsSource = versions;
        if (CboSecondary.SelectedItem == null && versions.Count > 1)
             CboSecondary.SelectedIndex = 1; // Default to second latest
        else if (CboSecondary.SelectedItem == null && versions.Any())
             CboSecondary.SelectedIndex = 0;
    }

    private void CboPrimary_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CboPrimary.SelectedItem is TranscriptEntry entry)
        {
             try 
             {
                 var segments = System.Text.Json.JsonSerializer.Deserialize<List<AudioTranscriber.TranscriptionSegment>>(entry.JsonContent);
                 LstTranscript.ItemsSource = segments;
             }
             catch { }
        }
    }
    
    private void CboSecondary_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CboSecondary.SelectedItem is TranscriptEntry entry)
        {
             try 
             {
                 var segments = System.Text.Json.JsonSerializer.Deserialize<List<AudioTranscriber.TranscriptionSegment>>(entry.JsonContent);
                 LstTranscriptSecondary.ItemsSource = segments;
             }
             catch { }
        }
    }

    private void ChkCompare_Checked(object sender, RoutedEventArgs e)
    {
        ColSecondary.Width = new GridLength(1, GridUnitType.Star);
        CboSecondary.Visibility = Visibility.Visible;
        LstTranscriptSecondary.Visibility = Visibility.Visible;
    }

    private void ChkCompare_Unchecked(object sender, RoutedEventArgs e)
    {
        ColSecondary.Width = new GridLength(0);
        CboSecondary.Visibility = Visibility.Hidden;
        LstTranscriptSecondary.Visibility = Visibility.Hidden;
    }

    private void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_file == null || CboPrimary.SelectedItem is not TranscriptEntry entry) return;
        
        var result = MessageBox.Show($"Delete transcript version '{entry.Model}' from {entry.Created}?", "Confirm Delete", MessageBoxButton.YesNo);
        if (result == MessageBoxResult.Yes)
        {
             _file.Transcripts.Remove(entry);
             _saveCallback?.Invoke();
             LoadVersions();
        }
    }

    private void LstTranscriptSecondary_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LstTranscriptSecondary.SelectedItem is AudioTranscriber.TranscriptionSegment seg)
        {
             MediaPlayer.Position = seg.Start;
             MediaPlayer.Play();
             BtnPlayPause.Content = "⏸";
             _timer.Start();
        }
    }

    private async void BtnRetranscribe_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show("Start new transcription? This will create a new version.", "Confirm", MessageBoxButton.YesNo);
        if (result == MessageBoxResult.Yes)
        {
             await RunTranscription(true);
        }
    }

    private void Timer_Tick(object? sender, EventArgs e)
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
