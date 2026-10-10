#!/data/data/com.termux/files/usr/bin/bash
set -euo pipefail

native="/data/data/com.termux/files/home/lampac-native"
root="$native/dotnet-host"
cli_home="${DOTNET_CLI_HOME:-$HOME/.local/share/lampac-dotnet}"
tmp_dir="${TMPDIR:-$HOME/.cache/lampac-dotnet-tmp}"

mkdir -p "$cli_home/.dotnet" "$HOME/.local/share/NuGet/Migrations" "$tmp_dir"
: > "$cli_home/.dotnet/10.0.400.dotnetFirstUseSentinel"
: > "$HOME/.local/share/NuGet/Migrations/1"

export TMPDIR="$tmp_dir"
export TMP="$tmp_dir"
export TEMP="$tmp_dir"
export DOTNET_ROOT="$root"
export DOTNET_HOST_PATH="$root/dotnet"
export _DotNetHostDirectory="$root"
export _DotNetHostFileName="dotnet"
export DOTNET_CLI_HOME="$cli_home"
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export DOTNET_CLI_USE_MSBUILD_SERVER=0
export MSBUILDDISABLENODEREUSE=1
export DOTNET_GCHeapHardLimit="${DOTNET_GCHeapHardLimit:-600000000}"
export DOTNET_GCHeapHardLimitPercent="${DOTNET_GCHeapHardLimitPercent:-10}"
export NUGET_PACKAGES="${NUGET_PACKAGES:-/data/data/com.termux/files/usr/var/lib/proot-distro/containers/ubuntu/rootfs/root/.nuget/packages}"
export CscToolPath="$native/csc"
export CscToolExe="csc"
export UseSharedCompilation=false

case "${1:-}" in
  build|restore|publish|test|pack|msbuild|clean)
    set -- "$@" -m:1
    ;;
esac

exec "$root/dotnet" "$@"
