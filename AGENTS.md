## graphify

This project has a knowledge graph at graphify-out/ with god nodes, community structure, and cross-file relationships. It covers the code only (game scripts, backend, tools — about 118 files); `.graphifyignore` excludes Unity's Library, assets, builds, third-party packages and docs, so building it costs no tokens.

The `graphify` command is NOT on PATH here. Use the full path:
`C:/Users/david/AppData/Roaming/Python/Python313/Scripts/graphify.exe` (written `graphify` below).

Rules:
- When graphify-out/graph.json exists, run `graphify query "<question>"` before your first grep, search or source-file read for a codebase question, and put this rule in every subagent prompt that explores code. Query with code identifiers (the code is in English, e.g. `AccountService SaveProfile`), not French phrases. Read raw files only for lines the graph or an earlier read already located. Use `graphify path "<A>" "<B>"` for relationships and `graphify explain "<concept>"` for focused concepts. Keep `--budget` small (300–1500).
- Read graphify-out/GRAPH_REPORT.md only for broad architecture review or when query/path/explain do not surface enough context.
- After modifying code, run `graphify update .` from the repo root to keep the graph current (AST-only, no API cost; `.graphifyignore` keeps it to code).
- graphify-out/ is git-ignored (local graph, rebuilt by the git post-commit hook).

## Model Selection & Routing (STRICT)

Never waste high-tier model context or quota on code reading and search. Strictly separate exploration (small models) from complex reasoning and implementation (top-tier models):

- **Code reading, searching, and exploration (MANDATORY DELEGATION)**:
  - **NEVER** read full files, browse directories, or run broad searches directly from the main agent when running on a top-tier model.
  - **ALWAYS delegate** codebase searches, file reading, `graphify` queries, and exploratory lookups to a subagent with a small model:
    - Call `invoke_subagent` with `Model: "flash"` (or `"flash_lite"` for simple/high-volume lookups), using `TypeName: "self"` (when it needs `run_command` to run `graphify`) or `TypeName: "research"` (for read-only file/codebase inspection).
    - In `swarm` or `run_workflow`, pin reader/searcher workers to `gemini-3.8-flash-low`, `gemini-3.8-flash-medium`, `gemini-4-hydrogen-low`, or `gemini-4-neon-low`.
  - Instruct the `flash` subagent to return **only a concise summary, exact file paths, line numbers, and the minimal code snippets** required, so the main agent's context window stays small.
  - The main agent may only call `read_file` directly on the exact line range (`StartLine`–`EndLine`) reported by the subagent right before calling `edit_file`.
- **Complex tasks (TOP-TIER MODELS ONLY)**:
  - Reserve top-tier models strictly for architecture decisions, complex implementation, multi-file refactoring, tricky debugging, and final verification:
    - **Gemini 4 Argon** (`gemini-4-argon-high`, `gemini-4-argon-medium`)
    - **Gemini 4 Hydrogen Ultra / High** (`gemini-4-hydrogen-xhigh`, `gemini-4-hydrogen-high`)
    - **Gemini 4 Neon Ultra / High** (`gemini-4-neon-xhigh`, `gemini-4-neon-high`)
    - **Claude Opus / Sonnet High** (`claude-opus-5-5-high`, `claude-sonnet-5-5-high`) when quota is available
    - With `invoke_subagent` for complex coding/debugging subtasks: use `Model: "pro"` or `"inherit"` (when the parent agent runs on a top-tier model).
