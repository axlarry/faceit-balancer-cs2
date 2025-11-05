using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Entities;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Net.Http;
using System.Threading.Tasks;
using System.IO;
using System;

namespace FaceITBalancer;

[MinimumApiVersion(1)]
public class FaceITBalancer : BasePlugin
{
    public override string ModuleName => "FaceIT Team Balancer";
    public override string ModuleVersion => "0.1.4-Beta";
    public override string ModuleAuthor => "Larry Lacurte.ro";
    public override string ModuleDescription => "Balances teams based on FaceIT ELO ratings";

    private Dictionary<ulong, PlayerData> _playerData = new();
    private HttpClient _httpClient = new();
    private string _apiKey = "";
    private bool _apiEnabled = false;

    private class PlayerData
    {
        public int Level { get; set; } = 1;
        public int ELO { get; set; } = 500;
        public string Nickname { get; set; } = "Unknown";
        public bool DataLoaded { get; set; } = false;
    }

    private class PluginConfig
    {
        public string ApiKey { get; set; } = "";
        public bool AutoBalance { get; set; } = false;
        public int MinPlayers { get; set; } = 10;
    }

    public override void Load(bool hotReload)
    {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(15);
        
        LoadConfig();
        RegisterCommands();
        
        if (_apiEnabled)
        {
            Console.WriteLine("[FaceIT] ✅ Plugin loaded with API support!");
        }
        else
        {
            Console.WriteLine("[FaceIT] ✅ Plugin loaded (manual mode)");
            Console.WriteLine("[FaceIT] 💡 Players can use !setlevel [1-10] or !setelo [ELO]");
        }
    }

    private void LoadConfig()
    {
        var configPath = Path.Combine(ModuleDirectory, "config.json");
        
        if (!File.Exists(configPath))
        {
            Console.WriteLine("[FaceIT] ❌ config.json does not exist!");
            CreateDefaultConfig(configPath);
            return;
        }

        try
        {
            var json = File.ReadAllText(configPath);
            var config = JsonSerializer.Deserialize<PluginConfig>(json);
            
            if (config != null && !string.IsNullOrEmpty(config.ApiKey) && config.ApiKey != "API_KEY_HERE")
            {
                _apiKey = config.ApiKey;
                _apiEnabled = true;
                
                // Add correct Authorization header for FaceIT API
                if (!_apiKey.StartsWith("Bearer "))
                {
                    _apiKey = "Bearer " + _apiKey;
                }
                
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", _apiKey);
                Console.WriteLine($"[FaceIT] ✅ API Key configured: {_apiKey.Substring(0, Math.Min(15, _apiKey.Length))}...");
            }
            else
            {
                Console.WriteLine("[FaceIT] ℹ️  API Key not configured - using manual mode");
                _apiEnabled = false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FaceIT] ❌ Error loading config.json: {ex.Message}");
            _apiEnabled = false;
        }
    }

    private void CreateDefaultConfig(string configPath)
    {
        var defaultConfig = new PluginConfig
        {
            ApiKey = "API_KEY_HERE",
            AutoBalance = false,
            MinPlayers = 10
        };

        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(defaultConfig, options);
        File.WriteAllText(configPath, json);
        
        Console.WriteLine($"[FaceIT] 📁 Config created: {configPath}");
        Console.WriteLine("[FaceIT] ⚠️  Configure API Key in config file!");
    }

    private void RegisterCommands()
    {
        // Help and info commands
        AddCommand("css_faceit", "FaceIT commands help", OnFaceITHelpCommand);
        AddCommand("css_faceit_help", "FaceIT commands help", OnFaceITHelpCommand);
        
        // Admin commands
        AddCommand("css_fbalance", "Balance teams", OnBalanceCommand);
        AddCommand("css_fbalance5v5", "Balance 5v5", OnBalance5v5Command);
        AddCommand("css_getfaceit_all", "Load FaceIT data for all players", OnGetFaceITAllCommand);
        
        // Player commands
        AddCommand("css_getfaceit", "Load FaceIT data", OnGetFaceITCommand);
        AddCommand("css_setlevel", "Set level manually", OnSetLevelCommand);
        AddCommand("css_setelo", "Set ELO manually", OnSetELOCommand);
        AddCommand("css_myfaceit", "View your data", OnMyFaceITCommand);
        
        // Admin data viewing commands
        AddCommand("css_playersdata", "View all players data", OnPlayersDataCommand);
    }

    // ================== HELP COMMAND ==================
    [CommandHelper(minArgs: 0, usage: "", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnFaceITHelpCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid) 
        {
            ShowHelpInConsole();
            return;
        }

        ShowHelpInChat(player);
    }

    private void ShowHelpInConsole()
    {
        Console.WriteLine("[FaceIT] 📖 AVAILABLE COMMANDS:");
        Console.WriteLine("[FaceIT] =========================");
        Console.WriteLine("[FaceIT] 🔹 !faceit / !faceit_help - Show this help");
        Console.WriteLine("[FaceIT] ");
        Console.WriteLine("[FaceIT] 👤 PLAYER COMMANDS:");
        Console.WriteLine("[FaceIT] 🔹 !getfaceit - Load your data from FaceIT");
        Console.WriteLine("[FaceIT] 🔹 !setlevel [1-10] - Set level manually");
        Console.WriteLine("[FaceIT] 🔹 !setelo [ELO] - Set ELO manually (1-3900)");
        Console.WriteLine("[FaceIT] 🔹 !myfaceit - View your loaded data");
        Console.WriteLine("[FaceIT] ");
        Console.WriteLine("[FaceIT] ⚡ ADMIN COMMANDS:");
        Console.WriteLine("[FaceIT] 🔹 !getfaceit_all - Load data for all players");
        Console.WriteLine("[FaceIT] 🔹 !playersdata - View all players data");
        Console.WriteLine("[FaceIT] 🔹 !fbalance - Balance teams by ELO");
        Console.WriteLine("[FaceIT] 🔹 !fbalance5v5 - Balance 5v5 by ELO");
        Console.WriteLine("[FaceIT] ");
        Console.WriteLine("[FaceIT] 💡 Note: Balancing is done by ELO, not by level!");
    }

    private void ShowHelpInChat(CCSPlayerController player)
    {
        player.PrintToChat(" [FaceIT] 📖 AVAILABLE COMMANDS:");
        player.PrintToChat(" [FaceIT] =========================");
        player.PrintToChat(" [FaceIT] 🔹 !faceit / !faceit_help - This help");
        player.PrintToChat(" [FaceIT] ");
        player.PrintToChat(" [FaceIT] 👤 PLAYER COMMANDS:");
        player.PrintToChat(" [FaceIT] 🔹 !getfaceit - Your FaceIT data");
        player.PrintToChat(" [FaceIT] 🔹 !setlevel [1-10] - Manual level");
        player.PrintToChat(" [FaceIT] 🔹 !setelo [ELO] - Manual ELO (1-3900)");
        player.PrintToChat(" [FaceIT] 🔹 !myfaceit - View your data");
        
        if (HasAdminPermissions(player))
        {
            player.PrintToChat(" [FaceIT] ");
            player.PrintToChat(" [FaceIT] ⚡ ADMIN COMMANDS:");
            player.PrintToChat(" [FaceIT] 🔹 !getfaceit_all - Data for all players");
            player.PrintToChat(" [FaceIT] 🔹 !playersdata - All players data");
            player.PrintToChat(" [FaceIT] 🔹 !fbalance - Balance teams");
            player.PrintToChat(" [FaceIT] 🔹 !fbalance5v5 - Balance 5v5");
        }
        
        player.PrintToChat(" [FaceIT] ");
        player.PrintToChat(" [FaceIT] 💡 Balancing is done by ELO!");
    }

    // ================== API COMMANDS ==================
    [CommandHelper(minArgs: 0, usage: "", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnGetFaceITCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid) 
        {
            Console.WriteLine("[FaceIT] ❌ Player is null or invalid");
            return;
        }

        var steamID = player.SteamID;
        Console.WriteLine($"[FaceIT] 🔍 GetFaceIT command from: {player.PlayerName}, SteamID: {steamID}");

        if (steamID == 0)
        {
            player.PrintToChat(" [FaceIT] ❌ Cannot get SteamID");
            Console.WriteLine($"[FaceIT] ❌ Invalid SteamID: {steamID}");
            return;
        }

        if (!_apiEnabled)
        {
            player.PrintToChat(" [FaceIT] ❌ API is not configured");
            player.PrintToChat(" [FaceIT] 💡 Use !setlevel for manual setup");
            return;
        }

        player.PrintToChat(" [FaceIT] 🔍 Searching for your data...");
        _ = FetchFaceITData(player, steamID.ToString());
    }

    [CommandHelper(minArgs: 0, usage: "", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnGetFaceITAllCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid) return;

        if (!HasAdminPermissions(player))
        {
            player.PrintToChat(" [FaceIT] ❌ You don't have permission for this command!");
            return;
        }

        if (!_apiEnabled)
        {
            player.PrintToChat(" [FaceIT] ❌ API is not configured");
            return;
        }

        var players = Utilities.GetPlayers()
            .Where(p => p != null && p.IsValid && p.IsBot == false)
            .ToList();

        if (players.Count == 0)
        {
            player.PrintToChat(" [FaceIT] ❌ No players on server");
            return;
        }

        player.PrintToChat($" [FaceIT] 🔍 Loading data for {players.Count} players...");
        Server.PrintToChatAll($" [FaceIT] 🔍 Admin is loading FaceIT data for all players...");

        _ = FetchFaceITDataForAllPlayers(players, player);
    }

    private async Task FetchFaceITDataForAllPlayers(List<CCSPlayerController> players, CCSPlayerController adminPlayer)
    {
        int successCount = 0;
        int errorCount = 0;

        foreach (var player in players)
        {
            if (player.IsValid && !player.IsBot)
            {
                try
                {
                    var steamID = player.SteamID;
                    if (steamID != 0)
                    {
                        var url = $"https://open.faceit.com/data/v4/players?game=cs2&game_player_id={steamID}";
                        var response = await _httpClient.GetAsync(url);
                        
                        if (response.IsSuccessStatusCode)
                        {
                            var json = await response.Content.ReadAsStringAsync();
                            ParseFaceITResponse(player, json);
                            successCount++;
                            
                            // Small delay between requests to avoid rate limiting
                            await Task.Delay(500);
                        }
                        else
                        {
                            errorCount++;
                            Console.WriteLine($"[FaceIT] ❌ Error for {player.PlayerName}: {response.StatusCode}");
                        }
                    }
                    else
                    {
                        errorCount++;
                    }
                }
                catch (Exception ex)
                {
                    errorCount++;
                    Console.WriteLine($"[FaceIT] ❌ Exception for {player.PlayerName}: {ex.Message}");
                }
            }
        }

        adminPlayer.PrintToChat($" [FaceIT] ✅ Data loaded for {successCount} players");
        if (errorCount > 0)
        {
            adminPlayer.PrintToChat($" [FaceIT] ⚠️  Errors for {errorCount} players");
        }
        
        Console.WriteLine($"[FaceIT] ✅ Bulk data load complete: {successCount} success, {errorCount} errors");
    }

    private async Task FetchFaceITData(CCSPlayerController player, string steamID)
    {
        try
        {
            var url = $"https://open.faceit.com/data/v4/players?game=cs2&game_player_id={steamID}";
            Console.WriteLine($"[FaceIT] 🔍 Fetching data for SteamID: {steamID}");
            Console.WriteLine($"[FaceIT] 🔗 URL: {url}");
            
            var response = await _httpClient.GetAsync(url);
            var responseContent = await response.Content.ReadAsStringAsync();
            
            Console.WriteLine($"[FaceIT] 📡 Response Status: {response.StatusCode}");
            Console.WriteLine($"[FaceIT] 📄 Response Content: {responseContent}");

            if (response.IsSuccessStatusCode)
            {
                ParseFaceITResponse(player, responseContent);
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                player.PrintToChat(" [FaceIT] ❌ Authentication error (Invalid API Key)");
                Console.WriteLine($"[FaceIT] ❌ API Key invalid or expired");
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                player.PrintToChat(" [FaceIT] ❌ Player not found on FaceIT");
                Console.WriteLine($"[FaceIT] ❌ Player not found on FaceIT");
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                player.PrintToChat(" [FaceIT] ❌ Too many requests. Please wait.");
                Console.WriteLine($"[FaceIT] ❌ Rate limit exceeded");
            }
            else
            {
                player.PrintToChat(" [FaceIT] ❌ Error getting data");
                Console.WriteLine($"[FaceIT] ❌ API Error: {response.StatusCode}");
            }
        }
        catch (HttpRequestException ex)
        {
            player.PrintToChat(" [FaceIT] ❌ Network error");
            Console.WriteLine($"[FaceIT] ❌ Network Error: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            player.PrintToChat(" [FaceIT] ❌ Timeout connecting to FaceIT");
            Console.WriteLine($"[FaceIT] ❌ Request timeout");
        }
        catch (Exception ex)
        {
            player.PrintToChat(" [FaceIT] ❌ Unexpected error");
            Console.WriteLine($"[FaceIT] ❌ Unexpected Error: {ex.Message}");
        }
    }

    private void ParseFaceITResponse(CCSPlayerController player, string json)
    {
        try
        {
            Console.WriteLine($"[FaceIT] 🔍 Parsing JSON response...");
            
            using JsonDocument doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Check if there are errors in response
            if (root.TryGetProperty("errors", out var errors))
            {
                foreach (var error in errors.EnumerateArray())
                {
                    if (error.TryGetProperty("message", out var message))
                    {
                        var errorMsg = message.GetString();
                        Console.WriteLine($"[FaceIT] ❌ API Error: {errorMsg}");
                        player.PrintToChat($" [FaceIT] ❌ {errorMsg}");
                        return;
                    }
                }
            }

            // Extract nickname
            var nickname = root.TryGetProperty("nickname", out var nicknameElement) 
                ? nicknameElement.GetString() ?? player.PlayerName 
                : player.PlayerName;

            // Extract CS2 data
            int skillLevel = 1;
            int faceitElo = 500;
            
            if (root.TryGetProperty("games", out var games) && 
                games.TryGetProperty("cs2", out var cs2))
            {
                if (cs2.TryGetProperty("skill_level", out var levelElement) && levelElement.ValueKind != JsonValueKind.Null)
                {
                    skillLevel = levelElement.GetInt32();
                }
                
                if (cs2.TryGetProperty("faceit_elo", out var eloElement) && eloElement.ValueKind != JsonValueKind.Null)
                {
                    faceitElo = eloElement.GetInt32();
                }
                
                Console.WriteLine($"[FaceIT] ✅ CS2 data found: Level={skillLevel}, ELO={faceitElo}");
            }
            else
            {
                player.PrintToChat(" [FaceIT] ⚠️  No CS2 data found");
                Console.WriteLine($"[FaceIT] ⚠️  No CS2 data found in response");
                return;
            }

            // Save data
            _playerData[player.SteamID] = new PlayerData
            {
                Level = skillLevel,
                ELO = faceitElo,
                Nickname = nickname,
                DataLoaded = true
            };

            player.PrintToChat($" [FaceIT] ✅ Data loaded: {nickname}");
            player.PrintToChat($" [FaceIT] 🎯 Level: {skillLevel} | ELO: {faceitElo}");
            
            Console.WriteLine($"[FaceIT] ✅ Data loaded for {nickname}: Level {skillLevel}, ELO {faceitElo}");
        }
        catch (JsonException ex)
        {
            player.PrintToChat(" [FaceIT] ❌ Error parsing data");
            Console.WriteLine($"[FaceIT] ❌ JSON Parse Error: {ex.Message}");
            Console.WriteLine($"[FaceIT] ❌ JSON Content: {json}");
        }
        catch (Exception ex)
        {
            player.PrintToChat(" [FaceIT] ❌ Unexpected error");
            Console.WriteLine($"[FaceIT] ❌ Parse Unexpected Error: {ex.Message}");
        }
    }

    // ================== MANUAL COMMANDS ==================
    [CommandHelper(minArgs: 1, usage: "[level 1-10]", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnSetLevelCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid) return;

        string levelStr = command.GetArg(1);
        if (!int.TryParse(levelStr, out int level) || level < 1 || level > 10)
        {
            player.PrintToChat(" [FaceIT] ❌ Level must be between 1-10");
            player.PrintToChat(" [FaceIT] 💡 Use: !setlevel [1-10]");
            return;
        }

        int elo = LevelToELO(level);
        SetManualData(player, level, elo);
        player.PrintToChat($" [FaceIT] ✅ Level set: {level} (ELO: {elo})");
        Console.WriteLine($"[FaceIT] ✅ Manual data set for {player.PlayerName}: Level {level}, ELO {elo}");
    }

    [CommandHelper(minArgs: 1, usage: "[ELO]", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnSetELOCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid) return;

        string eloStr = command.GetArg(1);
        if (!int.TryParse(eloStr, out int elo) || elo < 1 || elo > 3900)
        {
            player.PrintToChat(" [FaceIT] ❌ ELO must be between 1-3900");
            player.PrintToChat(" [FaceIT] 💡 Use: !setelo [ELO]");
            return;
        }

        int level = ELOToLevel(elo);
        SetManualData(player, level, elo);
        player.PrintToChat($" [FaceIT] ✅ ELO set: {elo} (Level: {level})");
        Console.WriteLine($"[FaceIT] ✅ Manual data set for {player.PlayerName}: Level {level}, ELO {elo}");
    }

    [CommandHelper(minArgs: 0, usage: "", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnMyFaceITCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid) return;

        ulong steamId = player.SteamID;
        if (_playerData.TryGetValue(steamId, out PlayerData? data) && data != null && data.DataLoaded)
        {
            player.PrintToChat($" [FaceIT] 🎯 {data.Nickname} | Level: {data.Level} | ELO: {data.ELO}");
        }
        else
        {
            player.PrintToChat(" [FaceIT] 💡 Use !getfaceit or !setlevel [1-10]");
            player.PrintToChat(" [FaceIT] 💡 See all commands with !faceit_help");
        }
    }

    // ================== ADMIN COMMANDS ==================
    [CommandHelper(minArgs: 0, usage: "", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnPlayersDataCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid) 
        {
            // Executed from server console
            ShowPlayersDataInConsole();
            return;
        }

        // Executed by player - check admin permissions
        if (!HasAdminPermissions(player))
        {
            player.PrintToChat(" [FaceIT] ❌ You don't have permission for this command!");
            player.PrintToChat(" [FaceIT] 💡 See available commands with !faceit_help");
            return;
        }

        ShowPlayersDataInChat(player);
    }

    private void ShowPlayersDataInConsole()
    {
        var playersWithData = _playerData.Where(p => p.Value.DataLoaded).ToList();
        
        Console.WriteLine($"[FaceIT] 📊 Players with data: {playersWithData.Count}");
        foreach (var (steamId, data) in playersWithData)
        {
            Console.WriteLine($"[FaceIT] 👤 {data.Nickname} - Level: {data.Level}, ELO: {data.ELO}, SteamID: {steamId}");
        }
    }

    private void ShowPlayersDataInChat(CCSPlayerController adminPlayer)
    {
        var playersWithData = _playerData.Where(p => p.Value.DataLoaded).ToList();
        
        if (playersWithData.Count == 0)
        {
            adminPlayer.PrintToChat(" [FaceIT] 📊 No players with loaded data");
            adminPlayer.PrintToChat(" [FaceIT] 💡 Use !getfaceit_all to load data");
            return;
        }

        adminPlayer.PrintToChat(" [FaceIT] 📊 Players with loaded data:");
        adminPlayer.PrintToChat(" [FaceIT] ===============================");
        
        // Sort by ELO (descending)
        var sortedPlayers = playersWithData.OrderByDescending(p => p.Value.ELO).ToList();
        
        foreach (var (steamId, data) in sortedPlayers.Take(10)) // Limit to first 10 for chat
        {
            adminPlayer.PrintToChat($" [FaceIT] 👤 {data.Nickname} | ELO: {data.ELO} | Level: {data.Level}");
        }
        
        if (sortedPlayers.Count > 10)
        {
            adminPlayer.PrintToChat($" [FaceIT] 📈 ... and {sortedPlayers.Count - 10} more players");
        }
        
        int totalELO = sortedPlayers.Sum(p => p.Value.ELO);
        int averageELO = sortedPlayers.Count > 0 ? totalELO / sortedPlayers.Count : 0;
        
        adminPlayer.PrintToChat($" [FaceIT] 📈 Total: {sortedPlayers.Count} players | Average ELO: {averageELO}");
        
        // Also show in console for complete details
        Console.WriteLine($"[FaceIT] 📊 Players with data: {sortedPlayers.Count}");
        foreach (var (steamId, data) in sortedPlayers)
        {
            Console.WriteLine($"[FaceIT] 👤 {data.Nickname} - Level: {data.Level}, ELO: {data.ELO}, SteamID: {steamId}");
        }
    }

    [CommandHelper(minArgs: 0, usage: "", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnBalanceCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid) return;

        if (!HasAdminPermissions(player))
        {
            player.PrintToChat(" [FaceIT] ❌ You don't have permission!");
            player.PrintToChat(" [FaceIT] 💡 See available commands with !faceit_help");
            return;
        }

        BalanceTeamsByELO();
        Server.PrintToChatAll(" [FaceIT] ✅ Teams balanced by admin!");
    }

    [CommandHelper(minArgs: 0, usage: "", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnBalance5v5Command(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid) return;

        if (!HasAdminPermissions(player))
        {
            player.PrintToChat(" [FaceIT] ❌ You don't have permission!");
            player.PrintToChat(" [FaceIT] 💡 See available commands with !faceit_help");
            return;
        }

        Balance5v5TeamsByELO();
        Server.PrintToChatAll(" [FaceIT] ✅ 5v5 balanced by admin!");
    }

    // ================== BALANCING FUNCTIONS ==================
    private void BalanceTeamsByELO()
    {
        var players = Utilities.GetPlayers()
            .Where(p => p != null && p.IsValid && (p.TeamNum == 2 || p.TeamNum == 3))
            .Where(p => _playerData.ContainsKey(p.SteamID) && _playerData[p.SteamID].DataLoaded)
            .ToList();

        Console.WriteLine($"[FaceIT] 🔍 Found {players.Count} players with data for balancing");

        if (players.Count < 2)
        {
            Server.PrintToChatAll(" [FaceIT] ❌ Not enough players with data");
            Server.PrintToChatAll(" [FaceIT] 💡 Use !getfaceit or !setlevel [1-10]");
            Server.PrintToChatAll(" [FaceIT] 💡 Admins can use !getfaceit_all for all players");
            return;
        }

        // Sort by ELO (descending) - BALANCING BY ELO
        players.Sort((a, b) => _playerData[b.SteamID].ELO.CompareTo(_playerData[a.SteamID].ELO));

        int team1Total = 0, team2Total = 0;
        int team1Count = 0, team2Count = 0;

        foreach (var player in players)
        {
            var playerData = _playerData[player.SteamID];
            
            if (team1Total <= team2Total && team1Count < (players.Count + 1) / 2)
            {
                // Switch to Terrorist (team 2)
                player.TeamNum = 2;
                team1Total += playerData.ELO;
                team1Count++;
                Console.WriteLine($"[FaceIT] ➡️  {playerData.Nickname} -> T (ELO: {playerData.ELO})");
            }
            else
            {
                // Switch to Counter-Terrorist (team 3)
                player.TeamNum = 3;
                team2Total += playerData.ELO;
                team2Count++;
                Console.WriteLine($"[FaceIT] ➡️  {playerData.Nickname} -> CT (ELO: {playerData.ELO})");
            }
        }

        Server.PrintToChatAll($" [FaceIT] ✅ Teams balanced by ELO!");
        Server.PrintToChatAll($" [FACEIT] █ T: {team1Total} ELO ({team1Count}p) | █ CT: {team2Total} ELO ({team2Count}p)");
        
        Console.WriteLine($"[FaceIT] ✅ Balance complete: T={team1Total} ({team1Count}p) vs CT={team2Total} ({team2Count}p)");
    }

    private void Balance5v5TeamsByELO()
    {
        var players = Utilities.GetPlayers()
            .Where(p => p != null && p.IsValid)
            .Where(p => _playerData.ContainsKey(p.SteamID) && _playerData[p.SteamID].DataLoaded)
            .ToList();

        Console.WriteLine($"[FaceIT] 🔍 Found {players.Count} players with data for 5v5 balancing");

        if (players.Count < 10)
        {
            Server.PrintToChatAll($" [FaceIT] ❌ {players.Count}/10 players with data");
            Server.PrintToChatAll(" [FaceIT] 💡 Use !getfaceit or !setlevel [1-10]");
            return;
        }

        // Sort by ELO (descending) and take first 10 - BALANCING BY ELO
        players.Sort((a, b) => _playerData[b.SteamID].ELO.CompareTo(_playerData[a.SteamID].ELO));
        if (players.Count > 10) 
            players = players.Take(10).ToList();

        var team1 = new List<CCSPlayerController>();
        var team2 = new List<CCSPlayerController>();
        int team1Total = 0, team2Total = 0;

        foreach (var player in players)
        {
            var playerData = _playerData[player.SteamID];
            
            if (team1.Count < 5 && (team1Total <= team2Total || team2.Count >= 5))
            {
                team1.Add(player);
                team1Total += playerData.ELO;
                Console.WriteLine($"[FaceIT] 5v5 ➡️  {playerData.Nickname} -> T (ELO: {playerData.ELO})");
            }
            else
            {
                team2.Add(player);
                team2Total += playerData.ELO;
                Console.WriteLine($"[FaceIT] 5v5 ➡️  {playerData.Nickname} -> CT (ELO: {playerData.ELO})");
            }
        }

        // Apply teams
        foreach (var player in team1) 
            player.TeamNum = 2; // Terrorist
        foreach (var player in team2) 
            player.TeamNum = 3; // Counter-Terrorist

        Server.PrintToChatAll(" [FaceIT] ✅ 5v5 BALANCED by ELO!");
        Server.PrintToChatAll($" [FACEIT] █ T: {team1Total} ELO | █ CT: {team2Total} ELO");
        
        Console.WriteLine($"[FaceIT] ✅ 5v5 Balance complete: T={team1Total} vs CT={team2Total}");
    }

    // ================== UTILITY FUNCTIONS ==================
    private void SetManualData(CCSPlayerController player, int level, int elo)
    {
        _playerData[player.SteamID] = new PlayerData
        {
            Level = level,
            ELO = elo,
            Nickname = player.PlayerName ?? "Manual",
            DataLoaded = true
        };
    }

    private int LevelToELO(int level)
    {
        int[] eloMap = { 200, 400, 600, 800, 1000, 1200, 1400, 1600, 1800, 2000 };
        return level >= 1 && level <= 10 ? eloMap[level - 1] : 1000;
    }

    private int ELOToLevel(int elo)
    {
        if (elo < 300) return 1;
        if (elo < 500) return 2;
        if (elo < 700) return 3;
        if (elo < 900) return 4;
        if (elo < 1100) return 5;
        if (elo < 1300) return 6;
        if (elo < 1500) return 7;
        if (elo < 1700) return 8;
        if (elo < 1900) return 9;
        return 10;
    }

    private bool HasAdminPermissions(CCSPlayerController player)
    {
        return AdminManager.PlayerHasPermissions(player, "@css/generic") ||
               AdminManager.PlayerHasPermissions(player, "@css/ban") ||
               AdminManager.PlayerHasPermissions(player, "generic");
    }

    public override void Unload(bool hotReload)
    {
        _httpClient?.Dispose();
        Console.WriteLine("[FaceIT] Plugin unloaded");
    }
}
