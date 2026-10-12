#!/bin/sh
# Tell Cursor to load the repo plugins when this workspace opens.
python3 -c '
import json, sys
from pathlib import Path

raw = sys.stdin.read()
data = json.loads(raw) if raw.strip() else {}
roots = data.get("workspace_roots") or [str(Path.cwd())]
paths = []
for root in roots:
    base = Path(root)
    for name in ("compound-engineering", "anthropic-skills"):
        plugin = (base / "plugins" / name).resolve()
        if (plugin / ".cursor-plugin" / "plugin.json").is_file():
            paths.append(str(plugin))
json.dump({"pluginPaths": paths}, sys.stdout)
sys.stdout.write("\n")
'
