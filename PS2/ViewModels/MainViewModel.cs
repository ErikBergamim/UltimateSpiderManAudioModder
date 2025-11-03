using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Win32;
using WpfApp1.Models;

namespace WpfApp1.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private string _pakFilePath;
        private string _statusMessage;
        private bool _isPakLoaded;
        private string _applicationDirectory;

        private bool _isPlayerVisible;
        private string _currentPlayingFile;
        private int _currentSampleRate;
        private string _currentWavPath;
        private string _currentVagPath;
        private string _currentTempPath;

        private PakCategory _selectedCategory;
        private bool _isLoadingItems;
        private string _searchText;

        // Armazena todos os itens agrupados por categoria
        private Dictionary<string, List<PakItem>> _categorizedItems;
        
        // Lista completa da categoria atual (para filtragem)
        private List<PakItem> _currentCategoryItems;

        public ObservableCollection<PakCategory> Categories { get; set; }
        public ObservableCollection<PakItem> PakItems { get; set; }

        public PakCategory SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                _selectedCategory = value;
                OnPropertyChanged();
                LoadItemsForCategory(value);
            }
        }

        public bool IsLoadingItems
        {
            get => _isLoadingItems;
            set
            {
                _isLoadingItems = value;
                OnPropertyChanged();
            }
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                _searchText = value;
                OnPropertyChanged();
                FilterItems();
            }
        }

        public string PakFilePath
        {
            get => _pakFilePath;
            set
            {
                _pakFilePath = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PakFileName));
            }
        }

        public string PakFileName => string.IsNullOrEmpty(PakFilePath) 
            ? "Nenhum arquivo carregado" 
            : Path.GetFileName(PakFilePath);

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                _statusMessage = value;
                OnPropertyChanged();
            }
        }

        public bool IsPakLoaded
        {
            get => _isPakLoaded;
            set
            {
                _isPakLoaded = value;
                OnPropertyChanged();
            }
        }

        public bool IsPlayerVisible
        {
            get => _isPlayerVisible;
            set
            {
                _isPlayerVisible = value;
                OnPropertyChanged();
            }
        }

        public string CurrentPlayingFile
        {
            get => _currentPlayingFile;
            set
            {
                _currentPlayingFile = value;
                OnPropertyChanged();
            }
        }

        public int CurrentSampleRate
        {
            get => _currentSampleRate;
            set
            {
                _currentSampleRate = value;
                OnPropertyChanged();
            }
        }

        public MainViewModel()
        {
            Categories = new ObservableCollection<PakCategory>();
            PakItems = new ObservableCollection<PakItem>();
            _categorizedItems = new Dictionary<string, List<PakItem>>();
            _currentCategoryItems = new List<PakItem>();
            StatusMessage = "Aguardando carregamento de arquivo .pak";
            SearchText = string.Empty;
            
            _applicationDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            Debug.WriteLine($"[INIT] Diretório da aplicação: {_applicationDirectory}");
        }

        private string ExtractCategoryName(string fileName)
        {
            string nameWithoutExtension = fileName;
            if (nameWithoutExtension.EndsWith(".raw", StringComparison.OrdinalIgnoreCase))
                nameWithoutExtension = nameWithoutExtension.Substring(0, nameWithoutExtension.Length - 4);

            int hashIndex = nameWithoutExtension.IndexOf('#');
            if (hashIndex > 0)
                return nameWithoutExtension.Substring(0, hashIndex).Trim();

            int parenthesisIndex = nameWithoutExtension.IndexOf(" (");
            if (parenthesisIndex > 0)
                return nameWithoutExtension.Substring(0, parenthesisIndex).Trim();

            return nameWithoutExtension;
        }

        /// <summary>
        /// Comparador personalizado para ordenação natural de arquivos
        /// CITY_ARENA#1, CITY_ARENA#2, ..., CITY_ARENA#10, CITY_ARENA#11
        /// </summary>
        private class NaturalStringComparer : IComparer<string>
        {
            public int Compare(string x, string y)
            {
                if (x == y) return 0;
                if (x == null) return -1;
                if (y == null) return 1;

                int ix = 0, iy = 0;

                while (ix < x.Length && iy < y.Length)
                {
                    if (char.IsDigit(x[ix]) && char.IsDigit(y[iy]))
                    {
                        string numX = "";
                        string numY = "";

                        while (ix < x.Length && char.IsDigit(x[ix]))
                            numX += x[ix++];

                        while (iy < y.Length && char.IsDigit(y[iy]))
                            numY += y[iy++];

                        int comparison = int.Parse(numX).CompareTo(int.Parse(numY));
                        if (comparison != 0)
                            return comparison;
                    }
                    else
                    {
                        int comparison = x[ix].CompareTo(y[iy]);
                        if (comparison != 0)
                            return comparison;
                        ix++;
                        iy++;
                    }
                }

                return x.Length.CompareTo(y.Length);
            }
        }

        private async void LoadItemsForCategory(PakCategory category)
        {
            if (category == null)
            {
                PakItems.Clear();
                _currentCategoryItems.Clear();
                return;
            }

            IsLoadingItems = true;
            SearchText = string.Empty;

            try
            {
                // Limpar em thread principal
                PakItems.Clear();
                _currentCategoryItems.Clear();

                // Processar em background
                await Task.Run(() =>
                {
                    List<PakItem> itemsToLoad = new List<PakItem>();

                    if (category.Name == "Todas")
                    {
                        foreach (var categoryItems in _categorizedItems.Values)
                        {
                            itemsToLoad.AddRange(categoryItems);
                        }
                    }
                    else
                    {
                        if (_categorizedItems.ContainsKey(category.Name))
                        {
                            itemsToLoad.AddRange(_categorizedItems[category.Name]);
                        }
                    }

                    // Ordenar em background
                    _currentCategoryItems = itemsToLoad
                        .OrderBy(item => item.FileName, new NaturalStringComparer())
                        .ToList();
                });

                // Adicionar à ObservableCollection em lotes (evita freeze da UI)
                int batchSize = 50;
                int currentIndex = 0;

                while (currentIndex < _currentCategoryItems.Count)
                {
                    int itemsToAdd = Math.Min(batchSize, _currentCategoryItems.Count - currentIndex);
                    
                    for (int i = 0; i < itemsToAdd; i++)
                    {
                        PakItems.Add(_currentCategoryItems[currentIndex + i]);
                    }

                    currentIndex += itemsToAdd;

                    // Dar tempo para UI atualizar
                    await Task.Delay(1);
                }

                StatusMessage = $"{PakItems.Count} arquivo(s) em '{category.Name}'";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERRO] LoadItemsForCategory: {ex.Message}");
                StatusMessage = $"Erro ao carregar categoria: {ex.Message}";
            }
            finally
            {
                IsLoadingItems = false;
            }
        }

        private async void FilterItems()
        {
            if (_currentCategoryItems == null || _currentCategoryItems.Count == 0)
                return;

            IsLoadingItems = true;

            try
            {
                PakItems.Clear();

                List<PakItem> filteredItems;

                if (string.IsNullOrWhiteSpace(SearchText))
                {
                    filteredItems = _currentCategoryItems;
                }
                else
                {
                    // Filtrar em background
                    string searchLower = SearchText.ToLower();
                    filteredItems = await Task.Run(() =>
                    {
                        return _currentCategoryItems
                            .Where(item => item.FileName.ToLower().Contains(searchLower))
                            .ToList();
                    });
                }

                // Adicionar em lotes
                int batchSize = 50;
                int currentIndex = 0;

                while (currentIndex < filteredItems.Count)
                {
                    int itemsToAdd = Math.Min(batchSize, filteredItems.Count - currentIndex);
                    
                    for (int i = 0; i < itemsToAdd; i++)
                    {
                        PakItems.Add(filteredItems[currentIndex + i]);
                    }

                    currentIndex += itemsToAdd;
                    await Task.Delay(1);
                }

                if (string.IsNullOrWhiteSpace(SearchText))
                {
                    StatusMessage = $"{PakItems.Count} arquivo(s) em '{_selectedCategory?.Name}'";
                }
                else
                {
                    StatusMessage = $"{PakItems.Count} arquivo(s) encontrado(s) (filtrado de {_currentCategoryItems.Count})";
                }
            }
            finally
            {
                IsLoadingItems = false;
            }
        }

        public void LoadPakFile(string pakPath)
        {
            try
            {
                PakFilePath = pakPath;
                Categories.Clear();
                PakItems.Clear();
                _categorizedItems.Clear();
                _currentCategoryItems.Clear();

                string pakFileName = Path.GetFileNameWithoutExtension(pakPath);
                string jsonFileName = pakFileName + ".json";

                string jsonPath = null;
                string jsonPathSamePak = Path.Combine(Path.GetDirectoryName(pakPath), jsonFileName);
                string jsonPathAppDir = Path.Combine(_applicationDirectory, jsonFileName);

                if (File.Exists(jsonPathSamePak))
                {
                    jsonPath = jsonPathSamePak;
                    Debug.WriteLine($"[JSON] Carregando do diretório do PAK: {jsonPath}");
                }
                else if (File.Exists(jsonPathAppDir))
                {
                    jsonPath = jsonPathAppDir;
                    Debug.WriteLine($"[JSON] Carregando do diretório da aplicação: {jsonPath}");
                }
                else
                {
                    MessageBox.Show(
                        $"Arquivo JSON não encontrado: {jsonFileName}\n\n" +
                        $"Procurado em:\n" +
                        $"1. {jsonPathSamePak}\n" +
                        $"2. {jsonPathAppDir}\n\n" +
                        $"Coloque o JSON em um destes locais.",
                        "Erro",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    StatusMessage = "Erro: Arquivo JSON não encontrado";
                    return;
                }

                StatusMessage = "Carregando e categorizando arquivos...";
                IsLoadingItems = true;

                var jsonContent = File.ReadAllText(jsonPath);
                var metadata = JsonSerializer.Deserialize<PakMetadata>(jsonContent, 
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = false });

                if (metadata?.Mappings != null)
                {
                    Debug.WriteLine($"[CATEGORIZAÇÃO] Processando {metadata.Mappings.Count} itens...");

                    foreach (var mapping in metadata.Mappings)
                    {
                        var item = new PakItem
                        {
                            FileName = mapping.RawFile,
                            Size = mapping.RawSize,
                            OffsetStart = mapping.PakOffsetStart,
                            OffsetEnd = mapping.PakOffsetEnd
                        };

                        string categoryName = ExtractCategoryName(mapping.RawFile);

                        if (!_categorizedItems.ContainsKey(categoryName))
                        {
                            _categorizedItems[categoryName] = new List<PakItem>();
                        }

                        _categorizedItems[categoryName].Add(item);
                    }

                    // Ordenar itens dentro de cada categoria
                    var comparer = new NaturalStringComparer();
                    foreach (var category in _categorizedItems.Keys.ToList())
                    {
                        _categorizedItems[category] = _categorizedItems[category]
                            .OrderBy(item => item.FileName, comparer)
                            .ToList();
                    }

                    var sortedCategories = _categorizedItems.Keys.OrderBy(k => k).ToList();

                    Categories.Add(new PakCategory 
                    { 
                        Name = "Todas", 
                        ItemCount = metadata.Mappings.Count 
                    });

                    foreach (var categoryName in sortedCategories)
                    {
                        Categories.Add(new PakCategory 
                        { 
                            Name = categoryName, 
                            ItemCount = _categorizedItems[categoryName].Count 
                        });

                        Debug.WriteLine($"[CATEGORIA] {categoryName}: {_categorizedItems[categoryName].Count} itens");
                    }

                    IsPakLoaded = true;
                    StatusMessage = $"{metadata.Mappings.Count} arquivo(s) organizados em {Categories.Count - 1} categoria(s)";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao carregar arquivo: {ex.Message}", 
                    "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusMessage = "Erro ao carregar arquivo";
            }
            finally
            {
                IsLoadingItems = false;
            }
        }

        private string GetMFAudioPath()
        {
            return Path.Combine(_applicationDirectory, "MFAudio.exe");
        }

        private string GetVgmStreamPath()
        {
            return Path.Combine(_applicationDirectory, "vgmstream-cli.exe");
        }

        private string ExtractFileId(string fileName)
        {
            var match = Regex.Match(fileName, @"\(([0-9a-fA-F]{8})\)");
            return match.Success ? match.Groups[1].Value : null;
        }

        private byte[] ReverseEndian(string hexId)
        {
            if (string.IsNullOrEmpty(hexId) || hexId.Length != 8)
                return null;

            byte[] bytes = new byte[4];
            for (int i = 0; i < 4; i++)
            {
                bytes[i] = Convert.ToByte(hexId.Substring((3 - i) * 2, 2), 16);
            }
            return bytes;
        }

        private long FindSignatureOffset(byte[] buffer, long startPosition, int searchRange = 32)
        {
            byte[] signature = { 0xFF, 0xFF, 0xFF, 0xFF };
            long maxPosition = Math.Min(startPosition + searchRange, buffer.Length - signature.Length);

            for (long i = startPosition; i < maxPosition; i++)
            {
                bool found = true;
                for (int j = 0; j < signature.Length; j++)
                {
                    if (buffer[i + j] != signature[j])
                    {
                        found = false;
                        break;
                    }
                }

                if (found)
                {
                    Debug.WriteLine($"[ASSINATURA] FF FF FF FF encontrada no offset 0x{i:X8} (distância: {i - startPosition} bytes do ID)");
                    return i;
                }
            }

            return -1;
        }

        private int ExtractSampleRate(byte[] buffer, long signatureOffset)
        {
            long sampleRateOffset = signatureOffset + 8;

            if (sampleRateOffset + 2 > buffer.Length)
            {
                Debug.WriteLine($"[ERRO] Offset da sample rate fora do buffer");
                return -1;
            }

            byte lowByte = buffer[sampleRateOffset];
            byte highByte = buffer[sampleRateOffset + 1];
            
            int sampleRate = (highByte << 8) | lowByte;

            Debug.WriteLine($"[SAMPLE RATE] {sampleRate} Hz");

            return sampleRate;
        }

        private (bool success, int sampleRate) FindAndModifyByte(string hexId)
        {
            try
            {
                byte[] searchPattern = ReverseEndian(hexId);
                if (searchPattern == null)
                {
                    StatusMessage = "ID inválido para modificação";
                    return (false, -1);
                }

                using (var pakStream = new FileStream(PakFilePath, FileMode.Open, FileAccess.ReadWrite))
                {
                    byte[] buffer = new byte[pakStream.Length];
                    pakStream.Read(buffer, 0, buffer.Length);

                    int occurrencesFound = 0;
                    long validOffset = -1;
                    int sampleRate = -1;

                    for (long i = 0; i <= buffer.Length - searchPattern.Length - 3; i++)
                    {
                        bool found = true;
                        for (int j = 0; j < searchPattern.Length; j++)
                        {
                            if (buffer[i + j] != searchPattern[j])
                            {
                                found = false;
                                break;
                            }
                        }

                        if (found)
                        {
                            occurrencesFound++;
                            long targetOffset = i + 6;

                            Debug.WriteLine($"[OCORRÊNCIA #{occurrencesFound}] ID encontrado no offset 0x{i:X8}");

                            long signatureOffset = FindSignatureOffset(buffer, i, 32);
                            if (signatureOffset >= 0)
                            {
                                validOffset = targetOffset;
                                sampleRate = ExtractSampleRate(buffer, signatureOffset);
                                Debug.WriteLine($"[VALIDADO] Ocorrência #{occurrencesFound} possui assinatura FF FF FF FF");
                                break;
                            }
                            else
                            {
                                Debug.WriteLine($"[IGNORADO] Ocorrência #{occurrencesFound} sem assinatura");
                            }
                        }
                    }

                    if (validOffset >= 0 && validOffset < buffer.Length)
                    {   
                        byte originalValue = buffer[validOffset];

                        pakStream.Seek(validOffset, SeekOrigin.Begin);
                        pakStream.WriteByte(0x00);
                        pakStream.Flush();

                        StatusMessage = $"Byte modificado no offset 0x{validOffset:X8}: 0x{originalValue:X2} -> 0x00";
                        Debug.WriteLine($"[MODIFICAÇÃO] Offset: 0x{validOffset:X8} | Original: 0x{originalValue:X2} | Novo: 0x00");

                        return (true, sampleRate);
                    }
                    else if (occurrencesFound > 0)
                    {
                        StatusMessage = $"{occurrencesFound} ocorrência(s) encontrada(s) mas nenhuma válida";
                        return (false, -1);
                    }
                    else
                    {
                        StatusMessage = $"Sequência não encontrada para ID: {hexId}";
                        return (false, -1);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao modificar byte: {ex.Message}", 
                    "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                return (false, -1);
            }
        }

        public void ConvertAndReplaceAudio(PakItem item)
        {
            try
            {
                string mfAudioPath = GetMFAudioPath();

                if (!File.Exists(mfAudioPath))
                {
                    MessageBox.Show(
                        $"MFAudio.exe não encontrado no diretório da aplicação:\n{_applicationDirectory}\n\nColoque o MFAudio.exe na mesma pasta do executável.",
                        "Arquivo não encontrado",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                string fileId = ExtractFileId(item.FileName);
                int sampleRate = -1;

                if (!string.IsNullOrEmpty(fileId))
                {
                    Debug.WriteLine($"[INFO] ID extraído: {fileId}");
                    
                    byte[] searchPattern = ReverseEndian(fileId);
                    if (searchPattern != null)
                    {
                        using (var pakStream = new FileStream(PakFilePath, FileMode.Open, FileAccess.Read))
                        {
                            byte[] buffer = new byte[pakStream.Length];
                            pakStream.Read(buffer, 0, buffer.Length);

                            for (long i = 0; i <= buffer.Length - searchPattern.Length - 3; i++)
                            {
                                bool found = true;
                                for (int j = 0; j < searchPattern.Length; j++)
                                {
                                    if (buffer[i + j] != searchPattern[j])
                                    {
                                        found = false;
                                        break;
                                    }
                                }

                                if (found)
                                {
                                    long signatureOffset = FindSignatureOffset(buffer, i, 32);
                                    if (signatureOffset >= 0)
                                    {
                                        sampleRate = ExtractSampleRate(buffer, signatureOffset);
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }

                if (sampleRate <= 0)
                {
                    MessageBox.Show(
                        $"Não foi possível detectar a sample rate.\n\nID: {fileId ?? "não encontrado"}",
                        "Erro",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                var dialog = new OpenFileDialog
                {
                    Filter = "Audio Files (*.wav)|*.wav|All Files (*.*)|*.*",
                    Title = $"Selecione o arquivo de áudio para converter e substituir {item.FileName}"
                };

                if (dialog.ShowDialog() == true)
                {
                    StatusMessage = $"Convertendo '{Path.GetFileName(dialog.FileName)}' para {sampleRate} Hz...";

                    string tempOutputPath = Path.Combine(Path.GetTempPath(), $"temp_converted_{Guid.NewGuid()}.raw");

                    try
                    {
                        string arguments = $"\"{dialog.FileName}\" \"{tempOutputPath}\" /OTRAWC /OC1 /OF{sampleRate}";
                        
                        Debug.WriteLine($"[CONVERSÃO] {mfAudioPath} {arguments}");

                        var processInfo = new ProcessStartInfo
                        {
                            FileName = mfAudioPath,
                            Arguments = arguments,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        };

                        using (var process = Process.Start(processInfo))
                        {
                            process.WaitForExit();

                            if (process.ExitCode != 0)
                            {
                                string error = process.StandardError.ReadToEnd();
                                MessageBox.Show(
                                    $"Erro ao converter arquivo.\n\nCódigo: {process.ExitCode}\n{error}",
                                    "Erro de Conversão",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Error);
                                StatusMessage = "Erro na conversão";
                                return;
                            }
                        }

                        if (!File.Exists(tempOutputPath))
                        {
                            MessageBox.Show("O arquivo convertido não foi criado.", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                            StatusMessage = "Erro: arquivo convertido não encontrado";
                            return;
                        }

                        var convertedFileInfo = new FileInfo(tempOutputPath);
                        long bytesToWrite = Math.Min(convertedFileInfo.Length, item.Size);
                        bool willBeTruncated = convertedFileInfo.Length > item.Size;

                        string message = $"Converter e substituir '{item.FileName}'?\n\n" +
                                       $"Sample Rate: {sampleRate} Hz\n" +
                                       $"Tamanho original: {item.Size:N0} bytes\n" +
                                       $"Tamanho convertido: {convertedFileInfo.Length:N0} bytes\n" +
                                       $"Bytes a serem gravados: {bytesToWrite:N0} bytes";

                        if (willBeTruncated)
                        {
                            message += $"\n\n⚠️ TRUNCADO!\nBytes perdidos: {(convertedFileInfo.Length - item.Size):N0}";
                        }

                        var result = MessageBox.Show(message, "Confirmar", MessageBoxButton.YesNo,
                            willBeTruncated ? MessageBoxImage.Warning : MessageBoxImage.Question);

                        if (result == MessageBoxResult.Yes)
                        {
                            ReplaceFileInPakTruncated(tempOutputPath, item);
                            var (byteModified, _) = FindAndModifyByte(fileId);

                            item.Status = willBeTruncated ? "Truncado" : "Convertido";
                            
                            string successMessage = $"Conversão concluída!\n\n" +
                                                   $"Arquivo: {item.FileName}\n" +
                                                   $"Sample Rate: {sampleRate} Hz\n" +
                                                   $"Bytes gravados: {bytesToWrite:N0}";

                            if (willBeTruncated)
                                successMessage += $"\n\n⚠️ Truncado: {(convertedFileInfo.Length - item.Size):N0} bytes perdidos";

                            if (byteModified)
                                successMessage += $"\n\n✅ Byte de controle modificado";
                            else
                                successMessage += $"\n\n⚠️ Byte de controle não modificado";

                            StatusMessage = $"Áudio '{item.FileName}' convertido!";
                            MessageBox.Show(successMessage, "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    }
                    finally
                    {
                        if (File.Exists(tempOutputPath))
                            File.Delete(tempOutputPath);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusMessage = "Erro ao converter áudio";
            }
        }

        public void ReplaceRawFile(PakItem item)
        {
            try
            {
                var dialog = new OpenFileDialog
                {
                    Filter = "RAW Files (*.raw)|*.raw|All Files (*.*)|*.*",
                    Title = $"Selecione o arquivo para substituir {item.FileName}"
                };

                if (dialog.ShowDialog() == true)
                {
                    var newFileInfo = new FileInfo(dialog.FileName);
                    long bytesToWrite = Math.Min(newFileInfo.Length, item.Size);
                    bool willBeTruncated = newFileInfo.Length > item.Size;

                    string message = $"Substituir '{item.FileName}'?\n\n" +
                                   $"Tamanho original: {item.Size:N0} bytes\n" +
                                   $"Tamanho novo: {newFileInfo.Length:N0} bytes\n" +
                                   $"Bytes a gravar: {bytesToWrite:N0} bytes";

                    if (willBeTruncated)
                        message += $"\n\n⚠️ TRUNCADO!\nBytes perdidos: {(newFileInfo.Length - item.Size):N0}";

                    var result = MessageBox.Show(message, "Confirmar", MessageBoxButton.YesNo,
                        willBeTruncated ? MessageBoxImage.Warning : MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        ReplaceFileInPakTruncated(dialog.FileName, item);
                        item.Status = willBeTruncated ? "Truncado" : "Modificado";
                        StatusMessage = $"Arquivo '{item.FileName}' substituído!";

                        if (willBeTruncated)
                        {
                            MessageBox.Show(
                                $"Substituído com truncamento!\n\nGravados: {bytesToWrite:N0} bytes\nPerdidos: {(newFileInfo.Length - item.Size):N0} bytes",
                                "Concluído", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void ExtractRawFile(PakItem item)
        {
            try
            {
                var dialog = new SaveFileDialog
                {
                    Filter = "RAW Files (*.raw)|*.raw|All Files (*.*)|*.*",
                    FileName = item.FileName,
                    Title = $"Extrair {item.FileName}"
                };

                if (dialog.ShowDialog() == true)
                {
                    ExtractFileFromPak(dialog.FileName, item);
                    StatusMessage = $"Arquivo '{item.FileName}' extraído!";
                    MessageBox.Show(
                        $"Extraído com sucesso!\n\nLocalização: {dialog.FileName}\nTamanho: {item.Size:N0} bytes",
                        "Concluído", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ReplaceFileInPak(string newRawFilePath, PakItem item)
        {
            using (var pakStream = new FileStream(PakFilePath, FileMode.Open, FileAccess.ReadWrite))
            {
                pakStream.Seek(item.OffsetStart, SeekOrigin.Begin);

                var newFileData = File.ReadAllBytes(newRawFilePath);
                int bytesToWrite = (int)Math.Min(newFileData.Length, item.Size);
                pakStream.Write(newFileData, 0, bytesToWrite);

                if (bytesToWrite < item.Size)
                {
                    var padding = new byte[item.Size - bytesToWrite];
                    pakStream.Write(padding, 0, padding.Length);
                }
            }
        }

        private void ReplaceFileInPakTruncated(string newRawFilePath, PakItem item)
        {
            using (var pakStream = new FileStream(PakFilePath, FileMode.Open, FileAccess.ReadWrite))
            {
                pakStream.Seek(item.OffsetStart, SeekOrigin.Begin);

                var newFileData = File.ReadAllBytes(newRawFilePath);
                int bytesToWrite = (int)Math.Min(newFileData.Length, item.Size);
                pakStream.Write(newFileData, 0, bytesToWrite);

                if (bytesToWrite < item.Size)
                {
                    var padding = new byte[item.Size - bytesToWrite];
                    pakStream.Write(padding, 0, padding.Length);
                }
            }
        }

        private void ExtractFileFromPak(string outputPath, PakItem item)
        {
            using (var pakStream = new FileStream(PakFilePath, FileMode.Open, FileAccess.Read))
            {
                pakStream.Seek(item.OffsetStart, SeekOrigin.Begin);

                var buffer = new byte[item.Size];
                pakStream.Read(buffer, 0, (int)item.Size);

                File.WriteAllBytes(outputPath, buffer);
            }
        }

        public string PrepareAudioForPlayback(PakItem item)
        {
            try
            {
                StatusMessage = $"Preparando reprodução...";

                string fileId = ExtractFileId(item.FileName);
                int sampleRate = -1;

                if (!string.IsNullOrEmpty(fileId))
                {
                    byte[] searchPattern = ReverseEndian(fileId);
                    if (searchPattern != null)
                    {
                        using (var pakStream = new FileStream(PakFilePath, FileMode.Open, FileAccess.Read))
                        {
                            byte[] buffer = new byte[pakStream.Length];
                            pakStream.Read(buffer, 0, buffer.Length);

                            for (long i = 0; i <= buffer.Length - searchPattern.Length - 3; i++)
                            {
                                bool found = true;
                                for (int j = 0; j < searchPattern.Length; j++)
                                {
                                    if (buffer[i + j] != searchPattern[j])
                                    {
                                        found = false;
                                        break;
                                    }
                                }

                                if (found)
                                {
                                    long signatureOffset = FindSignatureOffset(buffer, i, 32);
                                    if (signatureOffset >= 0)
                                    {
                                        sampleRate = ExtractSampleRate(buffer, signatureOffset);
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }

                if (sampleRate <= 0)
                {
                    MessageBox.Show("Sample rate não detectada.", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                    return null;
                }

                _currentTempPath = Path.Combine(Path.GetTempPath(), $"audio_{Guid.NewGuid()}.temp");
                _currentVagPath = Path.Combine(Path.GetTempPath(), $"audio_{Guid.NewGuid()}.vag");
                _currentWavPath = _currentVagPath + ".wav";

                using (var pakStream = new FileStream(PakFilePath, FileMode.Open, FileAccess.Read))
                {
                    pakStream.Seek(item.OffsetStart, SeekOrigin.Begin);
                    byte[] rawData = new byte[item.Size];
                    pakStream.Read(rawData, 0, (int)item.Size);
                    File.WriteAllBytes(_currentTempPath, rawData);
                }

                byte[] vagHeader = new byte[]
                {
                    0x56, 0x41, 0x47, 0x70, 0x00, 0x00, 0x00, 0x03,
                    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x1F, 0xE0,
                    0x00, 0x00, 0x5D, 0xC0, 0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                    0x72, 0x72, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
                };

                long fileSize = item.Size;
                vagHeader[0x0C] = (byte)((fileSize >> 24) & 0xFF);
                vagHeader[0x0D] = (byte)((fileSize >> 16) & 0xFF);
                vagHeader[0x0E] = (byte)((fileSize >> 8) & 0xFF);
                vagHeader[0x0F] = (byte)(fileSize & 0xFF);

                vagHeader[0x10] = (byte)((sampleRate >> 24) & 0xFF);
                vagHeader[0x11] = (byte)((sampleRate >> 16) & 0xFF);
                vagHeader[0x12] = (byte)((sampleRate >> 8) & 0xFF);
                vagHeader[0x13] = (byte)(sampleRate & 0xFF);

                using (var vagFile = new FileStream(_currentVagPath, FileMode.Create, FileAccess.Write))
                {
                    vagFile.Write(vagHeader, 0, vagHeader.Length);
                    byte[] rawData = File.ReadAllBytes(_currentTempPath);
                    vagFile.Write(rawData, 0, rawData.Length);
                }

                string vgmstreamPath = GetVgmStreamPath();

                if (!File.Exists(vgmstreamPath))
                {
                    MessageBox.Show(
                        $"vgmstream-cli.exe não encontrado:\n{_applicationDirectory}\n\nColoque o vgmstream-cli.exe na mesma pasta do executável.",
                        "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                    return null;
                }

                var processInfo = new ProcessStartInfo
                {
                    FileName = vgmstreamPath,
                    Arguments = $"\"{_currentVagPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (var process = Process.Start(processInfo))
                {
                    process.WaitForExit();
                    if (process.ExitCode != 0)
                    {
                        MessageBox.Show("Erro ao converter para WAV.", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                        return null;
                    }
                }

                if (File.Exists(_currentWavPath))
                {
                    CurrentPlayingFile = item.FileName;
                    CurrentSampleRate = sampleRate;
                    IsPlayerVisible = true;
                    StatusMessage = $"Reproduzindo '{item.FileName}'...";
                    return _currentWavPath;
                }

                return null;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
        }

        public void CleanupAudioFiles()
        {
            try
            {
                if (!string.IsNullOrEmpty(_currentTempPath) && File.Exists(_currentTempPath))
                    File.Delete(_currentTempPath);
                
                if (!string.IsNullOrEmpty(_currentVagPath) && File.Exists(_currentVagPath))
                    File.Delete(_currentVagPath);
                
                if (!string.IsNullOrEmpty(_currentWavPath) && File.Exists(_currentWavPath))
                    File.Delete(_currentWavPath);

                _currentTempPath = null;
                _currentVagPath = null;
                _currentWavPath = null;

                Debug.WriteLine("[CLEANUP] Arquivos temp removidos");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERRO CLEANUP] {ex.Message}");
            }
        }

        public void BatchConvertAndReplace()
        {
            try
            {
                string mfAudioPath = GetMFAudioPath();

                if (!File.Exists(mfAudioPath))
                {
                    MessageBox.Show(
                        $"MFAudio.exe não encontrado:\n{_applicationDirectory}\n\nColoque o MFAudio.exe na mesma pasta do executável.",
                        "Erro",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                var dialog = new OpenFileDialog
                {
                    Filter = "Audio Files (*.wav)|*.wav|All Files (*.*)|*.*",
                    Title = "Selecione os arquivos WAV para substituição em lote",
                    Multiselect = true
                };

                if (dialog.ShowDialog() == true && dialog.FileNames.Length > 0)
                {
                    int successCount = 0;
                    int failCount = 0;
                    int skippedCount = 0;
                    var processedFiles = new StringBuilder();

                    StatusMessage = $"Processando {dialog.FileNames.Length} arquivo(s)...";

                    var allItems = new List<PakItem>();
                    foreach (var categoryItems in _categorizedItems.Values)
                    {
                        allItems.AddRange(categoryItems);
                    }

                    foreach (string wavFilePath in dialog.FileNames)
                    {
                        string wavFileName = Path.GetFileNameWithoutExtension(wavFilePath);
                        
                        PakItem matchingItem = null;
                        foreach (var item in allItems)
                        {
                            string itemBaseName = item.FileName;
                            
                            if (itemBaseName.EndsWith(".raw", StringComparison.OrdinalIgnoreCase))
                                itemBaseName = itemBaseName.Substring(0, itemBaseName.Length - 4);
                            
                            int parenthesisIndex = itemBaseName.IndexOf(" (");
                            if (parenthesisIndex > 0)
                                itemBaseName = itemBaseName.Substring(0, parenthesisIndex);
                        
                            if (itemBaseName.Equals(wavFileName, StringComparison.OrdinalIgnoreCase))
                            {
                                matchingItem = item;
                                break;
                            }
                        }

                        if (matchingItem == null)
                        {
                            skippedCount++;
                            processedFiles.AppendLine($"⚠️ Ignorado: {wavFileName} (sem correspondência no PAK)");
                            continue;
                        }

                        try
                        {
                            string fileId = ExtractFileId(matchingItem.FileName);
                            int sampleRate = -1;

                            if (!string.IsNullOrEmpty(fileId))
                            {
                                byte[] searchPattern = ReverseEndian(fileId);
                                if (searchPattern != null)
                                {
                                    using (var pakStream = new FileStream(PakFilePath, FileMode.Open, FileAccess.Read))
                                    {
                                        byte[] buffer = new byte[pakStream.Length];
                                        pakStream.Read(buffer, 0, buffer.Length);

                                        for (long i = 0; i <= buffer.Length - searchPattern.Length - 3; i++)
                                        {
                                            bool found = true;
                                            for (int j = 0; j < searchPattern.Length; j++)
                                            {
                                                if (buffer[i + j] != searchPattern[j])
                                                {
                                                    found = false;
                                                    break;
                                                }
                                            }

                                            if (found)
                                            {
                                                long signatureOffset = FindSignatureOffset(buffer, i, 32);
                                                if (signatureOffset >= 0)
                                                {
                                                    sampleRate = ExtractSampleRate(buffer, signatureOffset);
                                                    break;
                                                }
                                            }
                                        }
                                    }
                                }
                            }

                            if (sampleRate <= 0)
                            {
                                failCount++;
                                processedFiles.AppendLine($"❌ Falha: {wavFileName} (sample rate não detectada)");
                                continue;
                            }

                            string tempOutputPath = Path.Combine(Path.GetTempPath(), $"batch_{Guid.NewGuid()}.raw");

                            try
                            {
                                string arguments = $"\"{wavFilePath}\" \"{tempOutputPath}\" /OTRAWC /OC1 /OF{sampleRate}";

                                var processInfo = new ProcessStartInfo
                                {
                                    FileName = mfAudioPath,
                                    Arguments = arguments,
                                    UseShellExecute = false,
                                    CreateNoWindow = true,
                                    RedirectStandardOutput = true,
                                    RedirectStandardError = true
                                };

                                using (var process = Process.Start(processInfo))
                                {
                                    process.WaitForExit();

                                    if (process.ExitCode != 0)
                                    {
                                        failCount++;
                                        processedFiles.AppendLine($"❌ Falha: {wavFileName} (erro na conversão)");
                                        continue;
                                    }
                                }

                                if (!File.Exists(tempOutputPath))
                                {
                                    failCount++;
                                    processedFiles.AppendLine($"❌ Falha: {wavFileName} (arquivo não criado)");
                                    continue;
                                }

                                ReplaceFileInPakTruncated(tempOutputPath, matchingItem);
                                FindAndModifyByte(fileId);

                                var convertedFileInfo = new FileInfo(tempOutputPath);
                                bool wasTruncated = convertedFileInfo.Length > matchingItem.Size;
                                
                                matchingItem.Status = wasTruncated ? "Truncado" : "Convertido";
                                
                                successCount++;
                                processedFiles.AppendLine($"✅ Sucesso: {wavFileName} → {matchingItem.FileName}");

                                File.Delete(tempOutputPath);
                            }
                            catch
                            {
                                if (File.Exists(tempOutputPath))
                                    File.Delete(tempOutputPath);
                                throw;
                            }
                        }
                        catch (Exception ex)
                        {
                            failCount++;
                            processedFiles.AppendLine($"❌ Erro: {wavFileName} ({ex.Message})");
                        }
                    }

                    StatusMessage = $"Lote concluído: {successCount} sucesso(s), {failCount} falha(s), {skippedCount} ignorado(s)";

                    MessageBox.Show(
                        $"Processamento em lote concluído!\n\n" +
                        $"✅ Sucesso: {successCount}\n" +
                        $"❌ Falhas: {failCount}\n" +
                        $"⚠️ Ignorados: {skippedCount}\n\n" +
                        $"Detalhes:\n{processedFiles}",
                        "Substituição em Lote",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro no processamento em lote: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusMessage = "Erro no processamento em lote";
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}