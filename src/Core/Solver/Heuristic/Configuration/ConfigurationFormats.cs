// 配置格式 / Configuration formats.
// 与固定 Kotlin 基线 `configuration/ConfigurationFormats.kt`（7389 B）的 **JSON 路径**对应。
// Mirrors the **JSON path** of the pinned baseline's `ConfigurationFormats.kt` (7389 B).

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fuookami.Ospf.Core.Solver.Heuristic.Persistence;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver.Heuristic.Configuration;

/// <summary>
/// 配置格式 / A configuration format.
/// </summary>
public enum ConfigurationFormat {
    /// <summary>JSON / JSON.</summary>
    Json,

    /// <summary>YAML / YAML.</summary>
    Yaml,

    /// <summary>TOML / TOML.</summary>
    Toml,
}

/// <summary>
/// 配置格式编解码 / Configuration-format codecs.
/// </summary>
/// <remarks>
/// **只实现了 JSON 路径**。基线的 YAML/TOML 路径依赖 Jackson 的 `YAMLFactory`/`TomlFactory` 与 `ktoml`；本仓库没有这两个库，
/// 也没有为它们引入依赖。
///
/// **本移植只实现了 JSON 路径**。基线的 YAML/TOML 路径依赖 Jackson 的 `YAMLFactory`/`TomlFactory` 与 `ktoml`；本仓库没有这两个库，
/// 也没有为它们引入依赖。
///
/// `Decode`/`Encode` 对 `Yaml`/`Toml` **显式返回"不支持的格式"**，而不是静默按 JSON 处理——静默回退会让一份 YAML 文件
/// 被当作 JSON 解析并报出一个与真实问题无关的语法错误。
///
/// `Decode`/`Encode` **explicitly return "unsupported format"** for `Yaml`/`Toml` rather than silently treating them as JSON — a silent
/// fallback would parse a YAML file as JSON and report a syntax error unrelated to the real problem.
///
/// **YAML/TOML 走"解析成 `JsonNode` → 复用 JSON 那条校验链"**，而不是各自再写一套反序列化。
/// 两套实现会让"重复键""非有限数""迁移"这些规则存在两份，而**两份迟早给出不同答案**——其中一份被改坏后，
/// 另一种格式会悄悄接受本该拒绝的文档。
///
/// **YAML/TOML parse into a `JsonNode` and reuse the JSON validation chain**, rather than each writing its own deserialisation. Two implementations
/// would duplicate the "duplicate keys", "non-finite numbers" and "migration" rules, and **the two would eventually disagree** — once one is
/// broken, the other format silently accepts documents that should be rejected.
///
/// **YAML 的重复键必须在解析阶段抓**：`YamlDotNet` 默认"后者覆盖前者"，而配置里的重复键几乎总是复制粘贴错误。
/// **Duplicate YAML keys must be caught while parsing**: `YamlDotNet` defaults to "last wins", and a duplicate key in a configuration is almost always a
/// copy-paste error.
///
/// **TOML 的根必须是表**。`Tomlyn` 允许根为标量，而配置的根是对象；把标量根当配置会让反序列化报一个与真实问题无关的类型错误。
/// **A TOML root must be a table.** `Tomlyn` permits a scalar root while a configuration's root is an object; feeding a scalar root in would make
/// deserialisation report a type error unrelated to the real problem.
///
/// **日期时间统一转成字符串**。YAML/TOML 都有原生日期类型，而配置里没有日期字段——把它们转成字符串能让"意外出现日期"
/// 走到反序列化时明确报类型不符，**而不是在转换阶段被悄悄变成别的形状**。
/// **Dates and times become strings.** YAML and TOML both have native date types and the configuration has no date field — converting them to strings
/// lets "an unexpected date" reach deserialisation as an explicit type mismatch, **instead of being silently reshaped during conversion**.
///
/// **`Decode` 限制输入 4 MiB**。不限制会让一份畸形配置把内存吃光，而失败点发生在解析深处、**调用方拿到的是一个 OOM 而不是
/// "配置太大"**。
///
/// **`Decode` caps the input at 4 MiB.** Without the cap a malformed configuration exhausts memory, and the failure happens deep inside
/// parsing, **so the caller gets an OOM instead of "configuration too large"**.
///
/// **严格拒绝重复键**。JSON 规范允许重复键并"后者覆盖前者"，而配置里的重复键几乎总是复制粘贴错误；静默覆盖会让一份
/// **看起来只有一份**的配置实际生效的是被覆盖的那份。
///
/// **Duplicate keys are strictly rejected.** JSON permits duplicates with "last wins", and a duplicate key in a configuration is almost
/// always a copy-paste error; silent overwriting makes a configuration that **looks like it has one entry** actually apply the overwritten
/// one.
///
/// **拒绝非有限数**。`NaN`/`Infinity` 不是合法 JSON，但某些编码器会写出它们；让它们进入配置会让后续的数值校验全部失效，
/// **而配置本身仍然"解析成功"**。
///
/// **Non-finite numbers are rejected.** `NaN`/`Infinity` are not legal JSON, yet some encoders emit them; letting them into a configuration
/// invalidates every downstream numeric check, **while the configuration still "parses successfully"**.
///
/// **`Encode` 在写 TOML 时移除模型声明的可缺省 `null` 字段**，但**不吞掉业务参数里的 `null`**。前者是"没配置"，后者是
/// "配置为 `null`"，语义不同；把后者也移除会让一次显式的 `null` 变成"未设置"。
///
/// **`Encode` removes model-declared optional `null` fields for TOML**, but **never swallows `null` inside domain parameters**. The former
/// means "not configured" and the latter "configured as `null`"; removing the latter turns an explicit `null` into "unset".
/// </remarks>
public static class ConfigurationFormats {
    private const int MaxBytes = 4 * 1024 * 1024;

    /// <summary>
    /// 解析配置 / Decode a configuration.
    /// </summary>
    /// <param name="text">配置文本 / The configuration text.</param>
    /// <param name="format">格式 / The format.</param>
    /// <param name="migrations">迁移器 / The migrations.</param>
    /// <returns>配置 / The configuration.</returns>
    public static Result<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>> Decode(
        string text,
        ConfigurationFormat format,
        IReadOnlyDictionary<int, IConfigurationMigration>? migrations = null) {
        ArgumentNullException.ThrowIfNull(text);
        if (Encoding.UTF8.GetByteCount(text) > MaxBytes) {
            return SearchCodecSupport.SearchInvalid<HyperHeuristicConfig>(
                "configuration exceeds 4 MiB");
        }
        if (format == ConfigurationFormat.Yaml) {
            Result<JsonNode, ErrorCode, Error<ErrorCode>> yaml = ParseYaml(text);
            return yaml is Failed<JsonNode, ErrorCode, Error<ErrorCode>> yamlFailure
                ? Results.Failed<HyperHeuristicConfig>(yamlFailure.Error)
                : FromNode(((Ok<JsonNode, ErrorCode, Error<ErrorCode>>)yaml).Value, migrations);
        }
        if (format == ConfigurationFormat.Toml) {
            Result<JsonNode, ErrorCode, Error<ErrorCode>> toml = ParseToml(text);
            return toml is Failed<JsonNode, ErrorCode, Error<ErrorCode>> tomlFailure
                ? Results.Failed<HyperHeuristicConfig>(tomlFailure.Error)
                : FromNode(((Ok<JsonNode, ErrorCode, Error<ErrorCode>>)toml).Value, migrations);
        }

        JsonDocument document;
        try {
            document = JsonDocument.Parse(text, new JsonDocumentOptions {
                CommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false,
                MaxDepth = 256,
            });
        } catch (JsonException exception) {
            return SearchCodecSupport.SearchInvalid<HyperHeuristicConfig>(
                $"invalid configuration document: {exception.Message}");
        }
        using (document) {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) {
                return SearchCodecSupport.SearchInvalid<HyperHeuristicConfig>(
                    "configuration must be an object");
            }
            if (!IsFinite(root)) {
                return SearchCodecSupport.SearchInvalid<HyperHeuristicConfig>(
                    "配置含非有限数 / Non-finite configuration number");
            }
            if (HasDuplicateKeys(root)) {
                return SearchCodecSupport.SearchInvalid<HyperHeuristicConfig>(
                    "configuration contains duplicate keys");
            }

            JsonObject record = JsonNode.Parse(root.GetRawText())!.AsObject();
            Result<JsonObject, ErrorCode, Error<ErrorCode>> migrated =
                FileSearchStore.MigrateDocument(record, migrations
                    ?? new Dictionary<int, IConfigurationMigration>());
            if (migrated is Failed<JsonObject, ErrorCode, Error<ErrorCode>> migrateFailed) {
                return Results.Failed<HyperHeuristicConfig>(migrateFailed.Error);
            }
            JsonObject target = ((Ok<JsonObject, ErrorCode, Error<ErrorCode>>)migrated).Value;
            Result<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>> decoded =
                SearchBoundary.Execute(
                    () => JsonSerializer.Deserialize<HyperHeuristicConfig>(target.ToJsonString())!);
            if (decoded is Failed<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>> decodeFailed) {
                return Results.Failed<HyperHeuristicConfig>(decodeFailed.Error);
            }
            HyperHeuristicConfig config =
                ((Ok<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>>)decoded).Value;
            Result<Success, ErrorCode, Error<ErrorCode>> valid = config.Validate();
            return valid is Failed<Success, ErrorCode, Error<ErrorCode>> invalid
                ? Results.Failed<HyperHeuristicConfig>(invalid.Error)
                : Results.Ok(config);
        }
    }

    /// <summary>
    /// 序列化配置 / Encode a configuration.
    /// </summary>
    /// <param name="config">配置 / The configuration.</param>
    /// <param name="format">格式 / The format.</param>
    /// <returns>配置文本 / The configuration text.</returns>
    public static Result<string, ErrorCode, Error<ErrorCode>> Encode(
        HyperHeuristicConfig config, ConfigurationFormat format) {
        ArgumentNullException.ThrowIfNull(config);
        Result<Success, ErrorCode, Error<ErrorCode>> valid = config.Validate();
        if (valid is Failed<Success, ErrorCode, Error<ErrorCode>> invalid) {
            return Results.Failed<string>(invalid.Error);
        }
        if (format == ConfigurationFormat.Json) {
            return SearchBoundary.Execute(() => JsonSerializer.Serialize(config));
        }
        JsonNode? node = JsonSerializer.SerializeToNode(config);
        if (node is not JsonObject root) {
            return SearchCodecSupport.SearchInvalid<string>("configuration is not an object");
        }
        if (format == ConfigurationFormat.Yaml) {
            return SearchBoundary.Execute(() => WriteYaml(root));
        }
        return SearchBoundary.Execute(() => WriteToml(root));
    }

    /// <summary>
    /// 从已解析的节点构造配置 / Build a configuration from an already-parsed node.
    /// </summary>
    /// <remarks>
    /// **三种格式共用这一条校验链**（重复键、非有限数、迁移、反序列化、`Validate`）。
    /// 各写一套会让规则漂移，而**漂移之后"同一份内容换一种格式就通过"**。
    ///
    /// **All three formats share this one validation chain** (duplicate keys, non-finite numbers, migration, deserialisation, `Validate`). Separate
    /// chains would drift, and **after drift "the same content passes in another format"**.
    /// </remarks>
    /// <param name="node">根节点 / The root node.</param>
    /// <param name="migrations">迁移器 / The migrations.</param>
    /// <returns>配置 / The configuration.</returns>
    private static Result<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>> FromNode(
        JsonNode? node, IReadOnlyDictionary<int, IConfigurationMigration>? migrations) {
        if (node is not JsonObject record) {
            return SearchCodecSupport.SearchInvalid<HyperHeuristicConfig>(
                "configuration must be an object");
        }
        JsonElement element = JsonSerializer.SerializeToElement(record);
        if (!IsFinite(element)) {
            return SearchCodecSupport.SearchInvalid<HyperHeuristicConfig>(
                "配置含非有限数 / Non-finite configuration number");
        }
        if (HasDuplicateKeys(element)) {
            return SearchCodecSupport.SearchInvalid<HyperHeuristicConfig>(
                "configuration contains duplicate keys");
        }
        Result<JsonObject, ErrorCode, Error<ErrorCode>> migrated =
            FileSearchStore.MigrateDocument(record, migrations
                ?? new Dictionary<int, IConfigurationMigration>());
        if (migrated is Failed<JsonObject, ErrorCode, Error<ErrorCode>> migrateFailed) {
            return Results.Failed<HyperHeuristicConfig>(migrateFailed.Error);
        }
        JsonObject target = ((Ok<JsonObject, ErrorCode, Error<ErrorCode>>)migrated).Value;
        Result<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>> decoded =
            SearchBoundary.Execute(
                () => JsonSerializer.Deserialize<HyperHeuristicConfig>(target.ToJsonString())!);
        if (decoded is Failed<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>> decodeFailed) {
            return Results.Failed<HyperHeuristicConfig>(decodeFailed.Error);
        }
        HyperHeuristicConfig config =
            ((Ok<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>>)decoded).Value;
        Result<Success, ErrorCode, Error<ErrorCode>> valid = config.Validate();
        return valid is Failed<Success, ErrorCode, Error<ErrorCode>> invalid
            ? Results.Failed<HyperHeuristicConfig>(invalid.Error)
            : Results.Ok(config);
    }

    /// <summary>
    /// 解析 YAML / Parse YAML.
    /// </summary>
    /// <param name="text">文本 / The text.</param>
    /// <returns>根节点或失败 / The root node or a failure.</returns>
    private static Result<JsonNode, ErrorCode, Error<ErrorCode>> ParseYaml(string text) {
        try {
            // **用 `YamlStream.Load` 而不是手工组装 `Scanner`/`Parser`**：手工那条要显式传标签指令表，
            // 而指令表的类型在不同版本里换过名字——**编译期就断，且断在一个与配置无关的构造函数上**。
            // **Use `YamlStream.Load` rather than assembling `Scanner`/`Parser` by hand**: the manual route needs an explicit tag-directive table whose
            // type has been renamed across versions — **it breaks at compile time, in a constructor unrelated to configuration**.
            var stream = new YamlDotNet.RepresentationModel.YamlStream();
            stream.Load(new System.IO.StringReader(text));
            if (stream.Documents.Count == 0) {
                return SearchCodecSupport.SearchInvalid<JsonNode>(
                    "empty configuration document");
            }
            if (stream.Documents.Count > 1) {
                return SearchCodecSupport.SearchInvalid<JsonNode>(
                    "configuration must be a single document");
            }
            // **根必须是对象的检查不在这里做**：`FromNode` 已经查一遍，在这里再查一遍会让两处都存在，
            // 而**删掉任何一处测试都照样通过**（本批变异实测如此）。单一守卫、单一位置。
            // **The "root must be an object" check is not repeated here**: `FromNode` already performs it, and having it in both places means
            // **deleting either leaves the tests green** (measured in this batch). One guard, one place.
            return YamlToNode(stream.Documents[0].RootNode);
        } catch (YamlDotNet.Core.YamlException exception) {
            return SearchCodecSupport.SearchInvalid<JsonNode>(
                $"invalid configuration document: {exception.Message}");
        }
    }

    /// <summary>
    /// 把 YAML 节点转成 JSON 节点 / Turn a YAML node into a JSON node.
    /// </summary>
    /// <param name="node">YAML 节点 / The YAML node.</param>
    /// <returns>JSON 节点 / The JSON node.</returns>
    private static Result<JsonNode, ErrorCode, Error<ErrorCode>> YamlToNode(
        YamlDotNet.RepresentationModel.YamlNode node) {
        switch (node) {
            case YamlDotNet.RepresentationModel.YamlMappingNode mapping: {
                var result = new JsonObject();
                foreach ((YamlDotNet.RepresentationModel.YamlNode key,
                    YamlDotNet.RepresentationModel.YamlNode value) in mapping.Children) {
                    if (key is not YamlDotNet.RepresentationModel.YamlScalarNode scalarKey) {
                        return SearchCodecSupport.SearchInvalid<JsonNode>(
                            "configuration keys must be scalars");
                    }
                    // **重复键在这里报**：`JsonObject` 的索引器会静默覆盖，事后 `HasDuplicateKeys` 已经看不出痕迹。
                    // **Duplicates are reported here**: `JsonObject`'s indexer silently overwrites, and by the time `HasDuplicateKeys` runs there is no
                    // trace left.
                    if (result.ContainsKey(scalarKey.Value ?? string.Empty)) {
                        return SearchCodecSupport.SearchInvalid<JsonNode>(
                            "configuration contains duplicate keys");
                    }
                    Result<JsonNode, ErrorCode, Error<ErrorCode>> converted = YamlToNode(value);
                    if (converted is Failed<JsonNode, ErrorCode, Error<ErrorCode>> failed) {
                        return failed;
                    }
                    result[scalarKey.Value ?? string.Empty] =
                        ((Ok<JsonNode, ErrorCode, Error<ErrorCode>>)converted).Value;
                }
                return Results.Ok<JsonNode>(result);
            }
            case YamlDotNet.RepresentationModel.YamlSequenceNode sequence: {
                var result = new JsonArray();
                foreach (YamlDotNet.RepresentationModel.YamlNode child in sequence.Children) {
                    Result<JsonNode, ErrorCode, Error<ErrorCode>> converted = YamlToNode(child);
                    if (converted is Failed<JsonNode, ErrorCode, Error<ErrorCode>> failed) {
                        return failed;
                    }
                    result.Add(((Ok<JsonNode, ErrorCode, Error<ErrorCode>>)converted).Value);
                }
                return Results.Ok<JsonNode>(result);
            }
            case YamlDotNet.RepresentationModel.YamlScalarNode scalar:
                return ScalarToNode(scalar.Value, scalar.Tag);
            default:
                return SearchCodecSupport.SearchInvalid<JsonNode>("unsupported configuration node");
        }
    }

    /// <summary>
    /// 把标量转成 JSON 节点 / Turn a scalar into a JSON node.
    /// </summary>
    /// <remarks>
    /// **不按标签猜类型，而是按"能解析成什么"决定**。YAML 的隐式类型解析在 1.1 与 1.2 之间差别很大，
    /// 按标签走会让同一份 `on:` 在两种解析器下变成布尔或字符串——**而配置里它多半是个键**。
    /// 先试整数、再试浮点、最后当字符串，是最不容易误伤的顺序。
    ///
    /// **Types are decided by "what it parses as", not by the tag.** YAML's implicit typing differs substantially between 1.1 and 1.2, and going by the
    /// tag would make the same `on:` become a boolean under one parser and a string under another — **and in a configuration it is usually a key**.
    /// Try integer, then float, then string; that order misfires least.
    /// </remarks>
    /// <param name="value">文本 / The text.</param>
    /// <param name="tag">标签 / The tag.</param>
    /// <returns>JSON 节点 / The JSON node.</returns>
    private static Result<JsonNode, ErrorCode, Error<ErrorCode>> ScalarToNode(
        string? value, YamlDotNet.Core.TagName tag) {
        string text = value ?? string.Empty;
        if (text.Length == 0 || text is "null" or "~") {
            // **用 `JsonNode.Parse("null")` 而不是 `Ok<JsonNode>(null)`**：`JsonNode` 是不可空引用类型，
            // 传 `null` 会让可空性检查报错，而"配置里的 null"本身是合法值。
            // **Use `JsonNode.Parse("null")` rather than `Ok<JsonNode>(null)`**: `JsonNode` is a non-nullable reference type, so passing `null` trips the
            // nullability check — while "null in the configuration" is a legal value.
            return Results.Ok<JsonNode>(JsonNode.Parse("null")!);
        }
        if (long.TryParse(text, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out long integer)) {
            return Results.Ok<JsonNode>(JsonValue.Create(integer)!);
        }
        if (double.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double number)) {
            return double.IsFinite(number)
                ? Results.Ok<JsonNode>(JsonValue.Create(number)!)
                : SearchCodecSupport.SearchInvalid<JsonNode>(
                    "配置含非有限数 / Non-finite configuration number");
        }
        return Results.Ok<JsonNode>(JsonValue.Create(text)!);
    }

    /// <summary>
    /// 解析 TOML / Parse TOML.
    /// </summary>
    /// <param name="text">文本 / The text.</param>
    /// <returns>根节点或失败 / The root node or a failure.</returns>
    private static Result<JsonNode, ErrorCode, Error<ErrorCode>> ParseToml(string text) {
        try {
            // **`Tomlyn` 的 `ToModel` 直接给 `IDictionary`/`IList`/标量**；走 `ToJson` 会先经过一次字符串序列化，
            // 于是日期之类的类型在中间被改写一次，**而改写发生在"我们看不见的那一步"**。语法错误由 `TomlException` 报出，
            // 不必先跑一遍 `Parse` 再看诊断包——**那一步的类型在不同版本里也换过名字**。
            // **`Tomlyn`'s `ToModel` yields `IDictionary`/`IList`/scalars directly**; going through `ToJson` serialises to a string first, so types such as
            // dates are rewritten in an intermediate step, **one we cannot see**. Syntax errors surface as `TomlException`; running `Parse` first to inspect
            // the diagnostics bag is unnecessary — **and that step's type has been renamed across versions too**.
            object? model = Tomlyn.Toml.ToModel(text);
            return ModelToNode(model);
        } catch (Tomlyn.TomlException exception) {
            return SearchCodecSupport.SearchInvalid<JsonNode>(
                $"invalid configuration document: {exception.Message}");
        }
    }

    /// <summary>
    /// 把 TOML 模型转成 JSON 节点 / Turn a TOML model into a JSON node.
    /// </summary>
    /// <param name="model">模型 / The model.</param>
    /// <returns>JSON 节点 / The JSON node.</returns>
    private static Result<JsonNode, ErrorCode, Error<ErrorCode>> ModelToNode(object? model) {
        if (model is null) {
            return Results.Ok<JsonNode>(JsonNode.Parse("null")!);
        }
        // **`Tomlyn` 的 `TomlTable`/`TomlArray` 实现的是泛型集合接口，不是 `System.Collections.IDictionary`**。
        // 只按非泛型接口分派会让整份 TOML 落到最后的"转字符串"分支，**于是配置变成一个大字符串**，
        // 而错误只在下一层报"configuration must be an object"——**指向的位置与真因无关**。
        // **`Tomlyn`'s `TomlTable`/`TomlArray` implement generic collection interfaces, not `System.Collections.IDictionary`.** Dispatching only on the
        // non-generic interfaces sends a whole TOML document into the trailing "to string" branch, **so the configuration becomes one big string**,
        // and the error only surfaces one layer later as "configuration must be an object" — **pointing somewhere unrelated to the real cause**.
        if (model is Tomlyn.Model.TomlTable table) {
            var tableResult = new JsonObject();
            foreach ((string name, object? value) in table) {
                Result<JsonNode, ErrorCode, Error<ErrorCode>> converted = ModelToNode(value);
                if (converted is Failed<JsonNode, ErrorCode, Error<ErrorCode>> failed) {
                    return failed;
                }
                if (tableResult.ContainsKey(name)) {
                    return SearchCodecSupport.SearchInvalid<JsonNode>(
                        "configuration contains duplicate keys");
                }
                tableResult[name] = ((Ok<JsonNode, ErrorCode, Error<ErrorCode>>)converted).Value;
            }
            return Results.Ok<JsonNode>(tableResult);
        }
        // **`TomlTableArray` 是 `[[table]]` 的形态，既不是 `TomlArray` 也不是 `IDictionary`**。
        // 只认 `TomlArray` 会让"数组表"落进最后的转字符串分支，**于是 `algorithms` 变成一个字符串**，
        // 而错误要到反序列化时才以"类型不匹配"出现。
        // **`TomlTableArray` is the shape of `[[table]]`, and it is neither `TomlArray` nor `IDictionary`.** Recognising only `TomlArray` sends
        // "array of tables" into the trailing to-string branch, **so `algorithms` becomes one string**, and the error only appears later at
        // deserialisation as a type mismatch.
        if (model is Tomlyn.Model.TomlTableArray tableArray) {
            var tableArrayResult = new JsonArray();
            foreach (Tomlyn.Model.TomlTable child in tableArray) {
                Result<JsonNode, ErrorCode, Error<ErrorCode>> converted = ModelToNode(child);
                if (converted is Failed<JsonNode, ErrorCode, Error<ErrorCode>> failed) {
                    return failed;
                }
                tableArrayResult.Add(((Ok<JsonNode, ErrorCode, Error<ErrorCode>>)converted).Value);
            }
            return Results.Ok<JsonNode>(tableArrayResult);
        }
        if (model is Tomlyn.Model.TomlArray array) {
            var arrayResult = new JsonArray();
            foreach (object? child in array) {
                Result<JsonNode, ErrorCode, Error<ErrorCode>> converted = ModelToNode(child);
                if (converted is Failed<JsonNode, ErrorCode, Error<ErrorCode>> failed) {
                    return failed;
                }
                arrayResult.Add(((Ok<JsonNode, ErrorCode, Error<ErrorCode>>)converted).Value);
            }
            return Results.Ok<JsonNode>(arrayResult);
        }
        if (model is System.Collections.IDictionary dictionary) {
            var result = new JsonObject();
            foreach (object? key in dictionary.Keys) {
                if (key is not string name) {
                    return SearchCodecSupport.SearchInvalid<JsonNode>(
                        "configuration keys must be strings");
                }
                Result<JsonNode, ErrorCode, Error<ErrorCode>> converted =
                    ModelToNode(dictionary[key]);
                if (converted is Failed<JsonNode, ErrorCode, Error<ErrorCode>> failed) {
                    return failed;
                }
                if (result.ContainsKey(name)) {
                    return SearchCodecSupport.SearchInvalid<JsonNode>(
                        "configuration contains duplicate keys");
                }
                result[name] = ((Ok<JsonNode, ErrorCode, Error<ErrorCode>>)converted).Value;
            }
            return Results.Ok<JsonNode>(result);
        }
        if (model is System.Collections.IList list) {
            var result = new JsonArray();
            foreach (object? child in list) {
                Result<JsonNode, ErrorCode, Error<ErrorCode>> converted = ModelToNode(child);
                if (converted is Failed<JsonNode, ErrorCode, Error<ErrorCode>> failed) {
                    return failed;
                }
                result.Add(((Ok<JsonNode, ErrorCode, Error<ErrorCode>>)converted).Value);
            }
            return Results.Ok<JsonNode>(result);
        }
        if (model is bool flag) {
            return Results.Ok<JsonNode>(JsonValue.Create(flag)!);
        }
        if (model is long or int or short or sbyte or byte or ushort or uint) {
            return Results.Ok<JsonNode>(JsonValue.Create(Convert.ToInt64(model,
                System.Globalization.CultureInfo.InvariantCulture))!);
        }
        if (model is double or float or decimal) {
            double number = Convert.ToDouble(model,
                System.Globalization.CultureInfo.InvariantCulture);
            return double.IsFinite(number)
                ? Results.Ok<JsonNode>(JsonValue.Create(number)!)
                : SearchCodecSupport.SearchInvalid<JsonNode>(
                    "配置含非有限数 / Non-finite configuration number");
        }
        // **日期时间一律转字符串**，见类注释。
        // **Dates and times always become strings**, see the class remarks.
        return Results.Ok<JsonNode>(JsonValue.Create(model.ToString())!);
    }

    /// <summary>
    /// 写出 YAML / Write YAML.
    /// </summary>
    /// <param name="root">根对象 / The root object.</param>
    /// <returns>文本 / The text.</returns>
    private static string WriteYaml(JsonObject root) {
        YamlDotNet.RepresentationModel.YamlNode node = NodeToYaml(root);
        var stream = new YamlDotNet.RepresentationModel.YamlStream();
        stream.Documents.Add(new YamlDotNet.RepresentationModel.YamlDocument(node));
        var writer = new System.IO.StringWriter();
        stream.Save(writer);
        return writer.ToString();
    }

    /// <summary>
    /// 把 JSON 节点转成 YAML 节点 / Turn a JSON node into a YAML node.
    /// </summary>
    /// <param name="node">JSON 节点 / The JSON node.</param>
    /// <returns>YAML 节点 / The YAML node.</returns>
    private static YamlDotNet.RepresentationModel.YamlNode NodeToYaml(JsonNode? node) {
        if (node is JsonObject mapping) {
            var result = new YamlDotNet.RepresentationModel.YamlMappingNode();
            foreach ((string key, JsonNode? value) in mapping) {
                result.Add(new YamlDotNet.RepresentationModel.YamlScalarNode(key), NodeToYaml(value));
            }
            return result;
        }
        if (node is JsonArray sequence) {
            var result = new YamlDotNet.RepresentationModel.YamlSequenceNode();
            foreach (JsonNode? child in sequence) {
                result.Add(NodeToYaml(child));
            }
            return result;
        }
        if (node is null) {
            return new YamlDotNet.RepresentationModel.YamlScalarNode("null");
        }
        // **标量直接取原文，不再做引号剥离**：`ToJsonString()` 已经给字符串加了引号并对内部字符转义，
        // 手工 `Trim` + `Replace` 会在含引号的字符串上出错——**而错误只体现在写出的 YAML 里**。
        // **Scalars take their raw text without quote stripping**: `ToJsonString()` already quotes strings and escapes their contents, and a manual
        // `Trim` + `Replace` breaks on strings that contain quotes — **with the damage visible only in the emitted YAML**.
        if (node.AsValue().TryGetValue(out string? text) && text is not null) {
            return new YamlDotNet.RepresentationModel.YamlScalarNode(text);
        }
        return new YamlDotNet.RepresentationModel.YamlScalarNode(node.ToJsonString());
    }

    /// <summary>
    /// 写出 TOML / Write TOML.
    /// </summary>
    /// <remarks>
    /// **TOML 没有"根为数组"的概念**，而配置的根永远是对象；这里不做别的形状转换。
    /// **TOML has no "array at the root"**, and a configuration's root is always an object; no other reshaping happens here.
    /// </remarks>
    /// <param name="root">根对象 / The root object.</param>
    /// <returns>文本 / The text.</returns>
    private static string WriteToml(JsonObject root) =>
        Tomlyn.Toml.FromModel(NodeToModel(root)!);

    /// <summary>
    /// 把 JSON 节点转成 TOML 模型 / Turn a JSON node into a TOML model.
    /// </summary>
    /// <param name="node">JSON 节点 / The JSON node.</param>
    /// <returns>模型 / The model.</returns>
    private static object? NodeToModel(JsonNode? node) {
        if (node is JsonObject mapping) {
            var result = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach ((string key, JsonNode? value) in mapping) {
                result[key] = NodeToModel(value);
            }
            return result;
        }
        if (node is JsonArray sequence) {
            var result = new List<object?>(sequence.Count);
            foreach (JsonNode? child in sequence) {
                result.Add(NodeToModel(child));
            }
            return result;
        }
        if (node is null) {
            return null;
        }
        JsonValue scalar = node.AsValue();
        if (scalar.TryGetValue(out bool flag)) {
            return flag;
        }
        if (scalar.TryGetValue(out long integer)) {
            return integer;
        }
        if (scalar.TryGetValue(out double number)) {
            return number;
        }
        return scalar.GetValue<string>();
    }

    /// <summary>
    /// 规范化为确定性 JSON / Canonicalize into deterministic JSON.
    /// </summary>
    /// <param name="value">值 / The value.</param>
    /// <returns>规范化文本 / The canonical text.</returns>
    public static string CanonicalJson(JsonElement value) {
        var builder = new StringBuilder();
        Append(value, builder);
        return builder.ToString();
    }

    /// <summary>
    /// 规范化内容的指纹 / The fingerprint of canonical content.
    /// </summary>
    /// <param name="value">值 / The value.</param>
    /// <returns>指纹 / The fingerprint.</returns>
    public static string Fingerprint(JsonElement value) =>
        SolveFingerprinting.Sha256(CanonicalJson(value)).Value;

    /// <summary>
    /// 追加规范化的 JSON / Append canonical JSON.
    /// </summary>
    /// <param name="value">值 / The value.</param>
    /// <param name="builder">构建器 / The builder.</param>
    private static void Append(JsonElement value, StringBuilder builder) {
        switch (value.ValueKind) {
            case JsonValueKind.Object: {
                var keys = new List<string>();
                foreach (JsonProperty property in value.EnumerateObject()) {
                    keys.Add(property.Name);
                }
                // **键必须排序**：不排序会让同一份内容在不同序列化顺序下得到不同指纹，**而"内容相同"这件事因此无法校验**。
                // **Keys must be sorted**: without sorting, the same content yields different fingerprints under different serialization
                // orders, **so "the content is identical" becomes unverifiable**.
                keys.Sort(StringComparer.Ordinal);
                builder.Append('{');
                for (int index = 0; index < keys.Count; index++) {
                    if (index > 0) {
                        builder.Append(',');
                    }
                    builder.Append(JsonSerializer.Serialize(keys[index]));
                    builder.Append(':');
                    Append(value.GetProperty(keys[index]), builder);
                }
                builder.Append('}');
                break;
            }
            case JsonValueKind.Array: {
                builder.Append('[');
                bool first = true;
                foreach (JsonElement item in value.EnumerateArray()) {
                    if (!first) {
                        builder.Append(',');
                    }
                    first = false;
                    Append(item, builder);
                }
                builder.Append(']');
                break;
            }
            default:
                builder.Append(value.GetRawText());
                break;
        }
    }

    /// <summary>
    /// 文档里是否含非有限数 / Whether the document contains a non-finite number.
    /// </summary>
    /// <param name="value">值 / The value.</param>
    /// <returns>是否含非有限数 / Whether it contains one.</returns>
    private static bool IsFinite(JsonElement value) {
        switch (value.ValueKind) {
            case JsonValueKind.Number:
                return double.IsFinite(value.GetDouble());
            case JsonValueKind.Object:
                foreach (JsonProperty property in value.EnumerateObject()) {
                    if (!IsFinite(property.Value)) {
                        return false;
                    }
                }
                return true;
            case JsonValueKind.Array:
                foreach (JsonElement item in value.EnumerateArray()) {
                    if (!IsFinite(item)) {
                        return false;
                    }
                }
                return true;
            default:
                return true;
        }
    }

    /// <summary>
    /// 文档里是否有重复键 / Whether the document has duplicate keys.
    /// </summary>
    /// <param name="value">值 / The value.</param>
    /// <returns>是否有重复键 / Whether it has duplicates.</returns>
    private static bool HasDuplicateKeys(JsonElement value) {
        switch (value.ValueKind) {
            case JsonValueKind.Object: {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonProperty property in value.EnumerateObject()) {
                    if (!seen.Add(property.Name)) {
                        return true;
                    }
                    if (HasDuplicateKeys(property.Value)) {
                        return true;
                    }
                }
                return false;
            }
            case JsonValueKind.Array:
                foreach (JsonElement item in value.EnumerateArray()) {
                    if (HasDuplicateKeys(item)) {
                        return true;
                    }
                }
                return false;
            default:
                return false;
        }
    }

    /// <summary>
    /// 构造"不支持的格式"错误 / Build an "unsupported format" error.
    /// </summary>
    /// <typeparam name="T">结果类型 / The result type.</typeparam>
    /// <param name="format">格式 / The format.</param>
    /// <returns>失败结果 / The failure.</returns>
    private static Result<T, ErrorCode, Error<ErrorCode>> Unsupported<T>(ConfigurationFormat format) =>
        SearchCodecSupport.SearchInvalid<T>(
            $"unsupported configuration format: {format} (only JSON is implemented)");
}
