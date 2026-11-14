#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
if [[ -x "$PWD/.dotnet/dotnet" ]]; then
    export DOTNET_ROOT="$PWD/.dotnet"
    export PATH="$DOTNET_ROOT:$PATH"
fi
if ! dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; then
    echo "SDK .NET 10 não encontrado. Consulte a instalação no README.md." >&2
    exit 1
fi
: "${Jwt__Key:?Defina Jwt__Key com pelo menos 32 bytes aleatórios}"
cargo build --release --locked --manifest-path native/security-core/Cargo.toml
export LD_LIBRARY_PATH="$PWD/native/security-core/target/release${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
export ASPNETCORE_URLS="${ASPNETCORE_URLS:-http://127.0.0.1:5080}"
dotnet run --project api/SecurityApi.csproj
