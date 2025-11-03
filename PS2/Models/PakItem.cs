using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WpfApp1.Models
{
    public class PakItem : INotifyPropertyChanged
    {
        private string _status = "Original";

        public string FileName { get; set; }
        public long Size { get; set; }
        public long OffsetStart { get; set; }
        public long OffsetEnd { get; set; }

        public string Status
        {
            get => _status;
            set
            {
                _status = value;
                OnPropertyChanged();
            }
        }

        public string SizeDisplay => $"{Size:N0} bytes";
        public string OffsetDisplay => $"0x{OffsetStart:X8} - 0x{OffsetEnd:X8}";

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}