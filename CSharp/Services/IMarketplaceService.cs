using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Launcher.Services
{
    public interface IMarketplaceService
    {
        Task<MarketplaceSearchResult> SearchProjectsAsync(
            string query, 
            string? projectType = null, 
            List<string>? versions = null, 
            string? loader = null, 
            List<string>? categories = null,
            int offset = 0, 
            int limit = 20, 
            CancellationToken cancellationToken = default);

        Task<MarketplaceProject> GetProjectAsync(string projectId, CancellationToken cancellationToken = default);
        Task<List<MarketplaceVersion>> GetProjectVersionsAsync(string projectId, CancellationToken cancellationToken = default);
    }

    public class MarketplaceSearchResult
    {
        public List<MarketplaceProjectHeader> Hits { get; set; } = new();
        public int TotalHits { get; set; }
        public int Offset { get; set; }
        public int Limit { get; set; }
    }

    public class MarketplaceProjectHeader
    {
        public string ProjectId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string IconUrl { get; set; } = string.Empty;
        public List<string> Categories { get; set; } = new();
        public string Author { get; set; } = string.Empty;
        public int Downloads { get; set; }
        public int Followers { get; set; }
        public string LatestVersion { get; set; } = string.Empty;
    }

    public class MarketplaceProject
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string IconUrl { get; set; } = string.Empty;
        public string SourceUrl { get; set; } = string.Empty;
        public string IssuesUrl { get; set; } = string.Empty;
        public string WikiUrl { get; set; } = string.Empty;
        public List<string> Categories { get; set; } = new();
        public List<string> Gallery { get; set; } = new();
        public int Downloads { get; set; }
    }

    public class MarketplaceVersion
    {
        public string Id { get; set; } = string.Empty;
        public string VersionNumber { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string VersionType { get; set; } = "release"; // release, beta, alpha
        public DateTime DatePublished { get; set; }
        public List<string> GameVersions { get; set; } = new();
        public List<string> Loaders { get; set; } = new();
        public List<MarketplaceFile> Files { get; set; } = new();
    }

    public class MarketplaceFile
    {
        public string Url { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string Hash { get; set; } = string.Empty; // SHA-1
        public int Size { get; set; }
    }
}
