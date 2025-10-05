using System;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Waterjam.Core.Systems.Console;

namespace Waterjam.Core.Services.Network.Repositories;

/// <summary>
/// Downloads mods from GitHub releases.
/// Supports versioned releases and automatic latest version resolution.
/// </summary>
public class GitHubModRepository : IModRepository
{
    private static readonly System.Net.Http.HttpClient _httpClient = new System.Net.Http.HttpClient();
    private const string GITHUB_API = "https://api.github.com";

    public string RepositoryId => "github";
    public string DisplayName => "GitHub Releases";
    public int Priority => 50; // Medium priority

    static GitHubModRepository()
    {
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Waterjam-ModLoader");
    }

    public bool CanHandle(ModRequirementInfo requirement)
    {
        // Can handle if repository type is github or URL is github.com
        return requirement.RepositoryType == "github" ||
               (requirement.DownloadUrl?.Contains("github.com") == true) ||
               requirement.Metadata.ContainsKey("github_repo");
    }

    public async Task<ModDownloadResult> DownloadModAsync(
        ModRequirementInfo requirement,
        IProgress<ModDownloadProgress> progress)
    {
        var result = new ModDownloadResult();

        try
        {
            // Parse GitHub repository info
            var (owner, repo) = ParseGitHubInfo(requirement);

            if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(repo))
            {
                result.Error = "Invalid GitHub repository information";
                return result;
            }

            ConsoleSystem.Log(
                $"[GitHubRepo] Downloading {requirement.Id} from {owner}/{repo}",
                ConsoleChannel.Network
            );

            // Get release by version or latest
            var releaseUrl = string.IsNullOrEmpty(requirement.Version) || requirement.Version == "latest"
                ? $"{GITHUB_API}/repos/{owner}/{repo}/releases/latest"
                : $"{GITHUB_API}/repos/{owner}/{repo}/releases/tags/{requirement.Version}";

            var releaseResponse = await _httpClient.GetStringAsync(releaseUrl);
            var release = JsonSerializer.Deserialize<GitHubRelease>(releaseResponse);

            if (release == null || release.assets == null || release.assets.Length == 0)
            {
                result.Error = "No assets found in release";
                return result;
            }

            // Find the mod asset (usually first .zip file)
            var asset = Array.Find(release.assets, a => a.name.EndsWith(".zip"));

            if (asset == null)
            {
                result.Error = "No .zip asset found in release";
                return result;
            }

            // Download the asset
            progress?.Report(new ModDownloadProgress
            {
                ModId = requirement.Id,
                Status = "Downloading from GitHub",
                BytesDownloaded = 0,
                TotalBytes = asset.size
            });

            using var response = await _httpClient.GetAsync(
                asset.browser_download_url,
                HttpCompletionOption.ResponseHeadersRead
            );

            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync();
            using var ms = new System.IO.MemoryStream();

            var buffer = new byte[8192];
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await ms.WriteAsync(buffer, 0, bytesRead);
                totalRead += bytesRead;

                progress?.Report(new ModDownloadProgress
                {
                    ModId = requirement.Id,
                    Status = "Downloading from GitHub",
                    BytesDownloaded = totalRead,
                    TotalBytes = asset.size
                });
            }

            var content = ms.ToArray();

            // Save to temp
            var tempPath = Godot.ProjectSettings.GlobalizePath($"user://temp/{requirement.Id}.zip");
            var tempDir = System.IO.Path.GetDirectoryName(tempPath);
            System.IO.Directory.CreateDirectory(tempDir);

            await System.IO.File.WriteAllBytesAsync(tempPath, content);

            result.Success = true;
            result.LocalPath = tempPath;
            result.BytesDownloaded = content.Length;
            result.ActualChecksum = ComputeSHA256(content);

            ConsoleSystem.Log(
                $"[GitHubRepo] Downloaded {requirement.Id} v{release.tag_name}: {content.Length} bytes",
                ConsoleChannel.Network
            );

            return result;
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            ConsoleSystem.LogErr(
                $"[GitHubRepo] Download failed for {requirement.Id}: {ex.Message}",
                ConsoleChannel.Network
            );
            return result;
        }
    }

    public async Task<bool> VerifyIntegrityAsync(string localPath, byte[] expectedChecksum)
    {
        try
        {
            var content = await System.IO.File.ReadAllBytesAsync(localPath);
            var actualChecksum = ComputeSHA256(content);
            return actualChecksum.SequenceEqual(expectedChecksum);
        }
        catch
        {
            return false;
        }
    }

    public async Task<ModMetadata> GetMetadataAsync(string modId)
    {
        // TODO: Parse from GitHub repository metadata
        return await Task.FromResult<ModMetadata>(null);
    }

    private (string owner, string repo) ParseGitHubInfo(ModRequirementInfo requirement)
    {
        // Try metadata first
        if (requirement.Metadata.TryGetValue("github_repo", out var repoPath))
        {
            var parts = repoPath.Split('/');
            if (parts.Length >= 2)
            {
                return (parts[0], parts[1]);
            }
        }

        // Try parsing from URL
        if (!string.IsNullOrEmpty(requirement.DownloadUrl))
        {
            var uri = new Uri(requirement.DownloadUrl);
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 2)
            {
                return (segments[0], segments[1]);
            }
        }

        return (null, null);
    }

    private byte[] ComputeSHA256(byte[] data)
    {
        using var sha256 = SHA256.Create();
        return sha256.ComputeHash(data);
    }

    // GitHub API response models
    private class GitHubRelease
    {
        public string tag_name { get; set; }
        public string name { get; set; }
        public GitHubAsset[] assets { get; set; }
    }

    private class GitHubAsset
    {
        public string name { get; set; }
        public long size { get; set; }
        public string browser_download_url { get; set; }
    }
}