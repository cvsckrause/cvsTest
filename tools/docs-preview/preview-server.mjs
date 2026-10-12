import fs from "node:fs";
import http from "node:http";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { Marked } from "marked";

const toolRoot = path.dirname(fileURLToPath(import.meta.url));
const docsDir = process.env.DOCS_DIR || path.resolve(toolRoot, "../../docs");
const host = process.env.HOST || "0.0.0.0";
const port = Number(process.env.PORT || 4173);
const mermaidJs = path.join(toolRoot, "node_modules/mermaid/dist/mermaid.min.js");

function escapeHtml(value) {
  return value
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

function slugify(value) {
  return value
    .toLowerCase()
    .normalize("NFKD")
    .replace(/[\u0300-\u036f]/g, "")
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-|-$/g, "") || "section";
}

function listDocs() {
  if (!fs.existsSync(docsDir)) return [];
  return fs
    .readdirSync(docsDir)
    .filter((name) => name.endsWith(".md"))
    .sort();
}

function resolveDoc(name) {
  if (!name || name !== path.basename(name) || !name.endsWith(".md")) return null;
  const fullPath = path.join(docsDir, name);
  if (!fullPath.startsWith(path.resolve(docsDir) + path.sep)) return null;
  if (!fs.existsSync(fullPath) || !fs.statSync(fullPath).isFile()) return null;
  return fullPath;
}

function normalizeMarkdown(markdown) {
  return markdown.replace(/^[•·]\s+/gm, "- ");
}

function renderDocument(markdown) {
  const normalized = normalizeMarkdown(markdown);
  const parser = new Marked({ gfm: true });
  const tokens = parser.lexer(normalized);
  const used = new Map();
  const headings = [];
  const ids = [];

  for (const token of tokens) {
    if (token.type !== "heading") continue;
    const plain = String(token.text);
    let id = slugify(plain);
    const seen = used.get(id) || 0;
    used.set(id, seen + 1);
    if (seen > 0) id = `${id}-${seen + 1}`;
    ids.push(id);
    headings.push({ depth: token.depth, text: plain, id });
  }

  let headingIndex = 0;
  parser.use({
    renderer: {
      heading({ tokens: inlineTokens, depth }) {
        const text = this.parser.parseInline(inlineTokens);
        const id = ids[headingIndex++] || `section-${headingIndex}`;
        return `<h${depth} id="${id}">${text}</h${depth}>\n`;
      },
    },
  });

  return { body: parser.parser(tokens), headings };
}

function pageShell({ title, body }) {
  return `<!DOCTYPE html>
<html lang="de">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>${escapeHtml(title)}</title>
  <style>
    :root { color-scheme: light; }
    body { margin: 0; font: 16px/1.55 "Segoe UI", Helvetica, Arial, sans-serif; color: #1c2430; background: #f4f6f8; }
    header { background: #12324d; color: #fff; padding: 16px 24px; }
    header a { color: #fff; text-decoration: none; font-weight: 650; }
    main { display: grid; grid-template-columns: 280px 1fr; gap: 24px; max-width: 1200px; margin: 0 auto; padding: 24px; }
    nav, article { background: #fff; border: 1px solid #d8e0e8; border-radius: 10px; padding: 16px 20px; }
    nav { align-self: start; position: sticky; top: 16px; max-height: calc(100vh - 32px); overflow: auto; }
    nav a { display: block; color: #1d4e89; text-decoration: none; margin: 4px 0; }
    nav a.depth-2 { padding-left: 12px; }
    nav a.depth-3 { padding-left: 24px; }
    article h1, article h2, article h3 { line-height: 1.25; }
    article table { border-collapse: collapse; width: 100%; margin: 16px 0; }
    article th, article td { border: 1px solid #d0d7de; padding: 8px 10px; text-align: left; vertical-align: top; }
    article th { background: #eef3f8; }
    article code { background: #f3f5f7; padding: 0 4px; border-radius: 4px; }
    article pre { overflow: auto; background: #0f1720; color: #e8eef5; padding: 12px; border-radius: 8px; }
    article pre code { background: transparent; color: inherit; }
    .mermaid { overflow-x: auto; background: #fbfcfe; border: 1px solid #e1e7ee; border-radius: 8px; padding: 12px; margin: 16px 0; }
    .card-list { list-style: none; padding: 0; }
    .card-list a { display: block; padding: 12px 14px; border: 1px solid #d8e0e8; border-radius: 8px; margin: 8px 0; color: #12324d; text-decoration: none; background: #fff; }
    @media (max-width: 860px) { main { grid-template-columns: 1fr; } nav { position: static; max-height: none; } }
  </style>
</head>
<body>
  <header><a href="/">ERP-Architekturdocs</a></header>
  ${body}
</body>
</html>`;
}

function displayName(name) {
  return name.replace(/\.md$/i, "").replaceAll("_", " ");
}

function indexPage(docs) {
  const items = docs
    .map((name) => {
      const markdown = fs.readFileSync(path.join(docsDir, name), "utf8");
      const titleMatch = markdown.match(/^#\s+(.+)$/m);
      const firstHeading = titleMatch ? titleMatch[1].trim() : "";
      const subtitle = firstHeading
        ? `${name} — beginnt mit „${firstHeading}“`
        : name;
      return `<li><a href="/doc/${encodeURIComponent(name)}"><strong>${escapeHtml(displayName(name))}</strong><br><span>${escapeHtml(subtitle)}</span></a></li>`;
    })
    .join("");
  return pageShell({
    title: "ERP-Architekturdocs",
    body: `<main><article><h1>ERP-Architekturdocs</h1><p>Lokale Vorschau der Architekturunterlagen in diesem Repository. Diagramme werden als Mermaid gerendert.</p><ul class="card-list">${items}</ul></article></main>`,
  });
}

function docPage(name) {
  const fullPath = resolveDoc(name);
  if (!fullPath) return null;
  const markdown = fs.readFileSync(fullPath, "utf8");
  const { body, headings } = renderDocument(markdown);
  const title = headings[0]?.text || name;
  const toc = headings
    .filter((heading) => heading.depth <= 3)
    .map(
      (heading) =>
        `<a class="depth-${heading.depth}" href="#${heading.id}">${escapeHtml(heading.text)}</a>`,
    )
    .join("");
  const html = pageShell({
    title,
    body: `<main><nav><strong>Inhalt</strong>${toc}</nav><article>${body}</article></main>
<script src="/vendor/mermaid.min.js"></script>
<script>
  mermaid.initialize({ startOnLoad: false, securityLevel: "strict" });
  document.querySelectorAll("pre code.language-mermaid").forEach((node) => {
    const diagram = document.createElement("div");
    diagram.className = "mermaid";
    diagram.textContent = node.textContent;
    node.parentElement.replaceWith(diagram);
  });
  mermaid.run();
</script>`,
  });
  return html;
}

function send(response, status, body, type) {
  response.writeHead(status, {
    "Content-Type": type,
    "Cache-Control": "no-store",
  });
  response.end(body);
}

const server = http.createServer((request, response) => {
  const url = new URL(request.url || "/", `http://${request.headers.host || "localhost"}`);
  if (url.pathname === "/health") {
    const docs = listDocs();
    send(
      response,
      docs.length > 0 ? 200 : 503,
      JSON.stringify({ ok: docs.length > 0, docs }),
      "application/json; charset=utf-8",
    );
    return;
  }
  if (url.pathname === "/vendor/mermaid.min.js") {
    send(response, 200, fs.readFileSync(mermaidJs), "text/javascript; charset=utf-8");
    return;
  }
  if (url.pathname === "/") {
    send(response, 200, indexPage(listDocs()), "text/html; charset=utf-8");
    return;
  }
  if (url.pathname.startsWith("/doc/")) {
    const name = decodeURIComponent(url.pathname.slice("/doc/".length));
    const html = docPage(name);
    if (!html) {
      send(response, 404, "document not found", "text/plain; charset=utf-8");
      return;
    }
    send(response, 200, html, "text/html; charset=utf-8");
    return;
  }
  send(response, 404, "not found", "text/plain; charset=utf-8");
});

server.listen(port, host, () => {
  console.log(`ERP docs preview listening on http://${host}:${port}`);
});
