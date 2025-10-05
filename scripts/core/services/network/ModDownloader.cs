using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Waterjam.Events;
using Waterjam.Core.Systems.Console;
using Waterjam.Core.Services.Modular;
using Waterjam.Core.Services.Network.Repositories;

namespace Waterjam.Core.Services.Network;

/// <summary>
/// Handles downloading and installing mods from multiple sources.
/// Supports Steam Workshop, GitHub, mod.io, and direct HTTP.
/// </summary>
public partial class ModDownloader : BaseService,
    IGameEventHandler<NetworkModSyncResponseEvent>
{
    private readonly List<IModRepository> _repositories = new();
    private readonly Dictionary<string, ModDownloadResult> _downloadCache = new();

    [Export]
    public bool AutoDownloadEnabled { get; set; } = true;

    [Export]
    public bool RequireUserConsent { get; set; } = true;

    public override void _Ready()
    {
        base._Ready();

        // Register repositories in priority order
        RegisterRepositories();

        RegisterConsoleCommands();
        ConsoleSystem.Log("[ModDownloader] Initialized", ConsoleChannel.Network);
    }

    private void RegisterRepositories()
    {
        // High priority: Official platforms
        var steamRepo = new SteamWorkshopRepository();
        steamRepo.Initialize();
        _repositories.Add(steamRepo);

        var modioRepo = new ModIoRepository(
            apiKey: GetModIoApiKey(),
            gameId: GetModIoGameId()
        );
        _repositories.Add(modioRepo);

        // Medium priority: GitHub releases
        _repositories.Add(new GitHubModRepository());

        // Low priority: Direct HTTP (fallback)
        _repositories.Add(new HttpModRepository());

        // Sort by priority
        _repositories.Sort((a, b) => a.Priority.CompareTo(b.Priority));

        ConsoleSystem.Log(
            $"[ModDownloader] Registered {_repositories.Count} repositories",
            ConsoleChannel.Network
        );
    }

    public void OnGameEvent(NetworkModSyncResponseEvent evt)
    {
        if (evt.RequiredMods == null || evt.RequiredMods.Count == 0)
        {
            // No mods required, connection can proceed
            return;
        }

        // Check if we need to download mods
        var missingMods = evt.RequiredMods.Where(mod => !IsModInstalled(mod)).ToList();

        if (missingMods.Count == 0)
        {
            ConsoleSystem.Log("[ModDownloader] All required mods already installed", ConsoleChannel.Network);
            return;
        }

        if (RequireUserConsent)
        {
            ShowModSyncDialog(missingMods);
        }
        else if (AutoDownloadEnabled)
        {
            _ = DownloadModsAsync(missingMods);
        }
    }

    private void ShowModSyncDialog(List<ModRequirement> mods)
    {
        // Create dialog
        var dialog = new AcceptDialog();
        dialog.Title = "Server Requires Mods";

        var text = "The server requires the following mods:\n\n";

        foreach (var mod in mods)
        {
            var sizeMB = mod.SizeBytes / 1024.0 / 1024.0;
            text += $"• {mod.Id} v{mod.Version} ({sizeMB:F1} MB)\n";
        }

        text += $"\nTotal size: {mods.Sum(m => (long)m.SizeBytes) / 1024.0 / 1024.0:F1} MB\n";
        text += "\nDownload and install these mods?";

        dialog.DialogText = text;
        dialog.GetOkButton().Text = "Download";

        dialog.Confirmed += async () => await DownloadModsAsync(mods);
        dialog.Canceled += () =>
        {
            ConsoleSystem.Log("[ModDownloader] User declined mod download, disconnecting", ConsoleChannel.Network);
            GetNode<NetworkService>("/root/NetworkService")?.Disconnect();
        };

        GetTree().Root.CallDeferred("add_child", dialog);
        dialog.CallDeferred("popup_centered");
    }

    /// <summary>
    /// Download and install multiple mods
    /// </summary>
    public async Task<bool> DownloadModsAsync(List<ModRequirement> mods)
    {
        ConsoleSystem.Log($"[ModDownloader] Starting download of {mods.Count} mods", ConsoleChannel.Network);

        var results = new List<ModDownloadResult>();

        foreach (var mod in mods)
        {
            var requirement = new ModRequirementInfo
            {
                Id = mod.Id,
                Version = mod.Version,
                Checksum = mod.ToByteArray(),
                DownloadUrl = mod.DownloadUrl,
                SizeBytes = mod.SizeBytes,
                RepositoryType = mod.RepositoryType,
                RepositoryId = mod.RepositoryId,
                Metadata = mod.Metadata ?? new Dictionary<string, string>()
            };

            var result = await DownloadModAsync(requirement);
            results.Add(result);
        }

        var successCount = results.Count(r => r.Success);

        if (successCount == mods.Count)
        {
            ConsoleSystem.Log($"[ModDownloader] All {mods.Count} mods downloaded successfully", ConsoleChannel.Network);

            // Reload mod registry
            await ReloadModsAsync();

            // Reconnect to server
            RetryConnection();

            return true;
        }
        else
        {
            ConsoleSystem.LogErr(
                $"[ModDownloader] {mods.Count - successCount} mods failed to download",
                ConsoleChannel.Network
            );
            return false;
        }
    }

    /// <summary>
    /// Download a single mod using available repositories
    /// </summary>
    public async Task<ModDownloadResult> DownloadModAsync(ModRequirementInfo requirement)
    {
        // Check cache
        if (_downloadCache.TryGetValue(requirement.Id, out var cached))
        {
            return cached;
        }

        ConsoleSystem.Log($"[ModDownloader] Downloading mod: {requirement.Id}", ConsoleChannel.Network);

        // Try each repository in priority order
        foreach (var repo in _repositories)
        {
            if (!repo.CanHandle(requirement))
                continue;

            ConsoleSystem.Log(
                $"[ModDownloader] Trying {repo.DisplayName} for {requirement.Id}",
                ConsoleChannel.Network
            );

            var progress = new Progress<ModDownloadProgress>(p =>
            {
                if (p.Percentage % 10 < 0.1) // Log every 10%
                {
                    ConsoleSystem.Log(
                        $"[ModDownloader] {p.ModId}: {p.Percentage:F0}% ({p.Status})",
                        ConsoleChannel.Network
                    );
                }
            });

            var result = await repo.DownloadModAsync(requirement, progress);

            if (result.Success)
            {
                // Verify integrity
                if (requirement.Checksum != null && requirement.Checksum.Length > 0)
                {
                    var valid = await repo.VerifyIntegrityAsync(result.LocalPath, requirement.Checksum);

                    if (!valid)
                    {
                        ConsoleSystem.LogErr(
                            $"[ModDownloader] Checksum verification failed for {requirement.Id}",
                            ConsoleChannel.Network
                        );
                        result.Success = false;
                        result.Error = "Checksum mismatch";
                        continue; // Try next repository
                    }
                }

                // Install mod
                await InstallModAsync(result.LocalPath, requirement.Id);

                _downloadCache[requirement.Id] = result;
                return result;
            }
        }

        // All repositories failed
        var failResult = new ModDownloadResult
        {
            Success = false,
            Error = "No repository could download this mod"
        };

        ConsoleSystem.LogErr(
            $"[ModDownloader] Failed to download {requirement.Id} from any source",
            ConsoleChannel.Network
        );

        return failResult;
    }

    private async Task InstallModAsync(string zipPath, string modId)
    {
        try
        {
            var modPath = ProjectSettings.GlobalizePath($"user://mods/{modId}/");

            // Create directory
            System.IO.Directory.CreateDirectory(modPath);

            // Extract zip
            ConsoleSystem.Log($"[ModDownloader] Extracting {modId}...", ConsoleChannel.Network);

            using (var archive = ZipFile.OpenRead(zipPath))
            {
                archive.ExtractToDirectory(modPath, overwriteFiles: true);
            }

            ConsoleSystem.Log($"[ModDownloader] Installed {modId} to {modPath}", ConsoleChannel.Network);

            // Clean up temp file
            System.IO.File.Delete(zipPath);
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr(
                $"[ModDownloader] Failed to install {modId}: {ex.Message}",
                ConsoleChannel.Network
            );
        }
    }

    private bool IsModInstalled(ModRequirement mod)
    {
        // Check if mod exists in user://mods/ or res://mods/
        var userModPath = ProjectSettings.GlobalizePath($"user://mods/{mod.Id}/");
        var resModPath = ProjectSettings.GlobalizePath($"res://mods/{mod.Id}/");

        return System.IO.Directory.Exists(userModPath) || System.IO.Directory.Exists(resModPath);
    }

    private async Task ReloadModsAsync()
    {
        ConsoleSystem.Log("[ModDownloader] Reloading mod registry...", ConsoleChannel.Network);

        var registry = SystemRegistry.Instance;
        if (registry != null)
        {
            // Rediscover mods (will pick up newly installed ones)
            // await registry.DiscoverModSystemsAsync();
        }
    }

    private void RetryConnection()
    {
        ConsoleSystem.Log("[ModDownloader] Mods installed, connection can proceed", ConsoleChannel.Network);

        // Dispatch event to allow connection to proceed
        GameEvent.DispatchGlobal(new ModSyncCompleteEvent(true));
    }

    private string GetModIoApiKey()
    {
        // TODO: Load from settings or environment
        return null; // Not configured yet
    }

    private string GetModIoGameId()
    {
        // TODO: Load from project settings
        return null; // Not configured yet
    }

    private void RegisterConsoleCommands()
    {
        var consoleSystem = GetNodeOrNull<ConsoleSystem>("/root/ConsoleSystem");
        if (consoleSystem == null) return;

        consoleSystem.RegisterCommand(new ConsoleCommand(
            "mod_download",
            "Download a mod by ID",
            "mod_download <mod_id> [version] [repository]",
            async (args) =>
            {
                if (args.Length == 0)
                {
                    ConsoleSystem.Log("Usage: mod_download <mod_id> [version] [repository]", ConsoleChannel.Network);
                    return false;
                }

                var modId = args[0];
                var version = args.Length > 1 ? args[1] : "latest";
                var repoType = args.Length > 2 ? args[2] : null;

                var requirement = new ModRequirementInfo
                {
                    Id = modId,
                    Version = version,
                    RepositoryType = repoType
                };

                var result = await DownloadModAsync(requirement);

                if (result.Success)
                {
                    ConsoleSystem.Log($"Successfully downloaded {modId}", ConsoleChannel.Network);
                    return true;
                }
                else
                {
                    ConsoleSystem.LogErr($"Failed to download {modId}: {result.Error}", ConsoleChannel.Network);
                    return false;
                }
            }));

        consoleSystem.RegisterCommand(new ConsoleCommand(
            "mod_repositories",
            "List available mod repositories",
            "mod_repositories",
            async (args) =>
            {
                ConsoleSystem.Log("=== Mod Repositories ===", ConsoleChannel.Network);
                foreach (var repo in _repositories)
                {
                    ConsoleSystem.Log(
                        $"{repo.DisplayName} (Priority: {repo.Priority}, ID: {repo.RepositoryId})",
                        ConsoleChannel.Network
                    );
                }

                return true;
            }));
    }
}

// Additional event for mod sync completion
public record ModSyncCompleteEvent(bool Success) : IGameEvent;

// Extension to ModRequirement for repository support
public static class ModRequirementExtensions
{
    public static string RepositoryType { get; set; }
    public static string RepositoryId { get; set; }
    public static Dictionary<string, string> Metadata { get; set; }
}