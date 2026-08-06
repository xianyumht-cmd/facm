using System;

namespace FACM.Models
{
    internal enum CleanupItemState
    {
        Found,
        Missing,
        Blocked,
        Deleted,
        Failed
    }

    internal sealed class CleanupItem
    {
        public string DisplayName { get; set; }
        public string Path { get; set; }
        public long EstimatedBytes { get; set; }
        public CleanupItemState State { get; set; }
        public string Detail { get; set; }
        public bool IsGameDirectoryItem { get; set; }

        public string SizeText
        {
            get
            {
                if (EstimatedBytes <= 0) return "—";
                string[] units = { "B", "KB", "MB", "GB", "TB" };
                double size = EstimatedBytes;
                var index = 0;
                while (size >= 1024 && index < units.Length - 1)
                {
                    size /= 1024;
                    index++;
                }
                return string.Format(index == 0 ? "{0:0} {1}" : "{0:0.##} {1}", size, units[index]);
            }
        }
    }
}
