#!/usr/bin/env bash
set -e

MOD_DIR="/run/media/deck/3b03f019-ee3d-473e-beb1-98236afc5254/steamapps/common/Pathfinder Second Adventure/Mods/WrathTactics"

cd "$(dirname "$0")"
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir="$(pwd)/"

ssh deck-direct "mkdir -p '$MOD_DIR'"
scp WrathTactics/bin/Debug/WrathTactics.dll WrathTactics/bin/Debug/Info.json \
  "deck-direct:$MOD_DIR/"

echo "Deployed to Steam Deck."
