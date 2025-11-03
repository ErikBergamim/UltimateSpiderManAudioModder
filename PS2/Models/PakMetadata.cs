using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace WpfApp1.Models
{
    public class PakMetadata
    {
        [JsonPropertyName("mappings")]
        public List<FileMapping> Mappings { get; set; }
    }

    public class FileMapping
    {
        [JsonPropertyName("raw_file")]
        public string RawFile { get; set; }

        [JsonPropertyName("raw_size")]
        public long RawSize { get; set; }

        [JsonPropertyName("pak_offset_start")]
        public long PakOffsetStart { get; set; }

        [JsonPropertyName("pak_offset_end")]
        public long PakOffsetEnd { get; set; }
    }
}