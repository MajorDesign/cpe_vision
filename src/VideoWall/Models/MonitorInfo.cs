namespace VideoWall.Models
{
    public class MonitorInfo
    {
        public int Index { get; set; }
        public string DeviceName { get; set; } = string.Empty;
        public bool IsPrimary { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int WorkAreaX { get; set; }
        public int WorkAreaY { get; set; }
        public int WorkAreaWidth { get; set; }
        public int WorkAreaHeight { get; set; }

        public string Resolution => $"{Width}x{Height}";
        public string Position => $"({X}, {Y})";
    }
}
