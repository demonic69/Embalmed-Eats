#!/usr/bin/env bash
# Collects reference information from the vanilla game files so recipes and
# patches can be checked against the exact game version. Output goes to ci/probe.
set -uo pipefail

out=ci/probe
mkdir -p "$out/assets"

ls -la vs > "$out/server-root.txt"
ls vs/Mods vs/Lib > "$out/server-mods-lib.txt" 2>&1
cp vs/VintagestoryServer.runtimeconfig.json "$out/" 2>/dev/null

A=vs/assets
(cd "$A" && find . -type f -path '*itemtypes*' | sort) > "$out/itemtypes-files.txt"
(cd "$A" && find . -type f -path '*blocktypes*' | sort) > "$out/blocktypes-files.txt"
(cd "$A" && find . -type f -path '*recipes*' | sort) > "$out/recipe-files.txt"
(cd "$A" && find . -type f -path '*config*' | sort) > "$out/config-files.txt"

# Reference assets the mod builds on
pattern='beeswax|resin|lime|borax|salt|alum|vinegar|brine|honey|spirit|alcohol|shelf|displaycase|saltpeter|sulfur|fat|tallow|pitch|tar|wax|candle|oil|liquid'
(cd "$A" && find . -type f -name '*.json' | grep -Ei "/($pattern)[^/]*\.json$" ) | while read -r f; do
  mkdir -p "$out/assets/$(dirname "$f")"
  cp "$A/$f" "$out/assets/$f"
done
cp -r "$A/survival/recipes/barrel" "$out/assets/barrel-recipes" 2>/dev/null

grep -rlE '"shelvable"|onDisplayTransform|displaycase|onshelfTransform' "$A" --include=*.json > "$out/display-attr-files.txt" 2>/dev/null
grep -rhoE '"(shelvable|displaycaseable|onDisplayTransform|onshelfTransform|groundStorageTransform)"' "$A" --include=*.json | sort | uniq -c > "$out/display-attr-counts.txt" 2>/dev/null
grep -rhoE '"tags"\s*:\s*\[[^]]*\]' "$A" --include=*.json | sort | uniq -c | sort -rn | head -200 > "$out/tags-usage.txt" 2>/dev/null

for lf in "$A"/game/lang/en.json "$A"/survival/lang/en.json; do
  [ -f "$lf" ] && grep -iE "$pattern|poison|nause|vomit" "$lf" > "$out/lang-$(basename "$(dirname "$(dirname "$lf")")").txt"
done

echo "Probe done: $(find "$out" -type f | wc -l) files"

# Second round of reference info
cp "$A"/survival/config/handbook/07-foodpreservation.json "$out/" 2>/dev/null
(cd "$A" && find . -path '*sounds*' -type f | sed 's|^\./||' | sort) > "$out/sound-files.txt"
grep -rlE 'shelvable|displaycaseable' "$A"/survival/itemtypes/food "$A"/survival/blocktypes 2>/dev/null | sed "s|$A/||" > "$out/display-capable-files.txt"

# Third round: creative tabs, food item definitions, ground storage users
cp "$A"/game/config/creativetabs.json "$out/" 2>/dev/null
mkdir -p "$out/food"
cp -r "$A"/survival/itemtypes/food/. "$out/food/" 2>/dev/null
grep -h '"tabname-' "$A"/game/lang/en.json > "$out/lang-tabnames.txt" 2>/dev/null
grep -rlE 'GroundStorable' "$A"/survival/itemtypes "$A"/survival/blocktypes 2>/dev/null | sed "s|$A/||" | sort > "$out/groundstorable-files.txt"
grep -rhoE "layout: *['\"][A-Za-z0-9]+['\"]" "$A"/survival 2>/dev/null | sort | uniq -c | sort -rn > "$out/groundstorable-layouts.txt"
for f in survival/blocktypes/clay/bowl.json survival/blocktypes/food/pie.json survival/blocktypes/clay/crock.json; do
  [ -f "$A/$f" ] && { mkdir -p "$out/blocks"; cp "$A/$f" "$out/blocks/"; }
done
(cd "$A" && find . -path '*blocktypes*' \( -name '*bowl*' -o -name '*pie*' -o -name '*crock*' -o -name '*pot*' \) | sort) > "$out/meal-block-files.txt"
