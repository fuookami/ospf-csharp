#nullable enable

using Fuookami.Ospf.Math;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Serde;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fuookami.Ospf.Math.Symbol.Expression.Serde;
// ========== Serialization Models (internal DTOs) ==========

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ScalarConstantData), "Constant")]
[JsonDerivedType(typeof(ScalarReferenceData), "Reference")]
[JsonDerivedType(typeof(ScalarSymbolReferenceData), "SymbolReference")]
[JsonDerivedType(typeof(ScalarUnaryData), "Unary")]
[JsonDerivedType(typeof(ScalarBinaryData), "Binary")]
[JsonDerivedType(typeof(ScalarFunctionData), "Function")]
[JsonDerivedType(typeof(ScalarCustomData), "Custom")]
internal abstract record ScalarExpressionData {
    public abstract string TypeName { get; }
}

internal sealed record ScalarConstantData(JsonElement Value) : ScalarExpressionData {
    public override string TypeName => "Constant";
}

internal sealed record ScalarReferenceData(string Path) : ScalarExpressionData {
    public override string TypeName => "Reference";
}

internal sealed record ScalarSymbolReferenceData(string Identifier) : ScalarExpressionData {
    public override string TypeName => "SymbolReference";
}

internal sealed record ScalarUnaryData(string Operator, ScalarExpressionData Operand) : ScalarExpressionData {
    public override string TypeName => "Unary";
}

internal sealed record ScalarBinaryData(string Operator, ScalarExpressionData Left, ScalarExpressionData Right) : ScalarExpressionData {
    public override string TypeName => "Binary";
}

internal sealed record ScalarFunctionData(string Name, IReadOnlyList<ScalarExpressionData> Arguments) : ScalarExpressionData {
    public override string TypeName => "Function";
}

internal sealed record ScalarCustomData(string? Payload, string? Description) : ScalarExpressionData {
    public override string TypeName => "Custom";
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(BooleanConstantData), "BooleanConstant")]
[JsonDerivedType(typeof(ComparisonData), "Comparison")]
[JsonDerivedType(typeof(InData), "In")]
[JsonDerivedType(typeof(PatternMatchData), "PatternMatch")]
[JsonDerivedType(typeof(NullCheckData), "NullCheck")]
[JsonDerivedType(typeof(AndData), "And")]
[JsonDerivedType(typeof(OrData), "Or")]
[JsonDerivedType(typeof(NotData), "Not")]
[JsonDerivedType(typeof(BooleanCustomData), "Custom")]
internal abstract record BooleanExpressionData {
    public abstract string TypeName { get; }
}

internal sealed record BooleanConstantData(string Value) : BooleanExpressionData {
    public override string TypeName => "BooleanConstant";
}

internal sealed record ComparisonData(string Operator, ScalarExpressionData Left, ScalarExpressionData Right) : BooleanExpressionData {
    public override string TypeName => "Comparison";
}

internal sealed record InData(ScalarExpressionData Value, IReadOnlyList<ScalarExpressionData> Candidates, bool Negated = false) : BooleanExpressionData {
    public override string TypeName => "In";
}

internal sealed record PatternMatchData(ScalarExpressionData Value, ScalarExpressionData Pattern, string Mode, bool Negated = false) : BooleanExpressionData {
    public override string TypeName => "PatternMatch";
}

internal sealed record NullCheckData(string Path, [property: JsonPropertyName("nullCheckType")] string CheckType) : BooleanExpressionData {
    public override string TypeName => "NullCheck";
}

internal sealed record AndData(IReadOnlyList<BooleanExpressionData> Operands) : BooleanExpressionData {
    public override string TypeName => "And";
}

internal sealed record OrData(IReadOnlyList<BooleanExpressionData> Operands) : BooleanExpressionData {
    public override string TypeName => "Or";
}

internal sealed record NotData(BooleanExpressionData Operand) : BooleanExpressionData {
    public override string TypeName => "Not";
}

internal sealed record BooleanCustomData(string? Payload, string? Description) : BooleanExpressionData {
    public override string TypeName => "Custom";
}

// ========== Converters (internal) ==========

internal static class ExpressionSerdeConverters {
    internal static ScalarExpressionData ToData<T>(this ScalarExpression<T> expr) => expr switch {
        ScalarConstant<T> c => new ScalarConstantData(JsonValueFor(c.Value)),
        ScalarReference<T> r => new ScalarReferenceData(r.Path.Value),
        ScalarSymbolReference<T> s => new ScalarSymbolReferenceData(
            SymbolIdentitySerde.ToSerializedIdentifier(s.Symbol.ToSymbolIdentityExpr())),
        ScalarUnary<T> u => new ScalarUnaryData(u.Operator.ToString(), u.Operand.ToData()),
        ScalarBinary<T> b => new ScalarBinaryData(b.Operator.ToString(), b.Left.ToData(), b.Right.ToData()),
        ScalarFunction<T> f => new ScalarFunctionData(f.Name, f.Arguments.Select(a => a.ToData()).ToList()),
        ScalarCustom<T> c => new ScalarCustomData(c.Value?.ToString(), c.Description),
        _ => throw new InvalidOperationException($"Unknown scalar expression type: {expr.GetType()}"),
    };

    internal static ScalarExpression<object> ToScalarExpression(this ScalarExpressionData data) => data switch {
        ScalarConstantData c => new ScalarConstant<object>(ParseJsonValue(c.Value)!),
        ScalarReferenceData r => new ScalarReference<object>(PropertyPath.Parse(r.Path)),
        ScalarSymbolReferenceData s => new ScalarSymbolReference<object>(SymbolIdentitySerde.SymbolOfSerializedIdentifier(s.Identifier)),
        ScalarUnaryData u => new ScalarUnary<object>(Enum.Parse<UnaryOperator>(u.Operator), u.Operand.ToScalarExpression()),
        ScalarBinaryData b => new ScalarBinary<object>(Enum.Parse<BinaryOperator>(b.Operator), b.Left.ToScalarExpression(), b.Right.ToScalarExpression()),
        ScalarFunctionData f => new ScalarFunction<object>(f.Name, f.Arguments.Select(a => a.ToScalarExpression()).ToList()),
        ScalarCustomData c => new ScalarCustom<object>(c.Payload ?? (object)"Custom", c.Description),
        _ => throw new InvalidOperationException($"Unknown scalar expression data type: {data.GetType()}"),
    };

    internal static BooleanExpressionData ToData(this BooleanExpression expr) => expr switch {
        BooleanConstant c => new BooleanConstantData(c.Value switch {
            Trivalent.True => "true",
            Trivalent.False => "false",
            _ => "unknown",
        }),
        Comparison<object> c => new ComparisonData(c.Operator.ToString(), c.Left.ToData(), c.Right.ToData()),
        InExpression<object> e => new InData(e.Value.ToData(), e.Candidates.Select(x => x.ToData()).ToList(), e.Negated),
        PatternMatch<object> p => new PatternMatchData(p.Value.ToData(), p.Pattern.ToData(), p.Mode.ToString(), p.Negated),
        NullCheck n => new NullCheckData(n.Path.Value, n.Type.ToString()),
        AndExpression a => new AndData(a.Operands.Select(o => o.ToData()).ToList()),
        OrExpression o => new OrData(o.Operands.Select(x => x.ToData()).ToList()),
        NotExpression n => new NotData(n.Operand.ToData()),
        BooleanCustom c => new BooleanCustomData(c.Value?.ToString(), c.Description),
        _ => throw new InvalidOperationException($"Unknown boolean expression type: {expr.GetType()}"),
    };

    internal static BooleanExpression ToBooleanExpression(this BooleanExpressionData data) => data switch {
        BooleanConstantData c => new BooleanConstant(c.Value.ToLowerInvariant() switch {
            "true" => Trivalent.Invoke(true),
            "false" => Trivalent.Invoke(false),
            _ => new Trivalent.Unknown(),
        }),
        ComparisonData c => new Comparison<object>(
            Enum.Parse<ComparisonOperator>(c.Operator),
            c.Left.ToScalarExpression(),
            c.Right.ToScalarExpression()),
        InData e => new InExpression<object>(
            e.Value.ToScalarExpression(),
            e.Candidates.Select(x => x.ToScalarExpression()).ToList(),
            e.Negated),
        PatternMatchData p => new PatternMatch<object>(
            p.Value.ToScalarExpression(),
            p.Pattern.ToScalarExpression(),
            Enum.Parse<PatternMatchMode>(p.Mode),
            p.Negated),
        NullCheckData n => new NullCheck(
            PropertyPath.Parse(n.Path),
            Enum.Parse<NullCheckType>(n.CheckType)),
        AndData a => new AndExpression(a.Operands.Select(o => o.ToBooleanExpression()).ToList()),
        OrData o => new OrExpression(o.Operands.Select(x => x.ToBooleanExpression()).ToList()),
        NotData n => new NotExpression(n.Operand.ToBooleanExpression()),
        BooleanCustomData c => new BooleanCustom(c.Payload ?? (object)"Custom", c.Description),
        _ => throw new InvalidOperationException($"Unknown boolean expression data type: {data.GetType()}"),
    };

    private static JsonElement JsonValueFor<T>(T value) {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        return JsonSerializer.SerializeToElement(value, options);
    }

    private static object? ParseJsonValue(JsonElement el) => el.ValueKind switch {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.TryGetInt64(out long l) ? l : el.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => el.GetRawText(),
    };
}

// ========== Public API ==========

/// <summary>表达式序列化 / Expression Serde public API.</summary>
public static class ExpressionSerde {
    private static readonly JsonSerializerOptions Options = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>将布尔表达式序列化为 JSON / Serialize boolean expression to JSON.</summary>
    public static string ToJsonString(this BooleanExpression expr)
        => JsonSerializer.Serialize(expr.ToData(), Options);

    /// <summary>从 JSON 反序列化布尔表达式 / Deserialize boolean expression from JSON.</summary>
    public static BooleanExpression FromJson(string json)
        => JsonSerializer.Deserialize<BooleanExpressionData>(json, Options)!.ToBooleanExpression();

    /// <summary>尝试从 JSON 反序列化，失败返回 null / Try deserialize, null on failure.</summary>
    public static BooleanExpression? FromJsonOrNull(string json) {
        try { return FromJson(json); }
        catch (JsonException) { return null; }
    }

    /// <summary>将标量表达式序列化为 JSON / Serialize scalar expression to JSON.</summary>
    public static string ToJsonString<T>(this ScalarExpression<T> expr)
        => JsonSerializer.Serialize(expr.ToData(), Options);

    /// <summary>从 JSON 反序列化标量表达式 / Deserialize scalar expression from JSON.</summary>
    public static ScalarExpression<object> ScalarFromJson(string json)
        => JsonSerializer.Deserialize<ScalarExpressionData>(json, Options)!.ToScalarExpression();

    /// <summary>尝试从 JSON 反序列化标量表达式，失败返回 null / Try deserialize scalar, null on failure.</summary>
    public static ScalarExpression<object>? ScalarFromJsonOrNull(string json) {
        try { return ScalarFromJson(json); }
        catch (JsonException) { return null; }
    }
}
