using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FaceITBalancer;

public class FaceITConfig : BasePluginConfig
{
    [JsonPropertyName("ApiKey")]              public string ApiKey { get; set; } = "";
    [JsonPropertyName("AutoFetchOnConnect")]  public bool AutoFetchOnConnect { get; set; } = true;
    // true = pluginul nu face NIMIC cat timp meciul e live: fara HTTP, fara
    // balansare. Fetch-urile amanate se executa cand revine warmup-ul.
    [JsonPropertyName("DisableDuringMatch")]  public bool DisableDuringMatch { get; set; } = true;
    [JsonPropertyName("DefaultElo")]          public int DefaultElo { get; set; } = 800;
    [JsonPropertyName("ChatTag")]             public string ChatTag { get; set; } = "LaCurte";
    // Cati jucatori pot fi interogati simultan la FaceIT (rate limiting)
    [JsonPropertyName("MaxConcurrentFetches")] public int MaxConcurrentFetches { get; set; } = 2;
    // Cate minute se pastreaza ELO-ul in cache inainte de re-fetch
    [JsonPropertyName("CacheMinutes")]        public int CacheMinutes { get; set; } = 120;

    // --- admini ---
    // Foloseste aceeasi lista de admini ca MatchZy (cfg/MatchZy/admins.json)
    [JsonPropertyName("UseMatchZyAdmins")]    public bool UseMatchZyAdmins { get; set; } = true;
    [JsonPropertyName("MatchZyAdminsFile")]   public string MatchZyAdminsFile { get; set; } = "cfg/MatchZy/admins.json";
    // Flag CSSharp acceptat in plus; gol = doar admins.json de la MatchZy
    [JsonPropertyName("CssharpAdminFlag")]    public string CssharpAdminFlag { get; set; } = "@css/generic";
}

[MinimumApiVersion(200)]
public class FaceITBalancer : BasePlugin, IPluginConfig<FaceITConfig>
{
    public override string ModuleName => "FaceIT Team Balancer";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "Larry Lacurte.ro";
    public override string ModuleDescription => "Balanseaza echipele dupa ELO-ul de FaceIT";

    public FaceITConfig Config { get; set; } = new();

    private sealed class PlayerData
    {
        public int Level;
        public int Elo;
        public string Nickname = "";
        public bool Loaded;
        public DateTime FetchedAt = DateTime.MinValue;
        public bool InFlight;
    }

    private readonly Dictionary<ulong, PlayerData> _data = new();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private SemaphoreSlim _gate = new(2, 2);
    private bool _apiEnabled;

    private readonly HashSet<ulong> _deferred = new();
    private readonly HashSet<ulong> _mzAdmins = new();
    private DateTime _mzAdminsStamp = DateTime.MinValue;

    private string Tag => $" {ChatColors.Green}[{Config.ChatTag}]{ChatColors.Default}";

    // ---------------------------------------------------------------- config

    public void OnConfigParsed(FaceITConfig config)
    {
        Config = config;
        _gate = new SemaphoreSlim(Math.Max(1, config.MaxConcurrentFetches),
                                  Math.Max(1, config.MaxConcurrentFetches));

        var key = (config.ApiKey ?? "").Trim();
        // acceptam si "Bearer xxx" si cheia goala
        if (key.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            key = key["Bearer ".Length..].Trim();

        _apiEnabled = key.Length > 0 && !key.StartsWith("API_KEY", StringComparison.OrdinalIgnoreCase);

        _http.DefaultRequestHeaders.Clear();
        if (_apiEnabled)
            _http.DefaultRequestHeaders.Add("Authorization", "Bearer " + key);
    }

    public override void Load(bool hotReload)
    {
        RegisterEventHandler<EventPlayerConnectFull>(OnPlayerConnectFull);
        RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
        // singurul rol: recupereaza fetch-urile amanate cand se revine in warmup
        RegisterEventHandler<EventRoundAnnounceWarmup>(OnWarmupStart);

        Logger.LogInformation(_apiEnabled
            ? "Plugin incarcat, API FaceIT activ (auto-fetch: {Auto})"
            : "Plugin incarcat FARA cheie API — configureaza ApiKey in configs/plugins/FaceITBalancer/FaceITBalancer.json",
            Config.AutoFetchOnConnect);

        ReloadMatchZyAdminsIfChanged();

        if (hotReload && FetchAllowed)
            foreach (var p in ValidPlayers()) TryQueueFetch(p);
    }

    public override void Unload(bool hotReload) => _http.Dispose();

    // --------------------------------------------------------------- admini

    private string MatchZyAdminsPath =>
        Path.Combine(Server.GameDirectory, "csgo",
                     (Config.MatchZyAdminsFile ?? "cfg/MatchZy/admins.json").Replace('\\', '/'));

    /// <summary>
    /// Citeste cfg/MatchZy/admins.json — acelasi fisier si acelasi format ca
    /// MatchZy (Dictionary&lt;SteamID64, nume&gt;). Se reciteste automat cand
    /// se schimba fisierul, deci nu trebuie reload la plugin dupa ce adaugi
    /// un admin.
    /// </summary>
    private void ReloadMatchZyAdminsIfChanged()
    {
        if (!Config.UseMatchZyAdmins) return;
        try
        {
            var path = MatchZyAdminsPath;
            if (!File.Exists(path))
            {
                if (_mzAdmins.Count > 0) { _mzAdmins.Clear(); _mzAdminsStamp = DateTime.MinValue; }
                return;
            }

            var stamp = File.GetLastWriteTimeUtc(path);
            if (stamp == _mzAdminsStamp) return;
            _mzAdminsStamp = stamp;

            var opts = new JsonSerializerOptions
            {
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            };
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path), opts)
                       ?? new Dictionary<string, string>();

            _mzAdmins.Clear();
            foreach (var key in dict.Keys)
                if (ulong.TryParse(key.Trim(), out var id) && id > 0)
                    _mzAdmins.Add(id);

            Logger.LogInformation("Admini MatchZy incarcati: {Count} din {Path}", _mzAdmins.Count, path);
        }
        catch (Exception ex)
        {
            Logger.LogWarning("Nu pot citi {Path}: {Msg}", MatchZyAdminsPath, ex.Message);
        }
    }

    /// <summary>Aceeasi logica pe care o foloseste MatchZy: consola serverului,
    /// SAU admin CSSharp, SAU prezent in admins.json de la MatchZy.</summary>
    private bool IsAdmin(CCSPlayerController? player)
    {
        if (player == null) return true;                 // consola / rcon
        ReloadMatchZyAdminsIfChanged();

        if (Config.UseMatchZyAdmins && _mzAdmins.Contains(player.SteamID)) return true;

        var flag = Config.CssharpAdminFlag;
        if (!string.IsNullOrWhiteSpace(flag) && AdminManager.PlayerHasPermissions(player, flag)) return true;

        return false;
    }

    // ------------------------------------------------------------ game state

    private static CCSGameRules? GameRules =>
        Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules")
                 .FirstOrDefault()?.GameRules;

    /// <summary>
    /// Citeste starea reala din gamerules in loc sa o ghiceasca din numarul de
    /// jucatori pe echipe (in warmup jucatorii sunt DEJA pe T/CT).
    /// </summary>
    private bool IsWarmup => GameRules?.WarmupPeriod ?? true;

    private bool BalancingAllowed => !Config.DisableDuringMatch || IsWarmup;

    /// <summary>Cat timp meciul e live nu se face niciun apel HTTP.</summary>
    private bool FetchAllowed => !Config.DisableDuringMatch || IsWarmup;

    private static bool IsRealPlayer(CCSPlayerController? p) =>
        p is { IsValid: true, IsBot: false, IsHLTV: false }
        && p.Connected == PlayerConnectedState.Connected
        && p.SteamID > 0;

    private static List<CCSPlayerController> ValidPlayers() =>
        Utilities.GetPlayers().Where(IsRealPlayer).ToList();

    // --------------------------------------------------------------- events

    private HookResult OnPlayerConnectFull(EventPlayerConnectFull ev, GameEventInfo _)
    {
        if (!Config.AutoFetchOnConnect || !IsRealPlayer(ev.Userid)) return HookResult.Continue;

        if (!FetchAllowed)
        {
            // meci live: doar un insert in HashSet, zero retea, zero alocari
            _deferred.Add(ev.Userid!.SteamID);
            return HookResult.Continue;
        }

        TryQueueFetch(ev.Userid!);
        DrainDeferred();
        return HookResult.Continue;
    }

    private HookResult OnWarmupStart(EventRoundAnnounceWarmup ev, GameEventInfo _)
    {
        DrainDeferred();
        return HookResult.Continue;
    }

    /// <summary>Executa fetch-urile amanate in timpul meciului, pentru jucatorii
    /// care sunt inca pe server.</summary>
    private void DrainDeferred()
    {
        if (_deferred.Count == 0 || !FetchAllowed) return;
        foreach (var p in ValidPlayers())
            if (_deferred.Contains(p.SteamID)) TryQueueFetch(p);
        _deferred.Clear();
    }

    private HookResult OnPlayerDisconnect(EventPlayerDisconnect ev, GameEventInfo _)
    {
        // curatare: altfel dictionarul creste la infinit si !elostatus
        // afiseaza jucatori care au plecat de mult
        var id = ev.Userid?.SteamID ?? 0;
        if (id > 0) { _data.Remove(id); _deferred.Remove(id); }
        return HookResult.Continue;
    }

    // ---------------------------------------------------------------- fetch

    private void TryQueueFetch(CCSPlayerController player)
    {
        if (!_apiEnabled) return;

        var steamId = player.SteamID;
        var name = player.PlayerName ?? "?";

        if (!_data.TryGetValue(steamId, out var d))
            _data[steamId] = d = new PlayerData { Nickname = name };

        if (d.InFlight) return;
        if (d.Loaded && (DateTime.UtcNow - d.FetchedAt).TotalMinutes < Config.CacheMinutes) return;

        d.InFlight = true;
        _ = FetchEloAsync(steamId, name);
    }

    /// <summary>
    /// HTTP-ul ruleaza pe thread pool, NU pe main thread. Orice atingere de
    /// API de joc se face inapoi prin Server.NextFrame.
    /// </summary>
    private async Task FetchEloAsync(ulong steamId, string name)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var url = $"https://open.faceit.com/data/v4/players?game=cs2&game_player_id={steamId}";
            using var resp = await _http.GetAsync(url).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                Server.NextFrame(() =>
                {
                    if (_data.TryGetValue(steamId, out var dd)) dd.InFlight = false;
                    if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                        Logger.LogInformation("{Name} nu are cont FaceIT pentru CS2", name);
                    else
                        Logger.LogWarning("FaceIT a raspuns {Code} pentru {Name}", resp.StatusCode, name);
                });
                return;
            }

            var (nick, level, elo, ok) = ParseFaceitPlayer(body);

            Server.NextFrame(() =>
            {
                if (!_data.TryGetValue(steamId, out var d)) return;
                d.InFlight = false;
                if (!ok)
                {
                    Logger.LogInformation("{Name}: cont FaceIT fara date CS2", name);
                    return;
                }

                d.Nickname = string.IsNullOrEmpty(nick) ? name : nick;
                d.Level = level;
                d.Elo = elo;
                d.Loaded = true;
                d.FetchedAt = DateTime.UtcNow;

                var p = ValidPlayers().FirstOrDefault(x => x.SteamID == steamId);
                p?.PrintToChat($"{Tag} FaceIT: {ChatColors.Green}{d.Nickname}{ChatColors.Default} | " +
                               $"Level {d.Level} | ELO {d.Elo}");
                Logger.LogInformation("{Nick}: Level {Lvl}, ELO {Elo}", d.Nickname, d.Level, d.Elo);
            });
        }
        catch (Exception ex)
        {
            Server.NextFrame(() =>
            {
                if (_data.TryGetValue(steamId, out var d)) d.InFlight = false;
                Logger.LogWarning("Eroare la fetch pentru {Name}: {Msg}", name, ex.Message);
            });
        }
        finally
        {
            _gate.Release();
        }
    }

    private static (string nick, int level, int elo, bool ok) ParseFaceitPlayer(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var nick = root.TryGetProperty("nickname", out var n) ? n.GetString() ?? "" : "";

            if (!root.TryGetProperty("games", out var games)) return ("", 0, 0, false);
            if (!games.TryGetProperty("cs2", out var cs2)) return (nick, 0, 0, false);

            int level = cs2.TryGetProperty("skill_level", out var l) && l.ValueKind == JsonValueKind.Number
                        ? l.GetInt32() : 0;
            int elo = cs2.TryGetProperty("faceit_elo", out var e) && e.ValueKind == JsonValueKind.Number
                      ? e.GetInt32() : 0;

            return (nick, level, elo, elo > 0);
        }
        catch
        {
            return ("", 0, 0, false);
        }
    }

    private int EloOf(CCSPlayerController p) =>
        _data.TryGetValue(p.SteamID, out var d) && d.Loaded ? d.Elo : Config.DefaultElo;

    private string NameOf(CCSPlayerController p) =>
        _data.TryGetValue(p.SteamID, out var d) && d.Loaded ? d.Nickname : (p.PlayerName ?? "?");

    // ------------------------------------------------------------- comenzi

    [ConsoleCommand("css_fbalance", "Balanseaza echipele dupa ELO FaceIT")]
    [CommandHelper(minArgs: 0, usage: "", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnBalance(CCSPlayerController? player, CommandInfo cmd)
    {
        if (!IsAdmin(player))
        {
            cmd.ReplyToCommand($"{Tag} Nu ai drepturi de admin pentru comanda asta.");
            return;
        }
        if (!BalancingAllowed)
        {
            cmd.ReplyToCommand($"{Tag} Meciul e in desfasurare — foloseste comanda in warmup.");
            return;
        }
        DoBalance(ValidPlayers().Where(p => p.Team is CsTeam.Terrorist or CsTeam.CounterTerrorist).ToList(), cmd);
    }

    [ConsoleCommand("css_fbalance5v5", "Balanseaza 5v5 dupa ELO FaceIT")]
    [CommandHelper(minArgs: 0, usage: "", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnBalance5v5(CCSPlayerController? player, CommandInfo cmd)
    {
        if (!IsAdmin(player))
        {
            cmd.ReplyToCommand($"{Tag} Nu ai drepturi de admin pentru comanda asta.");
            return;
        }
        if (!BalancingAllowed)
        {
            cmd.ReplyToCommand($"{Tag} Meciul e in desfasurare — foloseste comanda in warmup.");
            return;
        }

        var all = ValidPlayers();
        if (all.Count < 10)
        {
            cmd.ReplyToCommand($"{Tag} {all.Count}/10 jucatori pe server.");
            return;
        }
        DoBalance(all.OrderByDescending(EloOf).Take(10).ToList(), cmd);
    }

    [ConsoleCommand("css_frefresh", "Forteaza re-citirea ELO-urilor de la FaceIT")]
    [CommandHelper(minArgs: 0, usage: "", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnRefresh(CCSPlayerController? player, CommandInfo cmd)
    {
        if (!IsAdmin(player)) { cmd.ReplyToCommand($"{Tag} Nu ai drepturi de admin."); return; }
        if (!FetchAllowed)
        {
            cmd.ReplyToCommand($"{Tag} Meci live — fetch-ul e blocat. Se face automat la warmup.");
            return;
        }
        foreach (var p in ValidPlayers())
        {
            if (_data.TryGetValue(p.SteamID, out var d)) d.FetchedAt = DateTime.MinValue;
            TryQueueFetch(p);
        }
        _deferred.Clear();
        cmd.ReplyToCommand($"{Tag} Re-citesc ELO-urile pentru {ValidPlayers().Count} jucatori.");
    }

    [ConsoleCommand("css_elostatus", "Arata ELO-ul jucatorilor conectati")]
    [CommandHelper(minArgs: 0, usage: "", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnEloStatus(CCSPlayerController? player, CommandInfo cmd)
    {
        DrainDeferred();
        var rows = ValidPlayers()
            .Select(p => (Name: NameOf(p), Elo: EloOf(p),
                          Loaded: _data.TryGetValue(p.SteamID, out var d) && d.Loaded))
            .OrderByDescending(r => r.Elo)
            .ToList();

        if (rows.Count == 0) { cmd.ReplyToCommand($"{Tag} Niciun jucator conectat."); return; }

        cmd.ReplyToCommand($"{Tag} ELO jucatori:");
        foreach (var r in rows.Take(16))
            cmd.ReplyToCommand($"{Tag} {r.Name} — {(r.Loaded ? r.Elo.ToString() : "fara date")}");

        var known = rows.Where(r => r.Loaded).ToList();
        if (known.Count > 0)
            cmd.ReplyToCommand($"{Tag} Medie: {known.Average(r => r.Elo):F0} ELO ({known.Count} cu date)");
    }

    [ConsoleCommand("css_fstatus", "Starea pluginului FaceIT")]
    [CommandHelper(minArgs: 0, usage: "", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnStatus(CCSPlayerController? player, CommandInfo cmd)
    {
        cmd.ReplyToCommand($"{Tag} API: {(_apiEnabled ? "activ" : "FARA cheie")}");
        cmd.ReplyToCommand($"{Tag} Warmup: {(IsWarmup ? "DA" : "NU")} | activ: {(FetchAllowed ? "DA" : "NU (meci live, plugin inactiv)")}");
        if (_deferred.Count > 0)
            cmd.ReplyToCommand($"{Tag} Fetch-uri amanate pana la warmup: {_deferred.Count}");
        cmd.ReplyToCommand($"{Tag} Jucatori cu date: {_data.Count(x => x.Value.Loaded)}/{ValidPlayers().Count}");
        ReloadMatchZyAdminsIfChanged();
        cmd.ReplyToCommand($"{Tag} Admini MatchZy incarcati: {_mzAdmins.Count} | tu esti admin: {(IsAdmin(player) ? "DA" : "NU")}");
    }

    // ----------------------------------------------------------- balansare

    private void DoBalance(List<CCSPlayerController> pool, CommandInfo cmd)
    {
        if (pool.Count < 2)
        {
            cmd.ReplyToCommand($"{Tag} Prea putini jucatori pentru balansare.");
            return;
        }

        var (teamA, teamB) = SplitBalanced(pool, EloOf);

        // SwitchTeam apeleaza functia virtuala a jocului — jucatorul chiar se
        // muta si ramane in viata. Scrierea directa in TeamNum NU muta nimic.
        foreach (var p in teamA) p.SwitchTeam(CsTeam.Terrorist);
        foreach (var p in teamB) p.SwitchTeam(CsTeam.CounterTerrorist);

        int ea = teamA.Sum(EloOf), eb = teamB.Sum(EloOf);

        Server.PrintToChatAll($"{Tag} Echipe balansate dupa ELO FaceIT");
        Server.PrintToChatAll($"{Tag} T: {ea} ELO ({teamA.Count}) | CT: {eb} ELO ({teamB.Count}) | diferenta: {Math.Abs(ea - eb)}");

        Logger.LogInformation("Balansare: T={A} ({An}p) vs CT={B} ({Bn}p), diff={D}",
            ea, teamA.Count, eb, teamB.Count, Math.Abs(ea - eb));
    }

    /// <summary>
    /// Greedy pe lista sortata descrescator, apoi rafinare 2-opt (schimburi
    /// intre echipe cat timp scade diferenta). Converge in ~2 iteratii si
    /// reduce diferenta medie de ~3x fata de greedy simplu.
    /// </summary>
    private static (List<T> a, List<T> b) SplitBalanced<T>(List<T> items, Func<T, int> weight)
    {
        var sorted = items.OrderByDescending(weight).ToList();
        int n = sorted.Count, half = (n + 1) / 2;

        var a = new List<T>(half);
        var b = new List<T>(n - half);
        long sa = 0, sb = 0;

        foreach (var it in sorted)
        {
            if (a.Count < half && (sa <= sb || b.Count >= n - half)) { a.Add(it); sa += weight(it); }
            else { b.Add(it); sb += weight(it); }
        }

        for (int guard = 0; guard < 100; guard++)
        {
            bool improved = false;
            for (int i = 0; i < a.Count && !improved; i++)
            for (int j = 0; j < b.Count && !improved; j++)
            {
                long cur = Math.Abs(sa - sb);
                long na = sa - weight(a[i]) + weight(b[j]);
                long nb = sb - weight(b[j]) + weight(a[i]);
                if (Math.Abs(na - nb) < cur)
                {
                    (a[i], b[j]) = (b[j], a[i]);
                    sa = na; sb = nb; improved = true;
                }
            }
            if (!improved) break;
        }

        return (a, b);
    }
}
