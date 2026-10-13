import { spawn } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const toolRoot = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(toolRoot, "../..");
const outDir = path.resolve(toolRoot, "dist");
const host = "127.0.0.1";
const port = Number(process.env.EXPORT_PORT || 4174);

function notFoundPage() {
  return `<!DOCTYPE html>
<html lang="de">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Nicht gefunden</title>
  <style>
    body { margin: 0; font: 16px/1.55 "Segoe UI", Helvetica, Arial, sans-serif; color: #1c2430; background: #f4f6f8; }
    header { background: #12324d; color: #fff; padding: 16px 24px; }
    header a { color: #fff; text-decoration: none; font-weight: 650; }
    main { max-width: 1200px; margin: 0 auto; padding: 24px; }
    article { background: #fff; border: 1px solid #d8e0e8; border-radius: 10px; padding: 16px 20px; }
  </style>
</head>
<body>
  <header><a href="/">ERP-Architekturdocs</a></header>
  <main><article><h1>Nicht gefunden</h1><p>Dieses Dokument ist nicht in der Vorschau.</p></article></main>
</body>
</html>`;
}

async function waitForServer(child) {
  let log = "";
  child.stdout.on("data", (chunk) => {
    log += chunk.toString();
  });
  child.stderr.on("data", (chunk) => {
    log += chunk.toString();
  });
  const deadline = Date.now() + 20000;
  while (Date.now() < deadline) {
    if (child.exitCode !== null) {
      throw new Error(`preview server exited early\n${log}`);
    }
    try {
      const response = await fetch(`http://${host}:${port}/health`);
      if (response.ok) return response.json();
    } catch {
      // server still starting
    }
    await new Promise((resolve) => setTimeout(resolve, 200));
  }
  throw new Error(`preview server did not become ready\n${log}`);
}

async function save(pathname, relativePath) {
  const response = await fetch(`http://${host}:${port}${pathname}`);
  if (!response.ok) {
    throw new Error(`${pathname} returned ${response.status}`);
  }
  const target = path.join(outDir, relativePath);
  fs.mkdirSync(path.dirname(target), { recursive: true });
  fs.writeFileSync(target, Buffer.from(await response.arrayBuffer()));
}

const child = spawn(process.execPath, [path.join(toolRoot, "preview-server.mjs")], {
  cwd: repoRoot,
  env: { ...process.env, HOST: host, PORT: String(port) },
  stdio: ["ignore", "pipe", "pipe"],
});

try {
  fs.rmSync(outDir, { recursive: true, force: true });
  fs.mkdirSync(outDir, { recursive: true });
  const health = await waitForServer(child);
  await save("/", path.join("index.html"));
  await save("/health", "health");
  await save("/vendor/mermaid.min.js", path.join("vendor", "mermaid.min.js"));
  for (const name of health.docs) {
    await save(`/doc/${encodeURIComponent(name)}`, path.join("doc", name));
  }
  fs.writeFileSync(path.join(outDir, "404.html"), notFoundPage());
  console.log(`exported ${health.docs.length} documents to ${outDir}`);
} finally {
  child.kill();
}
