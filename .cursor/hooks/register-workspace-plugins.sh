#!/bin/sh
# Register this repo's plugins when Cursor opens the workspace.
# stdin is the workspaceOpen payload; stdout is {"pluginPaths":[...]}.

input=$(cat)
array=$(printf '%s' "$input" | tr '\n' ' ' | sed -n 's/.*"workspace_roots"[[:space:]]*:[[:space:]]*\[\([^]]*\)\].*/\1/p')

json_escape() {
  printf '%s' "$1" | sed 's/\\/\\\\/g; s/"/\\"/g'
}

if [ -z "$array" ]; then
  set -- "$(pwd)"
else
  set --
  while IFS= read -r quoted; do
    [ -n "$quoted" ] || continue
    set -- "$@" "$(printf '%s' "$quoted" | sed 's/^"//;s/"$//')"
  done <<EOF
$(printf '%s\n' "$array" | grep -o '"[^"]*"')
EOF
fi

paths=""
for root in "$@"; do
  for name in compound-engineering anthropics-skills; do
    plugin="$root/plugins/$name"
    if [ -f "$plugin/.cursor-plugin/plugin.json" ]; then
      paths="$paths\"$(json_escape "$plugin")\","
    fi
  done
done

paths=${paths%,}
printf '{"pluginPaths":[%s]}\n' "$paths"
