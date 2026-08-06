FaceIT Team Balancer for CS2 v1.2.0
===================================

A Counter-Strike 2 plugin that fetches player ELO from FaceIT and balances teams
accordingly. Built for competitive match servers running MatchZy.

Features
--------
🔍 **Automatic ELO Fetching** - Pulls player ELO from the FaceIT Data API on connect
⚖️ **Near-Optimal Balancing** - Greedy split with 2-opt refinement
🤖 **Reliable Match Detection** - Reads the actual warmup state from game rules
💤 **Zero Background Work** - No timers; fully idle during live matches
👮 **Shared Admin List** - Reuses MatchZy's `cfg/MatchZy/admins.json`
🔧 **Configurable** - Managed by CounterStrikeSharp's config system
📊 **Real-time Status** - Inspect plugin state and ELO data at any time
💬 **Chat Integration** - All commands accessible via chat

Zero Background Work
--------------------
The plugin registers no timers and never polls. Code runs on three events only:

| Event | What it does |
|---|---|
| `EventPlayerConnectFull` | queries FaceIT for the joining player |
| `EventPlayerDisconnect` | drops that player's cached data |
| `EventRoundAnnounceWarmup` | flushes fetches deferred during the match |

While a match is live (`DisableDuringMatch: true`), a player connecting costs a
single `HashSet` insert — no HTTP request, no iteration over players. The lookup
is performed once warmup returns.

Match Detection
---------------
The plugin reads `CCSGameRules.WarmupPeriod` directly instead of guessing from
how many players sit on T and CT. Player counts are unreliable, because during
warmup everyone is already assigned to a team.

- Balancing commands work during warmup only
- No FaceIT API calls are made while a match is live
- Deferred lookups resume automatically when warmup starts

Commands
--------
Admin commands:
• `!fbalance` - Balance every player on T/CT by ELO
• `!fbalance5v5` - Balance the top 10 players by ELO
• `!frefresh` - Force a re-fetch of all ELO data

Player commands:
• `!elostatus` - Show ELO of connected players
• `!fstatus` - Show plugin state, warmup status and admin count

Balancing commands are rejected while a match is live.

Admins
------
By default the plugin reads `cfg/MatchZy/admins.json` — the same file and the
same format MatchZy uses, so there is no second list to maintain:

```json
{
  "76561198154367261": "Player One",
  "76561198000000002": "Player Two"
}
```

The file is re-read automatically whenever it changes (compared by
`LastWriteTimeUtc`), so adding an admin needs no plugin reload and no restart.

A caller is treated as an admin if any of the following is true:
- the command came from the server console or RCON
- the SteamID64 is listed in MatchZy's `admins.json`
- the player holds the CounterStrikeSharp flag set in `CssharpAdminFlag`

Set `"CssharpAdminFlag": ""` to rely on the MatchZy list only.

Requirements
------------
- CounterStrikeSharp **1.0.369 or newer** (these builds run on .NET 10)
- Metamod:Source 2.0
- A **server-side** API key from https://developers.faceit.com
  (client-side keys return 401 on the `/data/v4/players` endpoint)

Installation
------------
```bash
git clone https://github.com/axlarry/faceit-balancer-cs2.git
cd faceit-balancer-cs2
chmod +x build.sh
./build.sh -d /path/to/your/cs2/server
```

`build.sh` handles everything:
- installs the .NET 10 SDK into `~/.dotnet` if missing (no root required)
- detects the CounterStrikeSharp version installed on the server and compiles
  against that exact version
- deploys the build to `addons/counterstrikesharp/plugins/FaceITBalancer/`

Pass `--api-version 1.0.371` if auto-detection fails.

Configuration
-------------
Start the server once; CounterStrikeSharp generates the config at:

```
addons/counterstrikesharp/configs/plugins/FaceITBalancer/FaceITBalancer.json
```

Put the raw key in `ApiKey` — do **not** prefix it with `Bearer`, the plugin
adds that header itself. Then reload:

```
css_plugins reload FaceITBalancer
css_fstatus
```

`css_fstatus` reports `API: active` once the key is accepted.

| Key | Default | Description |
|---|---|---|
| `ApiKey` | `""` | FaceIT server-side API key |
| `AutoFetchOnConnect` | `true` | Look up ELO when a player joins |
| `DisableDuringMatch` | `true` | Plugin fully idle while a match is live |
| `DefaultElo` | `800` | ELO assumed for players without a FaceIT account |
| `ChatTag` | `"FaceIT"` | Chat prefix |
| `MaxConcurrentFetches` | `2` | Parallel requests to the FaceIT API |
| `CacheMinutes` | `120` | How long ELO stays cached |
| `UseMatchZyAdmins` | `true` | Read admins from MatchZy's list |
| `MatchZyAdminsFile` | `cfg/MatchZy/admins.json` | Path relative to `csgo/` |
| `CssharpAdminFlag` | `@css/generic` | Additional CounterStrikeSharp flag |

See [`FaceITBalancer.example.json`](FaceITBalancer.example.json) for a complete
example.

Balancing Algorithm
-------------------
Players are sorted by ELO and split greedily, then refined with 2-opt: pairs are
swapped between teams as long as the ELO gap shrinks. It converges in about two
iterations.

Measured over 2000 randomly generated 10-player lineups with realistic FaceIT
ELO distributions:

| Algorithm | Mean gap | Median | Worst case |
|---|---|---|---|
| Plain greedy | 208 | 158 | 1079 |
| Greedy + 2-opt | **64** | 44 | 639 |
| Optimal (brute force) | 29 | 17 | 639 |

The refinement finds the exact optimum in 42% of cases, at negligible cost.

Changelog
---------
### v1.2.0

Complete rewrite. Fixes the following:

- **Players were never moved.** `TeamNum` is a plain schema field; writing to it
  does not invoke the game's team-change function. The plugin now calls
  `SwitchTeam()`, which moves the player without killing them.
- **Match detection misfired during warmup.** It inferred a live match from the
  number of players on T/CT, but during warmup players are already assigned, so
  the plugin disabled itself exactly when it was supposed to work. It now reads
  `CCSGameRules.WarmupPeriod`.
- **The config was never loaded.** `config.example.json` did not match the
  deserialized class, so the API silently stayed disabled. Replaced with
  `IPluginConfig<T>`, whose schema CounterStrikeSharp validates.
- **Synchronous HTTP on the main thread.** `task.Wait()` with a 15-second
  timeout could stall the server tick. Requests now run on the thread pool and
  return to the main thread through `Server.NextFrame`.
- **Three always-on timers** firing every 1, 5 and 10 seconds — including during
  live matches and on an empty server. Removed; the plugin is now entirely
  event-driven.
- **Built for net7.0 against API 1.0.25**, incompatible with current
  CounterStrikeSharp. Now targets net10.0, with the API version detected from
  the server at build time.
- **`!fbalance` was open to everyone.** Balancing commands are now admin-only,
  using MatchZy's admin list.
- **Memory leak.** The player dictionary was never cleaned up on disconnect.

### v0.6.1 and earlier

Initial versions.

License
-------
MIT
