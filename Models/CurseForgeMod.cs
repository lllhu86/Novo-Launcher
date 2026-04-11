using Newtonsoft.Json;

namespace MinecraftLauncher.Models;

public class CurseForgeMod
{
    [JsonProperty("id")]
    public int Id { get; set; }

    [JsonProperty("gameId")]
    public int GameId { get; set; }

    [JsonProperty("name")]
    public string? Name { get; set; }

    [JsonProperty("slug")]
    public string? Slug { get; set; }

    [JsonProperty("links")]
    public ModLinks? Links { get; set; }

    [JsonProperty("summary")]
    public string? Summary { get; set; }

    [JsonProperty("downloadCount")]
    public int DownloadCount { get; set; }

    [JsonProperty("latestFiles")]
    public List<ModFile>? LatestFiles { get; set; }

    [JsonProperty("authors")]
    public List<ModAuthor>? Authors { get; set; }

    [JsonProperty("logo")]
    public ModAsset? Logo { get; set; }
}

public class ModLinks
{
    [JsonProperty("websiteUrl")]
    public string? WebsiteUrl { get; set; }
}

public class ModFile
{
    [JsonProperty("id")]
    public int Id { get; set; }

    [JsonProperty("displayName")]
    public string? DisplayName { get; set; }

    [JsonProperty("fileName")]
    public string? FileName { get; set; }

    [JsonProperty("downloadUrl")]
    public string? DownloadUrl { get; set; }

    [JsonProperty("gameVersions")]
    public List<string>? GameVersions { get; set; }

    [JsonProperty("modLoader")]
    public string? ModLoader { get; set; }

    [JsonProperty("fileLength")]
    public long FileLength { get; set; }
}

public class ModAuthor
{
    [JsonProperty("name")]
    public string? Name { get; set; }
}

public class ModAsset
{
    [JsonProperty("url")]
    public string? Url { get; set; }

    [JsonProperty("thumbnailUrl")]
    public string? ThumbnailUrl { get; set; }
}

public class CurseForgeSearchResult
{
    [JsonProperty("data")]
    public List<CurseForgeMod> Data { get; set; } = new();

    [JsonProperty("pagination")]
    public Pagination? Pagination { get; set; }
}

public class Pagination
{
    [JsonProperty("totalCount")]
    public int TotalCount { get; set; }

    [JsonProperty("index")]
    public int Index { get; set; }

    [JsonProperty("pageSize")]
    public int PageSize { get; set; }
}

public class ModLoaderType
{
    public string Name { get; set; } = "";
    public int Id { get; set; }
}
