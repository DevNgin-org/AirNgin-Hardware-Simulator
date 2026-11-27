#!/usr/bin/env bash
set -euo pipefail
rid=${1:?Use osx-arm64, osx-x64 or win-x64}
case "$rid" in osx-arm64|osx-x64|win-x64) ;; *) exit 2 ;; esac
project=$(cd "$(dirname "$0")/.." && pwd)
out=${2:-"$project/dist"}
mkdir -p "$out"
out=$(cd "$out" && pwd)
stage=$(mktemp -d)
trap 'rm -rf "$stage"' EXIT
archive="$stage/archive"
mkdir "$archive"
bundle="$archive/AirNgin Hardware Simulator.app"
if [[ $rid == osx-* ]]; then target="$bundle/Contents/MacOS"; else target="$archive"; fi
mkdir -p "$target"
dotnet publish "$project/HardwareSimulator.csproj" -c Release -r "$rid" --self-contained true \
  -p:PublishSingleFile=true -p:UsedAvaloniaProducts= -p:DebugType=None -p:DebugSymbols=false -o "$target" "${@:3}"
if [[ $rid == osx-* ]]; then
  cp "$project/packaging/Info.plist" "$bundle/Contents/Info.plist"
  codesign --force --deep --sign - "$bundle"
  codesign --verify --deep --strict "$bundle"
  cp "$project/README.md" "$project/README.en.md" "$project/examples/tester-demo.json" "$archive/"
  ditto -c -k --sequesterRsrc "$archive" "$stage/package.zip"
  ditto -x -k "$stage/package.zip" "$stage/check"
  codesign --verify --deep --strict "$stage/check/AirNgin Hardware Simulator.app"
else
  cp "$project/README.md" "$project/README.en.md" "$project/examples/tester-demo.json" "$target/"
  (cd "$target" && zip -q -r "$stage/package.zip" .)
fi
mv "$stage/package.zip" "$out/HardwareSimulator-$rid.zip"
