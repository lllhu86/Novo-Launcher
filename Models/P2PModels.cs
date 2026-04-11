namespace MinecraftLauncher.Models;

public class P2PSettings
{
    public string SignalingServerUrl { get; set; } = "wss://signaling.example.com";
    public string StunServerUrl { get; set; } = "stun:stun.l.google.com:19302";
    public string? TurnServerUrl { get; set; }
    public string? TurnUsername { get; set; }
    public string? TurnPassword { get; set; }
    public int LocalProxyPort { get; set; } = 25565;
}

public class RoomInfo
{
    public string RoomId { get; set; } = string.Empty;
    public string RoomName { get; set; } = string.Empty;
    public string HostId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public List<string> PeerIds { get; set; } = new();
}

public class PeerInfo
{
    public string PeerId { get; set; } = string.Empty;
    public string? PublicIp { get; set; }
    public int? PublicPort { get; set; }
    public string? LocalIp { get; set; }
    public int? LocalPort { get; set; }
}

public class ConnectionTestResult
{
    public bool Success { get; set; }
    public string? PublicIp { get; set; }
    public string? NatType { get; set; }
    public string? ErrorMessage { get; set; }
}

public class SignalingMessage
{
    public string Type { get; set; } = string.Empty;
    public string? RoomId { get; set; }
    public string? PeerId { get; set; }
    public string? TargetPeerId { get; set; }
    public string? Data { get; set; }
    public string? RoomName { get; set; }
    public RoomInfo? RoomInfo { get; set; }
    public PeerInfo? PeerInfo { get; set; }
}
