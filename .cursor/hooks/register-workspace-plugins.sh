#!/bin/sh
# Install this repo's plugins where Customize lists local plugins.
# Cursor ignores symlinks that point outside ~/.cursor/plugins/local, so copy files.

root=$(CDPATH= cd -- "$(dirname "$0")/../.." && pwd)
home=${HOME:-}
dest=""
if [ -n "$home" ]; then
  dest="$home/.cursor/plugins/local"
  mkdir -p "$dest"
  rev=$(git -C "$root" rev-parse HEAD 2>/dev/null || printf '%s' "unknown")
  for name in compound-engineering anthropics-skills; do
    src="$root/plugins/$name"
    [ -f "$src/.cursor-plugin/plugin.json" ] || continue
    target="$dest/$name"
    marker="$dest/.$name.workspace-rev"
    if [ -f "$marker" ] && [ "$(cat "$marker" 2>/dev/null)" = "$rev" ] && [ -f "$target/.cursor-plugin/plugin.json" ]; then
      continue
    fi
    rm -rf "$target"
    mkdir -p "$target"
    cp -aL "$src/." "$target/"
    printf '%s\n' "$rev" > "$marker"
  done
fi

paths=""
for name in compound-engineering anthropics-skills; do
  if [ -n "$dest" ] && [ -f "$dest/$name/.cursor-plugin/plugin.json" ]; then
    plugin="$dest/$name"
  else
    plugin="$root/plugins/$name"
  fi
  [ -f "$plugin/.cursor-plugin/plugin.json" ] || continue
  paths="$paths\"$plugin\","
done
paths=${paths%,}
printf '{"pluginPaths":[%s]}\n' "$paths"
