using System.Net.Http;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace MinecraftLauncher.Services;

public class TranslationService
{
    private readonly HttpClient _httpClient;
    private const string TRANSLATION_API = "https://api.mymemory.translated.net/get";

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