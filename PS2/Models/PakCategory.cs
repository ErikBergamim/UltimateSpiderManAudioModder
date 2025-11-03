namespace WpfApp1.Models
{
    public class PakCategory
    {
        public string Name { get; set; }
        public int ItemCount { get; set; }

        public string DisplayName => $"{Name} ({ItemCount})";
    }
}