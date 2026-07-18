using System;
using System.Collections.Generic;

namespace B4XMcpServer.Engine
{
    public enum KeywordScope { Undefined, Local, Global, CodeSpace }
    public enum KeywordType { Undefined, Parameter, Variable, Sub }
    public enum ModuleType { Undefined, Class, StaticCode, Service }
    public enum FunctionScope { Undefined, Private, Public }

    public class FunctionBlock
    {
        public int LineStart { get; set; } = -1;
        public int LineEnd { get; set; } = -1;
        public string FunctionName { get; set; } = "";
        public FunctionScope FunctionScopeValue { get; set; } = FunctionScope.Undefined;
        public string BlockText { get; set; } = "";
    }

    public class KeywordInfo
    {
        public string KeywordName { get; set; } = "";
        public int? DefinitionLine { get; set; }
        public int? DefinitionColumn { get; set; }
        public KeywordScope Scope { get; set; } = KeywordScope.Undefined;
        public KeywordType Type { get; set; } = KeywordType.Undefined;
        public string ClassName { get; set; } = "";
        public string ModuleName { get; set; } = "";
        public ModuleType ModuleTypeValue { get; set; } = ModuleType.Undefined;
    }

    public enum CompletionItemKind { Keyword, Variable, Function, Class, Struct, Method, Property, Value, Constant }
    public record CompletionItem(string Label, CompletionItemKind Kind, string Detail, string Documentation);

    public record SignatureInfo(string Label, string Documentation, List<SignatureParameter> Parameters);
    public record SignatureParameter(string Label, string Detail);

    public record FoldingRange(int StartLine, int EndLine);

    public record Location(int Line, int Column, int Length);

    [Flags]
    public enum AnalysisFlags
    {
        None = 0,
        Folding = 1,
        Definition = 2,
        Hover = 4,
        References = 8,
        Signature = 16,
        Completion = 32,
        AutoClose = 64,
        All = Folding | Definition | Hover | References | Signature | Completion | AutoClose,
    }

    public class DocumentAnalysisResult
    {
        public List<CompletionItem> Completions { get; set; } = new();
        public List<Location> References { get; set; } = new();
        public List<FoldingRange> FoldingRanges { get; set; } = new();
        public List<FunctionBlock> FunctionBlocks { get; set; } = new();
        public string? HoverText { get; set; }
        public SignatureInfo? SignatureHelp { get; set; }
        public KeywordInfo? Definition { get; set; }
        public string? AutoCloseStatement { get; set; }
    }
}
