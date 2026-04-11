using System.Collections.Generic;

namespace MinecraftLauncher.Models
{
    public class AICommand
    {
        public string Action { get; set; } = "";
        public Dictionary<string, object> Parameters { get; set; } = new();
    }

    public class AICommandResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public object? Data { get; set; }
    }

    public class ResourceSearchResult
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Author { get; set; } = "";
        public int Downloads { get; set; }
        public string Version { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public string ResourceType { get; set; } = "";
        public string GameVersion { get; set; } = "";
        public string IconUrl { get; set; } = "";
    }

    public class ProactiveAlert
    {
        public string AlertType { get; set; } = "";
        public string Title { get; set; } = "";
        public string Message { get; set; } = "";
        public string Severity { get; set; } = "info";
        public List<AlertAction> Actions { get; set; } = new();
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    public class AlertAction
    {
        public string Label { get; set; } = "";
        public string Action { get; set; } = "";
        public Dictionary<string, object> Parameters { get; set; } = new();
    }
}
