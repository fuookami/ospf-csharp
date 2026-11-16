// 配置格式测试 / Tests for the configuration formats.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Heuristic.Configuration;
using Fuookami.Ospf.Core.Solver.Heuristic.Persistence;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 配置格式编解码 / The configuration-format codecs.
/// </summary>
public sealed class ConfigurationFormatsTests {
    /// <summary>**JSON 往返一致** / JSON round-trips.</summary>
    [Fact]
    public void Json_ShouldRoundTrip() {
        HyperHeuristicConfig config = BuildConfig();
        Result<string, ErrorCode, Error<ErrorCode>> encoded =
            ConfigurationFormats.Encode(config, ConfigurationFormat.Json);

        encoded.Should().BeOfType<Ok<string, ErrorCode, Error<ErrorCode>>>();
        string text = ((Ok<string, ErrorCode, Error<ErrorCode>>)encoded).Value;
        Result<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>> decoded =
            ConfigurationFormats.Decode(text, ConfigurationFormat.Json);

        decoded.Should().BeOfType<Ok<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>>>();
        HyperHeuristicConfig restored =
            ((Ok<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>>)decoded).Value;
        restored.Problem.Type.Should().Be("model");
        restored.Algorithms.Count.Should().Be(1);
        restored.Budget.Evaluations.Should().Be(100L);
    }

    /// <summary>**YAML 与 TOML 不再报"不支持"，而是真的能编码** / YAML and TOML no longer report "unsupported" and really encode.</summary>
    /// <remarks>
 /// **这是对 earlier 记录的能力缺口的反转**：当时本仓库没有 `YamlDotNet`/`Tomlyn`，两种格式显式返回"不支持"。
 /// earlier 引入了这两个库并接通了通道，因此这条测试必须反向——留着它会让"通道坏了"和"通道好了"都表现为**测试失败**，
    /// 而失败原因完全不同。
    ///
 /// **This reverses the capability gap recorded in earlier**: back then this repository had neither `YamlDotNet` nor `Tomlyn`, and both formats
 /// explicitly reported "unsupported". earlier adds those libraries and wires the channels up, so this test must invert — keeping it as it was
    /// would make both "the channel broke" and "the channel works" show up as **a failing test**, for entirely different reasons.
    /// </remarks>
    [Fact]
    public void YamlAndToml_ShouldEncodeAndDecode() {
        HyperHeuristicConfig config = BuildConfig();

        ConfigurationFormats.Encode(config, ConfigurationFormat.Yaml)
            .Should().BeOfType<Ok<string, ErrorCode, Error<ErrorCode>>>();
        ConfigurationFormats.Encode(config, ConfigurationFormat.Toml)
            .Should().BeOfType<Ok<string, ErrorCode, Error<ErrorCode>>>();
        // **空文档仍然被拒**：它缺必填字段，而不是"格式不支持"。
        // **An empty document is still rejected**: it lacks required fields, which is not "unsupported format".
        ConfigurationFormats.Decode(string.Empty, ConfigurationFormat.Yaml)
            .Should().BeOfType<Failed<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>>>();
        ConfigurationFormats.Decode(string.Empty, ConfigurationFormat.Toml)
            .Should().BeOfType<Failed<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**超过 4 MiB 的配置被拒绝** / A configuration over 4 MiB is rejected.</summary>
    /// <remarks>
    /// 不限制会让一份畸形配置把内存吃光，而失败点发生在解析深处，**调用方拿到的是一个 OOM 而不是"配置太大"**。
    /// Without the cap a malformed configuration exhausts memory, and the failure happens deep inside parsing, **so the caller gets an OOM
    /// instead of "configuration too large"**.
    /// </remarks>
    [Fact]
    public void Decode_ShouldRejectOversizedInput() {
        string huge = "{\"pad\":\"" + new string('x', 4 * 1024 * 1024 + 1) + "\"}";

        Result<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>> decoded =
            ConfigurationFormats.Decode(huge, ConfigurationFormat.Json);
        // **必须断言错误消息，不能只断言失败**：去掉上限之后这份文档照样会因"非法配置"而失败，两种实现无法区分。
        // 第一版只断言 `Failed`，变异测试报告"已通过"。
        // **The error message must be asserted, not just the failure**: without the cap this document still fails as "illegal
        // configuration", so the two implementations are indistinguishable. The first version asserted only `Failed`, and the mutation
        // test reported a pass.
        decoded.Should().BeOfType<Failed<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>>>();
        ((Failed<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>>)decoded).Error.Message
            .Should().Contain("4 MiB");
    }

    /// <summary>**非对象配置被拒绝** / A non-object configuration is rejected.</summary>
    [Fact]
    public void Decode_ShouldRejectNonObject() {
        ConfigurationFormats.Decode("[]", ConfigurationFormat.Json)
            .Should().BeOfType<Failed<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>>>();
        ConfigurationFormats.Decode("\"text\"", ConfigurationFormat.Json)
            .Should().BeOfType<Failed<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**重复键被拒绝** / Duplicate keys are rejected.</summary>
    /// <remarks>
    /// JSON 规范允许重复键并"后者覆盖前者"，而配置里的重复键几乎总是复制粘贴错误；静默覆盖会让一份**看起来只有一份**的配置
    /// 实际生效的是被覆盖的那份。
    /// JSON permits duplicates with "last wins", and a duplicate key in a configuration is almost always a copy-paste error; silent
    /// overwriting makes a configuration that **looks like it has one entry** actually apply the overwritten one.
    /// </remarks>
    [Fact]
    public void Decode_ShouldRejectDuplicateKeys() {
        string text = "{\"version\":1,\"version\":1}";

        ConfigurationFormats.Decode(text, ConfigurationFormat.Json)
            .Should().BeOfType<Failed<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**非法 JSON 被拒绝** / Malformed JSON is rejected.</summary>
    [Fact]
    public void Decode_ShouldRejectMalformedJson() {
        ConfigurationFormats.Decode("{", ConfigurationFormat.Json)
            .Should().BeOfType<Failed<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>>>();
        ConfigurationFormats.Decode("{\"a\":1,}", ConfigurationFormat.Json)
            .Should().BeOfType<Failed<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**非法配置在解析后被校验拒绝** / An illegal configuration is rejected by validation after parsing.</summary>
    [Fact]
    public void Decode_ShouldValidateParsedConfiguration() {
        // 缺少 problem 与 algorithms。
        // Missing problem and algorithms.
        string text = "{\"budget\":{\"evaluations\":100}}";

        ConfigurationFormats.Decode(text, ConfigurationFormat.Json)
            .Should().BeOfType<Failed<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**规范化 JSON 的键按序排列** / Canonical JSON sorts keys.</summary>
    /// <remarks>
    /// 不排序会让同一份内容在不同序列化顺序下得到不同指纹，**而"内容相同"这件事因此无法校验**。
    /// Without sorting, the same content yields different fingerprints under different serialization orders, **so "the content is identical"
    /// becomes unverifiable**.
    /// </remarks>
    [Fact]
    public void CanonicalJson_ShouldSortKeys() {
        JsonElement left = JsonDocument.Parse("{\"b\":1,\"a\":2}").RootElement;
        JsonElement right = JsonDocument.Parse("{\"a\":2,\"b\":1}").RootElement;

        ConfigurationFormats.CanonicalJson(left).Should().Be("{\"a\":2,\"b\":1}");
        ConfigurationFormats.CanonicalJson(right).Should().Be("{\"a\":2,\"b\":1}");
        ConfigurationFormats.Fingerprint(left).Should().Be(ConfigurationFormats.Fingerprint(right));
    }

    /// <summary>**规范化的嵌套结构也排序** / Canonicalization sorts nested structures too.</summary>
    [Fact]
    public void CanonicalJson_ShouldSortNestedKeys() {
        JsonElement value = JsonDocument.Parse(
            "{\"outer\":{\"z\":[{\"y\":1,\"x\":2}],\"a\":3}}").RootElement;

        ConfigurationFormats.CanonicalJson(value)
            .Should().Be("{\"outer\":{\"a\":3,\"z\":[{\"x\":2,\"y\":1}]}}");
    }

    /// <summary>**指纹是 64 位十六进制** / The fingerprint is 64 lowercase hex characters.</summary>
    [Fact]
    public void Fingerprint_ShouldBeSha256Hex() {
        JsonElement value = JsonDocument.Parse("{\"a\":1}").RootElement;

        string fingerprint = ConfigurationFormats.Fingerprint(value);
        fingerprint.Length.Should().Be(64);
        fingerprint.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    /// <summary>**编码前先校验配置** / Encoding validates the configuration first.</summary>
    [Fact]
    public void Encode_ShouldValidateFirst() {
        var invalid = new HyperHeuristicConfig(
            new ComponentSpec("model"), Array.Empty<AlgorithmConfig>(),
            new BudgetConfig());

        ConfigurationFormats.Encode(invalid, ConfigurationFormat.Json)
            .Should().BeOfType<Failed<string, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**版本 2 的文档经迁移后可以解析** / A version-2 document parses after migration.</summary>
    [Fact]
    public void Decode_ShouldApplyMigration() {
        string text = "{\"version\":2,\"problem\":{\"type\":\"model\"},"
            + "\"algorithms\":[{\"id\":\"genetic\",\"algorithm\":{\"type\":\"genetic\"},\"size\":20}],"
            + "\"budget\":{\"evaluations\":100}}";
        var migrations = new Dictionary<int, IConfigurationMigration> {
            [2] = new DowngradeMigration(),
        };

        Result<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>> decoded =
            ConfigurationFormats.Decode(text, ConfigurationFormat.Json, migrations);

        decoded.Should().BeOfType<Ok<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**未知版本的文档被拒绝** / An unknown document version is rejected.</summary>
    [Fact]
    public void Decode_ShouldRejectUnknownVersion() {
        string text = "{\"version\":9,\"problem\":{\"type\":\"model\"},"
            + "\"algorithms\":[],\"budget\":{\"evaluations\":100}}";

        ConfigurationFormats.Decode(text, ConfigurationFormat.Json)
            .Should().BeOfType<Failed<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>构造一个合法配置 / Build a legal configuration.</summary>
    /// <returns>配置 / The configuration.</returns>
    private static HyperHeuristicConfig BuildConfig() => new(
        new ComponentSpec("model"),
        new[] { new AlgorithmConfig("genetic", new ComponentSpec("genetic")) },
        new BudgetConfig(Evaluations: 100L));

    /// <summary>把版本 2 降到版本 1 的迁移器 / A migrator that downgrades version 2 to version 1.</summary>
    private sealed class DowngradeMigration : IConfigurationMigration {
        /// <inheritdoc/>
        public Result<JsonObject, ErrorCode, Error<ErrorCode>> Migrate(JsonObject document) {
            JsonObject copy = JsonNode.Parse(document.ToJsonString())!.AsObject();
            copy["version"] = 1;
            return Results.Ok(copy);
        }
    }
}
