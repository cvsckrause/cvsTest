import assert from "node:assert/strict";
import test from "node:test";
import { gapPatch } from "./create-roadmap-issues.mjs";

const r22 = {
  labels: ["priority:P1", "size:S", "area:domain", "area:worker", "type:feature", "lane:B"],
};

test("a bare issue gains its plan labels and milestone", () => {
  const patch = gapPatch({ labels: [], milestone: null }, r22, 4);
  assert.deepEqual(patch, { labels: r22.labels, milestone: 4 });
});

test("lane B is added without dropping a label already on the issue", () => {
  const patch = gapPatch({ labels: [{ name: "priority:P1" }], milestone: { number: 4 } }, r22, 4);
  assert.deepEqual(patch.labels, ["priority:P1", "size:S", "area:domain", "area:worker", "type:feature", "lane:B"]);
  assert.equal(patch.milestone, undefined);
});

test("an issue that already matches the plan is left alone", () => {
  const patch = gapPatch({ labels: r22.labels.map((name) => ({ name })), milestone: { number: 4 } }, r22, 4);
  assert.equal(patch, null);
});

test("a milestone someone already set is not replaced", () => {
  const patch = gapPatch({ labels: r22.labels.map((name) => ({ name })), milestone: { number: 9 } }, r22, 4);
  assert.equal(patch, null);
});
