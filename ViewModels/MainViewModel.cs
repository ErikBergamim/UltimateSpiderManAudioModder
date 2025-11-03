using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using WpfApp1.Models;

namespace WpfApp1.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private string _wbkFilePath;
        private string _wbkFileName = "No file loaded";
        private bool _isWbkLoaded;
        private string _statusMessage = "Ready to load WBK file";
        private long _baseOffset = 0;
        private bool _useHexadecimalOffsets = false;
        private MediaPlayer _mediaPlayer;
        private DispatcherTimer _playerTimer;
        private string _currentPlayingFile = "";
        private bool _isPlaying = false;
        private double _currentPosition = 0;
        private double _totalDuration = 0;
        private double _volume = 0.5;
        private bool _isPlayerVisible = false;
        private int _currentSampleRate = 0;

        public ObservableCollection<WbkItem> WbkItems { get; set; }

        public string WbkFileName
        {
            get => _wbkFileName;
            set
            {
                _wbkFileName = value;
                OnPropertyChanged(nameof(WbkFileName));
            }
        }

        public bool IsWbkLoaded
        {
            get => _isWbkLoaded;
            set
            {
                _isWbkLoaded = value;
                OnPropertyChanged(nameof(IsWbkLoaded));
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                _statusMessage = value;
                OnPropertyChanged(nameof(StatusMessage));
            }
        }

        public string CurrentPlayingFile
        {
            get => _currentPlayingFile;
            set
            {
                _currentPlayingFile = value;
                OnPropertyChanged(nameof(CurrentPlayingFile));
            }
        }

        public bool IsPlaying
        {
            get => _isPlaying;
            set
            {
                _isPlaying = value;
                OnPropertyChanged(nameof(IsPlaying));
            }
        }

        public double CurrentPosition
        {
            get => _currentPosition;
            set
            {
                _currentPosition = value;
                OnPropertyChanged(nameof(CurrentPosition));
            }
        }

        public double TotalDuration
        {
            get => _totalDuration;
            set
            {
                _totalDuration = value;
                OnPropertyChanged(nameof(TotalDuration));
            }
        }

        public string CurrentPositionText => TimeSpan.FromSeconds(_currentPosition).ToString(@"mm\:ss");
        public string TotalDurationText => TimeSpan.FromSeconds(_totalDuration).ToString(@"mm\:ss");

        public double Volume
        {
            get => _volume;
            set
            {
                _volume = value;
                if (_mediaPlayer != null)
                {
                    _mediaPlayer.Volume = value;
                }
                OnPropertyChanged(nameof(Volume));
            }
        }

        public bool IsPlayerVisible
        {
            get => _isPlayerVisible;
            set
            {
                _isPlayerVisible = value;
                OnPropertyChanged(nameof(IsPlayerVisible));
            }
        }

        public int CurrentSampleRate
        {
            get => _currentSampleRate;
            set
            {
                _currentSampleRate = value;
                OnPropertyChanged(nameof(CurrentSampleRate));
            }
        }

        public MainViewModel()
        {
            WbkItems = new ObservableCollection<WbkItem>();
            _mediaPlayer = new MediaPlayer();
            
            // Configura o timer para atualizar a posição do player
            _playerTimer = new DispatcherTimer();
            _playerTimer.Interval = TimeSpan.FromMilliseconds(100);
            _playerTimer.Tick += PlayerTimer_Tick;

            // Configura eventos do MediaPlayer
            _mediaPlayer.MediaOpened += MediaPlayer_MediaOpened;
            _mediaPlayer.MediaEnded += MediaPlayer_MediaEnded;
            _mediaPlayer.MediaFailed += MediaPlayer_MediaFailed;
            _mediaPlayer.Volume = _volume;
        }

        private void PlayerTimer_Tick(object sender, EventArgs e)
        {
            if (_mediaPlayer.NaturalDuration.HasTimeSpan)
            {
                CurrentPosition = _mediaPlayer.Position.TotalSeconds;
                OnPropertyChanged(nameof(CurrentPositionText));
            }
        }

        private void MediaPlayer_MediaOpened(object sender, EventArgs e)
        {
            if (_mediaPlayer.NaturalDuration.HasTimeSpan)
            {
                TotalDuration = _mediaPlayer.NaturalDuration.TimeSpan.TotalSeconds;
                OnPropertyChanged(nameof(TotalDurationText));
            }
        }

        private void MediaPlayer_MediaEnded(object sender, EventArgs e)
        {
            IsPlaying = false;
            _playerTimer.Stop();
            CurrentPosition = 0;
            StatusMessage = "Playback finished";
        }

        private void MediaPlayer_MediaFailed(object sender, ExceptionEventArgs e)
        {
            IsPlaying = false;
            _playerTimer.Stop();
            MessageBox.Show($"Media playback failed: {e.ErrorException.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        public void PlayPauseAudio()
        {
            if (_mediaPlayer.Source == null)
                return;

            if (IsPlaying)
            {
                _mediaPlayer.Pause();
                _playerTimer.Stop();
                IsPlaying = false;
                StatusMessage = "Paused";
            }
            else
            {
                _mediaPlayer.Play();
                _playerTimer.Start();
                IsPlaying = true;
                StatusMessage = $"Playing: {CurrentPlayingFile}";
            }
        }

        public void StopAudio()
        {
            _mediaPlayer.Stop();
            _mediaPlayer.Close(); // Adicione esta linha
            _playerTimer.Stop();
            IsPlaying = false;
            IsPlayerVisible = false;
            CurrentPosition = 0;
            StatusMessage = "Stopped";
        }

        public void SeekPosition(double position)
        {
            if (_mediaPlayer.NaturalDuration.HasTimeSpan)
            {
                _mediaPlayer.Position = TimeSpan.FromSeconds(position);
                CurrentPosition = position;
            }
        }

        private long ReadBaseOffsetFromWbk(string wbkFilePath)
        {
            try
            {
                using (FileStream fs = new FileStream(wbkFilePath, FileMode.Open, FileAccess.Read))
                {
                    fs.Seek(0x10, SeekOrigin.Begin);
                    byte[] buffer = new byte[4];
                    fs.Read(buffer, 0, 4);
                    long baseOffset = BitConverter.ToInt32(buffer, 0);

                    // Verifica se o valor é diferente de 4096
                    if (baseOffset != 4096)
                    {
                        _useHexadecimalOffsets = true;
                        return 0; // Não usa base offset quando interpreta como hexadecimal
                    }

                    _useHexadecimalOffsets = false;
                    return baseOffset;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error reading base offset from WBK: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return 0;
            }
        }

        private string FindJsonInAllJsonsFolder(string wbkFileName)
        {
            try
            {
                // Diretório raiz do executável
                string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string allJsonsFolder = Path.Combine(appDirectory, "AllJsons");

                // Verifica se a pasta AllJsons existe
                if (!Directory.Exists(allJsonsFolder))
                {
                    MessageBox.Show($"AllJsons folder not found at: {allJsonsFolder}\n\nPlease create the folder and add JSON files.",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return null;
                }

                // Nome do arquivo JSON esperado (mesmo nome do WBK, mas com extensão .json)
                string jsonFileName = Path.ChangeExtension(wbkFileName, ".json");
                string jsonPath = Path.Combine(allJsonsFolder, jsonFileName);

                // Verifica se o arquivo existe
                if (!File.Exists(jsonPath))
                {
                    MessageBox.Show($"JSON file not found: {jsonPath}\n\nExpected file name: {jsonFileName}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return null;
                }

                return jsonPath;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error searching for JSON file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
        }

        public async Task LoadWbkFile(string filePath)
        {
            try
            {
                _wbkFilePath = filePath;
                WbkFileName = Path.GetFileName(filePath);

                // Lê o offset base do WBK e determina o modo de interpretação
                _baseOffset = ReadBaseOffsetFromWbk(filePath);

                // Busca o JSON na pasta AllJsons
                string jsonPath = FindJsonInAllJsonsFolder(WbkFileName);

                if (jsonPath == null)
                {
                    return; // Mensagem de erro já foi exibida em FindJsonInAllJsonsFolder
                }

                string jsonContent = await File.ReadAllTextAsync(jsonPath);

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                var metadata = JsonSerializer.Deserialize<WbkMetadata>(jsonContent, options);

                if (metadata == null)
                {
                    MessageBox.Show("Failed to parse JSON file. The file may be corrupted or empty.",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (metadata.Offsets == null || metadata.Offsets.Count == 0)
                {
                    MessageBox.Show("No offsets found in JSON file.\n\nExpected format with 'offsets' dictionary.",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                WbkItems.Clear();

                foreach (var kvp in metadata.Offsets.OrderBy(x => x.Value.StartOffset))
                {
                    string fileName = kvp.Key;
                    var mapping = kvp.Value;

                    string displayFileName = !string.IsNullOrEmpty(mapping.Hash)
                        ? $"{fileName} ({mapping.Hash})"
                        : fileName;

                    long actualStartOffset;
                    long actualEndOffset;

                    if (_useHexadecimalOffsets)
                    {
                        // Interpreta os valores do JSON como hexadecimais
                        actualStartOffset = mapping.StartOffset;
                        actualEndOffset = mapping.EndOffset;
                    }
                    else
                    {
                        // Calcula offsets reais somando o base offset (modo padrão)
                        actualStartOffset = _baseOffset + mapping.StartOffset;
                        actualEndOffset = _baseOffset + mapping.EndOffset;
                    }

                    WbkItems.Add(new WbkItem
                    {
                        FileName = displayFileName,
                        Hash = mapping.Hash ?? string.Empty,
                        StartOffset = actualStartOffset,
                        EndOffset = actualEndOffset,
                        Status = "Original"
                    });
                }

                IsWbkLoaded = true;
                string offsetMode = _useHexadecimalOffsets ? "Hexadecimal Mode" : $"Base Offset: 0x{_baseOffset:X}";
                StatusMessage = $"Loaded {WbkItems.Count} file(s) from {WbkFileName} | {offsetMode} | JSON: {Path.GetFileName(jsonPath)}";
            }
            catch (JsonException jsonEx)
            {
                MessageBox.Show($"JSON parsing error: {jsonEx.Message}\n\nPlease check your JSON file format.",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading WBK: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public async Task PlaySound(WbkItem item)
        {
            string tempRawFile = Path.Combine(Path.GetTempPath(), "ArquivoSom.temp");
            string tempWavFile = Path.Combine(Path.GetTempPath(), "ArquivoTemporario.wav");
            string convertedWavFile = Path.Combine(Path.GetTempPath(), "ArquivoTemporario.wav.wav");
            string vgmstreamPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vgmstream-cli.exe");

            try
            {
                // Para o player antes de começar uma nova reprodução
                StopAudio();
                
                // Limpa arquivos temporários anteriores
                CleanupTempFiles(tempRawFile, tempWavFile, convertedWavFile);

                StatusMessage = $"Preparing to play: {item.FileName}...";
                CurrentPlayingFile = item.FileName;

                // 1. Extrai os dados do WBK usando os offsets corretos
                byte[] audioData;
                using (FileStream fs = new FileStream(_wbkFilePath, FileMode.Open, FileAccess.Read))
                {
                    // Usa a propriedade Size que já calcula corretamente (EndOffset - StartOffset + 1)
                    long dataSize = item.Size;
                    audioData = new byte[dataSize];

                    // Posiciona no offset inicial
                    fs.Seek(item.StartOffset, SeekOrigin.Begin);

                    // Lê a quantidade exata de bytes
                    int bytesRead = fs.Read(audioData, 0, (int)dataSize);

                    if (bytesRead != dataSize)
                    {
                        MessageBox.Show($"Warning: Expected to read {dataSize} bytes but read {bytesRead} bytes.",
                            "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }

                    StatusMessage = $"Extracted {bytesRead} bytes from offset 0x{item.StartOffset:X} to 0x{item.EndOffset:X}";
                }

                // 2. Salva os dados brutos temporariamente
                await File.WriteAllBytesAsync(tempRawFile, audioData);

                // 3. Extrai sample rate e block size do hash
                var (sampleRate, idOffset) = FindHashAndExtractSampleRate(item.Hash);
                CurrentSampleRate = sampleRate;
                IsPlayerVisible = true;

                if (sampleRate == 0)
                {
                    sampleRate = 48000; // Default
                }

                // Block size padrão (pode ser ajustado se necessário)
                short blockSize = 0x04C3; // 1219 em decimal

                // 4. Cria o header WAV
                byte[] header = new byte[]
                {
                    0x52, 0x49, 0x46, 0x46, 0x66, 0x20, 0x16, 0x00, // RIFF + tamanho (será modificado)
                    0x57, 0x41, 0x56, 0x45, 0x66, 0x6D, 0x74, 0x20, // WAVE fmt 
                    0x14, 0x00, 0x00, 0x00, 0x11, 0x00, 0x01, 0x00, // fmt chunk
                    0x80, 0xBB, 0x00, 0x00, 0x80, 0x3E, 0x00, 0x00, // sample rate (será modificado)
                    0x50, 0xC3, 0x04, 0x00, 0x02, 0x00, 0x99, 0x86, // block size (será modificado)
                    0x66, 0x61, 0x63, 0x74, 0x04, 0x00, 0x00, 0x00,
                    0x00, 0x40, 0x2B, 0x00, 0x4C, 0x49, 0x53, 0x54,
                    0x1A, 0x00, 0x00, 0x00, 0x49, 0x4E, 0x46, 0x4F,
                    0x49, 0x53, 0x46, 0x54, 0x0D, 0x00, 0x00, 0x00,
                    0x4C, 0x61, 0x76, 0x66, 0x36, 0x32, 0x2E, 0x36,
                    0x2E, 0x31, 0x30, 0x31, 0x00, 0x00, 0x64, 0x61,
                    0x74, 0x61, 0x10, 0x20, 0x16, 0x00 // data + tamanho (será modificado)
                };

                // 5. Modifica o header com os valores corretos
                int totalFileSize = header.Length + audioData.Length - 8; // RIFF chunk size
                int dataChunkSize = audioData.Length;

                // Offset 0x04-0x07: Tamanho total do arquivo (RIFF chunk size)
                byte[] fileSizeBytes = BitConverter.GetBytes(totalFileSize);
                Array.Copy(fileSizeBytes, 0, header, 0x04, 4);

                // Offset 0x18-0x1B: Sample Rate
                byte[] sampleRateBytes = BitConverter.GetBytes(sampleRate);
                Array.Copy(sampleRateBytes, 0, header, 0x18, 4);

                // Offset 0x20-0x21: Block Size (2 bytes, Little-Endian)
                byte[] blockSizeBytes = BitConverter.GetBytes(blockSize);
                Array.Copy(blockSizeBytes, 0, header, 0x20, 2);

                // Offset 0x5A-0x5D: Tamanho do data chunk
                byte[] dataChunkSizeBytes = BitConverter.GetBytes(dataChunkSize);
                Array.Copy(dataChunkSizeBytes, 0, header, 0x5A, 4);

                // 6. Combina header + dados de áudio e salva
                using (FileStream fs = new FileStream(tempWavFile, FileMode.Create, FileAccess.Write))
                {
                    await fs.WriteAsync(header, 0, header.Length);
                    await fs.WriteAsync(audioData, 0, audioData.Length);
                }

                StatusMessage = ($"Converting with vgmstream-cli (Sample Rate: {sampleRate} Hz, Size: {audioData.Length} bytes)...");

                // 7. Verifica se vgmstream-cli existe
                if (!File.Exists(vgmstreamPath))
                {
                    MessageBox.Show("vgmstream-cli.exe not found in application directory!",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 8. Converte com vgmstream-cli
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = vgmstreamPath,
                    Arguments = $"\"{tempWavFile}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetTempPath()
                };

                using (Process process = new Process { StartInfo = psi })
                {
                    process.Start();
                    await Task.Run(() => process.WaitForExit());

                    if (process.ExitCode != 0)
                    {
                        string error = await process.StandardError.ReadToEndAsync();
                        MessageBox.Show($"vgmstream-cli conversion failed:\n{error}",
                            "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }

                // 9. Verifica se o arquivo convertido foi criado
                if (!File.Exists(convertedWavFile))
                {
                    MessageBox.Show("Converted WAV file was not created by vgmstream-cli.",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Aguarda um pouco para garantir que o arquivo está completamente gravado
                await Task.Delay(200);

                // 10. Fecha qualquer arquivo anterior e reproduz o novo áudio
                _mediaPlayer.Close();
                await Task.Delay(100); // Aguarda fechar completamente

                _mediaPlayer.Open(new Uri(convertedWavFile, UriKind.Absolute));
                _mediaPlayer.Play();
                _playerTimer.Start();
                IsPlaying = true;
                StatusMessage = $"Playing: {item.FileName}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error playing sound: {ex.Message}\n\nStack Trace: {ex.StackTrace}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                CleanupTempFiles(tempRawFile, tempWavFile, convertedWavFile);
                IsPlaying = false;
                _playerTimer.Stop();
            }
        }

        private void CleanupTempFiles(params string[] files)
        {
            foreach (string file in files)
            {
                try
                {
                    if (File.Exists(file))
                    {
                        File.Delete(file);
                    }
                }
                catch
                {
                    // Ignora erros de limpeza
                }
            }
        }

        private (int sampleRate, long idOffset) FindHashAndExtractSampleRate(string hash)
        {
            if (string.IsNullOrEmpty(hash) || hash.Length != 8)
                return (0, -1);

            try
            {
                // Converte hash para bytes em Little-Endian
                byte[] hashBytes = new byte[4];
                for (int i = 0; i < 4; i++)
                {
                    hashBytes[3 - i] = Convert.ToByte(hash.Substring(i * 2, 2), 16);
                }

                byte[] wbkData = File.ReadAllBytes(_wbkFilePath);
                byte[] signature = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF };

                // Procura pelo hash no WBK
                for (int i = 0; i < wbkData.Length - 32; i++)
                {
                    // Verifica se encontrou o hash
                    if (wbkData[i] == hashBytes[0] &&
                        wbkData[i + 1] == hashBytes[1] &&
                        wbkData[i + 2] == hashBytes[2] &&
                        wbkData[i + 3] == hashBytes[3])
                    {
                        // Procura assinatura FF FF FF FF nas proximidades (até 32 bytes à frente)
                        for (int j = i; j < i + 32 && j < wbkData.Length - 8; j++)
                        {
                            if (wbkData[j] == signature[0] &&
                                wbkData[j + 1] == signature[1] &&
                                wbkData[j + 2] == signature[2] &&
                                wbkData[j + 3] == signature[3])
                            {
                                // Encontrou assinatura! Extrai sample rate (4 bytes após assinatura)
                                int sampleRateOffset = j + 8;

                                if (sampleRateOffset + 4 <= wbkData.Length)
                                {
                                    // Lê sample rate (Little-Endian, 4 bytes)
                                    int sampleRate = BitConverter.ToInt32(wbkData, sampleRateOffset);

                                    return (sampleRate, i);
                                }
                            }
                        }
                    }
                }

                return (0, -1);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error finding hash and sample rate: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return (0, -1);
            }
        }

        private void ModifyControlByte(long idOffset)
        {
            try
            {
                using (FileStream fs = new FileStream(_wbkFilePath, FileMode.Open, FileAccess.ReadWrite))
                {
                    // Vai para ID + 6 bytes (terceiro byte após o ID considerando grupos de 2)
                    // Estrutura: [ID 4 bytes] [byte1] [byte2] [ESTE BYTE]
                    long controlByteOffset = idOffset + 6;

                    fs.Seek(controlByteOffset, SeekOrigin.Begin);

                    // Lê o byte atual
                    int currentByte = fs.ReadByte();

                    // Substitui para 0x00
                    fs.Seek(controlByteOffset, SeekOrigin.Begin);
                    fs.WriteByte(0x00);

                    StatusMessage = $"Control byte at 0x{controlByteOffset:X} changed from 0x{currentByte:X2} to 0x00";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error modifying control byte: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public async Task ConvertAndReplaceWav(WbkItem item, string wavFilePath)
        {
            string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
            string outputWavPath = Path.Combine(Path.GetTempPath(), "output.wav");

            try
            {
                if (!File.Exists(ffmpegPath))
                {
                    MessageBox.Show("ffmpeg.exe not found in application directory!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Extrai hash do nome do arquivo
                var (sampleRate, idOffset) = FindHashAndExtractSampleRate(item.Hash);

                if (sampleRate == 0 || idOffset == -1)
                {
                    MessageBox.Show($"Could not find hash signature or extract sample rate for {item.FileName}.\n\nUsing default 48000 Hz.",
                        "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                    sampleRate = 48000;
                }
                else
                {
                    StatusMessage = $"Found hash at 0x{idOffset:X} | Sample Rate: {sampleRate} Hz";
                }

                long blockSize = item.Size;

                if (File.Exists(outputWavPath))
                    File.Delete(outputWavPath);

                // Comando FFmpeg com sample rate e mono
                string arguments = $"-i \"{wavFilePath}\" -ar {sampleRate} -ac 1 -c:a adpcm_ima_wav -block_size {blockSize} \"{outputWavPath}\"";

                StatusMessage = $"Converting with FFmpeg: {sampleRate} Hz, Mono, Block Size: {blockSize}...";

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                string output = string.Empty;
                string error = string.Empty;

                using (Process process = new Process { StartInfo = startInfo })
                {
                    process.Start();

                    var outputTask = process.StandardOutput.ReadToEndAsync();
                    var errorTask = process.StandardError.ReadToEndAsync();

                    await Task.Run(() => process.WaitForExit());

                    output = await outputTask;
                    error = await errorTask;

                    if (process.ExitCode != 0)
                    {
                        MessageBox.Show($"FFmpeg conversion failed:\n{error}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }

                byte[] rawData = ExtractDataAfterDataChunk(outputWavPath);

                if (rawData == null || rawData.Length == 0)
                {
                    MessageBox.Show("Failed to extract audio data from converted WAV file.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (rawData.Length > item.Size)
                {
                    var result = MessageBox.Show(
                        $"Converted data is {rawData.Length} bytes but only {item.Size} bytes available.\n" +
                        $"The file will be TRUNCATED to fit. Continue?",
                        "File Too Large",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result == MessageBoxResult.No)
                        return;

                    Array.Resize(ref rawData, (int)item.Size);
                    item.Status = "Converted (Truncated)";
                }
                else if (rawData.Length < item.Size)
                {
                    byte[] paddedData = new byte[item.Size];
                    Array.Copy(rawData, paddedData, rawData.Length);
                    rawData = paddedData;
                    item.Status = "Converted";
                }
                else
                {
                    item.Status = "Converted";
                }

                // Escreve no WBK
                using (FileStream fs = new FileStream(_wbkFilePath, FileMode.Open, FileAccess.ReadWrite))
                {
                    fs.Seek(item.StartOffset, SeekOrigin.Begin);
                    await fs.WriteAsync(rawData, 0, rawData.Length);
                }

                // Modifica o byte de controle se o hash foi encontrado
                if (idOffset != -1)
                {
                    ModifyControlByte(idOffset);
                }

                StatusMessage = $"Successfully converted and replaced {item.FileName}";
                MessageBox.Show($"File converted and replaced successfully!\n\nFile: {item.FileName}\nSample Rate: {sampleRate} Hz\nSize: {rawData.Length} bytes\nWritten at: 0x{item.StartOffset:X}",
                    "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during conversion: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (File.Exists(outputWavPath))
                    File.Delete(outputWavPath);
            }
        }

        private byte[] ExtractDataAfterDataChunk(string wavFilePath)
        {
            try
            {
                byte[] wavBytes = File.ReadAllBytes(wavFilePath);
                byte[] dataMarker = Encoding.ASCII.GetBytes("data");

                // Procura pela palavra "data" no arquivo WAV
                for (int i = 0; i < wavBytes.Length - 4; i++)
                {
                    if (wavBytes[i] == dataMarker[0] &&
                        wavBytes[i + 1] == dataMarker[1] &&
                        wavBytes[i + 2] == dataMarker[2] &&
                        wavBytes[i + 3] == dataMarker[3])
                    {
                        // Encontrou "data", pula 4 bytes do marker + 4 bytes do size do chunk
                        int dataStartOffset = i + 8;

                        if (dataStartOffset >= wavBytes.Length)
                            return null;

                        // Retorna os bytes após o chunk header
                        byte[] rawData = new byte[wavBytes.Length - dataStartOffset];
                        Array.Copy(wavBytes, dataStartOffset, rawData, 0, rawData.Length);
                        return rawData;
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error extracting data chunk: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
        }

        public async Task ReplaceFileInWbk(WbkItem item, string replacementFilePath)
        {
            try
            {
                byte[] newData = await File.ReadAllBytesAsync(replacementFilePath);
                long availableSpace = item.Size;

                if (newData.Length > availableSpace)
                {
                    var result = MessageBox.Show(
                        $"File is {newData.Length} bytes but only {availableSpace} bytes available.\n" +
                        $"The file will be TRUNCATED to fit. Continue?",
                        "File Too Large",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result == MessageBoxResult.No)
                        return;

                    Array.Resize(ref newData, (int)availableSpace);
                    item.Status = "Truncated";
                }
                else if (newData.Length < availableSpace)
                {
                    byte[] paddedData = new byte[availableSpace];
                    Array.Copy(newData, paddedData, newData.Length);
                    newData = paddedData;
                    item.Status = "Replaced";
                }
                else
                {
                    item.Status = "Replaced";
                }

                using (FileStream fs = new FileStream(_wbkFilePath, FileMode.Open, FileAccess.ReadWrite))
                {
                    fs.Seek(item.StartOffset, SeekOrigin.Begin);
                    await fs.WriteAsync(newData, 0, newData.Length);
                }

                StatusMessage = $"Successfully replaced {item.FileName}";
                MessageBox.Show($"File replaced successfully!\n\nFile: {item.FileName}\nSize: {newData.Length} bytes\nWritten at: 0x{item.StartOffset:X}",
                    "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error replacing file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public async Task BatchConvertAndReplace(string[] wavFiles)
        {
            if (wavFiles == null || wavFiles.Length == 0)
            {
                MessageBox.Show("No files selected.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int successCount = 0;
            int failedCount = 0;
            int skippedCount = 0;
            StringBuilder report = new StringBuilder();
            report.AppendLine("Batch Conversion Report:");
            report.AppendLine("=========================\n");

            foreach (string wavFile in wavFiles)
            {
                try
                {
                    // Extrai o nome base do arquivo WAV (sem extensão)
                    string wavFileName = Path.GetFileNameWithoutExtension(wavFile);

                    // Procura por um item correspondente no WBK
                    // Remove a parte do hash entre parênteses para comparação
                    WbkItem matchingItem = WbkItems.FirstOrDefault(item =>
                    {
                        string itemBaseName = item.FileName;

                        // Remove o hash entre parênteses se existir
                        int hashStartIndex = itemBaseName.IndexOf('(');
                        if (hashStartIndex > 0)
                        {
                            itemBaseName = itemBaseName.Substring(0, hashStartIndex).Trim();
                        }

                        // Remove extensão .raw se existir
                        if (itemBaseName.EndsWith(".raw", StringComparison.OrdinalIgnoreCase))
                        {
                            itemBaseName = Path.GetFileNameWithoutExtension(itemBaseName);
                        }

                        return itemBaseName.Equals(wavFileName, StringComparison.OrdinalIgnoreCase);
                    });

                    if (matchingItem == null)
                    {
                        skippedCount++;
                        report.AppendLine($"⚠️ SKIPPED: {Path.GetFileName(wavFile)}");
                        report.AppendLine($"   Reason: No matching entry found in WBK\n");
                        continue;
                    }

                    // Executa a conversão e substituição
                    StatusMessage = $"Processing {successCount + 1}/{wavFiles.Length}: {Path.GetFileName(wavFile)}...";

                    await ConvertAndReplaceWavSilent(matchingItem, wavFile);

                    successCount++;
                    report.AppendLine($"✅ SUCCESS: {Path.GetFileName(wavFile)}");
                    report.AppendLine($"   Matched with: {matchingItem.FileName}");
                    report.AppendLine($"   Size: {matchingItem.SizeDisplay}\n");
                }
                catch (Exception ex)
                {
                    failedCount++;
                    report.AppendLine($"❌ FAILED: {Path.GetFileName(wavFile)}");
                    report.AppendLine($"   Error: {ex.Message}\n");
                }
            }

            report.AppendLine("=========================");
            report.AppendLine($"Total: {wavFiles.Length} file(s)");
            report.AppendLine($"✅ Success: {successCount}");
            report.AppendLine($"❌ Failed: {failedCount}");
            report.AppendLine($"⚠️ Skipped: {skippedCount}");

            StatusMessage = $"Batch complete: {successCount} success, {failedCount} failed, {skippedCount} skipped";
            MessageBox.Show(report.ToString(), "Batch Conversion Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async Task ConvertAndReplaceWavSilent(WbkItem item, string wavFilePath)
        {
            string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
            string outputWavPath = Path.Combine(Path.GetTempPath(), $"output_{Guid.NewGuid()}.wav");

            try
            {
                if (!File.Exists(ffmpegPath))
                    throw new Exception("ffmpeg.exe not found in application directory!");

                var (sampleRate, idOffset) = FindHashAndExtractSampleRate(item.Hash);

                if (sampleRate == 0 || idOffset == -1)
                    sampleRate = 48000;

                long blockSize = item.Size;

                if (File.Exists(outputWavPath))
                    File.Delete(outputWavPath);

                string arguments = $"-i \"{wavFilePath}\" -ar {sampleRate} -ac 1 -c:a adpcm_ima_wav -block_size {blockSize} \"{outputWavPath}\"";

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (Process process = new Process { StartInfo = startInfo })
                {
                    process.Start();
                    await Task.Run(() => process.WaitForExit());

                    if (process.ExitCode != 0)
                    {
                        string error = await process.StandardError.ReadToEndAsync();
                        throw new Exception($"FFmpeg conversion failed: {error}");
                    }
                }

                byte[] rawData = ExtractDataAfterDataChunk(outputWavPath);

                if (rawData == null || rawData.Length == 0)
                    throw new Exception("Failed to extract audio data from converted WAV file.");

                if (rawData.Length > item.Size)
                {
                    Array.Resize(ref rawData, (int)item.Size);
                    item.Status = "Converted (Truncated)";
                }
                else if (rawData.Length < item.Size)
                {
                    byte[] paddedData = new byte[item.Size];
                    Array.Copy(rawData, paddedData, rawData.Length);
                    rawData = paddedData;
                    item.Status = "Converted";
                }
                else
                {
                    item.Status = "Converted";
                }

                using (FileStream fs = new FileStream(_wbkFilePath, FileMode.Open, FileAccess.ReadWrite))
                {
                    fs.Seek(item.StartOffset, SeekOrigin.Begin);
                    await fs.WriteAsync(rawData, 0, rawData.Length);
                }

                if (idOffset != -1)
                    ModifyControlByte(idOffset);
            }
            finally
            {
                if (File.Exists(outputWavPath))
                    File.Delete(outputWavPath);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}