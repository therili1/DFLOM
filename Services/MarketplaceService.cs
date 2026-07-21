using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Launcher.Services
{
    /// <summary>
    /// Реальна інтеграція з публічним Modrinth API v2 (https://docs.modrinth.com/api)
    /// для пошуку модів, ресурспаків та шейдерів.
    /// </summary>
    public class MarketplaceService : IMarketplaceService
    {
        private const string BaseUrl = "https://api.modrinth.com/v2/";
        private readonly HttpClient _httpClient;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public MarketplaceService()
        {
            _httpClient = new HttpClient { BaseAddress = new Uri(BaseUrl), Timeout = TimeSpan.FromSeconds(20) };
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SkyLightLauncher/1.0 (+https://github.com)");
        }

        public async Task<MarketplaceSearchResult> SearchProjectsAsync(
            string query,
            string? projectType = null,
            List<string>? versions = null,
            string? loader = null,
            List<string>? categories = null,
            int offset = 0,
            int limit = 20,
            CancellationToken cancellationToken = default)
        {
            // Modrinth facets: список OR-груп, самі групи об'єднуються через AND.
            // Тобто "версії X OR Y" AND "категорія A OR B" AND "тип проєкту mod".
            var facets = new List<List<string>>();

            if (!string.IsNullOrWhiteSpace(projectType) && projectType != "all")
            {
                facets.Add(new List<string> { $"project_type:{projectType}" });
            }

            if (versions != null && versions.Count > 0 && !versions.Contains("all"))
            {
                facets.Add(versions.Select(v => $"versions:{v}").ToList());
            }

            if (!string.IsNullOrWhiteSpace(loader) && loader != "all")
            {
                // Лоадер на Modrinth — це теж категорія (напр. "fabric", "forge").
                facets.Add(new List<string> { $"categories:{loader.ToLowerInvariant()}" });
            }

            if (categories != null && categories.Count > 0)
            {
                facets.Add(categories.Select(c => $"categories:{c.ToLowerInvariant()}").ToList());
            }

            var queryParams = new List<string>
            {
                $"query={Uri.EscapeDataString(query ?? string.Empty)}",
                $"offset={offset}",
                $"limit={limit}"
            };
            if (facets.Count > 0)
            {
                queryParams.Add($"facets={Uri.EscapeDataString(JsonSerializer.Serialize(facets))}");
            }

            var url = $"search?{string.Join("&", queryParams)}";
            var response = await _httpClient.GetFromJsonAsync<ModrinthSearchResponse>(url, JsonOptions, cancellationToken)
                           ?? new ModrinthSearchResponse();

            var result = new MarketplaceSearchResult
            {
                Offset = response.Offset,
                Limit = response.Limit,
                TotalHits = response.TotalHits
            };

            foreach (var hit in response.Hits)
            {
                result.Hits.Add(new MarketplaceProjectHeader
                {
                    ProjectId = hit.ProjectId ?? hit.Slug ?? string.Empty,
                    Title = hit.Title ?? string.Empty,
                    Description = hit.Description ?? string.Empty,
                    IconUrl = hit.IconUrl ?? string.Empty,
                    Categories = hit.Categories ?? new(),
                    Author = hit.Author ?? string.Empty,
                    Downloads = hit.Downloads,
                    Followers = hit.Follows,
                    LatestVersion = hit.LatestVersion ?? string.Empty
                });
            }

            return result;
        }

        public async Task<MarketplaceProject> GetProjectAsync(string projectId, CancellationToken cancellationToken = default)
        {
            var dto = await _httpClient.GetFromJsonAsync<ModrinthProject>($"project/{projectId}", JsonOptions, cancellationToken)
                       ?? throw new InvalidOperationException($"Проєкт {projectId} не знайдено на Modrinth.");

            return new MarketplaceProject
            {
                Id = dto.Id ?? projectId,
                Title = dto.Title ?? string.Empty,
                Description = dto.Description ?? string.Empty,
                Body = dto.Body ?? string.Empty,
                IconUrl = dto.IconUrl ?? string.Empty,
                SourceUrl = dto.SourceUrl ?? string.Empty,
                IssuesUrl = dto.IssuesUrl ?? string.Empty,
                WikiUrl = dto.WikiUrl ?? string.Empty,
                Categories = dto.Categories ?? new(),
                Gallery = dto.Gallery?.Where(g => !string.IsNullOrEmpty(g.Url)).Select(g => g.Url!).ToList() ?? new(),
                Downloads = dto.Downloads
            };
        }

        public async Task<List<MarketplaceVersion>> GetProjectVersionsAsync(string projectId, CancellationToken cancellationToken = default)
        {
            var dtos = await _httpClient.GetFromJsonAsync<List<ModrinthVersion>>($"project/{projectId}/version", JsonOptions, cancellationToken)
                       ?? new List<ModrinthVersion>();

            var result = new List<MarketplaceVersion>();
            foreach (var dto in dtos)
            {
                var version = new MarketplaceVersion
                {
                    Id = dto.Id ?? string.Empty,
                    VersionNumber = dto.VersionNumber ?? string.Empty,
                    Name = dto.Name ?? string.Empty,
                    VersionType = dto.VersionType ?? "release",
                    DatePublished = dto.DatePublished,
                    GameVersions = dto.GameVersions ?? new(),
                    Loaders = dto.Loaders ?? new()
                };

                foreach (var file in dto.Files ?? new())
                {
                    version.Files.Add(new MarketplaceFile
                    {
                        Url = file.Url ?? string.Empty,
                        FileName = file.Filename ?? string.Empty,
                        Hash = file.Hashes?.Sha1 ?? string.Empty,
                        Size = file.Size
                    });
                }

                result.Add(version);
            }

            // Найновіші версії зверху — так само, як на самому Modrinth.
            return result.OrderByDescending(v => v.DatePublished).ToList();
        }

        // ==== DTO-класи, що відповідають схемі Modrinth API ====

        private class ModrinthSearchResponse
        {
            [JsonPropertyName("hits")] public List<ModrinthSearchHit> Hits { get; set; } = new();
            [JsonPropertyName("offset")] public int Offset { get; set; }
            [JsonPropertyName("limit")] public int Limit { get; set; }
            [JsonPropertyName("total_hits")] public int TotalHits { get; set; }
        }

        private class ModrinthSearchHit
        {
            [JsonPropertyName("project_id")] public string? ProjectId { get; set; }
            [JsonPropertyName("slug")] public string? Slug { get; set; }
            [JsonPropertyName("title")] public string? Title { get; set; }
            [JsonPropertyName("description")] public string? Description { get; set; }
            [JsonPropertyName("icon_url")] public string? IconUrl { get; set; }
            [JsonPropertyName("categories")] public List<string>? Categories { get; set; }
            [JsonPropertyName("author")] public string? Author { get; set; }
            [JsonPropertyName("downloads")] public int Downloads { get; set; }
            [JsonPropertyName("follows")] public int Follows { get; set; }
            [JsonPropertyName("latest_version")] public string? LatestVersion { get; set; }
        }

        private class ModrinthProject
        {
            [JsonPropertyName("id")] public string? Id { get; set; }
            [JsonPropertyName("title")] public string? Title { get; set; }
            [JsonPropertyName("description")] public string? Description { get; set; }
            [JsonPropertyName("body")] public string? Body { get; set; }
            [JsonPropertyName("icon_url")] public string? IconUrl { get; set; }
            [JsonPropertyName("source_url")] public string? SourceUrl { get; set; }
            [JsonPropertyName("issues_url")] public string? IssuesUrl { get; set; }
            [JsonPropertyName("wiki_url")] public string? WikiUrl { get; set; }
            [JsonPropertyName("categories")] public List<string>? Categories { get; set; }
            [JsonPropertyName("gallery")] public List<ModrinthGalleryImage>? Gallery { get; set; }
            [JsonPropertyName("downloads")] public int Downloads { get; set; }
        }

        // Реальна структура елемента галереї Modrinth — це об'єкт з полями
        // url/title/description/featured тощо, а НЕ просто рядок з посиланням
        // (це і спричиняло помилку JSON-десеріалізації на проєктах з галереєю).
        private class ModrinthGalleryImage
        {
            [JsonPropertyName("url")] public string? Url { get; set; }
            [JsonPropertyName("title")] public string? Title { get; set; }
            [JsonPropertyName("featured")] public bool Featured { get; set; }
        }

        private class ModrinthVersion
        {
            [JsonPropertyName("id")] public string? Id { get; set; }
            [JsonPropertyName("version_number")] public string? VersionNumber { get; set; }
            [JsonPropertyName("name")] public string? Name { get; set; }
            [JsonPropertyName("version_type")] public string? VersionType { get; set; }
            [JsonPropertyName("date_published")] public DateTime DatePublished { get; set; }
            [JsonPropertyName("game_versions")] public List<string>? GameVersions { get; set; }
            [JsonPropertyName("loaders")] public List<string>? Loaders { get; set; }
            [JsonPropertyName("files")] public List<ModrinthFile>? Files { get; set; }
        }

        private class ModrinthFile
        {
            [JsonPropertyName("url")] public string? Url { get; set; }
            [JsonPropertyName("filename")] public string? Filename { get; set; }
            [JsonPropertyName("size")] public int Size { get; set; }
            [JsonPropertyName("hashes")] public ModrinthHashes? Hashes { get; set; }
        }

        private class ModrinthHashes
        {
            [JsonPropertyName("sha1")] public string? Sha1 { get; set; }
            [JsonPropertyName("sha512")] public string? Sha512 { get; set; }
        }
    }
}
