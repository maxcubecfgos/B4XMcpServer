---
name: b4x
description: B4X (B4A for Android, B4J for desktop/server, B4i for iOS) development patterns, XUI cross-platform library, B4XPages, resumable subs, SQLite, and current best practices. Use whenever writing, reviewing, refactoring, or reasoning about B4X/B4A/B4J/B4i code, .bas modules, b4xlib libraries, or migrating older B4X syntax to current idioms.
---

# B4X Development

Full language reference, XUI library, B4XPages framework, database patterns,
collections, custom views, and the current best-practices table (what's
deprecated vs. current) live in [reference.md](reference.md).

## When to load reference.md

Read `reference.md` whenever the task involves:

- Writing or reviewing B4A/B4J/B4i code
- B4XPages lifecycle, XUI/B4XView/B4XCanvas usage
- SQLite (SQL/ResultSet), Resumable Subs, Wait For
- Deciding whether a pattern in existing code is outdated (see the
  "Best Practices (What to Avoid)" table in reference.md — check it
  before assuming an old snippet found online is still current)

## Quick rules to apply even without reading further

- Always target B4XPages, never legacy Activities, for new B4A projects.
- Never use the Starter service in new B4A code (deprecated as of v13.5) —
  declare process-global objects in B4XMainPage/Main instead.
- Always use parameterized queries (ExecQuery2/ExecNonQuery2), never
  ExecQuery/ExecNonQuery with concatenated SQL.
- Prefer B4XView/B4XCanvas/XUI over platform-specific views for anything
  meant to be cross-platform.

## Before editing .bas/.b4a/.b4j/.b4i files directly

These files have a strict metadata header (before `@EndOfDesignText@`) and,
for project files, sequentially numbered Library/File/Module entries with
matching counts. Editing these incorrectly corrupts the project silently
(no compile error) rather than loudly. Read section 18 of reference.md
("Module & Project File Structure") before writing to any of these files —
it covers the exact rules for adding/removing libraries, modules, and
designer properties without breaking numbering, and what must never be
touched (Version=, .meta files).

When modifying these files via the MCP code-editing tools
(`edit_line`, `insert_line`, `replace_lines`, `edit_sub`, `analyze_module`),
always use **FILE-LINE** coordinates — 1-based line numbers counted from
the absolute first line of the file, INCLUDING the IDE metadata header.
`get_file_content` returns an explicit `lines` array that maps the exact
text to FILE-LINE numbers; copy those numbers directly into editing
tools. Do NOT subtract a "header offset" yourself — the tools
internally reject edits targeting header rows `[1, lineOffset]`. The
legacy `content` field on `get_file_content` is source-line only and is
kept for backward compatibility; prefer `lines` to avoid the off-by-N
confusion that compiler error output (which is FILE-LINE) is meant to
prevent.
