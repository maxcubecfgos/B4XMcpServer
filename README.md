# B4X MCP Server

A Model Context Protocol (MCP) server that helps AI assistants understand and work with B4X projects (B4A, B4J, B4i). It's a companion tool for B4X developers, not a replacement for the IDE.

## Why This Exists

If you've ever tried to get an AI to help with B4X code, you know the struggle. B4X has its own way of doing things and most AIs simply don't get it. They invent methods that don't exist, corrupt project files, and confidently produce code that won't compile.

This server gives AIs the context they need to work with B4X projects correctly. It handles the tricky parts (parsing, compiling, layout encoding) so the AI can focus on writing good code. It's here to make your life easier, not to replace the B4X IDE — you'll still use the IDE for visual design, debugging, etc.

## What It Does

- Reads and writes B4X source files safely (strips IDE metadata where appropriate, preserves it where it matters)
- Compiles projects using the correct platform builder and returns errors with file names, line numbers, and source lines
- Decodes and encodes binary layout files (`.bal`/`.bjl`) to/from JSON
- Manages libraries (list available, search docs, enable/disable in project)
- Provides git diff, log, and status
- Includes a reference of B4X language gotchas that trip up AIs (and sometimes developers too)

## Installation

### Option 1: Download the prebuilt executable (recommended)

Every push of a `v*` tag triggers a GitHub Actions workflow that builds a **self-contained, single-file executable** and attaches it to a new GitHub Release — no .NET runtime or SDK installation required on your machine.

1. Download and unzip `B4XMcpServer-<version>-win-x64.zip` from the [Releases](../../releases) page.
2. Place `B4XMcpServer.exe` anywhere on disk (e.g. `C:\Tools\B4XMcpServer\B4XMcpServer.exe`).
3. Point your MCP client at it directly (see [Configuration](#configuration) below).

Prerequisites: B4A and/or B4J installed (for actual project compilation). Git and ADB are optional — features that depend on them will simply be unavailable if missing.

### Option 2: Build from source

Prerequisites: .NET 8.0 SDK.

```bash
cd B4XMcpServer
dotnet publish B4XMcpServer.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

This is the exact command the GitHub Actions release workflow runs, producing `publish\B4XMcpServer.exe`.

> **Note:** "self-contained" bundles the .NET runtime, not Git or ADB. Those remain external tools the server shells out to — install them separately and make sure they're on your `PATH` if you want the git and device features.

## Configuration

Add to your MCP client config (Claude Desktop, Cline, Continue.dev, etc.):

```json
{
  "mcpServers": {
    "b4x-tools": {
      "command": "C:\\path\\to\\B4XMcpServer.exe",
      "args": []
    }
  }
}
```

No `dotnet` command, project path, or runtime installation needed — the executable is fully self-contained.

## How It Works

The server knows about B4X project structure — the metadata section vs. source code section, how modules and layouts are registered, which files are safe to touch and which should be left alone. It handles the binary layout format correctly and knows what types and methods actually exist in B4A/B4J.

When an AI edits code, the server validates the project structure before compiling. When compilation fails, it parses the builder output into structured errors the AI can act on. Every destructive operation creates a `.bak` backup first.

## A Note on AI Models

This was developed and tested with free-tier AI models — the kind that hallucinate, get confused, and drift off-task. The guardrails built into this server (structural validation, language gotchas, pre-compile checks) were designed specifically to keep these limited models on the rails.

With stronger models, it works even better. Fewer mistakes, fewer compile-fix cycles, faster results. But even with the free models, it gets the job done without corrupting your project.

## Auto-installing AGENTS.md

When `B4XMcpServer.exe` is launched manually (from a terminal or by double-clicking) and the current directory contains a `.b4a`, `.b4j`, or `.b4i` project file, the executable will automatically create an `AGENTS.md` in that directory — or append a `B4X MCP` block to an existing one that lacks the marker — before exiting. This lets agents like FreeBuff or Codebuff discover the helper by reading `AGENTS.md` at session start: no separate configuration needed.

The block is wrapped between `<!-- BEGIN B4X MCP (auto-generated) -->` and `<!-- END B4X MCP (auto-generated) -->` markers and lists every tool exposed by the server with its description, plus a sample invocation pattern and the recommended `compile_project` post-edit verification step.

MCP-aware clients (Claude Desktop, Cursor, Cline…) pipe both stdio streams and skip this auto-install step entirely — they go straight to MCP server mode unchanged.

To re-generate the AGENTS.md block after adding new tools, delete the file and re-run `B4XMcpServer.exe` from the project directory. The block is idempotent across consecutive runs whenever the markers are intact.

### Known limitation

Git Bash / mintty on Windows sometimes reports stdout as redirected even for interactive sessions, causing the installer to fall through to MCP mode. Workaround: run the executable from Windows PowerShell or cmd.

## License

MIT License. See LICENSE for details.
