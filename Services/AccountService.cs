using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using MinecraftLauncher.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MinecraftLauncher
{
    public class AccountService
    {
        private static readonly Lazy<AccountService> _instance = new(() => new AccountService());
        public static AccountService Instance => _instance.Value;
        
        private readonly string _accountFilePath;
        private AccountData _accountData;
        private readonly HttpClient _httpClient;
        
        // 微软 OAuth 配置
        private const string CLIENT_ID = "YOUR_CLIENT_ID"; // 需要替换为实际的 Client ID
        private const string DEVICE_CODE_URL = "https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode";
        private const string TOKEN_URL = "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";
        private const string XBOX_AUTH_URL = "https://user.auth.xboxlive.com/user/authenticate";
        private const string XSTS_AUTH_URL = "https://xsts.auth.xboxlive.com/xsts/authorize";
        private const string MC_AUTH_URL = "https://api.minecraftservices.com/authentication/login_with_xbox";
        private const string MC_PROFILE_URL = "https://api.minecraftservices.com/minecraft/profile";

        private AccountService()
        {
            var launcherDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NovoLauncher");
            if (!Directory.Exists(launcherDir))
            {
                Directory.CreateDirectory(launcherDir);
            }
            _accountFilePath = Path.Combine(launcherDir, "accounts.json");
            _accountData = LoadAccounts();
            _httpClient = new HttpClient();
        }

        private AccountData LoadAccounts()
        {
            if (File.Exists(_accountFilePath))
            {
                var json = File.ReadAllText(_accountFilePath);
                return JsonConvert.DeserializeObject<AccountData>(json) ?? new AccountData();
            }
            return new AccountData();
        }

        private void SaveAccounts()
        {
            var json = JsonConvert.SerializeObject(_accountData, Formatting.Indented);
            File.WriteAllText(_accountFilePath, json);
        }

        public List<Account> GetAccounts()
        {
            return _accountData.Accounts;
        }

        public int GetSelectedIndex()
        {
            return _accountData.SelectedIndex;
        }

        public void SetSelectedIndex(int index)
        {
            _accountData.SelectedIndex = index;
            SaveAccounts();
        }

        public void AddAccount(Account account)
        {
            _accountData.Accounts.Add(account);
            if (_accountData.SelectedIndex == -1)
            {
                _accountData.SelectedIndex = 0;
            }
            SaveAccounts();
        }

        public void RemoveAccount(int index)
        {
            if (index >= 0 && index < _accountData.Accounts.Count)
            {
                _accountData.Accounts.RemoveAt(index);
                if (_accountData.SelectedIndex >= _accountData.Accounts.Count)
                {
                    _accountData.SelectedIndex = _accountData.Accounts.Count > 0 ? 0 : -1;
                }
                SaveAccounts();
            }
        }

        public Account? GetSelectedAccount()
        {
            if (_accountData.SelectedIndex >= 0 && _accountData.SelectedIndex < _accountData.Accounts.Count)
            {
                return _accountData.Accounts[_accountData.SelectedIndex];
            }
            return null;
        }

        public async Task<Account?> MicrosoftLoginAsync()
        {
            // 获取设备代码
            var deviceCode = await GetDeviceCodeAsync();
            
            // 复制到剪贴板并打开浏览器
            Clipboard.SetText(deviceCode.UserCode);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = deviceCode.VerificationUri,
                UseShellExecute = true
            });
            
            // 轮询验证
            var account = await PollForTokenAsync(deviceCode.DeviceCode);
            return account;
        }

        public async Task<DeviceCodeResponse> GetDeviceCodeAsync()
        {
            if (CLIENT_ID == "YOUR_CLIENT_ID")
            {
                throw new Exception("请先配置 Client ID！\n\n请打开 AccountService.cs 文件，\n将 CLIENT_ID 替换为你在 Azure 注册的 Client ID。\n\n详见 LOGIN_SETUP.md");
            }
            
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("client_id", CLIENT_ID),
                new KeyValuePair<string, string>("scope", "XboxLive.signin offline_access")
            });
            
            var response = await _httpClient.PostAsync(DEVICE_CODE_URL, content);
            var json = await response.Content.ReadAsStringAsync();
            
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"请求失败：{response.StatusCode}\n{json}");
            }
            
            var result = JsonConvert.DeserializeObject<DeviceCodeResponse>(json);
            
            if (result == null)
            {
                throw new Exception("无法解析设备代码响应");
            }
            
            return result;
        }

        public async Task<Account?> VerifyDeviceCodeAsync(string deviceCode)
        {
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("client_id", CLIENT_ID),
                new KeyValuePair<string, string>("device_code", deviceCode),
                new KeyValuePair<string, string>("grant_type", "urn:ietf:params:oauth:grant-type:device_code")
            });
            
            var response = await _httpClient.PostAsync(TOKEN_URL, content);
            var json = await response.Content.ReadAsStringAsync();
            
            if (!response.IsSuccessStatusCode)
            {
                var error = JsonConvert.DeserializeObject<DeviceCodeError>(json);
                if (error?.Error == "authorization_pending")
                {
                    return null; // 等待用户完成登录
                }
                throw new Exception($"登录失败：{error?.ErrorDescription ?? "未知错误"}");
            }
            
            var tokenResult = JsonConvert.DeserializeObject<TokenResponse>(json);
            if (tokenResult == null)
            {
                throw new Exception("无法解析令牌响应");
            }
            
            // 使用 Xbox Live 认证
            var xboxAccount = await AuthenticateWithXboxAsync(tokenResult.AccessToken);
            return xboxAccount;
        }

        private async Task<Account?> PollForTokenAsync(string deviceCode)
        {
            var interval = TimeSpan.FromSeconds(5);
            var expiresAt = DateTime.UtcNow.AddSeconds(900); // 15 分钟过期
            
            while (DateTime.UtcNow < expiresAt)
            {
                var account = await VerifyDeviceCodeAsync(deviceCode);
                if (account != null)
                {
                    return account;
                }
                
                await Task.Delay(interval);
            }
            
            throw new TimeoutException("登录超时");
        }

        private async Task<Account> AuthenticateWithXboxAsync(string accessToken)
        {
            // Xbox Live 认证
            var xboxRequest = new
            {
                Properties = new
                {
                    AuthMethod = "RPS",
                    SiteName = "user.auth.xboxlive.com",
                    RpsTicket = $"d={accessToken}"
                },
                RelyingParty = "http://auth.xboxlive.com",
                TokenType = "JWT"
            };
            
            var xboxContent = new StringContent(
                JsonConvert.SerializeObject(xboxRequest),
                Encoding.UTF8,
                "application/json"
            );
            
            var xboxResponse = await _httpClient.PostAsync(XBOX_AUTH_URL, xboxContent);
            var xboxJson = await xboxResponse.Content.ReadAsStringAsync();
            var xboxResult = JObject.Parse(xboxJson);
            var xboxToken = xboxResult["Token"]?.ToString();
            var xboxClaims = xboxResult["DisplayClaims"]?["xui"]?[0];
            var uhs = xboxClaims?["uhs"]?.ToString();
            
            if (string.IsNullOrEmpty(xboxToken) || string.IsNullOrEmpty(uhs))
            {
                throw new Exception("Xbox 认证失败");
            }
            
            // XSTS 认证
            var xstsRequest = new
            {
                Properties = new
                {
                    SandboxId = "RETAIL",
                    UserTokens = new[] { xboxToken }
                },
                RelyingParty = "rp://api.minecraftservices.com/",
                TokenType = "JWT"
            };
            
            var xstsContent = new StringContent(
                JsonConvert.SerializeObject(xstsRequest),
                Encoding.UTF8,
                "application/json"
            );
            
            var xstsResponse = await _httpClient.PostAsync(XSTS_AUTH_URL, xstsContent);
            var xstsJson = await xstsResponse.Content.ReadAsStringAsync();
            var xstsResult = JObject.Parse(xstsJson);
            var xstsToken = xstsResult["Token"]?.ToString();
            var xstsClaims = xstsResult["DisplayClaims"]?["xui"]?[0];
            var xstsUhs = xstsClaims?["uhs"]?.ToString();
            
            if (string.IsNullOrEmpty(xstsToken) || string.IsNullOrEmpty(xstsUhs))
            {
                throw new Exception("XSTS 认证失败");
            }
            
            if (uhs != xstsUhs)
            {
                throw new Exception("UHS 不匹配");
            }
            
            // Minecraft 认证
            var mcRequest = new
            {
                identityToken = $"XBL3.0 x={uhs};{xstsToken}"
            };
            
            var mcContent = new StringContent(
                JsonConvert.SerializeObject(mcRequest),
                Encoding.UTF8,
                "application/json"
            );
            
            var mcResponse = await _httpClient.PostAsync(MC_AUTH_URL, mcContent);
            var mcJson = await mcResponse.Content.ReadAsStringAsync();
            var mcResult = JObject.Parse(mcJson);
            var mcAccessToken = mcResult["access_token"]?.ToString();
            
            if (string.IsNullOrEmpty(mcAccessToken))
            {
                throw new Exception("Minecraft 认证失败");
            }
            
            // 获取 Minecraft 档案
            var profileRequest = new HttpRequestMessage(HttpMethod.Get, MC_PROFILE_URL);
            profileRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", mcAccessToken);
            var profileResponse = await _httpClient.SendAsync(profileRequest);
            var profileJson = await profileResponse.Content.ReadAsStringAsync();
            
            if (!profileResponse.IsSuccessStatusCode)
            {
                throw new Exception("您的微软账号没有购买 Minecraft");
            }
            
            var profileResult = JObject.Parse(profileJson);
            var playerName = profileResult["name"]?.ToString();
            var playerUuid = profileResult["id"]?.ToString();
            
            if (string.IsNullOrEmpty(playerName) || string.IsNullOrEmpty(playerUuid))
            {
                throw new Exception("无法获取玩家档案");
            }
            
            // 返回账号信息
            return new Account
            {
                Type = "microsoft",
                Name = playerName,
                Uuid = playerUuid,
                AccessToken = mcAccessToken,
                RefreshToken = "", // 实际应该保存 refresh_token
                PlayerType = "msa",
                Auth = ""
            };
        }
    }

    public class DeviceCodeResponse
    {
        [JsonProperty("device_code")]
        public string DeviceCode { get; set; } = "";
        
        [JsonProperty("user_code")]
        public string UserCode { get; set; } = "";
        
        [JsonProperty("verification_uri")]
        public string VerificationUri { get; set; } = "";
        
        [JsonProperty("verification_uri_complete")]
        public string? VerificationUriComplete { get; set; }
        
        [JsonProperty("expires_in")]
        public int ExpiresIn { get; set; }
        
        [JsonProperty("interval")]
        public int Interval { get; set; }
        
        [JsonProperty("message")]
        public string? Message { get; set; }
    }

    public class DeviceCodeError
    {
        [JsonProperty("error")]
        public string Error { get; set; } = "";
        
        [JsonProperty("error_description")]
        public string ErrorDescription { get; set; } = "";
    }

    public class TokenResponse
    {
        [JsonProperty("access_token")]
        public string AccessToken { get; set; } = "";
        
        [JsonProperty("refresh_token")]
        public string RefreshToken { get; set; } = "";
        
        [JsonProperty("expires_in")]
        public int ExpiresIn { get; set; }
    }
}
