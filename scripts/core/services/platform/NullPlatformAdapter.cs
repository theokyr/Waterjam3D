namespace Waterjam.Core.Services.Platform;

/// <summary>
/// Null platform adapter providing no-op implementations. Useful as default/fallback.
/// </summary>
public class NullPlatformAdapter : IPlatformAdapter
{
    private sealed class NullCloudStorage : ICloudStorage
    {
        public bool IsAvailable => false;
        public bool Save(string filename, byte[] data) => false;
        public byte[] Load(string filename) => null;
    }

    private sealed class NullAchievements : IAchievementPlatform
    {
        public bool IsAvailable => false;
        public bool Unlock(string achievementId) => false;
        public bool SetStat(string statName, int value) => false;
    }

    public string PlatformName => "Null";
    public ICloudStorage Cloud { get; } = new NullCloudStorage();
    public IAchievementPlatform Achievements { get; } = new NullAchievements();
}


