using System;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Waterjam.Core.Systems.Console;

namespace Waterjam.Core.Services.Network.Repositories;

/// <summary>
/// Downloads mods from mod.io platform.
/// Supports OAuth, versioning, and rich metadata.
/// </summary>
public class ModIoRepository : IModRepository
{
    private static readonly System.Net.Http.HttpClient _httpClient = new System.Net.Http.HttpClient();
    private const string MODIO_API = "https://api.mod.io/v1";

    private string _apiKey;
    private string _gameId;

    public string RepositoryId => "modio";
    public string DisplayName => "mod.io";
    public int Priority => 20; // High priority (official platform)

    public ModIoRepository(string apiKey = null, string gameId = null)
    {
        _apiKey = apiKey;
        _gameId = gameId;

        if (!string.IsNullOrEmpty(_apiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
        }
    }

    public bool CanHandle(ModRequirementInfo requirement)
    {
        // Can handle if repository type is modio or has modio ID
        return requirement.RepositoryType == "modio" ||
               requirement.Metadata.ContainsKey("modio_id");
    }

    public async Task<ModDownloadResult> DownloadModAsync(
        ModRequirementInfo requirement,
        IProgress<ModDownloadProgress> progress)
    {
        var result = new ModDownloadResult();

        if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_gameId))
        {
            result.Error = "mod.io not configured (missing API key or game ID)";
            return result;
        }

        try
        {
            // Parse mod ID
            if (!requirement.Metadata.TryGetValue("modio_id", out var modIdStr) ||
                !int.TryParse(modIdStr, out var modId))
            {
                result.Error = "Invalid mod.io ID";
                return result;
            }

            ConsoleSystem.Log(
                $"[ModIoRepo] Downloading mod {modId} ({requirement.Id})",
                ConsoleChannel.Network
            );

            progress?.Report(new ModDownloadProgress
            {
                ModId = requirement.Id,
                Status = "Querying mod.io",
                BytesDownloaded = 0,
                TotalBytes = (long)requirement.SizeBytes
            });

            // Get mod files
            var filesUrl = $"{MODIO_API}/games/{_gameId}/mods/{modId}/files";
            var filesResponse = await _httpClient.GetStringAsync(filesUrl);
            var filesData = JsonSerializer.Deserialize<ModIoFilesResponse>(filesResponse);

            if (filesData?.data == null || filesData.data.Length == 0)
            {
                result.Error = "No files found for mod";
                return result;
            }

            // Find matching version or use latest
            var modFile = string.IsNullOrEmpty(requirement.Version)
                ? filesData.data[0]
                : Array.Find(filesData.data, f => f.version == requirement.Version) ?? filesData.data[0];

            if (modFile == null)
            {
                result.Error = $"Version {requirement.Version} not found";
                return result;
            }

            // Download the file
            progress?.Report(new ModDownloadProgress
            {
                ModId = requirement.Id,
                Status = "Downloading from mod.io",
                BytesDownloaded = 0,
                TotalBytes = modFile.filesize
            });

            using var response = await _httpClient.GetAsync(
                modFile.download.binary_url,
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
                    Status = "Downloading from mod.io",
                    BytesDownloaded = totalRead,
                    TotalBytes = modFile.filesize
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
                $"[ModIoRepo] Downloaded {requirement.Id}: {content.Length} bytes",
                ConsoleChannel.Network
            );

            return result;
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            ConsoleSystem.LogErr(
                $"[ModIoRepo] Download failed: {ex.Message}",
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
        if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_gameId))
            return null;

        try
        {
            if (!int.TryParse(modId, out var modIdInt))
                return null;

            var url = $"{MODIO_API}/games/{_gameId}/mods/{modIdInt}";
            var response = await _httpClient.GetStringAsync(url);
            var modData = JsonSerializer.Deserialize<ModIoMod>(response);

            if (modData == null) return null;

            return new ModMetadata
            {
                Id = modData.id.ToString(),
                Name = modData.name,
                Version = modData.modfile?.version ?? "unknown",
                Author = modData.submitted_by?.username ?? "Unknown",
                Description = modData.summary,
                SizeBytes = (ulong)(modData.modfile?.filesize ?? 0),
                UpdatedAt = DateTimeOffset.FromUnixTimeSeconds(modData.date_updated).DateTime,
                Tags = modData.tags?.Select(t => t.name).ToArray() ?? Array.Empty<string>(),
                Downloads = modData.stats?.downloads_total ?? 0,
                Rating = modData.stats?.ratings_weighted_aggregate ?? 0
            };
        }
        catch
        {
            return null;
        }
    }

    private byte[] ComputeSHA256(byte[] data)
    {
        using var sha256 = SHA256.Create();
        return sha256.ComputeHash(data);
    }

    // mod.io API response models
    private class ModIoFilesResponse
    {
        public ModIoFile[] data { get; set; }
    }

    private class ModIoFile
    {
        public int id { get; set; }
        public string version { get; set; }
        public long filesize { get; set; }
        public ModIoDownload download { get; set; }
    }

    private class ModIoDownload
    {
        public string binary_url { get; set; }
    }

    private class ModIoMod
    {
        public int id { get; set; }
        public string name { get; set; }
        public string summary { get; set; }
        public long date_updated { get; set; }
        public ModIoFile modfile { get; set; }
        public ModIoUser submitted_by { get; set; }
        public ModIoTag[] tags { get; set; }
        public ModIoStats stats { get; set; }
    }

    private class ModIoUser
    {
        public string username { get; set; }
    }

    private class ModIoTag
    {
        public string name { get; set; }
    }

    private class ModIoStats
    {
        public int downloads_total { get; set; }
        public float ratings_weighted_aggregate { get; set; }
    }
}