#!/usr/bin/env node
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { JSDOM } from "jsdom";
import { marked } from "marked";

const dom = new JSDOM("<!DOCTYPE html><html><body></body></html>");
globalThis.window = dom.window;
globalThis.document = dom.window.document;
globalThis.DOMParser = dom.window.DOMParser;
globalThis.HTMLElement = dom.window.HTMLElement;
globalThis.SVGElement = dom.window.SVGElement;
globalThis.Element = dom.window.Element;
globalThis.Node = dom.window.Node;

const { default: mermaid } = await import("mermaid");
mermaid.initialize({ startOnLoad: false, securityLevel: "strict" });

const docsDir =
  process.env.DOCS_DIR ||
  path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../docs");

function extractMermaid(markdown) {
  const blocks = [];
  const pattern = /```mermaid\s*\n([\s\S]*?)```/g;
  let match;
  while ((match = pattern.exec(markdown)) !== null) {
    blocks.push(match[1].trim());
  }
  return blocks;
}

async function main() {
  if (!fs.existsSync(docsDir)) {
    console.error(`docs directory not found: ${docsDir}`);
    process.exit(1);
  }

  const files = fs
    .readdirSync(docsDir)
    .filter((name) => name.endsWith(".md"))
    .sort();

  if (files.length === 0) {
    console.error(`no markdown documents in ${docsDir}`);
    process.exit(1);
  }

  let diagrams = 0;
  for (const name of files) {
    const fullPath = path.join(docsDir, name);
    const markdown = fs.readFileSync(fullPath, "utf8");
    if (!markdown.trim()) {
      console.error(`${name}: empty document`);
      process.exit(1);
    }

    const html = marked.parse(markdown);
    if (typeof html !== "string" || !html.includes("<h")) {
      console.error(`${name}: markdown did not render any headings`);
      process.exit(1);
    }

    const blocks = extractMermaid(markdown);
    const types = [];
    for (const [index, block] of blocks.entries()) {
      try {
        const parsed = await mermaid.parse(block);
        types.push(
          parsed && typeof parsed === "object" && "diagramType" in parsed
            ? parsed.diagramType
            : "diagram",
        );
      } catch (error) {
        const message = error instanceof Error ? error.message : String(error);
        console.error(`${name}: mermaid diagram ${index + 1} failed to parse`);
        console.error(message);
        process.exit(1);
      }
      diagrams += 1;
    }

    const lines = markdown.split("\n").length;
    const typeSummary = types.length > 0 ? ` (${types.join(", ")})` : "";
    console.log(
      `${name}: ${lines} lines, ${blocks.length} mermaid diagram${blocks.length === 1 ? "" : "s"}${typeSummary}`,
    );
  }

  console.log(`OK: ${files.length} documents, ${diagrams} diagrams`);
}

main().catch((error) => {
  console.error(error instanceof Error ? error.message : String(error));
  process.exit(1);
});
