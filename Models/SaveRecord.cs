using System;
using System.Collections.Generic;

namespace MinecraftLauncher.Models
{
    public class SaveRecord
    {
        public string SaveName { get; set; } = "";
        public string SavePath { get; set; } = "";
        public string GameVersion { get; set; } = "";
        public DateTime LastPlayedTime { get; set; }
        public TimeSpan TotalPlayTime { get; set; }
        public long TotalPlayTimeSeconds => (long)TotalPlayTime.TotalSeconds;
        
        public string GameMode { get; set; } = "未知";
        public string Difficulty { get; set; } = "未知";
        public string Seed { get; set; } = "";
        public string WorldType { get; set; } = "未知";
        public bool HasCheats { get; set; }
        public bool IsModded { get; set; }
        
        public double PlayerX { get; set; }
        public double PlayerY { get; set; }
        public double PlayerZ { get; set; }
        public int Dimension { get; set; }
        public long GameTimeTicks { get; set; }
        public bool Raining { get; set; }
        public bool Thundering { get; set; }
        
        public List<string> EnabledMods { get; set; } = new();
        public List<string> DisabledMods { get; set; } = new();
        
        public string DisplayPlayTime
        {
            get
            {
                if (TotalPlayTime.TotalHours >= 1)
                {
                    return $"{(int)TotalPlayTime.TotalHours}小时{TotalPlayTime.Minutes}分钟";
                }
                else if (TotalPlayTime.TotalMinutes >= 1)
                {
                    return $"{(int)TotalPlayTime.TotalMinutes}分钟";
                }
                else
                {
                    return $"{TotalPlayTime.Seconds}秒";
                }
            }
        }
        
        public string DisplayLastPlayed
        {
            get
            {
                var diff = DateTime.Now - LastPlayedTime;
                if (diff.TotalMinutes < 1)
                    return "刚刚";
                else if (diff.TotalHours < 1)
                    return $"{(int)diff.TotalMinutes}分钟前";
                else if (diff.TotalDays < 1)
                    return $"{(int)diff.TotalHours}小时前";
                else if (diff.TotalDays < 7)
                    return $"{(int)diff.TotalDays}天前";
                else
                    return LastPlayedTime.ToString("yyyy-MM-dd");
            }
        }
        
        public string DisplayGameMode => GameMode switch
        {
            "survival" => "生存模式",
            "creative" => "创造模式",
            "adventure" => "冒险模式",
            "spectator" => "旁观模式",
            "hardcore" => "极限模式",
            _ => GameMode
        };
        
        public string DisplayDifficulty => Difficulty switch
        {
            "peaceful" => "和平",
            "easy" => "简单",
            "normal" => "普通",
            "hard" => "困难",
            _ => Difficulty
        };
        
        public string DisplayWorldType => WorldType switch
        {
            "default" => "标准",
            "flat" => "超平坦",
            "large_biomes" => "巨型生物群系",
            "amplified" => "放大化",
            "single_biome_surface" => "单一生物群系",
            _ => WorldType
        };
        
        public string DisplayDimension => Dimension switch
        {
            -1 => "下界",
            0 => "主世界",
            1 => "末地",
            _ => $"维度 {Dimension}"
        };
        
        public string DisplayGameTime
        {
            get
            {
                if (GameTimeTicks == 0) return "未知";
                
                var totalMinutes = GameTimeTicks / (20 * 60);
                if (totalMinutes < 1) return "< 1分钟";
                
                var days = (int)(totalMinutes / (20 * 60));
                var hours = (int)((totalMinutes % (20 * 60)) / 60);
                var minutes = (int)(totalMinutes % 60);
                
                if (days > 0) return $"{days}天{hours}小时{minutes}分";
                if (hours > 0) return $"{hours}小时{minutes}分";
                return $"{minutes}分钟";
            }
        }
        
        public string DisplayWeather
        {
            get
            {
                if (Thundering) return "雷暴";
                if (Raining) return "雨天";
                return "晴朗";
            }
        }
    }

    public class SaveRecordData
    {
        public List<SaveRecord> SaveRecords { get; set; } = new();
    }
}
