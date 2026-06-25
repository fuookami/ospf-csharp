#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Fuookami.Ospf.Math
{
    /// <summary>
    /// 序列化辅助方法（内部使用）/ Serialization helper methods (internal use)
    /// </summary>
    internal static class SerializationHelpers
    {
        /// <summary>默认 JSON 选项 / Default JSON options</summary>
        internal static JsonSerializerOptions? DefaultOptions;

        /// <summary>
        /// 要求 JSON 对象 / Require JSON object
        /// </summary>
        /// <param name="element">JSON 元素 / JSON element</param>
        /// <param name="serializerName">序列化器名称 / Serializer name</param>
        /// <returns>JSON 对象 / JSON object</returns>
        internal static JsonElement RequireJsonObject(JsonElement element, string serializerName)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw SerializationFailure($"Expected JSON object for {serializerName}, got {element.ValueKind}");
            }
            return element;
        }

        /// <summary>
        /// 要求 JSON 字段 / Require JSON fields
        /// </summary>
        /// <param name="element">JSON 元素 / JSON element</param>
        /// <param name="fields">所需字段 / Required fields</param>
        /// <param name="serializerName">序列化器名称 / Serializer name</param>
        internal static void RequireJsonFields(JsonElement element, IEnumerable<string> fields, string serializerName)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw SerializationFailure($"Expected JSON object for {serializerName}");
            }

            List<string> missing = fields.Where(f => !element.TryGetProperty(f, out _)).ToList();
            if (missing.Count > 0)
            {
                throw SerializationFailure($"Missing required fields [{string.Join(", ", missing)}] in {serializerName}");
            }
        }

        /// <summary>
        /// 创建序列化失败异常 / Create serialization failure exception
        /// </summary>
        internal static Exception SerializationFailure(string message) => new JsonException(message);
    }
}
