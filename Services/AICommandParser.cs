using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using MinecraftLauncher.Models;

namespace MinecraftLauncher.Services
{
    public class AICommandParser
    {
        private static readonly Regex _jsonRegex = new(@"\{[^{}]*\}", RegexOptions.Compiled);

        public List<AICommand> ParseCommands(string aiResponse)
        {
            var commands = new List<AICommand>();
            
            try
            {
                var jsonMatches = _jsonRegex.Matches(aiResponse);
                
                foreach (Match match in jsonMatches)
                {
                    try
                    {
                        var json = match.Value;
                        var command = JsonSerializer.Deserialize<AICommand>(json, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });
                        
                        if (command != null && !string.IsNullOrEmpty(command.Action))
                        {
                            commands.Add(command);
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                App.LogError("解析 AI 命令失败", ex);
            }
            
            return commands;
        }

        public bool IsActionRequest(string userMessage)
        {
            var actionKeywords = new[]
            {
                "下载", "安装", "搜索", "找", "帮我", "推荐",
                "download", "install", "search", "find", "help"
            };
            
            var lowerMessage = userMessage.ToLower();
            return actionKeywords.Any(k => lowerMessage.Contains(k));
        }

        public (string ResourceType, string Query, string? GameVersion) ParseResourceRequest(string userMessage)
        {
            var lowerMessage = userMessage.ToLower();
            
            string resourceType = "mod";
            if (lowerMessage.Contains("光影") || lowerMessage.Contains("shader"))
            {
                resourceType = "shader";
            }
            else if (lowerMessage.Contains("资源包") || lowerMessage.Contains("材质包") || lowerMessage.Contains("resourcepack"))
            {
                resourceType = "resourcepack";
            }
            else if (lowerMessage.Contains("地图") || lowerMessage.Contains("存档") || lowerMessage.Contains("map"))
            {
                resourceType = "map";
            }
            else if (lowerMessage.Contains("模组") || lowerMessage.Contains("mod"))
            {
                resourceType = "mod";
            }
            
            var versionMatch = Regex.Match(userMessage, @"(\d+\.\d+(?:\.\d+)?)");
            string? gameVersion = versionMatch.Success ? versionMatch.Groups[1].Value : null;
            
            var query = userMessage;
            var removePatterns = new[]
            {
                @"帮我找(一个|一下)?",
                @"搜索(一个|一下)?",
                @"下载(一个|一下)?",
                @"安装(一个|一下)?",
                @"推荐(一个|一下)?",
                @"(\d+\.\d+(?:\.\d+)?)",
                @"版本",
                @"的光影",
                @"光影",
                @"的资源包",
                @"资源包",
                @"材质包",
                @"的地图",
                @"地图",
                @"存档",
                @"的模组",
                @"模组"
            };
            
            foreach (var pattern in removePatterns)
            {
                query = Regex.Replace(query, pattern, "", RegexOptions.IgnoreCase);
            }
            
            query = Regex.Replace(query, @"\s+", " ").Trim();
            
            return (resourceType, query, gameVersion);
        }
    }
}
