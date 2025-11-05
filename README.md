# FaceIT Balancer for CS2

A Counter-Strike 2 plugin that automatically balances teams based on FaceIT ELO ratings.

## Features

- 🔍 **Automatic ELO Fetching** - Get player ELO from FaceIT API
- ⚖️ **Smart Team Balancing** - Balance teams based on ELO
- 👑 **Admin Commands** - Full control for server administrators
- 🎯 **Manual ELO Setting** - Set ELO manually if API is unavailable
- 📊 **Player Statistics** - View all player data
- 💬 **Chat Integration** - Easy-to-use chat commands

## Commands

### Player Commands
- `!faceit` / `!faceit_help` - Show help
- `!getfaceit` - Load your FaceIT data
- `!setlevel [1-10]` - Set level manually
- `!setelo [ELO]` - Set ELO manually (1-3000)
- `!myfaceit` - View your loaded data

### Admin Commands
- `!getfaceit_all` - Load data for all players
- `!playersdata` - View all players' data
- `!fbalance` - Balance teams by ELO
- `!fbalance5v5` - Balance 5v5 by ELO

## Installation

1. **Build the Plugin**
   ```bash
   dotnet build
