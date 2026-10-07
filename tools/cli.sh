#!/bin/sh
# Runs the developer CLI (tools/Cli). Try: tools/cli.sh help
exec dotnet run --project "$(dirname "$0")/Cli" -- "$@"
