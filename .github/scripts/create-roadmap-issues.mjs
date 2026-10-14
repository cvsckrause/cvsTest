#!/usr/bin/env node
// Creates the labels, milestones, and issues described in the roadmap plan.
// DRY_RUN defaults to true; only DRY_RUN=false performs writes.
import fs from "node:fs";

const PLAN = process.env.ROADMAP_PLAN ?? "docs/plans/2026-10-14-roadmap-next-work-items.md";
const DRY_RUN = (process.env.DRY_RUN ?? "true").toLowerCase() !== "false";
const REPO = process.env.GITHUB_REPOSITORY;
const TOKEN = process.env.GITHUB_TOKEN;
const API = process.env.GITHUB_API_URL ?? "https://api.github.com";
const SERVER = process.env.GITHUB_SERVER_URL ?? "https://github.com";

const LABELS = {
  "priority:P0": ["b60205", "Security or blocks parallel work"],
  "priority:P1": ["d93f0b", "Correctness of the existing slice"],
  "priority:P2": ["fbca04", "Operability"],
  "priority:P3": ["c5def5", "Next business scope"],
  "size:S": ["e6f4ea", "One component, no schema change"],
  "size:M": ["b7e1c1", "2-3 layers or one migration"],
  "size:L": ["5fb97a", "Cross-module, migration + UI + decision"],
  "area:api": ["1d76db", "ASP.NET Core API"],
  "area:worker": ["1d76db", "Outbox worker"],
  "area:web": ["1d76db", "React UI"],
  "area:infra": ["1d76db", "CDK / AWS"],
  "area:persistence": ["1d76db", "EF Core, migrations, PostgreSQL"],
  "area:domain": ["1d76db", "Domain modules"],
  "area:ci": ["1d76db", "CI/CD"],
  "type:security": ["ee0701", "Security"],
  "type:bug": ["d73a4a", "Incorrect behavior"],
  "type:feature": ["a2eeef", "New capability"],
  "type:tech-debt": ["bfd4f2", "Structural or tooling work"],
  "lane:A": ["dbeafe", "Dev A - platform / backend"],
  "lane:B": ["fef3c7", "Dev B - domain / fullstack"],
  epic: ["3e4b9e", "Epic, broken down in its own plan"],
};

const MILESTONES = {
  M2A: { title: "M2a Hardening", description: "Roadmap Wave 0-1: unblock parallel work, security, correct trading slice." },
  M2B: { title: "M2b Operability", description: "Roadmap Wave 2: DB roles, observability, scripted deploy." },
  M3: { title: "M3 Einkauf + E-Rechnung", description: "Roadmap Wave 3 (D5): Einkauf basic and E-Rechnung output." },
};

const EPIC_LANES = { "E-1": "lane:B", "E-2": "lane:A" };
const EPIC_MILESTONES = { "E-1": "M3", "E-2": "M3" };
const LANE_TEXT = { "lane:A": "Dev A (platform / backend)", "lane:B": "Dev B (domain / fullstack)" };

export function parsePlan(markdown) {
  const lines = markdown.split(/\r?\n/);
  const items = [];
  const decisions = {};
  let wave = null;
  let current = null;
  let section = null;

  const finish = () => {
    if (!current) return;
    current.body = current.bodyLines.join("\n").trim();
    delete current.bodyLines;
    items.push(current);
    current = null;
  };

  for (const line of lines) {
    const h2 = line.match(/^## (.+)$/);
    if (h2) {
      finish();
      section = h2[1];
      const w = section.match(/^Wave (\d)/);
      wave = w ? Number(w[1]) : null;
      continue;
    }

    const h3 = line.match(/^### (R-\d{2}) — (.+)$/);
    if (h3) {
      finish();
      current = {
        key: h3[1],
        title: `${h3[1]} — ${h3[2].trim()}`,
        milestone: wave === 2 ? "M2B" : "M2A",
        tagLine: null,
        bodyLines: [],
      };
      continue;
    }

    if (current) {
      if (/^---\s*$/.test(line)) {
        finish();
        continue;
      }
      if (current.tagLine === null && line.trim() !== "") {
        current.tagLine = line.trim();
        Object.assign(current, parseTagLine(current.tagLine, current.key));
        continue;
      }
      current.bodyLines.push(line);
      continue;
    }

    if (wave === 3 && line.startsWith("| **E-")) {
      items.push(parseEpicRow(line));
      continue;
    }

    if (section === "Decisions") {
      const row = line.match(/^\| (D\d) \| (.+?) \| (.+?) \|$/);
      if (row) decisions[row[1]] = { question: row[2].trim(), outcome: row[3].trim() };
    }
  }
  finish();
  return { items, decisions };
}

function parseTagLine(tagLine, key) {
  const [tagsPart, depsPart = ""] = tagLine.split("— depends on:");
  const labels = [];
  for (const [, name, value] of tagsPart.matchAll(/`([a-z]+):([^`]+)`/g)) {
    for (const v of value.split(",")) labels.push(`${name}:${v.trim()}`);
  }
  if (!labels.some((l) => l.startsWith("lane:"))) throw new Error(`${key}: tag line has no lane label`);
  return {
    labels,
    dependsOn: [...depsPart.matchAll(/R-\d{2}/g)].map((m) => m[0]),
    decisionRefs: [...depsPart.matchAll(/D\d/g)].map((m) => m[0]),
  };
}

function parseEpicRow(line) {
  const cells = line.split("|").slice(1, -1).map((c) => c.trim());
  if (cells.length !== 3) throw new Error(`Epic row must have 3 cells: ${line}`);
  const head = cells[0].match(/^\*\*(E-\d) (.+?)\*\*\s*(.*)$/);
  if (!head) throw new Error(`Cannot parse epic row: ${line}`);
  const [, key, rawName, rest] = head;
  const name = rawName.replace(/:$/, "").trim();
  const title = rest ? `${key} — ${name}${rest.startsWith("(") ? " " : ": "}${rest}` : `${key} — ${name}`;
  return {
    key,
    title,
    milestone: EPIC_MILESTONES[key] ?? null,
    labels: ["epic", "priority:P3", ...(EPIC_LANES[key] ? [EPIC_LANES[key]] : [])],
    dependsOn: [...cells[2].matchAll(/R-\d{2}/g)].map((m) => m[0]),
    decisionRefs: EPIC_MILESTONES[key] ? ["D5"] : [],
    body: `**Why.** ${cells[1]}\n\nThis epic is broken down in its own \`docs/plans/\` unified plan before work starts.`,
  };
}

export function renderBody(item, ctx) {
  const parts = [item.body, "", "---"];
  const lane = item.labels.find((l) => l.startsWith("lane:"));
  if (lane) parts.push(`**Lane:** ${LANE_TEXT[lane]}`);
  if (item.dependsOn.length) {
    parts.push("", "**Depends on:**");
    for (const dep of item.dependsOn) {
      const number = ctx.numbers.get(dep);
      parts.push(number ? `- [ ] #${number}` : `- [ ] ${ctx.titles.get(dep) ?? dep}`);
    }
  }
  if (item.decisionRefs.length) {
    parts.push("", "**Accepted decisions:**");
    for (const ref of item.decisionRefs) {
      const decision = ctx.decisions[ref];
      if (!decision) throw new Error(`${item.key}: unknown decision ${ref}`);
      parts.push(`- ${ref}: ${decision.outcome}`);
    }
  }
  parts.push("", `Source: [\`${PLAN}\`](${SERVER}/${ctx.repo}/blob/main/${PLAN})`, `<!-- roadmap-key: ${item.key} -->`);
  return parts.join("\n");
}

export function validate(items) {
  const keys = new Set(items.map((i) => i.key));
  const errors = [];
  for (const item of items) {
    for (const label of item.labels) if (!LABELS[label]) errors.push(`${item.key}: unknown label ${label}`);
    for (const dep of item.dependsOn) if (!keys.has(dep)) errors.push(`${item.key}: unknown dependency ${dep}`);
  }
  if (keys.size !== items.length) errors.push("Duplicate roadmap keys");
  if (errors.length) throw new Error(errors.join("\n"));
}

async function github(method, path, body) {
  const response = await fetch(`${API}${path}`, {
    method,
    headers: {
      Authorization: `Bearer ${TOKEN}`,
      Accept: "application/vnd.github+json",
      "X-GitHub-Api-Version": "2022-11-28",
      ...(body ? { "Content-Type": "application/json" } : {}),
    },
    body: body ? JSON.stringify(body) : undefined,
  });
  if (!response.ok) throw new Error(`${method} ${path} -> ${response.status}: ${await response.text()}`);
  return response.status === 204 ? null : response.json();
}

async function paginate(path) {
  const results = [];
  for (let page = 1; ; page++) {
    const batch = await github("GET", `${path}${path.includes("?") ? "&" : "?"}per_page=100&page=${page}`);
    results.push(...batch);
    if (batch.length < 100) return results;
  }
}

// GitHub's secondary rate limit for content creation is easy to hit with back-to-back writes.
const pause = () => new Promise((resolve) => setTimeout(resolve, 1500));

async function main() {
  const { items, decisions } = parsePlan(fs.readFileSync(PLAN, "utf8"));
  validate(items);
  const ctx = { repo: REPO ?? "OWNER/REPO", decisions, numbers: new Map(), titles: new Map(items.map((i) => [i.key, i.title])) };

  let existingLabels = null;
  let existingMilestones = null;
  let existingIssues = [];
  if (TOKEN && REPO) {
    existingLabels = new Set((await paginate(`/repos/${REPO}/labels`)).map((l) => l.name));
    existingMilestones = new Map((await paginate(`/repos/${REPO}/milestones?state=all`)).map((m) => [m.title, m.number]));
    existingIssues = (await paginate(`/repos/${REPO}/issues?state=all`)).filter((i) => !i.pull_request);
  } else if (!DRY_RUN) {
    throw new Error("GITHUB_TOKEN and GITHUB_REPOSITORY are required when DRY_RUN=false.");
  }

  const findExisting = (key) => existingIssues.find((i) => i.title.startsWith(`${key} — `) || i.body?.includes(`roadmap-key: ${key} `));
  for (const item of items) {
    const found = findExisting(item.key);
    if (found) ctx.numbers.set(item.key, found.number);
  }

  const report = [];
  const mode = DRY_RUN ? "DRY RUN (no writes)" : "LIVE";
  console.log(`Roadmap issues — ${mode} — ${PLAN} -> ${REPO ?? "(no repository)"}`);

  for (const [name, [color, description]] of Object.entries(LABELS)) {
    const exists = existingLabels?.has(name);
    if (exists) continue;
    console.log(`label     ${exists === undefined ? "would ensure" : DRY_RUN ? "would create" : "create"} ${name}`);
    if (!DRY_RUN) await github("POST", `/repos/${REPO}/labels`, { name, color, description });
  }

  const milestoneNumbers = new Map();
  for (const [id, milestone] of Object.entries(MILESTONES)) {
    const number = existingMilestones?.get(milestone.title);
    if (number) {
      milestoneNumbers.set(id, number);
      continue;
    }
    console.log(`milestone ${existingMilestones ? (DRY_RUN ? "would create" : "create") : "would ensure"} ${milestone.title}`);
    if (!DRY_RUN) milestoneNumbers.set(id, (await github("POST", `/repos/${REPO}/milestones`, milestone)).number);
  }

  const created = [];
  for (const item of items) {
    const number = ctx.numbers.get(item.key);
    const milestoneTitle = item.milestone ? MILESTONES[item.milestone].title : "(none)";
    if (number) {
      report.push({ item, action: `skipped (exists #${number})`, milestoneTitle });
      continue;
    }
    if (DRY_RUN) {
      report.push({ item, action: "would create", milestoneTitle });
      continue;
    }
    const issue = await github("POST", `/repos/${REPO}/issues`, {
      title: item.title,
      body: renderBody(item, ctx),
      labels: item.labels,
      ...(item.milestone ? { milestone: milestoneNumbers.get(item.milestone) } : {}),
    });
    ctx.numbers.set(item.key, issue.number);
    created.push(item);
    report.push({ item, action: `created #${issue.number}`, milestoneTitle });
    await pause();
  }

  // Second pass: dependencies on issues created later in this run are only now resolvable.
  // Issues left with unlinked dependencies by an earlier, interrupted run are repaired too.
  const unlinked = (key) => /^- \[ \] [RE]-\d/m.test(findExisting(key)?.body ?? "");
  const toLink = items.filter((item) => item.dependsOn.length && (created.includes(item) || unlinked(item.key)));
  for (const item of toLink) {
    if (DRY_RUN) {
      if (!created.includes(item)) console.log(`issue     would link deps of ${item.title}`);
      continue;
    }
    await github("PATCH", `/repos/${REPO}/issues/${ctx.numbers.get(item.key)}`, { body: renderBody(item, ctx) });
    await pause();
  }

  for (const { item, action, milestoneTitle } of report) {
    console.log(`issue     ${action.padEnd(22)} ${item.title}  [${item.labels.join(", ")}] {${milestoneTitle}} deps: ${item.dependsOn.join(", ") || "-"}`);
  }

  if (DRY_RUN && process.env.ROADMAP_PRINT_BODIES === "true") {
    for (const item of items) console.log(`\n===== ${item.title}\n${renderBody(item, ctx)}`);
  }

  if (process.env.GITHUB_STEP_SUMMARY) {
    const rows = report.map(({ item, action, milestoneTitle }) =>
      `| ${item.key} | ${action} | ${item.title.replace(/^\S+ — /, "").replace(/\|/g, "\\|")} | ${item.labels.join(" ")} | ${milestoneTitle} | ${item.dependsOn.join(", ") || "-"} |`);
    fs.appendFileSync(process.env.GITHUB_STEP_SUMMARY, [
      `## Roadmap issues — ${mode}`, "",
      "| Key | Action | Title | Labels | Milestone | Depends on |",
      "| --- | --- | --- | --- | --- | --- |",
      ...rows, "",
    ].join("\n"));
  }
}

if (import.meta.url === `file://${process.argv[1]}`) {
  main().catch((error) => {
    console.error(error.message);
    process.exit(1);
  });
}
