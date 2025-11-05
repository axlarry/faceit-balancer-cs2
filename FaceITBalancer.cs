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
    public override string ModuleDescription => "Echilibreaza echipele dupa ELO-ul FaceIT";

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
            Console.WriteLine("[FaceIT] ✅ Plugin incarcat cu API support!");
        }
        else
        {
            Console.WriteLine("[FaceIT] ✅ Plugin incarcat (mod manual)");
            Console.WriteLine("[FaceIT] 💡 Jucatorii pot folosi !setlevel [1-10] sau !setelo [ELO]");
        }
    }

    private void LoadConfig()
    {
        var configPath = Path.Combine(ModuleDirectory, "config.json");
        
        if (!File.Exists(configPath))
        {
            Console.WriteLine("[FaceIT] ❌ config.json nu exista!");
            CreateDefaultConfig(configPath);
            return;
        }

        try
        {
            var json = File.ReadAllText(configPath);
            var config = JsonSerializer.Deserialize<PluginConfig>(json);
            
            if (config != null && !string.IsNullOrEmpty(config.ApiKey) && config.ApiKey != "API_KEY_AICI")
            {
                _apiKey = config.ApiKey;
                _apiEnabled = true;
                
                // Adauga header-ul Authorization corect pentru FaceIT API
                if (!_apiKey.StartsWith("Bearer "))
                {
                    _apiKey = "Bearer " + _apiKey;
                }
                
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", _apiKey);
                Console.WriteLine($"[FaceIT] ✅ API Key configurat: {_apiKey.Substring(0, Math.Min(15, _apiKey.Length))}...");
            }
            else
            {
                Console.WriteLine("[FaceIT] ℹ️  API Key neconfigurat - foloseste mod manual");
                _apiEnabled = false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FaceIT] ❌ Eroare la incarcarea config.json: {ex.Message}");
            _apiEnabled = false;
        }
    }

    private void CreateDefaultConfig(string configPath)
    {
        var defaultConfig = new PluginConfig
        {
            ApiKey = "API_KEY_AICI",
            AutoBalance = false,
            MinPlayers = 10
        };

        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(defaultConfig, options);
        File.WriteAllText(configPath, json);
        
        Console.WriteLine($"[FaceIT] 📁 Config creat: {configPath}");
        Console.WriteLine("[FaceIT] ⚠️  Configureaza API Key in fisierul config!");
    }

    private void RegisterCommands()
    {
        // Comenzi help si info
        AddCommand("css_faceit", "Ajutor pentru comenzi FaceIT", OnFaceITHelpCommand);
        AddCommand("css_faceit_help", "Ajutor pentru comenzi FaceIT", OnFaceITHelpCommand);
        
        // Comenzi admin
        AddCommand("css_fbalance", "Echilibreaza echipele", OnBalanceCommand);
        AddCommand("css_fbalance5v5", "Echilibreaza 5v5", OnBalance5v5Command);
        AddCommand("css_getfaceit_all", "Incarca date FaceIT pentru toti jucatorii", OnGetFaceITAllCommand);
        
        // Comenzi jucatori
        AddCommand("css_getfaceit", "Incarca date FaceIT", OnGetFaceITCommand);
        AddCommand("css_setlevel", "Seteaza level manual", OnSetLevelCommand);
        AddCommand("css_setelo", "Seteaza ELO manual", OnSetELOCommand);
        AddCommand("css_myfaceit", "Vezi datele tale", OnMyFaceITCommand);
        
        // Comenzi admin pentru vizualizare date
        AddCommand("css_playersdata", "Vezi datele tuturor jucatorilor", OnPlayersDataCommand);
    }

    // ================== COMENDA HELP ==================
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
        Console.WriteLine("[FaceIT] 📖 COMENZI DISPONIBILE:");
        Console.WriteLine("[FaceIT] =========================");
        Console.WriteLine("[FaceIT] 🔹 !faceit / !faceit_help - Afiseaza acest ajutor");
        Console.WriteLine("[FaceIT] ");
        Console.WriteLine("[FaceIT] 👤 COMENZI JUCATORI:");
        Console.WriteLine("[FaceIT] 🔹 !getfaceit - Incarca datele tale de pe FaceIT");
        Console.WriteLine("[FaceIT] 🔹 !setlevel [1-10] - Seteaza level manual");
        Console.WriteLine("[FaceIT] 🔹 !setelo [ELO] - Seteaza ELO manual (1-3900)");
        Console.WriteLine("[FaceIT] 🔹 !myfaceit - Vezi datele tale incarcate");
        Console.WriteLine("[FaceIT] ");
        Console.WriteLine("[FaceIT] ⚡ COMENZI ADMIN:");
        Console.WriteLine("[FaceIT] 🔹 !getfaceit_all - Incarca date pentru toti jucatorii");
        Console.WriteLine("[FaceIT] 🔹 !playersdata - Vezi datele tuturor jucatorilor");
        Console.WriteLine("[FaceIT] 🔹 !fbalance - Echilibreaza echipele dupa ELO");
        Console.WriteLine("[FaceIT] 🔹 !fbalance5v5 - Echilibreaza 5v5 dupa ELO");
        Console.WriteLine("[FaceIT] ");
        Console.WriteLine("[FaceIT] 💡 Nota: Echilibrarea se face dupa ELO, nu dupa level!");
    }

    private void ShowHelpInChat(CCSPlayerController player)
    {
        player.PrintToChat(" [FaceIT] 📖 COMENZI DISPONIBILE:");
        player.PrintToChat(" [FaceIT] =========================");
        player.PrintToChat(" [FaceIT] 🔹 !faceit / !faceit_help - Acest ajutor");
        player.PrintToChat(" [FaceIT] ");
        player.PrintToChat(" [FaceIT] 👤 COMENZI JUCATORI:");
        player.PrintToChat(" [FaceIT] 🔹 !getfaceit - Datele tale de pe FaceIT");
        player.PrintToChat(" [FaceIT] 🔹 !setlevel [1-10] - Level manual");
        player.PrintToChat(" [FaceIT] 🔹 !setelo [ELO] - ELO manual (1-3900)");
        player.PrintToChat(" [FaceIT] 🔹 !myfaceit - Vezi datele tale");
        
        if (HasAdminPermissions(player))
        {
            player.PrintToChat(" [FaceIT] ");
            player.PrintToChat(" [FaceIT] ⚡ COMENZI ADMIN:");
            player.PrintToChat(" [FaceIT] 🔹 !getfaceit_all - Date pentru toti jucatorii");
            player.PrintToChat(" [FaceIT] 🔹 !playersdata - Datele tuturor jucatorilor");
            player.PrintToChat(" [FaceIT] 🔹 !fbalance - Echilibreaza echipele");
            player.PrintToChat(" [FaceIT] 🔹 !fbalance5v5 - Echilibreaza 5v5");
        }
        
        player.PrintToChat(" [FaceIT] ");
        player.PrintToChat(" [FaceIT] 💡 Echilibrarea se face dupa ELO!");
    }

    // ================== COMENZI API ==================
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
            player.PrintToChat(" [FaceIT] ❌ Nu pot obtine SteamID");
            Console.WriteLine($"[FaceIT] ❌ Invalid SteamID: {steamID}");
            return;
        }

        if (!_apiEnabled)
        {
            player.PrintToChat(" [FaceIT] ❌ API-ul nu este configurat");
            player.PrintToChat(" [FaceIT] 💡 Foloseste !setlevel pentru setare manuala");
            return;
        }

        player.PrintToChat(" [FaceIT] 🔍 Se cauta datele tale...");
        _ = FetchFaceITData(player, steamID.ToString());
    }

    [CommandHelper(minArgs: 0, usage: "", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnGetFaceITAllCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid) return;

        if (!HasAdminPermissions(player))
        {
            player.PrintToChat(" [FaceIT] ❌ Nu ai permisiune pentru aceasta comanda!");
            return;
        }

        if (!_apiEnabled)
        {
            player.PrintToChat(" [FaceIT] ❌ API-ul nu este configurat");
            return;
        }

        var players = Utilities.GetPlayers()
            .Where(p => p != null && p.IsValid && p.IsBot == false)
            .ToList();

        if (players.Count == 0)
        {
            player.PrintToChat(" [FaceIT] ❌ Nu sunt jucatori pe server");
            return;
        }

        player.PrintToChat($" [FaceIT] 🔍 Se incarca datele pentru {players.Count} jucatori...");
        Server.PrintToChatAll($" [FaceIT] 🔍 Adminul incarca date FaceIT pentru toti jucatorii...");

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
                            
                            // Mica pauza intre request-uri pentru a evita rate limiting
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

        adminPlayer.PrintToChat($" [FaceIT] ✅ Date incarcate pentru {successCount} jucatori");
        if (errorCount > 0)
        {
            adminPlayer.PrintToChat($" [FaceIT] ⚠️  Erori la {errorCount} jucatori");
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
                player.PrintToChat(" [FaceIT] ❌ Eroare de autentificare (API Key invalid)");
                Console.WriteLine($"[FaceIT] ❌ API Key invalid sau expirat");
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                player.PrintToChat(" [FaceIT] ❌ Jucatorul nu a fost gasit pe FaceIT");
                Console.WriteLine($"[FaceIT] ❌ Player not found on FaceIT");
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                player.PrintToChat(" [FaceIT] ❌ Prea multe request-uri. Asteapta putin.");
                Console.WriteLine($"[FaceIT] ❌ Rate limit exceeded");
            }
            else
            {
                player.PrintToChat(" [FaceIT] ❌ Eroare la obtinerea datelor");
                Console.WriteLine($"[FaceIT] ❌ API Error: {response.StatusCode}");
            }
        }
        catch (HttpRequestException ex)
        {
            player.PrintToChat(" [FaceIT] ❌ Eroare de retea");
            Console.WriteLine($"[FaceIT] ❌ Network Error: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            player.PrintToChat(" [FaceIT] ❌ Timeout la conexiunea cu FaceIT");
            Console.WriteLine($"[FaceIT] ❌ Request timeout");
        }
        catch (Exception ex)
        {
            player.PrintToChat(" [FaceIT] ❌ Eroare neasteptata");
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

            // Verifica daca exista erori in raspuns
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

            // Extrage nickname
            var nickname = root.TryGetProperty("nickname", out var nicknameElement) 
                ? nicknameElement.GetString() ?? player.PlayerName 
                : player.PlayerName;

            // Extrage datele CS2
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
                player.PrintToChat(" [FaceIT] ⚠️  Nu s-au gasit date pentru CS2");
                Console.WriteLine($"[FaceIT] ⚠️  No CS2 data found in response");
                return;
            }

            // Salveaza datele
            _playerData[player.SteamID] = new PlayerData
            {
                Level = skillLevel,
                ELO = faceitElo,
                Nickname = nickname,
                DataLoaded = true
            };

            player.PrintToChat($" [FaceIT] ✅ Date incarcate: {nickname}");
            player.PrintToChat($" [FaceIT] 🎯 Level: {skillLevel} | ELO: {faceitElo}");
            
            Console.WriteLine($"[FaceIT] ✅ Data loaded for {nickname}: Level {skillLevel}, ELO {faceitElo}");
        }
        catch (JsonException ex)
        {
            player.PrintToChat(" [FaceIT] ❌ Eroare la interpretarea datelor");
            Console.WriteLine($"[FaceIT] ❌ JSON Parse Error: {ex.Message}");
            Console.WriteLine($"[FaceIT] ❌ JSON Content: {json}");
        }
        catch (Exception ex)
        {
            player.PrintToChat(" [FaceIT] ❌ Eroare neasteptata");
            Console.WriteLine($"[FaceIT] ❌ Parse Unexpected Error: {ex.Message}");
        }
    }

    // ================== COMENZI MANUALE ==================
    [CommandHelper(minArgs: 1, usage: "[level 1-10]", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnSetLevelCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid) return;

        string levelStr = command.GetArg(1);
        if (!int.TryParse(levelStr, out int level) || level < 1 || level > 10)
        {
            player.PrintToChat(" [FaceIT] ❌ Level trebuie sa fie intre 1-10");
            player.PrintToChat(" [FaceIT] 💡 Foloseste: !setlevel [1-10]");
            return;
        }

        int elo = LevelToELO(level);
        SetManualData(player, level, elo);
        player.PrintToChat($" [FaceIT] ✅ Level setat: {level} (ELO: {elo})");
        Console.WriteLine($"[FaceIT] ✅ Manual data set for {player.PlayerName}: Level {level}, ELO {elo}");
    }

    [CommandHelper(minArgs: 1, usage: "[ELO]", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnSetELOCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid) return;

        string eloStr = command.GetArg(1);
        if (!int.TryParse(eloStr, out int elo) || elo < 1 || elo > 3900)
        {
            player.PrintToChat(" [FaceIT] ❌ ELO trebuie sa fie intre 1-3900");
            player.PrintToChat(" [FaceIT] 💡 Foloseste: !setelo [ELO]");
            return;
        }

        int level = ELOToLevel(elo);
        SetManualData(player, level, elo);
        player.PrintToChat($" [FaceIT] ✅ ELO setat: {elo} (Level: {level})");
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
            player.PrintToChat(" [FaceIT] 💡 Foloseste !getfaceit sau !setlevel [1-10]");
            player.PrintToChat(" [FaceIT] 💡 Vezi toate comenzile cu !faceit_help");
        }
    }

    // ================== COMENZI ADMIN ==================
    [CommandHelper(minArgs: 0, usage: "", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnPlayersDataCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid) 
        {
            // Executat din consolă server
            ShowPlayersDataInConsole();
            return;
        }

        // Executat de jucător - verifică permisiuni admin
        if (!HasAdminPermissions(player))
        {
            player.PrintToChat(" [FaceIT] ❌ Nu ai permisiune pentru aceasta comanda!");
            player.PrintToChat(" [FaceIT] 💡 Vezi comenzile disponibile cu !faceit_help");
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
            adminPlayer.PrintToChat(" [FaceIT] 📊 Niciun jucator cu date incarcate");
            adminPlayer.PrintToChat(" [FaceIT] 💡 Foloseste !getfaceit_all pentru a incarca date");
            return;
        }

        adminPlayer.PrintToChat(" [FaceIT] 📊 Jucatori cu date incarcate:");
        adminPlayer.PrintToChat(" [FaceIT] ===============================");
        
        // Sorteaza dupa ELO (descrescator)
        var sortedPlayers = playersWithData.OrderByDescending(p => p.Value.ELO).ToList();
        
        foreach (var (steamId, data) in sortedPlayers.Take(10)) // Limita la primele 10 pentru chat
        {
            adminPlayer.PrintToChat($" [FaceIT] 👤 {data.Nickname} | ELO: {data.ELO} | Level: {data.Level}");
        }
        
        if (sortedPlayers.Count > 10)
        {
            adminPlayer.PrintToChat($" [FaceIT] 📈 ... si inca {sortedPlayers.Count - 10} jucatori");
        }
        
        int totalELO = sortedPlayers.Sum(p => p.Value.ELO);
        int averageELO = sortedPlayers.Count > 0 ? totalELO / sortedPlayers.Count : 0;
        
        adminPlayer.PrintToChat($" [FaceIT] 📈 Total: {sortedPlayers.Count} jucatori | ELO Mediu: {averageELO}");
        
        // Afișează și în consolă pentru detalii complete
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
            player.PrintToChat(" [FaceIT] ❌ Nu ai permisiune!");
            player.PrintToChat(" [FaceIT] 💡 Vezi comenzile disponibile cu !faceit_help");
            return;
        }

        BalanceTeamsByELO();
        Server.PrintToChatAll(" [FaceIT] ✅ Echipe echilibrate de admin!");
    }

    [CommandHelper(minArgs: 0, usage: "", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnBalance5v5Command(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid) return;

        if (!HasAdminPermissions(player))
        {
            player.PrintToChat(" [FaceIT] ❌ Nu ai permisiune!");
            player.PrintToChat(" [FaceIT] 💡 Vezi comenzile disponibile cu !faceit_help");
            return;
        }

        Balance5v5TeamsByELO();
        Server.PrintToChatAll(" [FaceIT] ✅ 5v5 echilibrat de admin!");
    }

    // ================== FUNCTII ECHILIBRARE ==================
    private void BalanceTeamsByELO()
    {
        var players = Utilities.GetPlayers()
            .Where(p => p != null && p.IsValid && (p.TeamNum == 2 || p.TeamNum == 3))
            .Where(p => _playerData.ContainsKey(p.SteamID) && _playerData[p.SteamID].DataLoaded)
            .ToList();

        Console.WriteLine($"[FaceIT] 🔍 Found {players.Count} players with data for balancing");

        if (players.Count < 2)
        {
            Server.PrintToChatAll(" [FaceIT] ❌ Nu sunt suficienti jucatori cu date");
            Server.PrintToChatAll(" [FaceIT] 💡 Folositi !getfaceit sau !setlevel [1-10]");
            Server.PrintToChatAll(" [FaceIT] 💡 Adminii pot folosi !getfaceit_all pentru toti jucatorii");
            return;
        }

        // Sorteaza dupa ELO (descrescator) - ECHILIBRARE DUPA ELO
        players.Sort((a, b) => _playerData[b.SteamID].ELO.CompareTo(_playerData[a.SteamID].ELO));

        int team1Total = 0, team2Total = 0;
        int team1Count = 0, team2Count = 0;

        foreach (var player in players)
        {
            var playerData = _playerData[player.SteamID];
            
            if (team1Total <= team2Total && team1Count < (players.Count + 1) / 2)
            {
                // Schimba in Terrorist (echipa 2)
                player.TeamNum = 2;
                team1Total += playerData.ELO;
                team1Count++;
                Console.WriteLine($"[FaceIT] ➡️  {playerData.Nickname} -> T (ELO: {playerData.ELO})");
            }
            else
            {
                // Schimba in Counter-Terrorist (echipa 3)
                player.TeamNum = 3;
                team2Total += playerData.ELO;
                team2Count++;
                Console.WriteLine($"[FaceIT] ➡️  {playerData.Nickname} -> CT (ELO: {playerData.ELO})");
            }
        }

        Server.PrintToChatAll($" [FaceIT] ✅ Echipe echilibrate dupa ELO!");
        Server.PrintToChatAll($" [FACEIT] █ T: {team1Total} ELO ({team1Count}j) | █ CT: {team2Total} ELO ({team2Count}j)");
        
        Console.WriteLine($"[FaceIT] ✅ Balance complete: T={team1Total} ({team1Count}j) vs CT={team2Total} ({team2Count}j)");
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
            Server.PrintToChatAll($" [FaceIT] ❌ {players.Count}/10 jucatori cu date");
            Server.PrintToChatAll(" [FaceIT] 💡 Folositi !getfaceit sau !setlevel [1-10]");
            return;
        }

        // Sorteaza dupa ELO (descrescator) si ia primii 10 - ECHILIBRARE DUPA ELO
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

        // Aplica echipele
        foreach (var player in team1) 
            player.TeamNum = 2; // Terrorist
        foreach (var player in team2) 
            player.TeamNum = 3; // Counter-Terrorist

        Server.PrintToChatAll(" [FaceIT] ✅ 5v5 BALANCED dupa ELO!");
        Server.PrintToChatAll($" [FACEIT] █ T: {team1Total} ELO | █ CT: {team2Total} ELO");
        
        Console.WriteLine($"[FaceIT] ✅ 5v5 Balance complete: T={team1Total} vs CT={team2Total}");
    }

    // ================== FUNCTII UTILITARE ==================
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
        Console.WriteLine("[FaceIT] Plugin descarcat");
    }
}
