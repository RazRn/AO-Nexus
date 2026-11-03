#!/bin/bash
# Local (Linux) build of the AOSharp pack embedded in AONexus.exe with -AOSharpPack.
# Combines: the AO#\dist pack (AOSharp + AOSP suite + classic plugins), Z's suite built from
# source (build_z_suite.py) and Nexus Shortcuts (src/Shortcuts). Local use only: AOSharp and
# the plugins carry no licence that allows redistribution (Build.ps1 -Package refuses them).
#
# usage: build-local-pack.sh <dist> <z repos> <nuget> <out>
#   dist     folder laid out like AO#\dist (AOSharp\, Plugins\)
#   z repos  clones of gitlab.com/Zzzzzzzzzzzzzzzzzzzzzz/{buddies,handlers,managers,utilities}
#   nuget    extracted NuGet packages (<id>.<version>/) and ref48/ (.NET 4.8 reference assemblies)
#   out      receives AOSharpPack.zip, AOSharpPack.manifest.json and plugins.json
set -euo pipefail
DIST=$(realpath "$1"); ZREPOS=$(realpath "$2"); NUGET=$(realpath "$3"); OUT=$(realpath -m "$4")
HERE=$(dirname "$(realpath "$0")"); REPO=$(dirname "$HERE")
STAGE=$(mktemp -d); trap 'rm -rf "$STAGE"' EXIT
REF=$NUGET/ref48/build/.NETFramework/v4.8

cp -r "$DIST/AOSharp" "$DIST/Plugins" "$STAGE/"
python3 -I "$HERE/build_z_suite.py" "$ZREPOS" "$NUGET" "$DIST/AOSharp" "$DIST/Plugins/NavGen" "$STAGE/Plugins/Z"
mkdir -p "$STAGE/Plugins/Nexus"
dotnet /usr/lib/dotnet/sdk/8.0.132/Roslyn/bincore/csc.dll -nologo -noconfig -nostdlib+ -target:library -langversion:7.3 \
  -optimize+ -deterministic+ -out:"$STAGE/Plugins/Nexus/NexusShortcuts.dll" -r:"$REF/mscorlib.dll" -r:"$REF/System.dll" \
  -r:"$REF/System.Core.dll" -r:"$REF/Facades/netstandard.dll" -r:"$DIST/AOSharp/AOSharp.Core.dll" -r:"$DIST/AOSharp/AOSharp.Common.dll" \
  "$REPO/src/Shortcuts/NexusShortcutsPlugin.cs"
REVS=""
[ -f "$DIST/SOURCE-REVISIONS.json" ] && REVS="$DIST/SOURCE-REVISIONS.json"
# Record the Z revisions next to the dist's own.
python3 -I - "$ZREPOS" "$REVS" "$STAGE/SOURCE-REVISIONS.json" <<'PY'
import json, subprocess, sys
zrepos, revs, out = sys.argv[1:4]
data = json.load(open(revs)) if revs else []
for repo in ("buddies", "handlers", "managers", "utilities"):
    head = subprocess.run(["git", "-C", f"{zrepos}/{repo}", "rev-parse", "HEAD"], capture_output=True, text=True).stdout.strip()
    data.append({"Repository": "z-" + repo, "Url": f"https://gitlab.com/Zzzzzzzzzzzzzzzzzzzzzz/{repo}.git", "Commit": head, "LocalChanges": []})
json.dump(data, open(out, "w"), indent=2)
PY
python3 -I "$HERE/make_aosharp_pack.py" "$STAGE" "$OUT" "$HERE/aosharp-pack/plugins.json" "$STAGE/SOURCE-REVISIONS.json"

# Load check: every plugin's references must exist in the AOSharp it ships with (AOSharp hides
# plugins that fail to load). Needs Mono.Cecil 0.11.6 in <nuget> and mono to run it.
CECIL="$NUGET/mono.cecil.0.11.6/lib/net40/Mono.Cecil.dll"
mkdir -p "$STAGE/check/pack" "$STAGE/check/tool"
dotnet /usr/lib/dotnet/sdk/8.0.132/Roslyn/bincore/csc.dll -nologo -noconfig -nostdlib+ -target:exe -langversion:7.3 -optimize+ \
  -out:"$STAGE/check/tool/PluginLoadCheck.exe" -r:"$REF/mscorlib.dll" -r:"$REF/System.dll" -r:"$REF/System.Core.dll" \
  -r:"$REF/System.Web.Extensions.dll" -r:"$REF/Facades/netstandard.dll" -r:"$CECIL" "$HERE/plugin-load-check/PluginLoadCheck.cs"
cp "$CECIL" "$STAGE/check/tool/"
(cd "$STAGE/check/pack" && unzip -q "$OUT/AOSharpPack.zip")
if ! mono "$STAGE/check/tool/PluginLoadCheck.exe" "$STAGE/check/pack" "$REF" > "$OUT/plugin-load-check.txt"; then
  grep -v '^OK' "$OUT/plugin-load-check.txt" >&2
  rm -f "$OUT/AOSharpPack.zip" "$OUT/AOSharpPack.manifest.json"
  echo "Plugin load check failed; no pack was written. Details: $OUT/plugin-load-check.txt" >&2
  exit 1
fi
tail -1 "$OUT/plugin-load-check.txt"
