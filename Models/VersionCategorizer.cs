using System;
using System.Collections.Generic;

namespace MinecraftLauncher.Models
{
    public class CategorizedVersions
    {
        public List<VersionInfo> Releases { get; set; } = new();
        public List<VersionInfo> Snapshots { get; set; } = new();
        public List<VersionInfo> OldVersions { get; set; } = new();
        public List<VersionInfo> AprilFools { get; set; } = new();
    }

    public static class VersionCategorizer
    {
        private static readonly HashSet<string> AprilFoolsVersions = new()
        {
            "3D Shareware v1.34",
            "20w14infinite",
            "22w13oneblockatatime"
        };

        private static readonly Version OldVersionThreshold = new(1, 12, 2);

        public static CategorizedVersions Categorize(List<VersionInfo> versions)
        {
            var categorized = new CategorizedVersions();

            if (versions == null || versions.Count == 0)
                return categorized;

            foreach (var version in versions)
            {
                // 检查是否是愚人节版本
                if (IsAprilFoolsVersion(version.Id))
                {
                    categorized.AprilFools.Add(version);
                    continue;
                }

                // 检查是否是正式版（release 类型）
                if (version.Type == "release")
                {
                    if (IsOldVersion(version.Id))
                    {
                        categorized.OldVersions.Add(version);
                    }
                    else
                    {
                        categorized.Releases.Add(version);
                    }
                }
                // 检查是否是快照/预览版
                else if (version.Type == "snapshot" || version.Type == "pending" || version.Type == "old_beta" || version.Type == "old_alpha")
                {
                    categorized.Snapshots.Add(version);
                }
                // 其他类型（如 forge、fabric 等）不加入分类列表
            }

            // 排序
            categorized.Releases.Sort((a, b) => CompareVersions(b.Id, a.Id));
            categorized.Snapshots.Sort((a, b) => CompareVersions(b.Id, a.Id));
            categorized.OldVersions.Sort((a, b) => CompareVersions(b.Id, a.Id));
            categorized.AprilFools.Sort((a, b) => CompareVersions(b.Id, a.Id));

            return categorized;
        }

        private static bool IsAprilFoolsVersion(string versionId)
        {
            return AprilFoolsVersions.Contains(versionId) || 
                   versionId.Contains("infinite") || 
                   versionId.Contains("oneblockatatime");
        }

        private static bool IsOldVersion(string versionId)
        {
            try
            {
                // 提取版本号
                var version = ParseVersion(versionId);
                return version != null && version < OldVersionThreshold;
            }
            catch
            {
                return false;
            }
        }

        private static Version? ParseVersion(string versionId)
        {
            // 处理类似 "1.20.1" 的版本号
            if (Version.TryParse(versionId, out var version))
            {
                return version;
            }

            // 处理类似 "20w14infinite" 的快照版本
            return null;
        }

        private static int CompareVersions(string? a, string? b)
        {
            if (a == null && b == null) return 0;
            if (a == null) return -1;
            if (b == null) return 1;

            var versionA = ParseVersion(a);
            var versionB = ParseVersion(b);

            if (versionA != null && versionB != null)
            {
                return versionA.CompareTo(versionB);
            }

            // 如果无法解析，按字符串比较
            return string.Compare(a, b, StringComparison.Ordinal);
        }
    }
}
