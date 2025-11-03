using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Microsoft.Win32;
using WpfApp1.Models;
using WpfApp1.ViewModels;

namespace WpfApp1
{
    public partial class MainWindow : Window
    {
        private MainViewModel _viewModel;
        private DispatcherTimer _timer;
        private bool _isDraggingSlider = false;

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainViewModel();
            DataContext = _viewModel;

            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(100);
            _timer.Tick += Timer_Tick;
        }

        private void LoadPakButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "PAK Files (*.pak)|*.pak|All Files (*.*)|*.*",
                Title = "Selecione o arquivo .pak"
            };

            if (dialog.ShowDialog() == true)
            {
                _viewModel.LoadPakFile(dialog.FileName);
            }
        }

        private void ReplaceButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is PakItem item)
            {
                _viewModel.ReplaceRawFile(item);
            }
        }

        private void ExtractButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is PakItem item)
            {
                _viewModel.ExtractRawFile(item);
            }
        }

        private void ConvertAndReplaceButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is PakItem item)
            {
                _viewModel.ConvertAndReplaceAudio(item);
            }
        }

        private void PlaySoundButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is PakItem item)
            {
                string wavPath = _viewModel.PrepareAudioForPlayback(item);
                if (!string.IsNullOrEmpty(wavPath) && File.Exists(wavPath))
                {
                    PlayAudio(wavPath);
                }
            }
        }

        private void BatchReplaceButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.BatchConvertAndReplace();
        }

        private void PlayAudio(string filePath)
        {
            if (AudioPlayer != null)
            {
                AudioPlayer.Stop();
                AudioPlayer.Source = new Uri(filePath, UriKind.Absolute);
                AudioPlayer.Play();
                _timer.Start();
            }
        }

        private void AudioPlayer_MediaOpened(object sender, RoutedEventArgs e)
        {
            if (AudioPlayer?.NaturalDuration.HasTimeSpan == true && ProgressSlider != null && TotalTimeText != null)
            {
                ProgressSlider.Maximum = AudioPlayer.NaturalDuration.TimeSpan.TotalSeconds;
                TotalTimeText.Text = FormatTime(AudioPlayer.NaturalDuration.TimeSpan);
            }
        }

        private void AudioPlayer_MediaEnded(object sender, RoutedEventArgs e)
        {
            _timer.Stop();
            _viewModel.CleanupAudioFiles();
            if (ProgressSlider != null && CurrentTimeText != null)
            {
                ProgressSlider.Value = 0;
                CurrentTimeText.Text = "00:00";
            }
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            if (!_isDraggingSlider && AudioPlayer?.NaturalDuration.HasTimeSpan == true && ProgressSlider != null && CurrentTimeText != null)
            {
                ProgressSlider.Value = AudioPlayer.Position.TotalSeconds;
                CurrentTimeText.Text = FormatTime(AudioPlayer.Position);
            }
        }

        private void ProgressSlider_DragStarted(object sender, DragStartedEventArgs e)
        {
            _isDraggingSlider = true;
        }

        private void ProgressSlider_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            _isDraggingSlider = false;
            if (AudioPlayer != null && ProgressSlider != null)
            {
                AudioPlayer.Position = TimeSpan.FromSeconds(ProgressSlider.Value);
            }
        }

        private void ClosePlayer_Click(object sender, RoutedEventArgs e)
        {
            if (AudioPlayer != null)
            {
                AudioPlayer.Stop();
            }
            _timer.Stop();
            _viewModel.CleanupAudioFiles();
            _viewModel.IsPlayerVisible = false;
        }

        private string FormatTime(TimeSpan time)
        {
            return $"{(int)time.TotalMinutes:D2}:{time.Seconds:D2}";
        }
    }
}