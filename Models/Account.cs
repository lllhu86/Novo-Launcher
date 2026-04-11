namespace MinecraftLauncher.Models
{
    public class Account
    {
        public string Type { get; set; } = "offline"; // "offline" or "microsoft"
        public string Name { get; set; } = "";
        public string Uuid { get; set; } = "";
        public string AccessToken { get; set; } = ""; // 对于微软登录
        public string RefreshToken { get; set; } = ""; // 对于微软登录
        public string PlayerType { get; set; } = "Legacy"; // "Legacy" for offline, "msa" for microsoft
        public string Auth { get; set; } = ""; // 用于 Authlib-Injector
    }

    public class AccountData
    {
        public List<Account> Accounts { get; set; } = new();
        public int SelectedIndex { get; set; } = -1;
    }
}
