#nullable enable

using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fuookami.Ospf.Utils.Serialization;
/// <summary>Instant 序列化器（ISO-8601）/ Instant serializer (ISO-8601).</summary>
public sealed class InstantSerializer : JsonConverter<DateTimeOffset> {
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        DateTimeOffset.Parse(reader.GetString()!, CultureInfo.InvariantCulture);

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString("O", CultureInfo.InvariantCulture));
}

/// <summary>DateTime 序列化器（yyyy-MM-dd HH:mm:ss）/ DateTime serializer.</summary>
public sealed class DateTimeSerializer : JsonConverter<DateTimeOffset> {
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        DateTimeOffset.ParseExact(reader.GetString()!, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
}

/// <summary>LocalDateTime 序列化器 / LocalDateTime serializer.</summary>
public sealed class LocalDateTimeSerializer : JsonConverter<DateTime> {
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        DateTime.Parse(reader.GetString()!, CultureInfo.InvariantCulture);

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString("O", CultureInfo.InvariantCulture));
}

/// <summary>LocalMonth 序列化器（yyyy-MM）/ LocalMonth serializer.</summary>
public sealed class LocalMonthSerializer : JsonConverter<DateOnly> {
    public override DateOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        DateOnly.ParseExact(reader.GetString()!, "yyyy-MM", CultureInfo.InvariantCulture);

    public override void Write(Utf8JsonWriter writer, DateOnly value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString("yyyy-MM", CultureInfo.InvariantCulture));
}

/// <summary>LocalDate 序列化器（yyyy-MM-dd）/ LocalDate serializer.</summary>
public sealed class LocalDateSerializer : JsonConverter<DateOnly> {
    public override DateOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        DateOnly.ParseExact(reader.GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    public override void Write(Utf8JsonWriter writer, DateOnly value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
}

/// <summary>LocalTime 序列化器（HH:mm:ss）/ LocalTime serializer.</summary>
public sealed class LocalTimeSerializer : JsonConverter<TimeOnly> {
    public override TimeOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        TimeOnly.ParseExact(reader.GetString()!, "HH:mm:ss", CultureInfo.InvariantCulture);

    public override void Write(Utf8JsonWriter writer, TimeOnly value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
}
