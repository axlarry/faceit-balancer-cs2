using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Net.Http;
using System.IO;
using System;

namespace FaceITBalancer;

[MinimumApiVersion(1)]
public class FaceITBalancer : BasePlugin
{
    public override string ModuleName => "FaceIT Team Balancer";
    public override string ModuleVersion => "0.6.1";
    public override string ModuleAuthor => "Larry Lacurte.ro";
    public override string ModuleDescription => "FaceIT ELO fetcher with smart match detection";

    private Dictionary<ulong, PlayerData> _playerData = new();
    private HttpClient _httpClient = new();
    private string _apiKey = "";
    private bool _apiEnabled = false;
    private Queue<ulong> _playersToFetch = new Queue<ulong>();
    private bool _isFetching = false;
    private int _fetchCounter = 0;
    
    // Starea plugin-ului
    private bool _pluginEnabled = true;
    private bool _matchInProgress = false;
    private DateTime _lastStatusCheck = DateTime.MinValue;

    private class PlayerData
    {
        public int Level { get; set; } = 1;
        public int ELO { get; set; } = 500;
        public string Nickname { get; set; } = "Unknown";
        public bool DataLoaded { get; set; } = false;
        public DateTime LastFetchAttempt { get; set; } = DateTime.MinValue;
    }

    private class PluginConfig
    {
        public string ApiKey { get; set; } = "";
        public bool AutoFetchOnConnect { get; set; } = true;
        public int AutoFetchDelay { get; set; } = 5;
        public bool DisableDuringMatch { get; set; } = true;
    }

    private PluginConfig? config;

    public override void Load(bool hotReload)
    {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(15);
        
        LoadConfig();
        RegisterCommands();
        RegisterEventHandlers();
        
        if (_apiEnabled)
        {
            Console.WriteLine("[FaceIT] Plugin loaded with API support!");
            if (config != null)
            {
                Console.WriteLine($"[FaceIT] Auto-disable during match: {(config.DisableDuringMatch ? "YES" : "NO")}");
            }
        }
        else
        {
            Console.WriteLine("[FaceIT] Plugin loaded in manual mode");
            Console.WriteLine("[FaceIT] Configure API key in config.json");
        }
    }

    private void LoadConfig()
    {
        var configPath = Path.Combine(ModuleDirectory, "config.json");
        
        if (!File.Exists(configPath))
        {
            Console.WriteLine("[FaceIT] config.json does not exist!");
            CreateDefaultConfig(configPath);
            return;
        }

        try
        {
            var json = File.ReadAllText(configPath);
            config = JsonSerializer.Deserialize<PluginConfig>(json);
            
            if (config != null && !string.IsNullOrEmpty(config.ApiKey) && config.ApiKey != "API_KEY_HERE")
            {
                _apiKey = config.ApiKey;
                _apiEnabled = true;
                
                if (!_apiKey.StartsWith("Bearer "))
                {
                    _apiKey = "Bearer " + _apiKey;
                }
                
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", _apiKey);
                Console.WriteLine($"[FaceIT] API Key configured");
            }
            else
            {
                Console.WriteLine("[FaceIT] API Key not configured - plugin disabled");
                _apiEnabled = false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FaceIT] Error loading config.json: {ex.Message}");
            _apiEnabled = false;
        }
    }

    private void CreateDefaultConfig(string configPath)
    {
        var defaultConfig = new PluginConfig
        {
            ApiKey = "API_KEY_HERE",
            AutoFetchOnConnect = true,
            AutoFetchDelay = 5,
            DisableDuringMatch = true
        };

        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(defaultConfig, options);
        File.WriteAllText(configPath, json);
        
        Console.WriteLine($"[FaceIT] Config created: {configPath}");
        Console.WriteLine("[FaceIT] Configure API Key in config file!");
    }

    private void RegisterEventHandlers()
    {
        // Timer pentru verificarea starii meciului (la fiecare 10 secunde)
        AddTimer(10.0f, CheckMatchStatus, TimerFlags.REPEAT);
        
        // Timer simplu pentru a detecta playeri noi - doar daca plugin-ul este activ
        AddTimer(5.0f, CheckForNewPlayers, TimerFlags.REPEAT);
        
        // Timer pentru procesarea cozii de fetch (o data pe secunda) - doar daca plugin-ul este activ
        AddTimer(1.0f, ProcessFetchQueue, TimerFlags.REPEAT);
    }

    private void CheckMatchStatus()
    {
        if (!_apiEnabled || config?.DisableDuringMatch != true) 
            return;

        try
        {
            // Verifica doar o data la 10 secunde (performanta maxima)
            if ((DateTime.Now - _lastStatusCheck).TotalSeconds < 10)
                return;
                
            _lastStatusCheck = DateTime.Now;
            
            bool wasInProgress = _matchInProgress;
            
            // Detectare inteligenta a meciului:
            // 1. Numara jucatorii pe fiecare echipa
            var terrorists = Utilities.GetPlayers()
                .Where(p => p != null && p.IsValid && !p.IsBot && p.TeamNum == 2)
                .Count();
                
            var cts = Utilities.GetPlayers()
                .Where(p => p != null && p.IsValid && !p.IsBot && p.TeamNum == 3)
                .Count();
            
            // 2. Verifica daca sunt suficiente jucatori pentru un meci (minim 1v1, dar pentru siguranta 2v2)
            // 3. Verifica daca jucatorii nu sunt doar în warmup (nu sunt toti în spectate/necunoscuti)
            var totalPlayers = terrorists + cts;
            var unassigned = Utilities.GetPlayers()
                .Where(p => p != null && p.IsValid && !p.IsBot && p.TeamNum != 2 && p.TeamNum != 3)
                .Count();
            
            // Logica: Daca sunt cel putin 4 jucatori pe echipe si putini neasignati, probabil meci
            _matchInProgress = (totalPlayers >= 4 && unassigned <= 2);
            
            // Update plugin enabled status
            _pluginEnabled = !_matchInProgress;
            
            if (wasInProgress != _matchInProgress)
            {
                Console.WriteLine($"[FaceIT] Match status changed: {(_matchInProgress ? "IN PROGRESS" : "WARMUP/STOPPED")}");
                Console.WriteLine($"[FaceIT] Plugin is now: {(_pluginEnabled ? "ENABLED" : "DISABLED (match in progress)")}");
                Console.WriteLine($"[FaceIT] Players: T={terrorists}, CT={cts}, Unassigned={unassigned}");
                
                if (_matchInProgress)
                {
                    // Curata coada de fetch când meciul începe
                    _playersToFetch.Clear();
                    _isFetching = false;
                    Console.WriteLine("[FaceIT] Cleared fetch queue because match started");
                    
                    // Anunta pe chat
                    Server.PrintToChatAll(" [FaceIT] FaceIT balancer disabled - match is live!");
                }
                else
                {
                    Server.PrintToChatAll(" [FaceIT] FaceIT balancer enabled for warmup!");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FaceIT] Error checking match status: {ex.Message}");
        }
    }

    private void CheckForNewPlayers()
    {
        if (!_apiEnabled || !_pluginEnabled) return;

        try
        {
            var players = Utilities.GetPlayers()
                .Where(p => p != null && p.IsValid && !p.IsBot && p.SteamID != 0)
                .ToList();

            foreach (var player in players)
            {
                var steamID = player.SteamID;
                
                // Verifica daca player-ul nu are date înca si nu este deja în coada
                if (!_playerData.ContainsKey(steamID))
                {
                    // Adauga în dictionar
                    _playerData[steamID] = new PlayerData
                    {
                        Nickname = player.PlayerName ?? "Unknown",
                        LastFetchAttempt = DateTime.MinValue
                    };

                    // Adauga în coada pentru fetch
                    if (!_playersToFetch.Contains(steamID))
                    {
                        _playersToFetch.Enqueue(steamID);
                        Console.WriteLine($"[FaceIT] Added player {player.PlayerName} to fetch queue");
                    }
                }
                else if (!_playerData[steamID].DataLoaded && 
                         (DateTime.Now - _playerData[steamID].LastFetchAttempt).TotalMinutes > 5 &&
                         !_playersToFetch.Contains(steamID))
                {
                    // Reîncearca daca nu are date si ultima încercare a fost acum mai mult de 5 minute
                    _playersToFetch.Enqueue(steamID);
                    Console.WriteLine($"[FaceIT] Re-added player {player.PlayerName} to fetch queue");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FaceIT] Error in CheckForNewPlayers: {ex.Message}");
        }
    }

    private void ProcessFetchQueue()
    {
        if (!_apiEnabled || !_pluginEnabled || _isFetching || _playersToFetch.Count == 0) 
            return;

        _fetchCounter++;
        
        // Proceseaza doar 1 player la fiecare 10 frame-uri pentru a nu încarca serverul
        if (_fetchCounter % 10 != 0) 
            return;

        _isFetching = true;
        
        try
        {
            // Ia primul player din coada
            var steamID = _playersToFetch.Dequeue();
            
            // Gaseste player-ul
            var player = Utilities.GetPlayers()
                .FirstOrDefault(p => p != null && p.IsValid && !p.IsBot && p.SteamID == steamID);
                
            if (player == null)
            {
                _isFetching = false;
                return;
            }

            Console.WriteLine($"[FaceIT] Starting fetch for {player.PlayerName} (SteamID: {steamID})");
            
            // Folosim BeginFetch care va continua pe urmatorul frame
            BeginFetch(player, steamID);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FaceIT] Error in ProcessFetchQueue: {ex.Message}");
            _isFetching = false;
        }
    }

    private void BeginFetch(CCSPlayerController player, ulong steamID)
    {
        // Verifica daca avem deja date recente
        if (_playerData.ContainsKey(steamID) && _playerData[steamID].DataLoaded)
        {
            Console.WriteLine($"[FaceIT] Player {player.PlayerName} already has data loaded");
            _isFetching = false;
            return;
        }

        // Verifica când a fost ultima încercare
        if (_playerData.ContainsKey(steamID) && 
            (DateTime.Now - _playerData[steamID].LastFetchAttempt).TotalMinutes < 1)
        {
            Console.WriteLine($"[FaceIT] Skipping fetch for {player.PlayerName}, recent attempt");
            _isFetching = false;
            return;
        }

        // Update last attempt time
        if (_playerData.ContainsKey(steamID))
        {
            _playerData[steamID].LastFetchAttempt = DateTime.Now;
        }

        Console.WriteLine($"[FaceIT] Fetching FaceIT data for {player.PlayerName} (SteamID: {steamID})");
        
        // Folosim un timer pentru a face fetch-ul sincron la urmatorul frame
        AddTimer(0.1f, () => {
            ExecuteFetch(player, steamID);
        });
    }

    private void ExecuteFetch(CCSPlayerController player, ulong steamID)
    {
        if (!player.IsValid || player.IsBot)
        {
            _isFetching = false;
            return;
        }

        try
        {
            var url = $"https://open.faceit.com/data/v4/players?game=cs2&game_player_id={steamID}";
            
            // Folosim metoda sincron GetAsync (fara await)
            var task = _httpClient.GetAsync(url);
            task.Wait(); // A?tept?m sincron
            
            var response = task.Result;
            var responseContent = response.Content.ReadAsStringAsync().Result;
            
            Console.WriteLine($"[FaceIT] Response Status for {player.PlayerName}: {response.StatusCode}");

            // Proceseaza raspunsul imediat
            ProcessFaceITResponse(player, responseContent, response.IsSuccessStatusCode, response.StatusCode);
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"[FaceIT] Network error for {player.PlayerName}: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            Console.WriteLine($"[FaceIT] Timeout for {player.PlayerName}");
        }
        catch (AggregateException aex)
        {
            foreach (var ex in aex.InnerExceptions)
            {
                Console.WriteLine($"[FaceIT] Fetch error for {player.PlayerName}: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FaceIT] Unexpected error for {player.PlayerName}: {ex.Message}");
        }
        finally
        {
            _isFetching = false;
        }
    }

    private void ProcessFaceITResponse(CCSPlayerController player, string json, bool success, System.Net.HttpStatusCode statusCode)
    {
        if (!player.IsValid || player.IsBot) return;
        
        if (!success)
        {
            Console.WriteLine($"[FaceIT] Failed to get data for {player.PlayerName}: {statusCode}");
            return;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Verifica erori
            if (root.TryGetProperty("errors", out var errors))
            {
                foreach (var error in errors.EnumerateArray())
                {
                    if (error.TryGetProperty("message", out var message))
                    {
                        var errorMsg = message.GetString();
                        Console.WriteLine($"[FaceIT] API Error for {player.PlayerName}: {errorMsg}");
                        return;
                    }
                }
            }

            var nickname = root.TryGetProperty("nickname", out var nicknameElement) 
                ? nicknameElement.GetString() ?? player.PlayerName 
                : player.PlayerName;

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
                
                Console.WriteLine($"[FaceIT] CS2 data for {nickname}: Level={skillLevel}, ELO={faceitElo}");
            }
            else
            {
                Console.WriteLine($"[FaceIT] No CS2 data found for {player.PlayerName}");
                return;
            }

            _playerData[player.SteamID] = new PlayerData
            {
                Level = skillLevel,
                ELO = faceitElo,
                Nickname = nickname,
                DataLoaded = true,
                LastFetchAttempt = DateTime.Now
            };
            
            Console.WriteLine($"[FaceIT] Data saved for {nickname}: Level {skillLevel}, ELO {faceitElo}");
            
            // Anunta jucatorul
            player.PrintToChat($" [FaceIT] Your FaceIT data loaded: {nickname} | Level: {skillLevel} | ELO: {faceitElo}");
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"[FaceIT] JSON Parse Error for {player.PlayerName}: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FaceIT] Parse Error for {player.PlayerName}: {ex.Message}");
        }
    }

    private void RegisterCommands()
    {
        // Comenzi esentiale
        AddCommand("css_fbalance", "Balance teams by ELO", OnBalanceCommand);
        AddCommand("css_fbalance5v5", "Balance 5v5 by ELO", OnBalance5v5Command);
        AddCommand("css_elostatus", "Show ELO status of all players", OnELOStatusCommand);
        AddCommand("css_fstatus", "Show FaceIT plugin status", OnFaceITStatusCommand);
    }

    [CommandHelper(minArgs: 0, usage: "", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnBalanceCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid) return;
        
        if (!_pluginEnabled)
        {
            player.PrintToChat(" [FaceIT] Plugin disabled - match is in progress!");
            player.PrintToChat(" [FaceIT] Use during warmup only.");
            return;
        }

        BalanceTeamsByELO();
        Server.PrintToChatAll(" [FaceIT] Teams balanced by ELO!");
    }

    [CommandHelper(minArgs: 0, usage: "", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnBalance5v5Command(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid) return;
        
        if (!_pluginEnabled)
        {
            player.PrintToChat(" [FaceIT] Plugin disabled - match is in progress!");
            player.PrintToChat(" [FaceIT] Use during warmup only.");
            return;
        }

        Balance5v5TeamsByELO();
        Server.PrintToChatAll(" [FaceIT] 5v5 balanced by ELO!");
    }

    [CommandHelper(minArgs: 0, usage: "", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnELOStatusCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid) return;

        ShowELOStatus(player);
    }

    [CommandHelper(minArgs: 0, usage: "", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnFaceITStatusCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid) return;
        
        player.PrintToChat(" [FaceIT] ====== FACEIT PLUGIN STATUS ======");
        player.PrintToChat($" [FaceIT] Plugin enabled: {(_pluginEnabled ? "YES" : "NO")}");
        player.PrintToChat($" [FaceIT] Match in progress: {(_matchInProgress ? "YES" : "NO")}");
        player.PrintToChat($" [FaceIT] Players with data: {_playerData.Count(p => p.Value.DataLoaded)}");
        player.PrintToChat($" [FaceIT] Auto-fetch enabled: {(_apiEnabled ? "YES" : "NO")}");
        player.PrintToChat(" [FaceIT] Commands: !fbalance, !fbalance5v5, !elostatus");
        player.PrintToChat(" [FaceIT] ===================================");
    }

    private void BalanceTeamsByELO()
    {
        var players = Utilities.GetPlayers()
            .Where(p => p != null && p.IsValid && (p.TeamNum == 2 || p.TeamNum == 3))
            .Where(p => _playerData.ContainsKey(p.SteamID) && _playerData[p.SteamID].DataLoaded)
            .ToList();

        Console.WriteLine($"[FaceIT] Found {players.Count} players with data for balancing");

        if (players.Count < 2)
        {
            Server.PrintToChatAll(" [FaceIT] Not enough players with FaceIT data");
            Server.PrintToChatAll(" [FaceIT] Players need to connect and have FaceIT accounts");
            return;
        }

        // Sorteaza dupa ELO (descrescator)
        players.Sort((a, b) => _playerData[b.SteamID].ELO.CompareTo(_playerData[a.SteamID].ELO));

        int team1Total = 0, team2Total = 0;
        int team1Count = 0, team2Count = 0;

        foreach (var p in players)
        {
            var data = _playerData[p.SteamID];
            
            if (team1Total <= team2Total && team1Count < (players.Count + 1) / 2)
            {
                p.TeamNum = 2; // T
                team1Total += data.ELO;
                team1Count++;
                Console.WriteLine($"[FaceIT] {data.Nickname} -> T (ELO: {data.ELO})");
            }
            else
            {
                p.TeamNum = 3; // CT
                team2Total += data.ELO;
                team2Count++;
                Console.WriteLine($"[FaceIT] {data.Nickname} -> CT (ELO: {data.ELO})");
            }
        }

        Server.PrintToChatAll($" [FaceIT] T: {team1Total} ELO ({team1Count}p) | CT: {team2Total} ELO ({team2Count}p)");
        Console.WriteLine($"[FaceIT] Balance complete: T={team1Total} ({team1Count}p) vs CT={team2Total} ({team2Count}p)");
    }

    private void Balance5v5TeamsByELO()
    {
        var players = Utilities.GetPlayers()
            .Where(p => p != null && p.IsValid)
            .Where(p => _playerData.ContainsKey(p.SteamID) && _playerData[p.SteamID].DataLoaded)
            .ToList();

        Console.WriteLine($"[FaceIT] Found {players.Count} players with data for 5v5 balancing");

        if (players.Count < 10)
        {
            Server.PrintToChatAll($" [FaceIT] {players.Count}/10 players with FaceIT data");
            return;
        }

        // Sorteaza si ia primii 10
        players.Sort((a, b) => _playerData[b.SteamID].ELO.CompareTo(_playerData[a.SteamID].ELO));
        if (players.Count > 10) 
            players = players.Take(10).ToList();

        var team1 = new List<CCSPlayerController>();
        var team2 = new List<CCSPlayerController>();
        int team1Total = 0, team2Total = 0;

        foreach (var p in players)
        {
            var data = _playerData[p.SteamID];
            
            if (team1.Count < 5 && (team1Total <= team2Total || team2.Count >= 5))
            {
                team1.Add(p);
                team1Total += data.ELO;
                Console.WriteLine($"[FaceIT] 5v5 {data.Nickname} -> T (ELO: {data.ELO})");
            }
            else
            {
                team2.Add(p);
                team2Total += data.ELO;
                Console.WriteLine($"[FaceIT] 5v5 {data.Nickname} -> CT (ELO: {data.ELO})");
            }
        }

        foreach (var p in team1) p.TeamNum = 2;
        foreach (var p in team2) p.TeamNum = 3;

        Server.PrintToChatAll(" [FaceIT] 5v5 BALANCED!");
        Server.PrintToChatAll($" [FACEIT] T: {team1Total} ELO | CT: {team2Total} ELO");
        
        Console.WriteLine($"[FaceIT] 5v5 Balance complete: T={team1Total} vs CT={team2Total}");
    }

    private void ShowELOStatus(CCSPlayerController player)
    {
        var playersWithData = _playerData.Where(p => p.Value.DataLoaded).ToList();
        
        if (playersWithData.Count == 0)
        {
            player.PrintToChat(" [FaceIT] No players with FaceIT data loaded");
            player.PrintToChat(" [FaceIT] Data loads automatically when players connect");
            return;
        }

        player.PrintToChat(" [FaceIT] Players with FaceIT data:");
        player.PrintToChat(" [FaceIT] ===============================");
        
        var sortedPlayers = playersWithData.OrderByDescending(p => p.Value.ELO).ToList();
        
        foreach (var (steamId, data) in sortedPlayers.Take(15))
        {
            player.PrintToChat($" [FaceIT] {data.Nickname} | ELO: {data.ELO} | Level: {data.Level}");
        }
        
        if (sortedPlayers.Count > 15)
        {
            player.PrintToChat($" [FaceIT] ... and {sortedPlayers.Count - 15} more players");
        }
        
        int totalELO = sortedPlayers.Sum(p => p.Value.ELO);
        int averageELO = sortedPlayers.Count > 0 ? totalELO / sortedPlayers.Count : 0;
        
        player.PrintToChat($" [FaceIT] Total: {sortedPlayers.Count} players | Average ELO: {averageELO}");
    }

    public override void Unload(bool hotReload)
    {
        _httpClient?.Dispose();
        Console.WriteLine("[FaceIT] Plugin unloaded");
    }
}
