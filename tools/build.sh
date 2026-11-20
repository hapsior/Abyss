#!/usr/bin/env bash
# Build C# w środowisku bez dostępu do nuget.org (chmura): paczki Godota z lokalnego katalogu.
set -e
source /opt/godot/env.sh
cd "$(dirname "$0")/.."
dotnet restore --source /opt/nuget-local -v q >/dev/null
dotnet build --no-restore -nologo -v q -clp:NoSummary "$@"
