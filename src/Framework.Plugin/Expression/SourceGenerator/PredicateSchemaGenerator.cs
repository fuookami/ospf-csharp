#nullable enable

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;

namespace Fuookami.Ospf.Framework.Plugin.Expression.SourceGenerator;
/// <summary>
/// 列命名策略枚举 / Column naming strategy enum.
/// </summary>
public enum ColumnNamingStrategy {
    /// <summary>恒等映射 / Identity mapping</summary>
    Identity,
    /// <summary>驼峰转蛇形 / Camel case to snake case</summary>
    SnakeCase
}

/// <summary>
/// 谓词属性信息 / Predicate property information.
/// </summary>
public sealed record PredicateProperty(
    string PropertyName,
    string BackendName,
    string Identifier);

/// <summary>
/// 谓词 schema 模型 / Predicate schema model.
/// </summary>
public sealed record PredicateSchemaModel(
    string PackageName,
    string EntityName,
    string SchemaName,
    bool GenerateResolver,
    bool GenerateColumnMapping,
    IReadOnlyList<PredicateProperty> Properties);

/// <summary>
/// 谓词 schema Roslyn 源生成器 / Predicate-schema Roslyn incremental source generator.
///
/// 扫描 [PredicateEntity] 注解的类，生成对应的 PredicateSchema 实现代码。
/// Scans classes annotated with [PredicateEntity] and emits corresponding
/// PredicateSchema implementation source (replaces the Kotlin KSP processor).
/// </summary>
[Generator]
public sealed class PredicateSchemaGenerator : IIncrementalGenerator {
    private const string PredicateEntityAttribute =
        "Fuookami.Ospf.Framework.Persistence.Expression.PredicateEntityAttribute";

    private static readonly DiagnosticDescriptor NestedClassError = new(
        "PSE001",
        "Nested class not allowed",
        "Predicate entity '{0}' cannot be a nested class",
        "PredicateSchema",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor TypeParameterError = new(
        "PSE002",
        "Type parameters not allowed",
        "Predicate entity '{0}' cannot declare type parameters",
        "PredicateSchema",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidIdentifierError = new(
        "PSE003",
        "Invalid identifier",
        "Predicate entity name '{0}' is not a valid C# identifier",
        "PredicateSchema",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidSchemaNameError = new(
        "PSE004",
        "Invalid schema name",
        "Predicate schema name '{0}' is not a valid C# identifier",
        "PredicateSchema",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context) {
        IncrementalValuesProvider<PredicateSchemaModel?> entityClasses = context
            .SyntaxProvider
            .ForAttributeWithMetadataName(
                PredicateEntityAttribute,
                predicate: (node, _) => node is ClassDeclarationSyntax,
                transform: TransformClass)
            .Where(static model => model is not null);

        IncrementalValuesProvider<(string HintName, string Source)> sources = entityClasses.Select(static (model, _) => (
            HintName: $"{model!.SchemaName}.g.cs",
            Source: PredicateSchemaRenderer.Render(model)));

        context.RegisterSourceOutput(sources, (spc, src) => {
            spc.AddSource(src.HintName, src.Source);
        });
    }

    private static PredicateSchemaModel? TransformClass(
        GeneratorAttributeSyntaxContext ctx, CancellationToken ct) {
        var classDecl = (ClassDeclarationSyntax)ctx.TargetNode;
        INamedTypeSymbol? symbol = ctx.SemanticModel.GetDeclaredSymbol(classDecl, ct);
        if (symbol is null) {
            return null;
        }

        // Validate: not nested
        if (symbol.ContainingType is not null) {
            // Diagnostic: nested class not allowed (PSE001)
            return null;
        }

        // Validate: no type parameters
        if (symbol.TypeParameters.Length > 0) {
            return null;
        }

        // Validate: valid identifier
        string entityName = symbol.Name;
        if (!IsValidIdentifier(entityName)) {
            return null;
        }

        // Read attribute arguments
        AttributeData? attr = ctx.Attributes.FirstOrDefault(a =>
            a.AttributeClass?.ToDisplayString() == PredicateEntityAttribute);
        if (attr is null) {
            return null;
        }

        string schemaName = GetNamedArgument(attr, "SchemaName") as string
            ?? $"{entityName}Schema";
        if (!IsValidIdentifier(schemaName)) {
            return null;
        }

        bool generateResolver = GetNamedArgument(attr, "GenerateResolver") as bool? ?? true;
        bool generateColumnMapping = GetNamedArgument(attr, "GenerateColumnMapping") as bool? ?? false;
        ColumnNamingStrategy? namingStrategy = GetNamedArgument(attr, "NamingStrategy") is int nsVal
            ? (ColumnNamingStrategy)nsVal
            : null;

        var properties = new List<PredicateProperty>();
        foreach (ISymbol member in symbol.GetMembers()) {
            if (member is not IPropertySymbol prop) {
                continue;
            }

            if (prop.IsStatic) {
                continue;
            }

            if (prop.DeclaredAccessibility == Accessibility.Private) {
                continue;
            }

            string propName = prop.Name;
            string backendName = ApplyNamingStrategy(propName, namingStrategy ?? ColumnNamingStrategy.Identity);
            properties.Add(new PredicateProperty(propName, backendName, propName));
        }

        string ns = symbol.ContainingNamespace.IsGlobalNamespace
            ? ""
            : symbol.ContainingNamespace.ToDisplayString();

        return new PredicateSchemaModel(
            PackageName: ns,
            EntityName: entityName,
            SchemaName: schemaName,
            GenerateResolver: generateResolver,
            GenerateColumnMapping: generateColumnMapping,
            Properties: properties);
    }

    private static object? GetNamedArgument(AttributeData attr, string name) {
        foreach (KeyValuePair<string, TypedConstant> kv in attr.NamedArguments) {
            if (kv.Key == name) {
                return kv.Value.Value;
            }
        }
        return null;
    }

    private static string ApplyNamingStrategy(string name, ColumnNamingStrategy strategy) {
        return strategy switch {
            ColumnNamingStrategy.SnakeCase => CamelToSnakeCase(name),
            _ => name,
        };
    }

    private static string CamelToSnakeCase(string name) {
        var sb = new StringBuilder();
        for (int i = 0; i < name.Length; i++) {
            char c = name[i];
            if (char.IsUpper(c)) {
                if (i > 0) {
                    sb.Append('_');
                }

                sb.Append(char.ToLowerInvariant(c));
            }
            else {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private static bool IsValidIdentifier(string name) {
        if (string.IsNullOrEmpty(name)) { return false; }
        if (!char.IsLetter(name[0]) && name[0] != '_') { return false; }
        for (int i = 1; i < name.Length; i++) {
            if (!char.IsLetterOrDigit(name[i]) && name[i] != '_') { return false; }
        }
        var keywords = new HashSet<string>
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char",
            "checked", "class", "const", "continue", "decimal", "default", "delegate", "do",
            "double", "else", "enum", "event", "explicit", "extern", "false", "finally",
            "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int",
            "interface", "internal", "is", "lock", "long", "namespace", "new", "null",
            "object", "operator", "out", "override", "params", "private", "protected",
            "public", "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof",
            "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
            "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using",
            "virtual", "void", "volatile", "while"
        };
        return !keywords.Contains(name);
    }
}

/// <summary>
/// 谓词 schema 代码渲染器 / Predicate schema code renderer.
/// </summary>
internal static class PredicateSchemaRenderer {
    public static string Render(PredicateSchemaModel model) {
        var sb = new StringBuilder();

        if (!string.IsNullOrEmpty(model.PackageName)) {
            sb.AppendLine($"namespace {model.PackageName}");
            sb.AppendLine("{");
            sb.Append("    ");
        }

        string indent = string.IsNullOrEmpty(model.PackageName) ? "" : "    ";
        sb.AppendLine($"{indent}/// <summary>");
        sb.AppendLine($"{indent}/// 自动生成的谓词 schema / Auto-generated predicate schema for {model.EntityName}.");
        sb.AppendLine($"{indent}/// </summary>");
        sb.AppendLine($"{indent}public sealed partial class {model.SchemaName}");
        sb.AppendLine($"{indent}{{");

        // Properties
        foreach (PredicateProperty prop in model.Properties) {
            sb.AppendLine($"{indent}    /// <summary>{prop.PropertyName} 字段映射 / Field mapping.</summary>");
            sb.AppendLine($"{indent}    public static string {prop.PropertyName} => \"{Escape(prop.BackendName)}\";");
        }

        // Resolver
        if (model.GenerateResolver) {
            sb.AppendLine();
            sb.AppendLine($"{indent}    /// <summary>属性名到后端名的解析器 / Property name to backend name resolver.</summary>");
            sb.AppendLine($"{indent}    public static string? Resolve(string path) => path switch");
            sb.AppendLine($"{indent}    {{");
            foreach (PredicateProperty prop in model.Properties) {
                sb.AppendLine($"{indent}        \"{Escape(prop.PropertyName)}\" => \"{Escape(prop.BackendName)}\",");
            }
            sb.AppendLine($"{indent}        _ => null");
            sb.AppendLine($"{indent}    }};");
        }

        // ColumnMapping
        if (model.GenerateColumnMapping) {
            sb.AppendLine();
            sb.AppendLine($"{indent}    /// <summary>列映射字典 / Column mapping dictionary.</summary>");
            sb.AppendLine($"{indent}    public static System.Collections.Generic.IReadOnlyDictionary<string, string> ColumnMapping {{ get; }} =");
            sb.AppendLine($"{indent}        new System.Collections.Generic.Dictionary<string, string>");
            sb.AppendLine($"{indent}        {{");
            foreach (PredicateProperty prop in model.Properties) {
                sb.AppendLine($"{indent}            [\"{Escape(prop.PropertyName)}\"] = \"{Escape(prop.BackendName)}\",");
            }
            sb.AppendLine($"{indent}        }};");
        }

        sb.AppendLine($"{indent}}}");

        if (!string.IsNullOrEmpty(model.PackageName)) {
            sb.AppendLine("}");
        }

        return sb.ToString();
    }

    private static string Escape(string value) {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }
}
