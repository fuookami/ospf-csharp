#nullable enable

using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fuookami.Ospf.Framework.Solver.Remote.Domain;

/// <summary>
/// 远程标识值类型的字符串编解码器工厂 / String converter factory for remote identifier value types.
/// </summary>
/// <remarks>
/// 固定 Kotlin 侧的标识一律是 <c>@JvmInline value class X(val value: String)</c>，序列化为**裸字符串**；
/// C# 的 <c>readonly record struct X(string Value)</c> 默认会写成 <c>{"value":"…"}</c>，两端无法互通。
/// 本工厂识别"只有一个公共 <c>Value</c> 字符串属性 + 一个字符串构造参数"的值类型并按裸字符串处理。
/// Pinned Kotlin identifiers are <c>@JvmInline value class X(val value: String)</c> and serialize as **bare
/// strings**; a C# <c>readonly record struct X(string Value)</c> would default to <c>{"value":"…"}</c> and the
/// two sides would not interoperate. This factory recognizes value types with exactly one public string
/// <c>Value</c> property plus one string constructor parameter and treats them as bare strings.
/// </remarks>
public sealed class RemoteWireIdentifierConverterFactory : JsonConverterFactory {
    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert) =>
        TryBuild(typeToConvert, out _);

    /// <inheritdoc/>
    public override JsonConverter CreateConverter(
        Type typeToConvert,
        JsonSerializerOptions options) {
        if (!TryBuild(typeToConvert, out ConverterShape shape)) {
            throw new NotSupportedException(
                $"不是远程标识值类型 / Not a remote identifier value type: {typeToConvert}");
        }
        Type converterType = typeof(Converter<>).MakeGenericType(typeToConvert);
        return (JsonConverter)Activator.CreateInstance(
            converterType,
            shape.Getter,
            shape.Setter)!;
    }

    private static bool TryBuild(Type type, out ConverterShape shape) {
        shape = default;
        if (!type.IsValueType || type.IsEnum) {
            return false;
        }
        PropertyInfo? value = type.GetProperty(
            "Value",
            BindingFlags.Public | BindingFlags.Instance);
        if (value is null || value.PropertyType != typeof(string) || !value.CanRead) {
            return false;
        }
        ConstructorInfo? constructor = type.GetConstructor(new[] { typeof(string) });
        if (constructor is null) {
            return false;
        }
        MethodInfo getter = value.GetGetMethod()!;
        shape = new ConverterShape(
            Getter: instance => (string)getter.Invoke(instance, null)!,
            Setter: text => constructor.Invoke(new object?[] { text }));
        return true;
    }

    private readonly record struct ConverterShape(
        Func<object, string> Getter,
        Func<string, object> Setter);

    private sealed class Converter<T> : JsonConverter<T>
        where T : struct {
        private readonly Func<object, string> _getter;
        private readonly Func<string, object> _setter;

        public Converter(Func<object, string> getter, Func<string, object> setter) {
            _getter = getter;
            _setter = setter;
        }

        public override T Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options) {
            if (reader.TokenType == JsonTokenType.Null) {
                return default;
            }
            string text = reader.GetString() ?? string.Empty;
            return (T)_setter(text);
        }

        public override void Write(
            Utf8JsonWriter writer,
            T value,
            JsonSerializerOptions options) =>
            writer.WriteStringValue(_getter(value));
    }
}
