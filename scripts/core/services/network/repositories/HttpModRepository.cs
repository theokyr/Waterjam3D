using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Godot;
using Waterjam.Core.Systems.Console;

namespace Waterjam.Core.Services.Network.Repositories;

/// <summary>
/// Downloads mods from direct HTTP/HTTPS URLs.
/// Simple fallback repository for custom hosting.
/// </summary>
public class HttpModRepository : IModRepository
{
    private static readonly System.Net.Http.HttpClient _httpClient = new System.Net.Http.HttpClient();

    public string RepositoryId => "http";
    public string DisplayName => "HTTP Download";
    public int Priority => 100; // Low priority (fallback)

    public bool CanHandle(ModRequirementInfo requirement)
    {
        // Can handle if there's a valid HTTP URL
        return !string.IsNullOrEmpty(requirement.DownloadUrl) &&
               (requirement.DownloadUrl.StartsWith("http://") ||
                requirement.DownloadUrl.StartsWith("https://"));
    }

    public async Task<ModDownloadResult> DownloadModAsync(
        ModRequirementInfo requirement,
        IProgress<ModDownloadProgress> progress)
    {
        var result = new ModDownloadResult();

        try
        {
            ConsoleSystem.Log(
                $"[HttpRepo] Downloading {requirement.Id} from {requirement.DownloadUrl}",
                ConsoleChannel.Network
            );

            progress?.Report(new ModDownloadProgress
            {
                ModId = requirement.Id,
                Status = "Downloading",
                BytesDownloaded = 0,
                TotalBytes = (long)requirement.SizeBytes
            });

            // Download with progress tracking
            using var response = await _httpClient.GetAsync(
                requirement.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead
            );

            if (!response.IsSuccessStatusCode)
            {
                result.Error = $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}";
                return result;
            }

            var contentLength = response.Content.Headers.ContentLength ?? (long)requirement.SizeBytes;

            using var stream = await response.Content.ReadAsStreamAsync();
            using var ms = new MemoryStream();

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
                    Status = "Downloading",
                    BytesDownloaded = totalRead,
                    TotalBytes = (long)contentLength
                });
            }

            var content = ms.ToArray();

            // Save to temp location
            var tempPath = ProjectSettings.GlobalizePath($"user://temp/{requirement.Id}.zip");
            var tempDir = System.IO.Path.GetDirectoryName(tempPath);
            System.IO.Directory.CreateDirectory(tempDir);

            await System.IO.File.WriteAllBytesAsync(tempPath, content);

            result.Success = true;
            result.LocalPath = tempPath;
            result.BytesDownloaded = content.Length;
            result.ActualChecksum = ComputeSHA256(content);

            ConsoleSystem.Log(
                $"[HttpRepo] Downloaded {requirement.Id}: {content.Length} bytes",
                ConsoleChannel.Network
            );

            return result;
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            ConsoleSystem.LogErr(
                $"[HttpRepo] Download failed for {requirement.Id}: {ex.Message}",
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

    public Task<ModMetadata> GetMetadataAsync(string modId)
    {
        // HTTP repository doesn't support metadata queries
        return Task.FromResult<ModMetadata>(null);
    }

    private byte[] ComputeSHA256(byte[] data)
    {
        using var sha256 = SHA256.Create();
        return sha256.ComputeHash(data);
    }
}