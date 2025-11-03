using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;

namespace WpfApp1
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private MainViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainViewModel();
            DataContext = _viewModel;
        }

        private void LoadWbkButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.LoadWbkFile();
        }

        private void ReplaceButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button && button.Tag is WbkItem item)
            {
                _viewModel.ReplaceRawAudio(item);
            }
        }

        private void ConvertAndReplaceButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button && button.Tag is WbkItem item)
            {
                _viewModel.ConvertAndReplaceWav(item);
            }
        }

        private void ReplaceMultipleButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.ReplaceMultipleWavFiles();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class MainViewModel : INotifyPropertyChanged
    {
        private string _wbkFilePath;
        private string _wbkFileName = "No file loaded";
        private string _statusMessage = "Ready to load WBK file";
        private bool _isWbkLoaded;
        private ObservableCollection<WbkItem> _wbkItems = new ObservableCollection<WbkItem>();

        public string WbkFileName
        {
            get => _wbkFileName;
            set { _wbkFileName = value; OnPropertyChanged(); }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        public bool IsWbkLoaded
        {
            get => _isWbkLoaded;
            set { _isWbkLoaded = value; OnPropertyChanged(); }
        }

        public ObservableCollection<WbkItem> WbkItems
        {
            get => _wbkItems;
            set { _wbkItems = value; OnPropertyChanged(); }
        }

        public void LoadWbkFile()
        {
            try
            {
                OpenFileDialog openFileDialog = new OpenFileDialog
                {
                    Filter = "WBK Files (*.wbk)|*.wbk|All Files (*.*)|*.*",
                    Title = "Select WBK File"
                };

                if (openFileDialog.ShowDialog() != true)
                    return;

                _wbkFilePath = openFileDialog.FileName;
                string wbkName = Path.GetFileNameWithoutExtension(_wbkFilePath);
                WbkFileName = Path.GetFileName(_wbkFilePath);

                // Localizar JSON correspondente
                string jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AllJsons", $"{wbkName}.json");

                if (!File.Exists(jsonPath))
                {
                    MessageBox.Show($"JSON file not found: {jsonPath}\n\nPlease ensure the JSON file exists in the AllJsons folder.",
                        "JSON Not Found", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Ler e parsear JSON
                string jsonContent = File.ReadAllText(jsonPath);
                var jsonData = JsonSerializer.Deserialize<WbkJsonData>(jsonContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (jsonData?.Offsets == null || jsonData.Offsets.Count == 0)
                {
                    MessageBox.Show("JSON file is empty or invalid.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Carregar items
                WbkItems.Clear();
                foreach (var kvp in jsonData.Offsets)
                {
                    string key = kvp.Key;
                    var offsetData = kvp.Value;

                    // Formato: "ACTIVISION_EN#1.raw (4f85b619)"
                    string displayName = $"{key} ({offsetData.Hash})";

                    WbkItems.Add(new WbkItem
                    {
                        FileName = displayName,
                        RawFileName = key,
                        StartOffset = offsetData.StartOffset,
                        EndOffset = offsetData.EndOffset,
                        Status = "Original",
                        Hash = offsetData.Hash,
                        OriginalName = offsetData.OriginalName
                    });
                }

                IsWbkLoaded = true;
                StatusMessage = $"Loaded {WbkItems.Count} file(s) from {WbkFileName}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading WBK file: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void ReplaceMultipleWavFiles()
        {
            try
            {
                if (!IsWbkLoaded)
                {
                    MessageBox.Show("Please load a WBK file first.", "No WBK Loaded", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Selecionar múltiplos WAV
                OpenFileDialog openFileDialog = new OpenFileDialog
                {
                    Filter = "WAV Files (*.wav)|*.wav|All Files (*.*)|*.*",
                    Title = "Select WAV Files to Convert and Replace",
                    Multiselect = true
                };

                if (openFileDialog.ShowDialog() != true)
                    return;

                string[] selectedFiles = openFileDialog.FileNames;
                if (selectedFiles.Length == 0)
                    return;

                // Criar dicionário de match: nome base -> arquivo WAV
                Dictionary<string, string> wavFilesDict = new Dictionary<string, string>();
                foreach (string filePath in selectedFiles)
                {
                    string fileName = Path.GetFileNameWithoutExtension(filePath);
                    wavFilesDict[fileName] = filePath;
                }

                // Encontrar matches
                List<(WbkItem Item, string WavPath)> matches = new List<(WbkItem, string)>();
                
                foreach (var item in WbkItems)
                {
                    // Extrair nome base (remover .raw e hash)
                    string itemBaseName = item.RawFileName.Replace(".raw", "");
                    
                    if (wavFilesDict.ContainsKey(itemBaseName))
                    {
                        matches.Add((item, wavFilesDict[itemBaseName]));
                    }
                }

                if (matches.Count == 0)
                {
                    MessageBox.Show("No matching files found!\n\nMake sure WAV file names match the WBK entries.\n\n" +
                        "Example: For 'STREAMS_VOICE_EN#1.raw', use 'STREAMS_VOICE_EN#1.wav'",
                        "No Matches", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Processar cada match sem confirmação
                int successCount = 0;
                int failCount = 0;
                StringBuilder report = new StringBuilder();
                report.AppendLine("Batch Conversion Report:");
                report.AppendLine(new string('-', 50));

                foreach (var match in matches)
                {
                    try
                    {
                        StatusMessage = $"Processing {Path.GetFileName(match.WavPath)}... ({successCount + failCount + 1}/{matches.Count})";
                        
                        bool success = ConvertAndReplaceWavInternal(match.Item, match.WavPath);
                        
                        if (success)
                        {
                            successCount++;
                            report.AppendLine($"✓ SUCCESS: {match.Item.RawFileName}");
                        }
                        else
                        {
                            failCount++;
                            report.AppendLine($"✗ FAILED: {match.Item.RawFileName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        failCount++;
                        report.AppendLine($"✗ ERROR: {match.Item.RawFileName} - {ex.Message}");
                    }
                }

                report.AppendLine(new string('-', 50));
                report.AppendLine($"Total: {matches.Count} | Success: {successCount} | Failed: {failCount}");

                StatusMessage = $"Batch complete: {successCount} succeeded, {failCount} failed";

                MessageBox.Show(report.ToString(), "Batch Operation Complete", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error in batch operation: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool ConvertAndReplaceWavInternal(WbkItem item, string inputWav)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "WBKConverter_" + Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                // Extrair sample rate do WBK
                int sampleRate = ExtractSampleRateFromWbk(item);
                if (sampleRate == 0)
                {
                    sampleRate = 48000;
                }

                // Modificar byte de controle no WBK (Hash + 6 bytes -> 0x00)
                ModifyControlByte(item);

                // Paths temporários
                string outputWav = Path.Combine(tempDir, "output.wav");
                string outputAdpcm = Path.Combine(tempDir, "output2.wav");

                // 1. Converter com FFmpeg
                string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
                if (!File.Exists(ffmpegPath))
                    return false;

                string ffmpegArgs = $"-i \"{inputWav}\" -ar {sampleRate} -ac 1 -c:a pcm_s16le \"{outputWav}\" -y";

                if (!RunProcessSilent(ffmpegPath, ffmpegArgs))
                    return false;

                if (!File.Exists(outputWav))
                    return false;

                // 2. Converter com XboxADPCM
                string xboxAdpcmPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "XboxADPCM.exe");
                if (!File.Exists(xboxAdpcmPath))
                    return false;

                string adpcmArgs = $"\"{outputWav}\" \"{outputAdpcm}\"";

                if (!RunProcessSilent(xboxAdpcmPath, adpcmArgs))
                    return false;

                if (!File.Exists(outputAdpcm))
                    return false;

                // 3. Extrair dados após "data"
                byte[] adpcmData = ExtractDataAfterDataChunk(outputAdpcm);

                if (adpcmData == null || adpcmData.Length == 0)
                    return false;

                // 4. Verificar tamanho e ajustar automaticamente
                int originalSize = item.EndOffset - item.StartOffset;
                int newSize = adpcmData.Length;

                if (newSize > originalSize)
                {
                    Array.Resize(ref adpcmData, originalSize);
                }
                else if (newSize < originalSize)
                {
                    Array.Resize(ref adpcmData, originalSize);
                }

                // 5. Substituir dados no WBK
                using (FileStream fs = new FileStream(_wbkFilePath, FileMode.Open, FileAccess.ReadWrite))
                {
                    fs.Seek(item.StartOffset, SeekOrigin.Begin);
                    fs.Write(adpcmData, 0, adpcmData.Length);
                }

                item.Status = "Replaced";
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                // Limpar arquivos temporários
                try
                {
                    if (Directory.Exists(tempDir))
                        Directory.Delete(tempDir, true);
                }
                catch { }
            }
        }

        public void ConvertAndReplaceWav(WbkItem item)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "WBKConverter_" + Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                // Selecionar WAV de entrada
                OpenFileDialog openFileDialog = new OpenFileDialog
                {
                    Filter = "WAV Files (*.wav)|*.wav|All Files (*.*)|*.*",
                    Title = "Select WAV File to Convert and Replace"
                };

                if (openFileDialog.ShowDialog() != true)
                    return;

                string inputWav = openFileDialog.FileName;
                StatusMessage = $"Converting {Path.GetFileName(inputWav)}...";

                // Extrair sample rate do WBK
                int sampleRate = ExtractSampleRateFromWbk(item);
                if (sampleRate == 0)
                {
                    sampleRate = 48000;
                }

                StatusMessage = $"Detected sample rate: {sampleRate} Hz";

                // Modificar byte de controle no WBK (Hash + 6 bytes -> 0x00)
                ModifyControlByte(item);

                // Paths temporários
                string outputWav = Path.Combine(tempDir, "output.wav");
                string outputAdpcm = Path.Combine(tempDir, "output2.wav");

                // 1. Converter com FFmpeg
                string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
                if (!File.Exists(ffmpegPath))
                {
                    MessageBox.Show($"ffmpeg.exe not found at: {ffmpegPath}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                StatusMessage = "Running FFmpeg conversion...";
                string ffmpegArgs = $"-i \"{inputWav}\" -ar {sampleRate} -ac 1 -c:a pcm_s16le \"{outputWav}\" -y";

                if (!RunProcess(ffmpegPath, ffmpegArgs))
                {
                    MessageBox.Show("FFmpeg conversion failed!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (!File.Exists(outputWav))
                {
                    MessageBox.Show("FFmpeg output file not created!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 2. Converter com XboxADPCM
                string xboxAdpcmPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "XboxADPCM.exe");
                if (!File.Exists(xboxAdpcmPath))
                {
                    MessageBox.Show($"XboxADPCM.exe not found at: {xboxAdpcmPath}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                StatusMessage = "Running XboxADPCM conversion...";
                string adpcmArgs = $"\"{outputWav}\" \"{outputAdpcm}\"";

                if (!RunProcess(xboxAdpcmPath, adpcmArgs))
                {
                    MessageBox.Show("XboxADPCM conversion failed!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (!File.Exists(outputAdpcm))
                {
                    MessageBox.Show("XboxADPCM output file not created!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 3. Extrair dados após "data"
                StatusMessage = "Extracting audio data...";
                byte[] adpcmData = ExtractDataAfterDataChunk(outputAdpcm);

                if (adpcmData == null || adpcmData.Length == 0)
                {
                    MessageBox.Show("Failed to extract audio data from converted file!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 4. Verificar tamanho e ajustar automaticamente (sem confirmação)
                int originalSize = item.EndOffset - item.StartOffset;
                int newSize = adpcmData.Length;

                if (newSize > originalSize)
                {
                    // Truncar automaticamente
                    Array.Resize(ref adpcmData, originalSize);
                }
                else if (newSize < originalSize)
                {
                    // Fazer padding automaticamente
                    StatusMessage = "Padding file to match original size...";
                    Array.Resize(ref adpcmData, originalSize);
                }

                // 5. Substituir dados no WBK
                StatusMessage = "Writing to WBK file...";
                using (FileStream fs = new FileStream(_wbkFilePath, FileMode.Open, FileAccess.ReadWrite))
                {
                    fs.Seek(item.StartOffset, SeekOrigin.Begin);
                    fs.Write(adpcmData, 0, adpcmData.Length);
                }

                item.Status = "Replaced";
                StatusMessage = $"Successfully converted and replaced {item.FileName}";

                MessageBox.Show($"File converted and replaced successfully!\n\n" +
                    $"File: {item.FileName}\n" +
                    $"Sample Rate: {sampleRate} Hz\n" +
                    $"Size: {FormatBytes(adpcmData.Length)}",
                    "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error converting and replacing file: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // Limpar arquivos temporários
                try
                {
                    if (Directory.Exists(tempDir))
                        Directory.Delete(tempDir, true);
                }
                catch { }
            }
        }

        private int ExtractSampleRateFromWbk(WbkItem item)
        {
            try
            {
                // Inverter endian do hash
                string hash = item.Hash.Replace(" ", "");
                if (hash.Length != 8)
                    return 0;

                byte[] hashBytes = new byte[4];
                for (int i = 0; i < 4; i++)
                {
                    hashBytes[i] = Convert.ToByte(hash.Substring(i * 2, 2), 16);
                }

                // Inverter endian: 4f85b619 -> 19 B6 85 4F
                Array.Reverse(hashBytes);

                // Procurar assinatura no WBK
                byte[] wbkData = File.ReadAllBytes(_wbkFilePath);
                byte[] signature = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF };

                for (int i = 0; i < wbkData.Length - 20; i++)
                {
                    // Verificar se encontrou o hash
                    bool hashMatch = true;
                    for (int j = 0; j < 4; j++)
                    {
                        if (wbkData[i + j] != hashBytes[j])
                        {
                            hashMatch = false;
                            break;
                        }
                    }

                    if (hashMatch)
                    {
                        // Verificar assinatura FF FF FF FF próxima
                        bool signatureFound = false;
                        int signatureOffset = 0;

                        for (int k = i + 4; k < Math.Min(i + 30, wbkData.Length - 4); k++)
                        {
                            if (wbkData[k] == 0xFF && wbkData[k + 1] == 0xFF &&
                                wbkData[k + 2] == 0xFF && wbkData[k + 3] == 0xFF)
                            {
                                signatureFound = true;
                                signatureOffset = k;
                                break;
                            }
                        }

                        if (signatureFound)
                        {
                            // Sample rate está 8 bytes após FF FF FF FF
                            int sampleRateOffset = signatureOffset + 8;

                            if (sampleRateOffset + 4 <= wbkData.Length)
                            {
                                // Ler sample rate (little endian)
                                int sampleRate = BitConverter.ToInt32(wbkData, sampleRateOffset);
                                return sampleRate;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error extracting sample rate: {ex.Message}", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            return 0;
        }

        private void ModifyControlByte(WbkItem item)
        {
            try
            {
                // Inverter endian do hash
                string hash = item.Hash.Replace(" ", "");
                if (hash.Length != 8)
                    return;

                byte[] hashBytes = new byte[4];
                for (int i = 0; i < 4; i++)
                {
                    hashBytes[i] = Convert.ToByte(hash.Substring(i * 2, 2), 16);
                }

                Array.Reverse(hashBytes);

                // Procurar hash no WBK
                byte[] wbkData = File.ReadAllBytes(_wbkFilePath);

                for (int i = 0; i < wbkData.Length - 10; i++)
                {
                    bool hashMatch = true;
                    for (int j = 0; j < 4; j++)
                    {
                        if (wbkData[i + j] != hashBytes[j])
                        {
                            hashMatch = false;
                            break;
                        }
                    }

                    if (hashMatch)
                    {
                        // Hash + 6 bytes -> mudar de 0x03 para 0x00
                        int controlByteOffset = i + 6;

                        if (controlByteOffset < wbkData.Length)
                        {
                            using (FileStream fs = new FileStream(_wbkFilePath, FileMode.Open, FileAccess.ReadWrite))
                            {
                                fs.Seek(controlByteOffset, SeekOrigin.Begin);
                                fs.WriteByte(0x00);
                            }
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error modifying control byte: {ex.Message}", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private byte[] ExtractDataAfterDataChunk(string wavFile)
        {
            try
            {
                byte[] fileData = File.ReadAllBytes(wavFile);
                byte[] dataMarker = Encoding.ASCII.GetBytes("data");

                // Procurar "data" chunk
                for (int i = 0; i < fileData.Length - 4; i++)
                {
                    if (fileData[i] == dataMarker[0] &&
                        fileData[i + 1] == dataMarker[1] &&
                        fileData[i + 2] == dataMarker[2] &&
                        fileData[i + 3] == dataMarker[3])
                    {
                        // Pular "data" (4 bytes) + tamanho do chunk (4 bytes)
                        int dataStart = i + 8;

                        if (dataStart < fileData.Length)
                        {
                            byte[] audioData = new byte[fileData.Length - dataStart];
                            Array.Copy(fileData, dataStart, audioData, 0, audioData.Length);
                            return audioData;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error extracting data: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            return null;
        }

        private bool RunProcess(string fileName, string arguments)
        {
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (Process process = Process.Start(startInfo))
                {
                    process.WaitForExit();

                    if (process.ExitCode != 0)
                    {
                        string error = process.StandardError.ReadToEnd();
                        MessageBox.Show($"Process failed with exit code {process.ExitCode}\n\nError: {error}",
                            "Process Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return false;
                    }

                    return true;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error running process: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private bool RunProcessSilent(string fileName, string arguments)
        {
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (Process process = Process.Start(startInfo))
                {
                    process.WaitForExit();
                    return process.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        public void ReplaceRawAudio(WbkItem item)
        {
            try
            {
                OpenFileDialog openFileDialog = new OpenFileDialog
                {
                    Filter = "RAW Audio Files (*.raw)|*.raw|All Files (*.*)|*.*",
                    Title = "Select RAW Audio File to Replace"
                };

                if (openFileDialog.ShowDialog() != true)
                    return;

                string rawFilePath = openFileDialog.FileName;
                byte[] rawData = File.ReadAllBytes(rawFilePath);

                int originalSize = item.EndOffset - item.StartOffset;
                int newSize = rawData.Length;

                // Ajustar tamanho automaticamente sem confirmação
                if (newSize > originalSize)
                {
                    Array.Resize(ref rawData, originalSize);
                }
                else if (newSize < originalSize)
                {
                    Array.Resize(ref rawData, originalSize);
                }

                // Substituir dados no WBK
                using (FileStream fs = new FileStream(_wbkFilePath, FileMode.Open, FileAccess.ReadWrite))
                {
                    fs.Seek(item.StartOffset, SeekOrigin.Begin);
                    fs.Write(rawData, 0, rawData.Length);
                }

                item.Status = "Replaced";
                StatusMessage = $"Successfully replaced {item.FileName}";

                MessageBox.Show($"File replaced successfully!\n\nFile: {item.FileName}\nSize: {FormatBytes(rawData.Length)}",
                    "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error replacing file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class WbkItem : INotifyPropertyChanged
    {
        private string _fileName;
        private string _rawFileName;
        private int _startOffset;
        private int _endOffset;
        private string _status;
        private string _hash;
        private string _originalName;

        public string FileName
        {
            get => _fileName;
            set { _fileName = value; OnPropertyChanged(); }
        }

        public string RawFileName
        {
            get => _rawFileName;
            set { _rawFileName = value; OnPropertyChanged(); }
        }

        public int StartOffset
        {
            get => _startOffset;
            set
            {
                _startOffset = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(OffsetDisplay));
                OnPropertyChanged(nameof(SizeDisplay));
            }
        }

        public int EndOffset
        {
            get => _endOffset;
            set
            {
                _endOffset = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(OffsetDisplay));
                OnPropertyChanged(nameof(SizeDisplay));
            }
        }

        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        public string Hash
        {
            get => _hash;
            set { _hash = value; OnPropertyChanged(); }
        }

        public string OriginalName
        {
            get => _originalName;
            set { _originalName = value; OnPropertyChanged(); }
        }

        public string OffsetDisplay => $"0x{StartOffset:X8} - 0x{EndOffset:X8}";

        public string SizeDisplay
        {
            get
            {
                long size = EndOffset - StartOffset;
                return FormatBytes(size);
            }
        }

        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class WbkJsonData
    {
        public string File { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("total_size")]
        public int TotalSize { get; set; }

        public System.Collections.Generic.Dictionary<string, OffsetData> Offsets { get; set; }
    }

    public class OffsetData
    {
        [System.Text.Json.Serialization.JsonPropertyName("start_offset")]
        public int StartOffset { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("end_offset")]
        public int EndOffset { get; set; }

        public int Size { get; set; }

        public string Hash { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("original_name")]
        public string OriginalName { get; set; }
    }
}