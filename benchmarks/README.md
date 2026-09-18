<!-- Modified for Chinh Thang Revit MCP, 2026-09-18. See FORK_CHANGES.md. -->


## When to run

Run the benchmark before merging when your PR:

- Adds five or more new handler files, **or**
- Edits any tool's description text, **or**
- Precedes tagging a minor or major release (`v0.X.0` for X ≥ 2).

Not required for: internal refactors that do not change the MCP surface, bug fixes that do not touch descriptions or parameter names, patch releases.

## How to run

2. Load `benchmarks/template.md` — either `/run benchmarks/template.md` or paste its contents into the chat.

## Threshold policy

Compare param-accuracy delta vs the most recent run in `runs/`:

| Δ vs last baseline | Reaction |
|---|---|
| 5% – 15% | Flag in the PR description; merge allowed. |
| ≥ 15% | Block merge. Investigate the specific query failures — usually a description edit or tool-name collision is the culprit. |

Thresholds are reviewer conventions, not enforced CI gates.

## Baseline

`runs/2026-04-16-fc99c67-v0.1.0-baseline.md` is the reference all future runs compare against. It is derived from an internal brainstorm benchmark (18-tool RICH-description result block). Rebaseline only at major releases.
