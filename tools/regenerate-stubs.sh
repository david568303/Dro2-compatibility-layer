#!/usr/bin/env bash
# Regenerates ci/GameStubs (the stand-ins CI compiles against) and proves they match the real game.
#
#   tools/regenerate-stubs.sh <game interop folder>      e.g. "<game>/BepInEx/interop"
#
# Run it after the code starts using a game type or member that was not used before. Steps:
#   1. build the plugin against the game's REAL interop assemblies;
#   2. write minimal stand-ins for everything it references (tools/StubGen);
#   3. build the stand-ins, then build the plugin against only them (exactly what CI does);
#   4. check both builds reference exactly the same members (so CI's DLL binds to the real game the same way).
# Needs the .NET 8 SDK.
set -euo pipefail

interop="${1:?usage: tools/regenerate-stubs.sh <game interop folder>}"
cd "$(dirname "$0")/.."
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

echo "== 1. plugin against the real interop"
dotnet build CarXModManager.csproj -c Release -o "$work/real" -p:UseBepInExPackages=true -p:GameInteropDirectory="$interop" -p:DeployToGame=false -v quiet -nologo

echo "== 2. generate the stand-ins"
dotnet build tools/StubGen -c Release -o "$work/stubgen" -v quiet -nologo
dotnet "$work/stubgen/StubGen.dll" generate --plugin "$work/real/CarXModManager.dll" --interop "$interop" --out ci/GameStubs

echo "== 3. build the stand-ins and the plugin against them"
rm -rf ci/stubs-out
while read -r project; do
    dotnet build "ci/GameStubs/$project" -c Release -o ci/stubs-out -v quiet -nologo
done < ci/GameStubs/build-order.txt
dotnet build CarXModManager.csproj -c Release -o "$work/stubbed" -p:UseBepInExPackages=true -p:GameInteropDirectory="$PWD/ci/stubs-out" -p:DeployToGame=false -v quiet -nologo

echo "== 4. do both builds reference the same members?"
dotnet "$work/stubgen/StubGen.dll" compare "$work/real/CarXModManager.dll" "$work/stubbed/CarXModManager.dll"
