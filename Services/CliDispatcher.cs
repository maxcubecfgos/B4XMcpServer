using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using B4XMcpServer.Utils;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace B4XMcpServer.Services
{
    /// <summary>
    /// argv-driven dispatcher. Routes argv[0] to one of four modes:
    /// <list type="bullet">
    ///   <item><description><c>--help</c> / <c>-h</c> / <c>/?</c> — human-readable tool list.</description></item>
    ///   <item><description><c>--list-tools</c> — JSON array of all tools with name, description and parameter shape.</description></item>
    ///   <item><description><c>--describe &lt;name&gt;</c> — JSON Schema for one tool's parameters.</description></item>
    ///   <item><description><c>&lt;tool-name&gt; [--key=value]…</c> — invoke a tool, mapping flags to its parameters via reflection.</description></item>
    /// </list>
    /// MCP-aware clients that pass no args fall through to the MCP host;
    /// manual no-args invocations are caught by <see cref="B4xProjectInstaller"/>
    /// before reaching this layer, so by the time <see cref="TryRun"/> is
    /// called we always have at least one argv element.
    /// </summary>
    public static class CliDispatcher
    {
        private const int ExitOk = 0;
        private const int ExitToolError = 2;
        private const int ExitUsage = 64; // sysexits.h EX_USAGE

    // Catalog is built once per process via reflection. The cost is small but
    // this keeps CLI invocations deterministic and makes the dispatcher easier
    // to test if ever called multiple times in the same process.
    private static readonly Lazy<IReadOnlyList<ToolEntry>> CatalogLazy = new(BuildCatalog);

    /// <summary>
    /// Parses <paramref name="args"/>, dispatches to the right mode, and
    /// returns the process exit code. Never throws an unhandled exception;
    /// all error paths return a meaningful exit code.
    /// </summary>
    public static async Task<int> TryRun(string[] args)
    {
        // Defensive: caller is supposed to gate on args.Length >= 1.
        if (args == null || args.Length == 0)
        {
            Console.Error.WriteLine("CliDispatcher.TryRun called without arguments; this is a programmer error.");
            return ExitUsage;
        }

        var catalog = CatalogLazy.Value;
        string action = args[0];

            switch (action)
            {
                case "--help":
                case "-h":
                case "/?":
                    PrintHelp(catalog);
                    return ExitOk;

                case "--list-tools":
                    PrintListTools(catalog);
                    return ExitOk;

                case "--describe":
                    if (args.Length < 2)
                    {
                        Console.Error.WriteLine("Usage: --describe <tool-name>");
                        return ExitUsage;
                    }
                    return PrintDescribe(catalog, args[1]);

                default:
                    return await InvokeToolAsync(catalog, action, args.Skip(1).ToArray()).ConfigureAwait(false);
            }
        }

        // ── Catalog records ─────────────────────────────────────────
        private sealed record ParamDef(string Name, Type Type, string Description, bool Required, object? DefaultValue);

        private sealed record ToolEntry(string Name, string Description, IReadOnlyList<ParamDef> Parameters, MethodInfo Method, Type DeclaringType);

        // ── Catalog build ───────────────────────────────────────────
        // Reflects over SupportedTools.AllTypes once per process invocation.
        // The cost (~50 ms) is dominated by the JIT and assembly load already
        // paid by top-level statements; we don't bother caching it in a
        // process-lifetime static because each CLI invocation is a fresh process.
        private static IReadOnlyList<ToolEntry> BuildCatalog()
        {
            var entries = new List<ToolEntry>();
            foreach (var type in SupportedTools.AllTypes)
            {
                var methods = type.GetMethods(
                        BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Where(m => m.GetCustomAttribute<McpServerToolAttribute>(inherit: false) != null)
                    .OrderBy(m => m.Name, StringComparer.Ordinal);

                foreach (var method in methods)
                {
                    var toolAttr = method.GetCustomAttribute<McpServerToolAttribute>()!;
                    var descAttr = method.GetCustomAttribute<DescriptionAttribute>();
                    string name = !string.IsNullOrEmpty(toolAttr.Name) ? toolAttr.Name : ToSnakeCase(method.Name);
                    string description = descAttr?.Description?.Trim() ?? string.Empty;

                    var parameters = method.GetParameters().Select(p => new ParamDef(
                        Name: p.Name!,
                        Type: p.ParameterType,
                        Description: p.GetCustomAttribute<DescriptionAttribute>()?.Description?.Trim() ?? string.Empty,
                        Required: p.HasDefaultValue
                        ? false
                        : (p.ParameterType == typeof(string)
                            ? true
                            : !IsAlwaysNullable(p.ParameterType)),
                        DefaultValue: p.HasDefaultValue ? p.DefaultValue : null
                    )).ToList();

                    entries.Add(new ToolEntry(name, description, parameters, method, method.DeclaringType!));
                }
            }
            return entries;
        }

        // ── Mode printers ───────────────────────────────────────────
        private static void PrintHelp(IReadOnlyList<ToolEntry> catalog)
        {
            var w = new StringBuilder();
            w.AppendLine("Usage: B4XMcpServer.exe <command> [--key=value ...]");
            w.AppendLine();
            w.AppendLine("Built-in commands:");
            w.AppendLine("  --help, -h, /?     Show this help message.");
            w.AppendLine("  --list-tools       JSON array of all available tools.");
            w.AppendLine("  --describe <tool>  JSON Schema for one tool's parameters.");
            w.AppendLine();
            w.AppendLine($"Available tools ({catalog.Count}):");
            w.AppendLine();
            foreach (var entry in catalog)
            {
                w.Append("  ").AppendLine(entry.Name);
                if (!string.IsNullOrEmpty(entry.Description))
                {
                    foreach (var line in entry.Description.Split('\n'))
                        w.Append("    ").AppendLine(line.TrimEnd('\r').Trim());
                }
                w.AppendLine();
            }
            w.AppendLine("Notes:");
            w.AppendLine("  * Flags are case-insensitive. Argument order does not matter.");
            w.AppendLine("  * Values are coerced from strings. Numbers and bool accept their natural forms;");
            w.AppendLine("    other complex types accept JSON.");
            w.AppendLine("  * Output is JSON on stdout. Errors go to stderr with non-zero exit code.");
            w.AppendLine();
            w.AppendLine("Examples:");
            w.AppendLine(@"  B4XMcpServer.exe compile_project --projectPath=""C:\MyApp""");
            w.AppendLine(@"  B4XMcpServer.exe get_file_content --filePath=""C:\MyApp\Main.b4a""");
            w.AppendLine(@"  B4XMcpServer.exe get_full_context --ProjectPath=""C:\MyApp"" --RunCompile=true");
            Console.Write(w.ToString());
        }

        private static void PrintListTools(IReadOnlyList<ToolEntry> catalog)
        {
            var rows = catalog.Select(e => new
            {
                name = e.Name,
                description = e.Description,
                parameters = e.Parameters.Select(p => new
                {
                    name = p.Name,
                    type = TypeNameFor(p.Type),
                    required = p.Required,
                }).ToArray(),
            }).ToArray();
            Console.WriteLine(JsonSerializer.Serialize(rows, JsonOptions.Default));
        }

        private static int PrintDescribe(IReadOnlyList<ToolEntry> catalog, string toolName)
        {
            var entry = catalog.FirstOrDefault(e => string.Equals(e.Name, toolName, StringComparison.Ordinal));
            if (entry == null)
            {
                Console.Error.WriteLine($"Unknown tool: '{toolName}'. Run --list-tools to see available tools.");
                return ExitUsage;
            }

            // Standard JSON Schema (subset) — meaningful to LLMs and to tools that
            // already understand JSON Schema. Avoids inventing a custom schema that
            // would need every consumer reinvented.
            var schema = new
            {
                name = entry.Name,
                description = entry.Description,
                parameters = new
                {
                    type = "object",
                    properties = entry.Parameters.ToDictionary(
                        p => p.Name,
                        p => (object)new
                        {
                            type = TypeNameFor(p.Type),
                            description = p.Description,
                            @default = p.DefaultValue,
                        }),
                    required = entry.Parameters.Where(p => p.Required).Select(p => p.Name).ToArray(),
                },
            };
            Console.WriteLine(JsonSerializer.Serialize(schema, JsonOptions.Default));
            return ExitOk;
        }

        // ── Tool invocation ─────────────────────────────────────────
        private static async Task<int> InvokeToolAsync(
            IReadOnlyList<ToolEntry> catalog,
            string toolName,
            string[] rest)
        {
            var entry = catalog.FirstOrDefault(e => string.Equals(e.Name, toolName, StringComparison.Ordinal));
            if (entry == null)
            {
                Console.Error.WriteLine($"Unknown tool: '{toolName}'. Run --list-tools to see available tools.");
                return ExitUsage;
            }

            var parsed = ParseFlags(rest, out var unknownFlags);
            if (unknownFlags.Count > 0)
            {
                Console.Error.WriteLine($"Unknown argument(s) for tool '{toolName}': {string.Join(", ", unknownFlags)}");
                return ExitUsage;
            }

            object?[] callArgs;
            try
            {
                callArgs = ResolveCallArgs(entry, parsed);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Argument error: {ex.Message}");
                return ExitUsage;
            }

            // Build a one-shot DI graph shared by every method on the same tool
            // class. ToolHostServices is the same helper Program.cs uses for the
            // MCP host, so CLI and MCP paths construct identical object graphs —
            // including cache managers and any future repository-level state.
            // BuildServiceProvider() validates scopes on dispose; the lifetime is
            // process-bounded, so leaving it alive until InvokeToolAsync returns
            // is safe and the cheapest option given each CLI invocation runs in
            // a fresh process. Note: ServiceCollection itself is intentionally
            // not wrapped in `using` — in the resolved package chain it does
            // not implement IDisposable, and even where it does Dispose is a
            // no-op (only clears internal lists, holds no unmanaged resources).
            var services = new ServiceCollection();
            ToolHostServices.RegisterCoreRepositories(services);
            using var sp = services.BuildServiceProvider();
            var instanceCache = new Dictionary<Type, object>();

            try
            {
                // Static methods (none today, but supported for future expansion)
                // keep passing null. Instance methods need a real instance so .NET
                // can match the method's `this` parameter with the resolved object
                // — otherwise MethodInfo.Invoke raises TargetException:
                // "Non-static method requires a target".
                object? target = entry.Method.IsStatic
                    ? null
                    : (instanceCache.TryGetValue(entry.DeclaringType, out var cached)
                        ? cached
                        : instanceCache[entry.DeclaringType] = ActivatorUtilities.CreateInstance(sp, entry.DeclaringType));

                object? result = entry.Method.Invoke(target, callArgs);

                if (result is Task task)
                {
                    // We are already on an async Main context, so a real await is safe
                    // and the deadlock-free idiom. Avoid .Result / .Wait() entirely.
                    await task.ConfigureAwait(false);
                    if (task.GetType().IsGenericType)
                    {
                        var resultProp = task.GetType().GetProperty("Result")!;
                        result = resultProp.GetValue(task);
                    }
                    else
                    {
                        result = null;
                    }
                }

                // Most tool methods return a JSON string already; pass it through
                // byte-for-byte. Anything else falls back to JSON serialization.
                if (result is string strResult)
                    Console.WriteLine(strResult);
                else
                    Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions.Default));

                return ExitOk;
            }
            catch (TargetInvocationException tie)
            {
                // Inner is the actual exception thrown by the tool method.
                var inner = tie.InnerException ?? tie;
                return EmitToolError(inner);
            }
            catch (Exception ex)
            {
                return EmitToolError(ex);
            }
        }

        private static int EmitToolError(Exception ex)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new
            {
                success = false,
                error = ex.Message,
                exceptionType = ex.GetType().FullName,
                stackTrace = ex.StackTrace,
            }, JsonOptions.Default));
            return ExitToolError;
        }

        // ── Parameter binding ───────────────────────────────────────
        private static object?[] ResolveCallArgs(ToolEntry entry, IReadOnlyDictionary<string, string> parsed)
        {
            var args = new object?[entry.Parameters.Count];

            // Request-object optimization: a method whose single parameter is a
            // class (e.g. GetFullContextRequest) is friendlier invoked by
            // binding the CLI flags directly to that object's writable
            // properties, with --Request=<json> as an explicit override.
            if (entry.Parameters.Count == 1 && !IsSimpleType(entry.Parameters[0].Type))
            {
                Type reqType = entry.Parameters[0].Type;

                if (parsed.TryGetValue("Request", out var jsonBlob) && !string.IsNullOrEmpty(jsonBlob))
                {
                    args[0] = JsonSerializer.Deserialize(jsonBlob, reqType, JsonOptions.Default);
                    return args;
                }

                var instance = Activator.CreateInstance(reqType)!;
                foreach (var prop in reqType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!prop.CanWrite) continue;
                    if (parsed.TryGetValue(prop.Name, out var raw))
                    {
                        prop.SetValue(instance, CoerceValue(raw, prop.PropertyType));
                    }
                }
                args[0] = instance;
                return args;
            }

            // Round-4 polish (expanded from round-3's 2-entry conservative table to 10 canonical
            // names, each with up to 3 synonyms). AI clients and human CLI users often infer
            // alternate parameter names from the [Description(...)] text, from common command-
            // line conventions (--file, --path, --query), or from a related tool seen earlier
            // (e.g. search_code uses --pattern, so search_library was being called with
            // --pattern). Canonical still wins when both are present; aliases are tried in
            // declared order and the first match wins.
            //
            // Keys are CANONICAL names that must match the C# parameter name on the tool.
            // Values are ALL synonyms the dispatcher will accept (in addition to canonical).
            //
            // NOTE on '--path': historically this was an alias for BOTH filePath and
            // projectPath, which is safe only when a tool has one OR the other (never
            // both). git_show broke that assumption by having both, so '--path' is now
            // reserved for projectPath. filePath keeps 'moduleName' and 'file' as aliases.
            var aliasByCanonical = new System.Collections.Generic.Dictionary<string, string[]>(System.StringComparer.OrdinalIgnoreCase)
            {
                // File-path family (get_file_content / analyze_module / edit_* / write_file).
                ["filePath"]    = new[] { "moduleName", "file" },
                // Layout-path family (get_layout_structure / list_layout_controls / layout_*_control).
                ["layoutPath"]  = new[] { "layoutFile", "layout" },
                // Project-path family (compile_project / validate_project / get_project_config /
                // get_project_structure / get_manifest / write_manifest / validate_event_handlers,
                // and all the new git_* tools that take a repo path). '--path' is the universal
                // "where is the project / repo" alias.
                ["projectPath"] = new[] { "path" },

                // Library inspection family (LibraryTools).
                ["libraryName"] = new[] { "library" },
                ["typeName"]    = new[] { "type" },
                ["eventName"]   = new[] { "event" },
                // Parity with search_code (which uses --pattern): search_library's first param
                // is internally named 'query' but humans naturally type --pattern first.
                ["query"]       = new[] { "pattern" },
                // Layout control management (LayoutTools).
                ["controlType"] = new[] { "type" },
                ["controlName"] = new[] { "name" },
                // Workflow + Runtime.
                ["task"]        = new[] { "input", "description" },
                ["stackTrace"]  = new[] { "trace" },
            };

            // Round-4 hardening (per-tool, recommended by reviewer): assert that the CURRENT
            // tool's parameters do not share the same alias. Global alias overlaps BETWEEN
            // different tools are intentional (e.g. "type" maps to typeName in LibraryTools and
            // controlType in LayoutTools; "path" maps to filePath in ProjectTools and
            // projectPath elsewhere). What would be a bug is one tool having TWO of its own
            // params share an alias — the first one would silently shadow the rest. This guard
            // catches that exact failure mode, per tool, with zero refactoring of the alias
            // table to a static field.
            {
                var toolAliases = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                foreach (var p in entry.Parameters)
                {
                    if (aliasByCanonical.TryGetValue(p.Name, out var aliases))
                    {
                        foreach (var alias in aliases)
                        {
                            if (!toolAliases.Add(alias))
                            {
                                throw new InvalidOperationException(
                                    $"B4XMcpServer error: tool '{entry.Name}' has conflicting alias '--{alias}' across its parameters. " +
                                    $"An alias can only appear once per tool's parameter list. Fix aliasByCanonical in CliDispatcher.cs.");
                            }
                        }
                    }
                }
            }

            for (int i = 0; i < entry.Parameters.Count; i++)
            {
                var p = entry.Parameters[i];

                // 1. Canonical name first (case-insensitive — parsed dict already uses OrdinalIgnoreCase).
                if (parsed.TryGetValue(p.Name, out var raw))
                {
                    args[i] = CoerceValue(raw, p.Type);
                    continue;
                }

                // 2. Try every configured alias in declared order; first match wins.
                if (aliasByCanonical.TryGetValue(p.Name, out var aliases))
                {
                    bool bound = false;
                    foreach (var alias in aliases)
                    {
                        if (parsed.TryGetValue(alias, out raw))
                        {
                            args[i] = CoerceValue(raw, p.Type);
                            bound = true;
                            break;
                        }
                    }
                    if (bound) continue;
                }

                // 3. Canonical + aliases exhausted. Surface a CLEAR error rather than letting
                // a null sail into PathSecurity.ValidateAbsolutePath ("Path cannot be empty.").
                if (p.Required)
                {
                    var attempted = new System.Collections.Generic.List<string> { $"--{p.Name}" };
                    if (aliasByCanonical.TryGetValue(p.Name, out var aliasesForMsg))
                        attempted.AddRange(aliasesForMsg.Select(a => $"--{a}"));
                    throw new InvalidOperationException(
                        $"Missing required parameter: tried {string.Join("/", attempted)} (type {TypeNameFor(p.Type)}). " +
                        $"Run 'B4XMcpServer.exe --list-tools' or '--describe <tool>' to see the exact canonical flag name.");
                }
                args[i] = p.DefaultValue;
            }
            return args;
        }

        // ── Coercion ────────────────────────────────────────────────
        private static object? CoerceValue(string raw, Type target)
        {
            Type underlying = Nullable.GetUnderlyingType(target) ?? target;

            if (underlying == typeof(string))
                return raw;

            // Empty string → null for nullable reference/value types; otherwise
            // rejection (caller passes through to TypeMismatch via the catch).
            if (string.IsNullOrEmpty(raw))
            {
                if (!target.IsValueType || Nullable.GetUnderlyingType(target) != null)
                    return null;
                throw new InvalidOperationException(
                    $"Cannot convert empty string to non-nullable {target.Name}");
            }

            try
            {
                if (underlying == typeof(bool))
                    return raw == "1"
                        || raw.Equals("true", StringComparison.OrdinalIgnoreCase)
                        || raw.Equals("yes", StringComparison.OrdinalIgnoreCase);

                if (underlying == typeof(int)) return int.Parse(raw, CultureInfo.InvariantCulture);
                if (underlying == typeof(long)) return long.Parse(raw, CultureInfo.InvariantCulture);
                if (underlying == typeof(short)) return short.Parse(raw, CultureInfo.InvariantCulture);
                if (underlying == typeof(byte)) return byte.Parse(raw, CultureInfo.InvariantCulture);
                if (underlying == typeof(double)) return double.Parse(raw, CultureInfo.InvariantCulture);
                if (underlying == typeof(float)) return float.Parse(raw, CultureInfo.InvariantCulture);
                if (underlying == typeof(decimal)) return decimal.Parse(raw, CultureInfo.InvariantCulture);
                if (underlying == typeof(DateTime)) return DateTime.Parse(raw, CultureInfo.InvariantCulture);
                if (underlying == typeof(DateTimeOffset)) return DateTimeOffset.Parse(raw, CultureInfo.InvariantCulture);
                if (underlying == typeof(Guid)) return Guid.Parse(raw);
                if (underlying.IsEnum) return Enum.Parse(underlying, raw, ignoreCase: true);

                // Fallback: try JSON. Allows callers to pass arbitrary objects
                // (records, dictionaries, arrays) as a JSON string.
                return JsonSerializer.Deserialize(raw, underlying, JsonOptions.Default);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Cannot convert value '{raw}' to {target.Name}: {ex.Message}", ex);
            }
        }

        // ── Argv parsing ────────────────────────────────────────────
        private static IReadOnlyDictionary<string, string> ParseFlags(string[] args, out List<string> unknownFlags)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            unknownFlags = new List<string>();
            foreach (var arg in args)
            {
                if (!arg.StartsWith("--", StringComparison.Ordinal))
                {
                    unknownFlags.Add(arg);
                    continue;
                }
                int eq = arg.IndexOf('=');
                if (eq <= 2)
                {
                    // Either exactly "--" or "--=" (no key); treat as malformed.
                    unknownFlags.Add(arg);
                    continue;
                }
                string key = arg.Substring(2, eq - 2);
                string value = arg.Substring(eq + 1);
                dict[key] = value;
            }
            return dict;
        }

        // ── Helpers ─────────────────────────────────────────────────
        private static bool IsSimpleType(Type t)
        {
            var u = Nullable.GetUnderlyingType(t) ?? t;
            return u == typeof(string)
                || u.IsPrimitive
                || u.IsEnum
                || u == typeof(decimal)
                || u == typeof(DateTime)
                || u == typeof(DateTimeOffset)
                || u == typeof(Guid);
        }

        private static bool IsAlwaysNullable(Type t)
            => !t.IsValueType || Nullable.GetUnderlyingType(t) != null;

        // JSON-Schema-ish type names. Good enough for LLM consumption; not
        // an exhaustive coverage of every BCL type.
        private static string TypeNameFor(Type t)
        {
            var u = Nullable.GetUnderlyingType(t) ?? t;
            if (u == typeof(int) || u == typeof(long) || u == typeof(short) || u == typeof(byte))
                return "integer";
            if (u == typeof(double) || u == typeof(float) || u == typeof(decimal))
                return "number";
            if (u == typeof(bool))
                return "boolean";
            if (u == typeof(string) || u == typeof(DateTime) || u == typeof(DateTimeOffset) || u == typeof(Guid))
                return "string";
            if (u.IsEnum)
                return "string";
            if (u.IsArray)
                return "array";
            return "object";
        }

        // PascalCase → snake_case. Identical algorithm as B4xProjectInstaller.ToSnakeCase:
        // duplicated here to keep the two layers independent. Refactoring into a
        // shared NameConverter is a candidate for a future cleanup pass.
        private static string ToSnakeCase(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            var sb = new StringBuilder(name.Length + 4);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1]))
                    sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }
    }
}
