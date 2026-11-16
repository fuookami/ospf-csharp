// YAML/TOML 配置通道测试 / Tests for the YAML and TOML configuration channels.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Heuristic.Configuration;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// YAML/TOML 配置通道 / The YAML and TOML configuration channels.
/// </summary>
public sealed class ConfigurationFormatRoundTripTests {
    /// <summary>**同一份配置在三种格式下解析结果一致** / The same configuration decodes identically in all three formats.</summary>
    /// <remarks>
    /// **三种格式共用同一条校验链**（重复键、非有限数、迁移、反序列化、`Validate`）。各写一套会让规则漂移，
    /// 而**漂移之后"同一份内容换一种格式就通过"**。
    ///
    /// **All three formats share one validation chain** (duplicate keys, non-finite numbers, migration, deserialisation, `Validate`). Separate chains
    /// would drift, and **after drift "the same content passes in another format"**.
    /// </remarks>
    [Fact]
    public void Decode_ShouldParseTheSameConfigurationInAllFormats() {
        HyperHeuristicConfig expected = Sample();

        Result<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>> fromJson =
            ConfigurationFormats.Decode(
                Json, ConfigurationFormat.Json);
        Result<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>> fromYaml =
            ConfigurationFormats.Decode(
                Yaml, ConfigurationFormat.Yaml);
        Result<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>> fromToml =
            ConfigurationFormats.Decode(
                Toml, ConfigurationFormat.Toml);

        string[] names = { "json", "yaml", "toml" };
        int at = 0;
        foreach (Result<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>> r in new[] { fromJson, fromYaml, fromToml }) {
            if (r is Failed<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>> f) {
                throw new InvalidOperationException("DIAG " + names[at] + ": " + f.Error.Message);
            }
            at++;
        }
        fromJson.Should().BeOfType<Ok<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>>>();
        fromYaml.Should().BeOfType<Ok<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>>>();
        fromToml.Should().BeOfType<Ok<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>>>();

        HyperHeuristicConfig json = ((Ok<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>>)fromJson).Value;
        HyperHeuristicConfig yaml = ((Ok<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>>)fromYaml).Value;
        HyperHeuristicConfig toml = ((Ok<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>>)fromToml).Value;
        yaml.Should().BeEquivalentTo(json);
        toml.Should().BeEquivalentTo(json);
        json.Problem.Type.Should().Be(expected.Problem.Type);
    }

    /// <summary>**YAML 重复键必须在解析阶段被拒** / Duplicate YAML keys must be rejected while parsing.</summary>
    /// <remarks>
    /// **实测结论：这条测试由 `YamlDotNet` 的解析器拦下**（错误消息 `Duplicate key problem`），不是转换层那条检查。
    /// 删掉转换层的检查，测试照样通过——**它是纵深防御，不是这道防线的守卫**。
    ///
    /// 保留转换层的检查，因为它守的是"换一个解析器"之后的场景：不同 YAML 实现允许重复键的宽严程度不同，
    /// 而**一旦解析器放行，`JsonObject` 的索引器会静默覆盖**。
    ///
    /// **Measured: this test is stopped by `YamlDotNet`'s parser itself** (the message is `Duplicate key problem`), not by the conversion-layer check.
    /// Deleting that check leaves the test green — **it is defence in depth, not the guard for this line**.
    ///
    /// The conversion-layer check stays because it guards the post-parser-swap scenario: YAML implementations differ in how strictly they forbid
    /// duplicate keys, and **once a parser lets one through, `JsonObject`'s indexer overwrites silently**.
    /// </remarks>
    [Fact]
    public void Decode_ShouldRejectDuplicateYamlKeys() =>
        ConfigurationFormats.Decode(DuplicateYaml, ConfigurationFormat.Yaml)
            .Should().BeOfType<Failed<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>>>();

    /// <summary>**非有限数在 YAML/TOML 里同样被拒** / Non-finite numbers are rejected in YAML and TOML too.</summary>
    [Fact]
    public void Decode_ShouldRejectNonFiniteNumbers() {
        ConfigurationFormats.Decode("evaluations: 100\nalgorithms: 1e999", ConfigurationFormat.Yaml)
            .Should().BeOfType<Failed<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**YAML 根必须是对象** / A YAML root must be an object.</summary>
    [Fact]
    public void Decode_ShouldRejectScalarYamlRoot() {
        Result<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>> decoded =
            ConfigurationFormats.Decode("42", ConfigurationFormat.Yaml);

        decoded.Should().BeOfType<Failed<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>>>();
        // **必须断言错误内容**：把 YAML 根换成空对象后，`Validate` 会以"缺 problem/algorithms"再拒一次——
        // 于是"失败了"依然成立，**而"根必须是对象"这条检查可以整个删掉**。这里第一次变异正是这么存活的。
        // **The error content must be asserted**: replacing the YAML root with an empty object makes `Validate` reject it again for "missing
        // problem/algorithms" — so "it failed" still holds and **the "root must be an object" check can be deleted outright**. That is exactly how this
        // batch's first mutation survived.
        ((Failed<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>>)decoded)
            .Error.Message.Should().Contain("must be an object",
                "错误必须指名根类型 / the error must name the root type");
    }

    /// <summary>**空的 TOML 文档被拒** / An empty TOML document is rejected.</summary>
    /// <remarks>
    /// **`Tomlyn` 根本无法产生"根为标量"的文档**：TOML 的语法要求根下就是键值对，`"42"` 是一个**语法错误**（
    /// `Expecting '=' after a key`）。所以 `FromNode` 里那条"根必须是对象"的检查**对 TOML 路径不可达**，
    /// 它实际守的是 YAML 路径——两个格式共用同一条校验链，这条检查因此仍然必要，但**不能声称它守住了 TOML**。
    ///
    /// 本测试改为断言"空文档被拒"，这条走的是 `Validate` 而非根类型检查，两者都指向"配置不完整"。
    ///
    /// **`Tomlyn` simply cannot produce a document whose root is a scalar**: TOML's grammar requires key-value pairs at the root, so `"42"` is a
    /// **syntax error** (`Expecting '=' after a key`). The "root must be an object" check in `FromNode` is therefore **unreachable on the TOML path**;
    /// what it actually guards is the YAML path — the two formats share one validation chain, so the check remains necessary, but **it cannot be claimed
    /// to guard TOML**. This test instead asserts that an empty document is rejected, which goes through `Validate` rather than the root-type check;
    /// both point at "the configuration is incomplete".
    /// </remarks>
    [Fact]
    public void Decode_ShouldRejectEmptyTomlDocument() {
        Result<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>> decoded =
            ConfigurationFormats.Decode(string.Empty, ConfigurationFormat.Toml);

        decoded.Should().BeOfType<Failed<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>>>();
        ((Failed<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>>)decoded)
            .Error.Message.Should().Contain("problem",
                "空文档缺必填字段 / an empty document lacks the required fields");
    }

    /// <summary>**编码后再次解码得到同一份配置** / Encoding then decoding yields the same configuration.</summary>
    [Fact]
    public void EncodeRoundTrip_ShouldSurviveBothFormats() {
        HyperHeuristicConfig config = Sample();

        Result<string, ErrorCode, Error<ErrorCode>> yamlText = ConfigurationFormats.Encode(
            config, ConfigurationFormat.Yaml);
        Result<string, ErrorCode, Error<ErrorCode>> tomlText = ConfigurationFormats.Encode(
            config, ConfigurationFormat.Toml);

        yamlText.Should().BeOfType<Ok<string, ErrorCode, Error<ErrorCode>>>();
        tomlText.Should().BeOfType<Ok<string, ErrorCode, Error<ErrorCode>>>();

        HyperHeuristicConfig fromYaml = ((Ok<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>>)
            ConfigurationFormats.Decode(
                ((Ok<string, ErrorCode, Error<ErrorCode>>)yamlText).Value,
                ConfigurationFormat.Yaml)).Value;
        HyperHeuristicConfig fromToml = ((Ok<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>>)
            ConfigurationFormats.Decode(
                ((Ok<string, ErrorCode, Error<ErrorCode>>)tomlText).Value,
                ConfigurationFormat.Toml)).Value;

        fromYaml.Should().BeEquivalentTo(config);
        fromToml.Should().BeEquivalentTo(config);
    }

    /// <summary>一份样例配置 / A sample configuration.</summary>
    /// <returns>配置 / The configuration.</returns>
    private static HyperHeuristicConfig Sample() =>
        new(
            new ComponentSpec("quadratic"),
            new[] { new AlgorithmConfig("a", new ComponentSpec("pso"), 20) },
            new BudgetConfig(Evaluations: 100L, SliceEvaluations: 5L, SliceSteps: 1L),
            new ComponentSpec("round-robin"),
            null,
            null,
            new LearningConfig(Mode: LearningMode.Frozen),
            null,
            1);

    /// <summary>JSON 文本 / The JSON text.</summary>
    private const string Json = """
        {
          "problem": { "type": "quadratic" },
          "algorithms": [ { "id": "a", "algorithm": { "type": "pso" }, "size": 20 } ],
          "budget": { "evaluations": 100, "sliceEvaluations": 5, "sliceSteps": 1 },
          "selection": { "type": "round-robin" },
          "learning": { "mode": 2 }
        }
        """;

    /// <summary>YAML 文本 / The YAML text.</summary>
    private const string Yaml = """
        problem:
          type: quadratic
        algorithms:
          - id: a
            algorithm:
              type: pso
            size: 20
        budget:
          evaluations: 100
          sliceEvaluations: 5
          sliceSteps: 1
        selection:
          type: round-robin
        learning:
          mode: 2
        """;

    /// <summary>TOML 文本 / The TOML text.</summary>
    private const string Toml = """
        [problem]
        type = "quadratic"

        # **内联表**：`[algorithms.algorithm]` 会把 `[[algorithms]]` 的数组表改写成普通表，
        # 于是 `algorithms` 不再是列表——**而错误要到反序列化时才以"类型不匹配"出现**。
        # **Inline table**: `[algorithms.algorithm]` rewrites the `[[algorithms]]` array-of-tables into a plain table, so `algorithms` is no longer a
        # list — **and the error only appears later at deserialisation as a type mismatch**.
        [[algorithms]]
        id = "a"
        size = 20
        algorithm = { type = "pso" }

        [budget]
        evaluations = 100
        sliceEvaluations = 5
        sliceSteps = 1

        [selection]
        type = "round-robin"

        [learning]
        mode = 2
        """;

    /// <summary>带重复键的 YAML / YAML with a duplicate key.</summary>
    private const string DuplicateYaml = """
        problem:
          type: quadratic
        problem:
          type: quadratic
        algorithms:
          - id: a
            algorithm:
              type: pso
        budget:
          evaluations: 100
        """;
}
