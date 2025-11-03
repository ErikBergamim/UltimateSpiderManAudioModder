using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace WpfApp1.Models
{
    public class WbkMetadata
    {
        [JsonPropertyName("file")]
        public string File { get; set; }

        [JsonPropertyName("total_size")]
        public long TotalSize { get; set; }

        [JsonPropertyName("offsets")]
        public Dictionary<string, WbkMapping> Offsets { get; set; }
    }

    public class WbkMapping
    {
        [JsonPropertyName("start_offset")]
        public long StartOffset { get; set; }

        [JsonPropertyName("end_offset")]
        public long EndOffset { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("hash")]
        public string Hash { get; set; }

        [JsonPropertyName("original_name")]
        public string OriginalName { get; set; }
    }

    public class WbkItem : INotifyPropertyChanged
    {
        private string _status = "Original";

        public string FileName { get; set; }
        public string Hash { get; set; }
        public long StartOffset { get; set; }
        public long EndOffset { get; set; }
        public long Size => EndOffset - StartOffset;
        public string SizeDisplay => $"{Size:N0} bytes";
        public string OffsetDisplay => $"0x{StartOffset:X8} - 0x{EndOffset:X8}";
        
        // Nome completo com hash para exibição
        public string DisplayName => !string.IsNullOrEmpty(Hash) ? $"{FileName} ({Hash})" : FileName;

        public string Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged(nameof(Status));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}