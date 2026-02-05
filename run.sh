#!/bin/bash
cd "$(dirname "$0")/bin/Release/net8.0-windows"
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
./RadioPlayer

