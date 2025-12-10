FaceIT Team Balancer for CS2 v0.6.1
===================================

A Counter-Strike 2 plugin that automatically fetches and balances teams based on FaceIT ELO ratings with smart match detection.

Features
--------
🔍 **Automatic ELO Fetching** - Fetches player ELO from FaceIT API with rate limiting
⚖️ **Smart Team Balancing** - Balance teams by ELO with 5v5 support
🤖 **Intelligent Match Detection** - Automatically disables during live matches
⏱️ **Queue System** - Processes players efficiently without server overload
🔧 **Configurable** - Easy setup via config.json
📊 **Real-time Status** - View plugin and match status anytime
💬 **Chat Integration** - All commands accessible via chat

Smart Match Detection
---------------------
Plugin automatically detects when a match is live and disables itself:
- Monitors player team assignments (T vs CT)
- Detects minimum 4 players on teams
- Re-enables during warmup/stopped matches
- Prevents API calls during matches to reduce load

Commands
--------
Player Commands:
• `!fbalance` - Balance teams by ELO (during warmup only)
• `!fbalance5v5` - Balance 5v5 teams by ELO (during warmup only)
• `!elostatus` - Show ELO status of all players
• `!fstatus` - Show FaceIT plugin status

(Note: Auto-fetch is enabled by default - players' data loads automatically on connect)

Installation
------------
1. **Build the Plugin:**
2. **Configure API Key:**
- Edit `config.json` in the plugin directory
- Replace `"API_KEY_HERE"` with your FaceIT API key
- Get API key from: https://developers.faceit.com

3. **Configuration Options:**
```json
{
  "ApiKey": "Bearer YOUR_API_KEY_HERE",
  "AutoFetchOnConnect": true,
  "AutoFetchDelay": 5,
  "DisableDuringMatch": true
}
