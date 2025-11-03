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
using System.Threading.Tasks;

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
        private CoefManager _coefManager;

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

                // Inicializar gerenciador de coefs
                _coefManager = new CoefManager(_wbkFilePath);

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

        public async void ReplaceMultipleWavFiles()
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

                // Processar cada match de forma assíncrona (1 por 1)
                int successCount = 0;
                int failCount = 0;
                StringBuilder report = new StringBuilder();

                foreach (var match in matches)
                {
                    try
                    {
                        StatusMessage = $"Processing {Path.GetFileName(match.WavPath)}... ({successCount + failCount + 1}/{matches.Count})";

                        bool success = await ConvertAndReplaceWavInternalAsync(match.Item, match.WavPath);

                        if (success)
                        {
                            successCount++;
                        }
                        else
                        {
                            failCount++;
                        }

                        // IMPORTANTE: Adicionar delay entre processamentos para liberar recursos
                        await Task.Delay(500);

                        // Forçar garbage collection entre conversões
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                    }
                    catch (Exception ex)
                    {
                        failCount++;
                        report.AppendLine($"✗ ERROR: {match.Item.RawFileName} - {ex.Message}");
                        
                        // Delay extra em caso de erro
                        await Task.Delay(1000);
                    }
                }

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
                string outputDsp = Path.Combine(tempDir, "output.dsp");

                // 1. Converter com FFmpeg
                string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
                if (!File.Exists(ffmpegPath))
                    return false;

                string ffmpegArgs = $"-i \"{inputWav}\" -ar {sampleRate} -ac 1 -c:a pcm_s16le \"{outputWav}\" -y";

                if (!RunProcessSilent(ffmpegPath, ffmpegArgs))
                    return false;

                if (!File.Exists(outputWav))
                    return false;

                // 2. Converter com DSPADPCM
                string dspadpcmPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dspadpcm.exe");
                if (!File.Exists(dspadpcmPath))
                    return false;

                string dspArgs = $"\"{outputWav}\" \"{outputDsp}\"";

                if (!RunProcessSilent(dspadpcmPath, dspArgs))
                    return false;

                if (!File.Exists(outputDsp))
                    return false;

                // 3. Ler arquivo DSP completo primeiro (para operações de COEF)
                byte[] dspDataComplete = File.ReadAllBytes(outputDsp);
                
                if (dspDataComplete == null || dspDataComplete.Length < 96)
                    return false;

                // 4. Extrair coef do DSP (offset 0x1C até 0x3B = 32 bytes) - ANTES de remover header
                byte[] coefData = ExtractCoefFromDsp(outputDsp);
                if (coefData == null || coefData.Length != 32)
                    return false;

                // 5. Salvar coef no final do WBK e obter offset relativo
                var fileHashes = WbkItems.ToDictionary(x => x.RawFileName, x => x.Hash);
                int coefRelativeOffset = _coefManager.SaveCoef(item.RawFileName, coefData, fileHashes);

                // 6. AGORA remover os primeiros 96 bytes (header DSP)
                byte[] dspData = new byte[dspDataComplete.Length - 96];
                Array.Copy(dspDataComplete, 96, dspData, 0, dspData.Length);

                // 7. Verificar tamanho e ajustar automaticamente
                int originalSize = item.EndOffset - item.StartOffset;
                int newSize = dspData.Length;

                if (newSize > originalSize)
                {
                    Array.Resize(ref dspData, originalSize);
                }
                else if (newSize < originalSize)
                {
                    Array.Resize(ref dspData, originalSize);
                }

                // 8. Substituir dados no WBK (sem o header DSP)
                using (FileStream fs = new FileStream(_wbkFilePath, FileMode.Open, FileAccess.ReadWrite))
                {
                    fs.Seek(item.StartOffset, SeekOrigin.Begin);
                    fs.Write(dspData, 0, dspData.Length);
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

        private async Task<bool> ConvertAndReplaceWavInternalAsync(WbkItem item, string inputWav)
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
                string outputDsp = Path.Combine(tempDir, "output.dsp");

                // 1. Converter com FFmpeg (ASYNC)
                string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
                if (!File.Exists(ffmpegPath))
                    return false;

                string ffmpegArgs = $"-i \"{inputWav}\" -ar {sampleRate} -ac 1 -c:a pcm_s16le \"{outputWav}\" -y";

                if (!await RunProcessAsync(ffmpegPath, ffmpegArgs))
                    return false;

                if (!File.Exists(outputWav))
                    return false;

                // Pequeno delay entre processos
                await Task.Delay(100);

                // 2. Converter com DSPADPCM (ASYNC)
                string dspadpcmPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dspadpcm.exe");
                if (!File.Exists(dspadpcmPath))
                    return false;

                string dspArgs = $"\"{outputWav}\" \"{outputDsp}\"";

                if (!await RunProcessAsync(dspadpcmPath, dspArgs))
                    return false;

                if (!File.Exists(outputDsp))
                    return false;

                // 3. Ler arquivo DSP completo primeiro (para operações de COEF)
                byte[] dspDataComplete = File.ReadAllBytes(outputDsp);
                
                if (dspDataComplete == null || dspDataComplete.Length < 96)
                    return false;

                // 4. Extrair coef do DSP (offset 0x1C até 0x3B = 32 bytes) - ANTES de remover header
                byte[] coefData = ExtractCoefFromDsp(outputDsp);
                if (coefData == null || coefData.Length != 32)
                    return false;

                // 5. Salvar coef no final do WBK e obter offset relativo
                var fileHashes = WbkItems.ToDictionary(x => x.RawFileName, x => x.Hash);
                int coefRelativeOffset = await Task.Run(() => _coefManager.SaveCoef(item.RawFileName, coefData, fileHashes));

                // 6. AGORA remover os primeiros 96 bytes (header DSP)
                byte[] dspData = new byte[dspDataComplete.Length - 96];
                Array.Copy(dspDataComplete, 96, dspData, 0, dspData.Length);

                // 7. Verificar tamanho e ajustar automaticamente
                int originalSize = item.EndOffset - item.StartOffset;
                int newSize = dspData.Length;

                if (newSize > originalSize)
                {
                    Array.Resize(ref dspData, originalSize);
                }
                else if (newSize < originalSize)
                {
                    Array.Resize(ref dspData, originalSize);
                }

                // 8. Substituir dados no WBK (sem o header DSP)
                using (FileStream fs = new FileStream(_wbkFilePath, FileMode.Open, FileAccess.ReadWrite))
                {
                    fs.Seek(item.StartOffset, SeekOrigin.Begin);
                    fs.Write(dspData, 0, dspData.Length);
                }

                item.Status = "Replaced";
                return true;
            }
            catch (Exception ex)
            {
                // Log do erro para debug
                System.Diagnostics.Debug.WriteLine($"Error in ConvertAndReplaceWavInternalAsync: {ex.Message}");
                return false;
            }
            finally
            {
                // Limpar arquivos temporários
                try
                {
                    // Aguardar um pouco antes de deletar para garantir que arquivos foram liberados
                    await Task.Delay(100);
                    
                    if (Directory.Exists(tempDir))
                    {
                        // Tentar deletar múltiplas vezes se necessário
                        for (int i = 0; i < 3; i++)
                        {
                            try
                            {
                                Directory.Delete(tempDir, true);
                                break;
                            }
                            catch
                            {
                                if (i < 2)
                                    await Task.Delay(200);
                            }
                        }
                    }
                }
                catch { }
            }
        }

        public async void ConvertAndReplaceWav(WbkItem item)
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
                int sampleRate = await Task.Run(() => ExtractSampleRateFromWbk(item));
                if (sampleRate == 0)
                {
                    sampleRate = 48000;
                }

                StatusMessage = $"Detected sample rate: {sampleRate} Hz";

                // Modificar byte de controle no WBK (Hash + 6 bytes -> 0x00)
                await Task.Run(() => ModifyControlByte(item));

                // Paths temporários
                string outputWav = Path.Combine(tempDir, "output.wav");
                string outputDsp = Path.Combine(tempDir, "output.dsp");

                // 1. Converter com FFmpeg
                string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
                if (!File.Exists(ffmpegPath))
                {
                    MessageBox.Show($"ffmpeg.exe not found at: {ffmpegPath}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                StatusMessage = "Running FFmpeg conversion...";
                string ffmpegArgs = $"-i \"{inputWav}\" -ar {sampleRate} -ac 1 -c:a pcm_s16le \"{outputWav}\" -y";

                if (!await RunProcessAsync(ffmpegPath, ffmpegArgs))
                {
                    MessageBox.Show("FFmpeg conversion failed!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (!File.Exists(outputWav))
                {
                    MessageBox.Show("FFmpeg output file not created!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 2. Converter com DSPADPCM
                string dspadpcmPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dspadpcm.exe");
                if (!File.Exists(dspadpcmPath))
                {
                    MessageBox.Show($"dspadpcm.exe not found at: {dspadpcmPath}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                StatusMessage = "Running DSPADPCM conversion...";
                string dspArgs = $"\"{outputWav}\" \"{outputDsp}\"";

                if (!await RunProcessAsync(dspadpcmPath, dspArgs))
                {
                    MessageBox.Show("DSPADPCM conversion failed!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (!File.Exists(outputDsp))
                {
                    MessageBox.Show("DSPADPCM output file not created!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 3. Ler arquivo DSP completo primeiro (para operações de COEF)
                StatusMessage = "Reading complete DSP file...";
                byte[] dspDataComplete = await Task.Run(() => File.ReadAllBytes(outputDsp));

                if (dspDataComplete == null || dspDataComplete.Length < 96)
                {
                    MessageBox.Show("DSP file is too small or invalid!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 4. Extrair coef do DSP (offset 0x1C até 0x3B = 32 bytes) - ANTES de remover header
                StatusMessage = "Extracting COEF data from DSP...";
                byte[] coefData = await Task.Run(() => ExtractCoefFromDsp(outputDsp));

                if (coefData == null || coefData.Length != 32)
                {
                    MessageBox.Show("Failed to extract COEF data from DSP file!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 5. Salvar coef no final do WBK e obter offset relativo
                StatusMessage = "Saving COEF data to WBK...";
                var fileHashes = WbkItems.ToDictionary(x => x.RawFileName, x => x.Hash);
                int coefRelativeOffset = await Task.Run(() => _coefManager.SaveCoef(item.RawFileName, coefData, fileHashes));

                // 6. AGORA remover os primeiros 96 bytes (header DSP)
                StatusMessage = "Removing DSP header (96 bytes)...";
                byte[] dspData = new byte[dspDataComplete.Length - 96];
                Array.Copy(dspDataComplete, 96, dspData, 0, dspData.Length);

                // 7. Verificar tamanho e ajustar automaticamente (sem confirmação)
                int originalSize = item.EndOffset - item.StartOffset;
                int newSize = dspData.Length;

                if (newSize > originalSize)
                {
                    // Truncar automaticamente
                    Array.Resize(ref dspData, originalSize);
                }
                else if (newSize < originalSize)
                {
                    // Fazer padding automaticamente
                    StatusMessage = "Padding file to match original size...";
                    Array.Resize(ref dspData, originalSize);
                }

                // 8. Substituir dados no WBK (sem o header DSP)
                StatusMessage = "Writing to WBK file...";
                await Task.Run(() =>
                {
                    using (FileStream fs = new FileStream(_wbkFilePath, FileMode.Open, FileAccess.ReadWrite))
                    {
                        fs.Seek(item.StartOffset, SeekOrigin.Begin);
                        fs.Write(dspData, 0, dspData.Length);
                    }
                });

                item.Status = "Replaced";
                StatusMessage = $"Successfully converted and replaced {item.FileName}";

                MessageBox.Show($"File converted and replaced successfully!\n\n" +
                    $"File: {item.FileName}\n" +
                    $"Sample Rate: {sampleRate} Hz\n" +
                    $"DSP Size (without header): {FormatBytes(dspData.Length)}\n" +
                    $"COEF Offset: 0x{coefRelativeOffset:X8}",
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

        private async Task<bool> RunProcessAsync(string fileName, string arguments)
        {
            Process process = null;
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

                process = Process.Start(startInfo);

                if (process == null)
                    return false;

                // Ler output e error de forma assíncrona para evitar deadlock
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();

                await process.WaitForExitAsync();

                string output = await outputTask;
                string error = await errorTask;

                if (process.ExitCode != 0)
                {
                    // Não mostrar MessageBox durante batch operation, apenas retornar false
                    System.Diagnostics.Debug.WriteLine($"Process failed with exit code {process.ExitCode}\n\nError: {error}");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error running process: {ex.Message}");
                return false;
            }
            finally
            {
                // Garantir que o processo seja devidamente fechado e liberado
                if (process != null)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill();
                        }
                        process.Dispose();
                    }
                    catch { }
                }
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

        private byte[] ExtractCoefFromDsp(string dspFile)
        {
            try
            {
                byte[] dspData = File.ReadAllBytes(dspFile);

                // COEF está no offset 0x1C até 0x3B (32 bytes)
                if (dspData.Length < 0x3C)
                    return null;

                byte[] coefData = new byte[32];
                Array.Copy(dspData, 0x1C, coefData, 0, 32);

                return coefData;
            }
            catch
            {
                return null;
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

                Array.Reverse(hashBytes);

                byte[] wbkData = File.ReadAllBytes(_wbkFilePath);

                for (int i = 0; i < wbkData.Length - 20; i++)
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
                            int sampleRateOffset = signatureOffset + 8;

                            if (sampleRateOffset + 4 <= wbkData.Length)
                            {
                                int sampleRate = BitConverter.ToInt32(wbkData, sampleRateOffset);
                                return sampleRate;
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            return 0;
        }

        private void ModifyControlByte(WbkItem item)
        {
            try
            {
                string hash = item.Hash.Replace(" ", "");
                if (hash.Length != 8)
                    return;

                byte[] hashBytes = new byte[4];
                for (int i = 0; i < 4; i++)
                {
                    hashBytes[i] = Convert.ToByte(hash.Substring(i * 2, 2), 16);
                }

                Array.Reverse(hashBytes);

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
            catch
            {
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



        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    // Classe para gerenciar os dados COEF
    public class CoefManager
    {
        private string _wbkFilePath;
        private string _coefJsonPath;
        private int _coefStartOffset;
        private Dictionary<string, int> _coefOffsets;

        public CoefManager(string wbkFilePath)
        {
            _wbkFilePath = wbkFilePath;
            string wbkName = Path.GetFileNameWithoutExtension(wbkFilePath);
            _coefJsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CoefData", $"{wbkName}_coef.json");

            // Criar pasta CoefData se não existir
            Directory.CreateDirectory(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CoefData"));

            LoadOrInitialize();
        }

        private void LoadOrInitialize()
        {
            if (File.Exists(_coefJsonPath))
            {
                // Carregar JSON existente
                string jsonContent = File.ReadAllText(_coefJsonPath);
                var coefData = JsonSerializer.Deserialize<CoefJsonData>(jsonContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                _coefStartOffset = coefData.CoefStartOffset;
                _coefOffsets = coefData.CoefOffsets ?? new Dictionary<string, int>();
            }
            else
            {
                // Inicializar novo
                FileInfo wbkInfo = new FileInfo(_wbkFilePath);
                _coefStartOffset = (int)wbkInfo.Length;
                _coefOffsets = new Dictionary<string, int>();
                SaveJson();
            }
        }

        public int SaveCoef(string fileName, byte[] coefData, Dictionary<string, string> fileHashes)
        {
            int relativeOffset;

            if (_coefOffsets.ContainsKey(fileName))
            {
                relativeOffset = _coefOffsets[fileName];
            }
            else
            {
                if (_coefOffsets.Count == 0)
                {
                    relativeOffset = 0;
                }
                else
                {
                    relativeOffset = _coefOffsets.Values.Max() + 32;
                }

                _coefOffsets[fileName] = relativeOffset;
            }

            int absoluteOffset = _coefStartOffset + relativeOffset;

            using (FileStream fs = new FileStream(_wbkFilePath, FileMode.Open, FileAccess.ReadWrite))
            {
                if (fs.Length < absoluteOffset + 32)
                {
                    fs.SetLength(absoluteOffset + 32);
                }

                fs.Seek(absoluteOffset, SeekOrigin.Begin);
                fs.Write(coefData, 0, 32);
            }

            SaveJson();

            // Atualizar ponteiros no WBK usando método direto (hash + 20 bytes)
            WriteCoefPointersDirectly(fileHashes);

            return relativeOffset;
        }

        public void WriteCoefPointersToWbk(Dictionary<string, string> fileHashes)
        {
            try
            {
                // 1. Escrever coef_start_offset no offset 0x5C (little endian)
                using (FileStream fs = new FileStream(_wbkFilePath, FileMode.Open, FileAccess.ReadWrite))
                {
                    fs.Seek(0x5C, SeekOrigin.Begin);
                    byte[] startOffsetBytes = BitConverter.GetBytes(_coefStartOffset);
                    fs.Write(startOffsetBytes, 0, 4);
                }

                // 2. Substituir FF FF FF FF pelos offsets relativos para cada arquivo
                byte[] wbkData = File.ReadAllBytes(_wbkFilePath);

                foreach (var kvp in _coefOffsets)
                {
                    string fileName = kvp.Key;
                    int relativeOffset = kvp.Value;

                    if (!fileHashes.ContainsKey(fileName))
                        continue;

                    string hash = fileHashes[fileName].Replace(" ", "");
                    if (hash.Length != 8)
                        continue;

                    byte[] hashBytes = new byte[4];
                    for (int i = 0; i < 4; i++)
                    {
                        hashBytes[i] = Convert.ToByte(hash.Substring(i * 2, 2), 16);
                    }
                    Array.Reverse(hashBytes);

                    for (int i = 0; i < wbkData.Length - 24; i++)
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
                            for (int k = i + 4; k < Math.Min(i + 30, wbkData.Length - 4); k++)
                            {
                                if (wbkData[k] == 0xFF && wbkData[k + 1] == 0xFF &&
                                    wbkData[k + 2] == 0xFF && wbkData[k + 3] == 0xFF)
                                {
                                    using (FileStream fs = new FileStream(_wbkFilePath, FileMode.Open, FileAccess.ReadWrite))
                                    {
                                        fs.Seek(k, SeekOrigin.Begin);
                                        byte[] offsetBytes = BitConverter.GetBytes(relativeOffset);
                                        fs.Write(offsetBytes, 0, 4);
                                    }
                                    break;
                                }
                            }
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error writing COEF pointers: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void WriteCoefPointersDirectly(Dictionary<string, string> fileHashes)
        {
            try
            {
                // 1. Escrever coef_start_offset no offset 0x5C
                using (FileStream fs = new FileStream(_wbkFilePath, FileMode.Open, FileAccess.ReadWrite))
                {
                    fs.Seek(0x5C, SeekOrigin.Begin);
                    byte[] startOffsetBytes = BitConverter.GetBytes(_coefStartOffset);
                    fs.Write(startOffsetBytes, 0, 4);
                }

                // 2. Ler WBK uma vez
                byte[] wbkData = File.ReadAllBytes(_wbkFilePath);

                // 3. Ordenar pelo número após o # no nome do arquivo
                var sortedOffsets = _coefOffsets
                    .OrderBy(kvp =>
                    {
                        // Extrair número após #
                        string name = kvp.Key;
                        int hashIndex = name.IndexOf('#');
                        if (hashIndex >= 0 && hashIndex < name.Length - 1)
                        {
                            string afterHash = name.Substring(hashIndex + 1);
                            string numberPart = new string(afterHash.TakeWhile(char.IsDigit).ToArray());
                            if (int.TryParse(numberPart, out int num))
                                return num;
                        }
                        return int.MaxValue; // Arquivos sem # vão pro final
                    })
                    .ThenBy(kvp => kvp.Key) // Desempate por nome
                    .ToList();

                // 4. Criar lista de posições encontradas para evitar duplicatas
                List<int> processedPositions = new List<int>();

                // 5. Para cada entrada no coef_offsets (ORDENADO)
                foreach (var kvp in sortedOffsets)
                {
                    string fileName = kvp.Key;
                    int relativeOffset = kvp.Value;

                    if (!fileHashes.ContainsKey(fileName))
                        continue;

                    string hash = fileHashes[fileName].Replace(" ", "");
                    if (hash.Length != 8)
                        continue;

                    // Converter hash para little endian
                    byte[] hashBytes = new byte[4];
                    for (int i = 0; i < 4; i++)
                    {
                        hashBytes[i] = Convert.ToByte(hash.Substring(i * 2, 2), 16);
                    }
                    Array.Reverse(hashBytes);

                    // Procurar hash no WBK
                    for (int i = 0; i < wbkData.Length - 28; i++)
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
                            // Escrever offset 24 bytes após o hash
                            int targetOffset = i + 24;

                            // Verificar se já processamos essa posição
                            if (!processedPositions.Contains(targetOffset))
                            {
                                using (FileStream fs = new FileStream(_wbkFilePath, FileMode.Open, FileAccess.ReadWrite))
                                {
                                    fs.Seek(targetOffset, SeekOrigin.Begin);
                                    byte[] offsetBytes = BitConverter.GetBytes(relativeOffset);
                                    fs.Write(offsetBytes, 0, 4);
                                }
                                processedPositions.Add(targetOffset);
                            }
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error writing COEF pointers directly: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveJson()
        {
            var coefData = new CoefJsonData
            {
                CoefStartOffset = _coefStartOffset,
                CoefOffsets = _coefOffsets
            };

            string jsonContent = JsonSerializer.Serialize(coefData, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(_coefJsonPath, jsonContent);
        }
    }

    // Classe para representar o JSON de COEF
    public class CoefJsonData
    {
        [System.Text.Json.Serialization.JsonPropertyName("coef_start_offset")]
        public int CoefStartOffset { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("coef_offsets")]
        public Dictionary<string, int> CoefOffsets { get; set; }
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

    // Método de extensão para WaitForExitAsync (compatibilidade .NET Core 3.1)
    public static class ProcessExtensions
    {
        public static Task WaitForExitAsync(this Process process)
        {
            var tcs = new TaskCompletionSource<bool>();
            process.EnableRaisingEvents = true;
            process.Exited += (sender, args) => tcs.TrySetResult(true);
            if (process.HasExited)
            {
                tcs.TrySetResult(true);
            }
            return tcs.Task;
        }
    }
}