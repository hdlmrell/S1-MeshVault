#!/usr/bin/env bash
# Collects game DLLs from your local r2modman profiles and uploads them
# as a "deps" pre-release for CI builds.
#
# Prerequisites: gh CLI authenticated (gh auth login)
# Usage: bash scripts/setup-ci-deps.sh

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
TARGETS="$REPO_ROOT/S1-MeshVault/LocalPaths.targets"

if ! command -v gh &>/dev/null; then
  echo "Error: gh CLI not found. Install from https://cli.github.com"
  exit 1
fi

if [ ! -f "$TARGETS" ]; then
  echo "Error: LocalPaths.targets not found."
  echo "Copy LocalPaths.targets.example and fill in your local paths first."
  exit 1
fi

extract_tag() {
  sed -n "s/.*<$1>\(.*\)<\/$1>.*/\1/p" "$TARGETS" | tr -d '\r'
}

# Resolve MSBuild-style $(VarName) references against other tags in the file
resolve() {
  local val
  val=$(extract_tag "$1")
  while [[ "$val" == *'$('* ]]; do
    local ref
    ref=$(echo "$val" | sed -n 's/.*\$(\([^)]*\)).*/\1/p')
    local ref_val
    ref_val=$(extract_tag "$ref")
    val="${val//\$($ref)/$ref_val}"
  done
  echo "$val"
}

IL2CPP_DIR=$(resolve BetaIl2CppDllPath)
MONO_DIR=$(resolve BetaMonoDllPath)
ML_NET6_DIR=$(resolve MelonLoaderNet6Path)
ML_DIR=$(resolve MonoMelonLoaderPath)

missing=0
for var_name in IL2CPP_DIR MONO_DIR ML_NET6_DIR ML_DIR; do
  dir="${!var_name}"
  if [ -z "$dir" ]; then
    echo "Error: $var_name not found in LocalPaths.targets"
    missing=1
  elif [ ! -d "$dir" ]; then
    echo "Error: $var_name directory does not exist: $dir"
    missing=1
  fi
done
[ "$missing" -eq 1 ] && exit 1

STAGING=$(mktemp -d)
trap 'rm -rf "$STAGING"' EXIT

mkdir -p "$STAGING/il2cpp" "$STAGING/mono" "$STAGING/melonloader/net6"

# IL2CPP game DLLs (shared + IL2CPP-only + beta)
IL2CPP_DLLS=(
  UnityEngine.dll UnityEngine.CoreModule.dll UnityEngine.PhysicsModule.dll
  UnityEngine.IMGUIModule.dll UnityEngine.UIModule.dll UnityEngine.UI.dll
  UnityEngine.InputLegacyModule.dll UnityEngine.ImageConversionModule.dll
  UnityEngine.TextRenderingModule.dll Unity.TextMeshPro.dll
  Unity.RenderPipelines.Universal.Runtime.dll Assembly-CSharp.dll
  Il2Cppmscorlib.dll Il2CppFishNet.Runtime.dll Il2CppScheduleOne.Core.dll
)

# Mono game DLLs (shared + Mono-only + beta)
MONO_DLLS=(
  UnityEngine.dll UnityEngine.CoreModule.dll UnityEngine.PhysicsModule.dll
  UnityEngine.IMGUIModule.dll UnityEngine.UIModule.dll UnityEngine.UI.dll
  UnityEngine.InputLegacyModule.dll UnityEngine.ImageConversionModule.dll
  UnityEngine.TextRenderingModule.dll Unity.TextMeshPro.dll
  Unity.RenderPipelines.Universal.Runtime.dll Assembly-CSharp.dll
  FishNet.Runtime.dll ScheduleOne.Core.dll
)

echo "Collecting IL2CPP DLLs..."
for dll in "${IL2CPP_DLLS[@]}"; do
  if [ -f "$IL2CPP_DIR/$dll" ]; then
    cp "$IL2CPP_DIR/$dll" "$STAGING/il2cpp/"
  else
    echo "  Warning: $dll not found in IL2CPP dir (skipping)"
  fi
done

echo "Collecting Mono DLLs..."
for dll in "${MONO_DLLS[@]}"; do
  if [ -f "$MONO_DIR/$dll" ]; then
    cp "$MONO_DIR/$dll" "$STAGING/mono/"
  else
    echo "  Warning: $dll not found in Mono dir (skipping)"
  fi
done

echo "Collecting MelonLoader DLLs..."
cp "$ML_NET6_DIR/MelonLoader.dll" "$STAGING/melonloader/net6/"
cp "$ML_NET6_DIR/Il2CppInterop.Runtime.dll" "$STAGING/melonloader/net6/"
cp "$ML_DIR/MelonLoader.dll" "$STAGING/melonloader/"

ARCHIVE="$REPO_ROOT/deps.tar.gz"
(cd "$STAGING" && tar -czf "$ARCHIVE" .)

il2cpp_count=$(ls -1 "$STAGING/il2cpp/" | wc -l)
mono_count=$(ls -1 "$STAGING/mono/" | wc -l)
echo ""
echo "Packed $il2cpp_count IL2CPP DLLs, $mono_count Mono DLLs, and MelonLoader"
echo "Archive: $(du -h "$ARCHIVE" | cut -f1)"
echo ""

cd "$REPO_ROOT"
if gh release view deps &>/dev/null; then
  echo "Updating existing deps release..."
  gh release upload deps "$ARCHIVE" --clobber
else
  echo "Creating deps pre-release..."
  gh release create deps "$ARCHIVE" \
    --prerelease \
    --title "Build Dependencies" \
    --notes "Game DLLs for CI builds. Do not delete this release."
fi

rm -f "$ARCHIVE"
echo "Done!"
