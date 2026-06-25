#nullable enable

using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fuookami.Ospf.Utils.Serialization
{
    /// <summary>时间单位 / Time span unit.</summary>
    public enum TimeSpanUnit
    {
        Ticks,
        Milliseconds,
        Seconds,
        Minutes,
        Hours,
        Days,
    }

    /// <summary>离散 Duration 序列化器 / Discrete duration serializer.</summary>
    public class DiscreteDurationSerializer : JsonConverter<TimeSpan>
    {
        private readonly TimeSpanUnit _unit;

        public DiscreteDurationSerializer(TimeSpanUnit unit) => _unit = unit;

        public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = reader.GetInt64();
            return _unit switch
            {
                TimeSpanUnit.Ticks => TimeSpan.FromTicks(value),
                TimeSpanUnit.Milliseconds => TimeSpan.FromMilliseconds(value),
                TimeSpanUnit.Seconds => TimeSpan.FromSeconds(value),
                TimeSpanUnit.Minutes => TimeSpan.FromMinutes(value),
                TimeSpanUnit.Hours => TimeSpan.FromHours(value),
                TimeSpanUnit.Days => TimeSpan.FromDays(value),
                _ => TimeSpan.FromTicks(value),
            };
        }

        public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
        {
            var serialized = _unit switch
            {
                TimeSpanUnit.Ticks => value.Ticks,
                TimeSpanUnit.Milliseconds => (long)value.TotalMilliseconds,
                TimeSpanUnit.Seconds => (long)value.TotalSeconds,
                TimeSpanUnit.Minutes => (long)value.TotalMinutes,
                TimeSpanUnit.Hours => (long)value.TotalHours,
                TimeSpanUnit.Days => (long)value.TotalDays,
                _ => value.Ticks,
            };
            writer.WriteNumberValue(serialized);
        }
    }

    /// <summary>连续 Duration 序列化器 / Continuous duration serializer.</summary>
    public class ContinuousDurationSerializer : JsonConverter<TimeSpan>
    {
        private readonly TimeSpanUnit _unit;

        public ContinuousDurationSerializer(TimeSpanUnit unit) => _unit = unit;

        public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = reader.GetDouble();
            return _unit switch
            {
                TimeSpanUnit.Milliseconds => TimeSpan.FromMilliseconds(value),
                TimeSpanUnit.Seconds => TimeSpan.FromSeconds(value),
                TimeSpanUnit.Minutes => TimeSpan.FromMinutes(value),
                TimeSpanUnit.Hours => TimeSpan.FromHours(value),
                TimeSpanUnit.Days => TimeSpan.FromDays(value),
                _ => TimeSpan.FromTicks((long)value),
            };
        }

        public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
        {
            var serialized = _unit switch
            {
                TimeSpanUnit.Milliseconds => value.TotalMilliseconds,
                TimeSpanUnit.Seconds => value.TotalSeconds,
                TimeSpanUnit.Minutes => value.TotalMinutes,
                TimeSpanUnit.Hours => value.TotalHours,
                TimeSpanUnit.Days => value.TotalDays,
                _ => (double)value.Ticks,
            };
            writer.WriteNumberValue(serialized);
        }
    }
}
