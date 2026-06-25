#nullable enable

using Fuookami.Ospf.Utils.MetaProgramming;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fuookami.Ospf.Utils.Serialization;
/// <summary>
/// JSON 命名策略 / JSON naming policy (mirrors ospf-kotlin JsonNamingPolicy).
/// Converts property names between frontend and backend naming systems.
/// </summary>
public sealed class OspfJsonNamingPolicy : JsonNamingPolicy {
    /// <summary>前端命名系统 / Frontend naming system.</summary>
    public NamingSystem Frontend { get; }

    /// <summary>后端命名系统 / Backend naming system.</summary>
    public NamingSystem Backend { get; }

    /// <summary>名称转换器 / Name transfer.</summary>
    public NameTransfer Transfer { get; }

    /// <summary>构造函数 / Constructor.</summary>
    public OspfJsonNamingPolicy(NamingSystem frontend, NamingSystem backend) {
        Frontend = frontend;
        Backend = backend;
        Transfer = new NameTransfer(frontend, backend);
    }

    /// <inheritdoc/>
    public override string ConvertName(string name) => Transfer.Invoke(name);
}

/// <summary>JSON 辅助方法 / JSON helper methods.</summary>
public static class Json {
    private static readonly JsonSerializerOptions DefaultOptions = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    /// <summary>从 JSON 文件读取 / Read from JSON file.</summary>
    public static T ReadFromJson<T>(string path, JsonSerializerOptions? options = null) {
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<T>(json, options ?? DefaultOptions)!;
    }

    /// <summary>从 JSON 流读取 / Read from JSON stream.</summary>
    public static T ReadFromJson<T>(Stream stream, JsonSerializerOptions? options = null) =>
        JsonSerializer.Deserialize<T>(stream, options ?? DefaultOptions)!;

    /// <summary>从 JSON 文件读取列表 / Read list from JSON file.</summary>
    public static List<T> ReadFromJsonList<T>(string path, JsonSerializerOptions? options = null) {
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<T>>(json, options ?? DefaultOptions) ?? new List<T>();
    }

    /// <summary>写入 JSON 字符串 / Write to JSON string.</summary>
    public static string WriteJson<T>(T value, JsonSerializerOptions? options = null) =>
        JsonSerializer.Serialize(value, options ?? DefaultOptions);

    /// <summary>写入 JSON 到文件 / Write JSON to file.</summary>
    public static void WriteJsonToFile<T>(string path, T value, JsonSerializerOptions? options = null) {
        string json = JsonSerializer.Serialize(value, options ?? DefaultOptions);
        File.WriteAllText(path, json);
    }

    /// <summary>写入 JSON 到流 / Write JSON to stream.</summary>
    public static void WriteJsonToStream<T>(Stream stream, T value, JsonSerializerOptions? options = null) =>
        JsonSerializer.Serialize(stream, value, options ?? DefaultOptions);
}
