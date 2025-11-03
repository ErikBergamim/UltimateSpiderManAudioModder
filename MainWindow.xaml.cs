using Microsoft.Win32;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using WpfApp1.Models;
using WpfApp1.ViewModels;

namespace WpfApp1
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private MainViewModel _viewModel;
        private bool _isUserDraggingSlider = false;

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainViewModel();
            DataContext = _viewModel;
        }

        private async void LoadWbkButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "WBK Files (*.wbk)|*.wbk|All Files (*.*)|*.*",
                Title = "Select WBK File"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                await _viewModel.LoadWbkFile(openFileDialog.FileName);
            }
        }

        private async void PlaySoundButton_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            WbkItem item = button?.Tag as WbkItem;

            if (item == null)
                return;

            await _viewModel.PlaySound(item);
        }

        private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.PlayPauseAudio();
            
            // Atualiza o ícone do botão
            Button button = sender as Button;
            if (button != null)
            {
                button.Content = _viewModel.IsPlaying ? "⏸️" : "▶️";
                button.ToolTip = _viewModel.IsPlaying ? "Pause" : "Play";
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.StopAudio();
        }

        private void PositionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            Slider slider = sender as Slider;
            if (slider != null && slider.IsMouseCaptureWithin)
            {
                _viewModel.SeekPosition(e.NewValue);
            }
        }

        private async void ConvertAndReplaceButton_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            WbkItem item = button?.Tag as WbkItem;

            if (item == null)
                return;

            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "WAV Files (*.wav)|*.wav|All Files (*.*)|*.*",
                Title = $"Select WAV file to convert for {item.FileName}"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                await _viewModel.ConvertAndReplaceWav(item, openFileDialog.FileName);
            }
        }

        private async void ReplaceButton_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            WbkItem item = button?.Tag as WbkItem;

            if (item == null)
                return;

            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "RAW Files (*.raw)|*.raw|All Files (*.*)|*.*",
                Title = $"Select replacement for {item.FileName}"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                await _viewModel.ReplaceFileInWbk(item, openFileDialog.FileName);
            }
        }

        private async void ReplaceMultipleButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "WAV Files (*.wav)|*.wav|All Files (*.*)|*.*",
                Title = "Select multiple WAV files to convert and replace",
                Multiselect = true
            };

            if (openFileDialog.ShowDialog() == true)
            {
                string[] selectedFiles = openFileDialog.FileNames;
                await _viewModel.BatchConvertAndReplace(selectedFiles);
            }
        }

        private void ClosePlayer_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.StopAudio();
        }

        private void PlaySoundInfo_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "Why are the sounds choppy?\n\n" +
                "This audio player only verifies if the audio has actually been replaced; " +
                "within the game, the sound will not play this way. This player is buggy " +
                "because I haven't had time to correctly implement the header reconstruction function.",
                "Audio Player Information",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }
}