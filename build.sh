#!/usr/bin/env bash
#===============================================================================
#  build.sh — compileaza si instaleaza FaceITBalancer pe serverul CS2
#
#  Ruleaza-l din folderul care contine FaceITBalancer.cs si FaceITBalancer.csproj
#
#     ./build.sh -d /home/cs2lacurte/serverfiles
#
#  Instaleaza .NET SDK local in ~/.dotnet daca lipseste (nu are nevoie de root),
#  detecteaza automat versiunea de CounterStrikeSharp de pe server si compileaza
#  fix pe ea.
#===============================================================================
set -Eeuo pipefail

ROOT="${1:-}"
API_VER=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    -d|--dir)          ROOT="${2:-}"; shift 2 ;;
    --api-version)     API_VER="${2:-}"; shift 2 ;;
    -h|--help)         echo "uz: $0 -d /cale/server [--api-version 1.0.371]"; exit 0 ;;
    *) shift ;;
  esac
done

C_G=$'\e[32m'; C_Y=$'\e[33m'; C_R=$'\e[31m'; C_0=$'\e[0m'
log()  { printf '[*] %s\n' "$*"; }
ok()   { printf '%s[+]%s %s\n' "$C_G" "$C_0" "$*"; }
warn() { printf '%s[!]%s %s\n' "$C_Y" "$C_0" "$*" >&2; }
die()  { printf '%s[x]%s %s\n' "$C_R" "$C_0" "$*" >&2; exit 1; }

[[ -n "$ROOT" ]] || die "lipseste calea serverului: $0 -d /home/cs2lacurte/serverfiles"
CSGO="${ROOT%/}/game/csgo"
[[ -f "$CSGO/gameinfo.gi" ]] || die "nu gasesc $CSGO/gameinfo.gi"
[[ -d "$CSGO/addons/counterstrikesharp" ]] || die "CounterStrikeSharp nu e instalat in $CSGO/addons"
[[ -f FaceITBalancer.csproj ]] || die "ruleaza scriptul din folderul cu FaceITBalancer.csproj"

#--------------------------------- .NET SDK -----------------------------------
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
DOTNET=""
if command -v dotnet >/dev/null 2>&1 && dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; then
  DOTNET="$(command -v dotnet)"
elif [[ -x "$HOME/.dotnet/dotnet" ]] && "$HOME/.dotnet/dotnet" --list-sdks 2>/dev/null | grep -q '^10\.'; then
  DOTNET="$HOME/.dotnet/dotnet"
else
  log "instalez .NET SDK 10 in ~/.dotnet (fara root)"
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
    || die "nu pot descarca dotnet-install.sh"
  bash /tmp/dotnet-install.sh --channel 10.0 --install-dir "$HOME/.dotnet" \
    || die "instalarea SDK-ului a esuat"
  DOTNET="$HOME/.dotnet/dotnet"
fi
export PATH="$(dirname "$DOTNET"):$PATH"
ok "SDK: $("$DOTNET" --version)"

#--------------------- versiunea CounterStrikeSharp de pe server ---------------
if [[ -z "$API_VER" ]]; then
  STATE="$CSGO/addons/.cs2-mods.state"
  if [[ -f "$STATE" ]]; then
    API_VER="$(sed -n 's/^CSSHARP=v\?//p' "$STATE" | tail -1)"
  fi
fi
[[ -n "$API_VER" ]] || die "nu pot detecta versiunea CounterStrikeSharp — foloseste --api-version 1.0.371"
ok "compilez pe CounterStrikeSharp.API $API_VER"

#---------------------------------- build -------------------------------------
rm -rf bin obj
"$DOTNET" build -c Release -p:CssApiVersion="$API_VER" -v minimal \
  || die "compilarea a esuat — trimite eroarea de mai sus"

OUT="$(find bin/Release -name FaceITBalancer.dll -print -quit)"
[[ -n "$OUT" ]] || die "nu gasesc FaceITBalancer.dll dupa build"
BUILDDIR="$(dirname "$OUT")"

#--------------------------------- deploy -------------------------------------
DEST="$CSGO/addons/counterstrikesharp/plugins/FaceITBalancer"
mkdir -p "$DEST"
cp -f "$BUILDDIR/FaceITBalancer.dll" "$DEST/"
[[ -f "$BUILDDIR/FaceITBalancer.deps.json" ]] && cp -f "$BUILDDIR/FaceITBalancer.deps.json" "$DEST/"
[[ -f "$BUILDDIR/FaceITBalancer.pdb" ]]       && cp -f "$BUILDDIR/FaceITBalancer.pdb" "$DEST/"

# permisiuni corecte daca rulezi ca root
if [[ $EUID -eq 0 ]]; then
  chown -R "$(stat -c '%u:%g' "$ROOT")" "$DEST"
fi

ok "instalat in $DEST"
CFG="$CSGO/addons/counterstrikesharp/configs/plugins/FaceITBalancer/FaceITBalancer.json"
if [[ -f "$CFG" ]]; then
  ok "config existent: $CFG"
else
  warn "configul se genereaza la prima pornire a serverului:"
  warn "  $CFG"
  warn "  porneste serverul, pune ApiKey acolo, apoi: css_plugins reload FaceITBalancer"
fi
