using System.Net.Http;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace MinecraftLauncher.Services;

public class TranslationService
{
    private readonly HttpClient _httpClient;
    private const string TRANSLATION_API = "https://api.mymemory.translated.net/get";

    private static readonly Dictionary<string, string> MinecraftGlossary = new(StringComparer.OrdinalIgnoreCase)
    {
        { "shader", "光影" },
        { "shaders", "光影" },
        { "mod", "模组" },
        { "mods", "模组" },
        { "modpack", "整合包" },
        { "modpacks", "整合包" },
        { "resource pack", "资源包" },
        { "resource packs", "资源包" },
        { "texture pack", "材质包" },
        { "texture packs", "材质包" },
        { "seed", "种子" },
        { "biome", "生物群系" },
        { "biomes", "生物群系" },
        { "chunk", "区块" },
        { "chunks", "区块" },
        { "mob", "生物" },
        { "mobs", "生物" },
        { "spawn", "生成" },
        { "spawning", "生成" },
        { "crafting", "合成" },
        { "smelting", "烧炼" },
        { "enchanting", "附魔" },
        { "enchantment", "附魔" },
        { "enchantments", "附魔" },
        { "potion", "药水" },
        { "potions", "药水" },
        { "redstone", "红石" },
        { "nether", "下界" },
        { "the nether", "下界" },
        { "end", "末地" },
        { "the end", "末地" },
        { "ender dragon", "末影龙" },
        { "wither", "凋灵" },
        { "creeper", "苦力怕" },
        { "enderman", "末影人" },
        { "villager", "村民" },
        { "iron golem", "铁傀儡" },
        { "render distance", "渲染距离" },
        { "game mode", "游戏模式" },
        { "survival", "生存" },
        { "creative", "创造" },
        { "adventure", "冒险" },
        { "spectator", "旁观" },
        { "hardcore", "极限" },
        { "datapack", "数据包" },
        { "datapacks", "数据包" },
        { "screenshot", "截图" },
        { "skin", "皮肤" },
        { "cape", "披风" },
        { "world", "世界" },
        { "save", "存档" },
        { "saves", "存档" },
        { "inventory", "物品栏" },
        { "hotbar", "快捷栏" },
        { "experience", "经验" },
        { "xp", "经验" },
        { "durability", "耐久" },
        { "durability", "耐久度" },
        { "block", "方块" },
        { "blocks", "方块" },
        { "item", "物品" },
        { "items", "物品" },
        { "armor", "盔甲" },
        { "weapon", "武器" },
        { "tool", "工具" },
        { "tools", "工具" },
        { "food", "食物" },
        { "hunger", "饥饿值" },
        { "health", "生命值" },
        { "mana", "魔力" },
        { "boss", "Boss" },
        { "dungeon", "地牢" },
        { "stronghold", "要塞" },
        { "temple", "神殿" },
        { "village", "村庄" },
        { "ocean monument", "海底神殿" },
        { "woodland mansion", "林地府邸" },
        { "bastion", "堡垒遗迹" },
        { "ruined portal", "废弃传送门" },
        { "nether fortress", "下界要塞" },
        { "end city", "末地城" },
        { "performance", "性能" },
        { "fps", "帧率" },
        { "lag", "卡顿" },
        { "tick", "刻" },
        { "ticks", "刻" },
        { "chunk loading", "区块加载" },
        { "memory", "内存" },
        { "ram", "内存" },
        { "java", "Java" },
        { "launcher", "启动器" },
        { "forge", "Forge" },
        { "fabric", "Fabric" },
        { "quilt", "Quilt" },
        { "optifine", "OptiFine" },
        { "sodium", "Sodium" },
        { "lithium", "Lithium" },
        { "iris", "Iris" },
        { "flywheel", "Flywheel" },
        { "create", "机械动力" },
        { "applied energistics", "应用能源" },
        { "jei", "JEI" },
        { "just enough items", "JEI" },
        { "rei", "Roughly Enough Items" },
        { "roughly enough items", "REI" }
    };

    public TranslationService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "NMCL/1.0");
    }

    public async Task<string> TranslateToChineseAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        try
        {
            var cleanText = StripHtmlTags(text);
            if (string.IsNullOrWhiteSpace(cleanText))
                return text;

            cleanText = ApplyGlossaryPreTranslation(cleanText);

            if (cleanText.Length > 500)
            {
                cleanText = cleanText.Substring(0, 500);
            }

            var langPair = "en|zh-CN";
            var url = $"{TRANSLATION_API}?q={Uri.EscapeDataString(cleanText)}&langpair={langPair}";

            App.LogInfo($"翻译请求：{cleanText.Substring(0, Math.Min(100, cleanText.Length))}...");

            var response = await _httpClient.GetStringAsync(url);
            var result = JsonConvert.DeserializeObject<TranslationResponse>(response);

            if (result?.ResponseStatus == 200 && !string.IsNullOrEmpty(result.ResponseData?.TranslatedText))
            {
                var translated = result.ResponseData.TranslatedText;
                translated = ApplyGlossaryPostTranslation(translated);
                App.LogInfo($"翻译结果：{translated}");

                if (translated.Equals(cleanText, StringComparison.OrdinalIgnoreCase))
                {
                    App.LogInfo("翻译API返回原文，尝试强制翻译...");
                    return await ForceTranslateToChineseAsync(cleanText);
                }

                return translated;
            }

            App.LogInfo($"翻译失败，状态码：{result?.ResponseStatus}");
            return text;
        }
        catch (Exception ex)
        {
            App.LogError($"翻译失败：{ex.Message}", ex);
            return text;
        }
    }

    private string ApplyGlossaryPreTranslation(string text)
    {
        foreach (var kvp in MinecraftGlossary)
        {
            text = System.Text.RegularExpressions.Regex.Replace(
                text,
                $@"\b{kvp.Key}\b",
                $"【{kvp.Value}】",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        return text;
    }

    private string ApplyGlossaryPostTranslation(string text)
    {
        foreach (var kvp in MinecraftGlossary)
        {
            text = text.Replace($"【{kvp.Value}】", kvp.Value);
        }
        return text;
    }

    private async Task<string> ForceTranslateToChineseAsync(string text)
    {
        try
        {
            var words = text.Split(' ');
            if (words.Length <= 10)
            {
                var url = $"{TRANSLATION_API}?q={Uri.EscapeDataString(text)}&langpair=en%7Czh-CN&defered=true";
                var response = await _httpClient.GetStringAsync(url);
                var result = JsonConvert.DeserializeObject<TranslationResponse>(response);
                if (result?.ResponseStatus == 200 && !string.IsNullOrEmpty(result.ResponseData?.TranslatedText))
                {
                    var translated = result.ResponseData.TranslatedText;
                    if (!translated.Equals(text, StringComparison.OrdinalIgnoreCase))
                    {
                        return translated;
                    }
                }
            }

            var fallbackUrl = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=en&tl=zh-CN&dt=t&q={Uri.EscapeDataString(text)}";
            var fallbackResponse = await _httpClient.GetStringAsync(fallbackUrl);
            if (!string.IsNullOrEmpty(fallbackResponse))
            {
                var jsonArray = JsonConvert.DeserializeObject<List<dynamic>>(fallbackResponse);
                if (jsonArray != null && jsonArray.Count > 0)
                {
                    var translatedText = "";
                    foreach (var item in jsonArray[0])
                    {
                        translatedText += item.ToString();
                    }
                    if (!string.IsNullOrEmpty(translatedText))
                    {
                        App.LogInfo($"Google翻译备用结果：{translatedText}");
                        return translatedText;
                    }
                }
            }

            return text;
        }
        catch (Exception ex)
        {
            App.LogError($"强制翻译失败：{ex.Message}", ex);
            return text;
        }
    }

    public async Task<string> TranslateAsync(string text, string sourceLang, string targetLang)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        try
        {
            var cleanText = StripHtmlTags(text);
            if (string.IsNullOrWhiteSpace(cleanText))
                return text;

            if (cleanText.Length > 500)
            {
                cleanText = cleanText.Substring(0, 500);
            }

            var langPair = $"{sourceLang}|{targetLang}";
            var url = $"{TRANSLATION_API}?q={Uri.EscapeDataString(cleanText)}&langpair={langPair}";

            var response = await _httpClient.GetStringAsync(url);
            var result = JsonConvert.DeserializeObject<TranslationResponse>(response);

            if (result?.ResponseStatus == 200 && !string.IsNullOrEmpty(result.ResponseData?.TranslatedText))
            {
                return result.ResponseData.TranslatedText;
            }

            return text;
        }
        catch (Exception ex)
        {
            App.LogError($"翻译失败：{ex.Message}", ex);
            return text;
        }
    }

    private string StripHtmlTags(string html)
    {
        if (string.IsNullOrEmpty(html))
            return html;

        var text = Regex.Replace(html, "<[^>]+>", " ");
        text = Regex.Replace(text, @"&nbsp;", " ");
        text = Regex.Replace(text, @"&amp;", "&");
        text = Regex.Replace(text, @"&lt;", "<");
        text = Regex.Replace(text, @"&gt;", ">");
        text = Regex.Replace(text, @"&quot;", "\"");
        text = Regex.Replace(text, @"&#39;", "'");
        text = Regex.Replace(text, @"\s+", " ");
        return text.Trim();
    }
}

public class TranslationResponse
{
    [JsonProperty("responseData")]
    public TranslationData ResponseData { get; set; }

    [JsonProperty("responseStatus")]
    public int ResponseStatus { get; set; }
}

public class TranslationData
{
    [JsonProperty("translatedText")]
    public string TranslatedText { get; set; }

    [JsonProperty("match")]
    public double Match { get; set; }
}