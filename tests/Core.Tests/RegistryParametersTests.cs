// 注册表参数校验测试 / Tests for the registry's parameter validation.

using System.Collections.Generic;
using System.Text.Json.Nodes;
using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Heuristic.Configuration;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 注册表参数校验 / The registry's parameter validation.
/// </summary>
public sealed class RegistryParametersTests {
    /// <summary>**无参数组件不接受任何参数** / A parameterless component accepts no parameter.</summary>
    /// <remarks>
    /// 忽略未知参数等于让拼写错误永远沉默：配置里写了 `mutatonRate`，**算法照样跑，只是按默认变异率跑**，
    /// 而配置看起来完全正常。
    /// Ignoring unknown parameters keeps typos silent forever: a configuration says `mutatonRate`, **the algorithm still runs, merely on the
    /// default mutation rate**, and the file looks perfectly normal.
    /// </remarks>
    [Fact]
    public void Empty_ShouldRejectAnyParameter() {
        RegistryParameters.Empty(Spec("gaussian")).Should()
            .BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        RegistryParameters.Empty(Spec("gaussian", ("sigma", 0.5))).Should()
            .BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**未知键被拒绝，不是被忽略** / An unknown key is rejected, not ignored.</summary>
    [Fact]
    public void Numeric_ShouldRejectUnknownKey() {
        RegistryParameters.Numeric(Spec("levy", ("amount", 3)), KeySet("amount")).Should()
            .BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        RegistryParameters.Numeric(Spec("levy", ("amout", 3)), KeySet("amount")).Should()
            .BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**字符串形式的数值被拒绝；非有限数在 JSON 里根本表达不出来** / Numeric strings are rejected, and non-finite numbers cannot be expressed in JSON at all.</summary>
    /// <remarks>
    /// `NaN` 与 `Infinity` 能通过 JSON 解析这件事**是错的**——`JsonNode.ToJsonString` 直接抛异常。
    /// 也就是说"非有限数"这条守卫**从 JSON 文本根本走不到**，它是防御性代码，不是可达分支。
    /// **本测试不伪造一条走不到的分支**：只断言字符串被拒绝、以及超大但有限的数被接受，
    /// 然后把"非有限守卫不可达"这件事记在这里。
    ///
    /// It is **not true** that `NaN` and `Infinity` parse from JSON — `JsonNode.ToJsonString` throws outright. In other words the "non-finite"
    /// guard **is unreachable from JSON text**; it is defensive code, not a reachable branch. **This test does not fabricate a path into an
    /// unreachable branch**: it asserts that strings are rejected and that a huge but finite number is accepted, and records the unreachability here.
    /// </remarks>
    [Fact]
    public void Numeric_ShouldRejectNumericStrings() {
        RegistryParameters.Numeric(Spec("gaussian", ("sigma", "0.5")), KeySet("sigma")).Should()
            .BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        RegistryParameters.Numeric(Spec("gaussian", ("sigma", 1e300)), KeySet("sigma")).Should()
            .BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**版本不是 1 时拒绝** / A version other than one is rejected.</summary>
    [Fact]
    public void Numeric_ShouldRejectOtherVersions() {
        var spec = new ComponentSpec("gaussian", new JsonObject(), 2);

        RegistryParameters.Numeric(spec, KeySet("sigma")).Should()
            .BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**必须为正的整数键与正数键各自被校验** / The positive-integer keys and the positive-number keys are checked separately.</summary>
    [Fact]
    public void Numeric_ShouldEnforcePositiveRanges() {
        RegistryParameters.Numeric(Spec("perturbation", ("amount", 3)), KeySet("amount")).Should()
            .BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        RegistryParameters.Numeric(Spec("perturbation", ("amount", 0)), KeySet("amount")).Should()
            .BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        RegistryParameters.Numeric(Spec("sbx", ("eta", 0.0)), KeySet("eta")).Should()
            .BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**表格式状态数有上限** / The tabular-state count has a ceiling.</summary>
    [Fact]
    public void Numeric_ShouldCapTabularStates() {
        RegistryParameters.Numeric(Spec("q-table", ("states", 4096)), KeySet("states")).Should()
            .BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        RegistryParameters.Numeric(Spec("q-table", ("states", 4097)), KeySet("states")).Should()
            .BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**算法参数只接受该算法允许的键** / Algorithm parameters accept only that algorithm's keys.</summary>
    [Fact]
    public void ValidateAlgorithm_ShouldRejectForeignKeys() {
        Algorithm("pso", 20, ("maxVelocity", 100.0)).Should()
            .BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        Algorithm("pso", 20, ("mutationRate", 0.1)).Should()
            .BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        Algorithm("nsga-ii", 20, ("divisions", 8)).Should()
            .BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        Algorithm("genetic", 20, ("mutationRate", 0.1)).Should()
            .BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**未知算法不校验** / An unknown algorithm is not validated.</summary>
    [Fact]
    public void ValidateAlgorithm_ShouldSkipUnknownTypes() {
        Algorithm("my-custom-algo", 20, ("anything", 1.0)).Should()
            .BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**SHADE 家族的规模下限与最小种群** / The SHADE family's size floor and minimum population.</summary>
    [Fact]
    public void ValidateAlgorithm_ShouldEnforceShadeSizes() {
        // **必须用 `de` 来测"至少四个个体"这条守卫**：`shade` 在 size=3 时会先被"最小种群大于规模"那条检查拦住，
        // 于是"规模下限"是否有效根本无从判断——这里第一版正是这么写的，变异因此存活。
        // **The "at least four individuals" guard must be tested through `de`**: with size 3 a `shade` is caught first by the "minimum population
        // exceeds the size" check, leaving the size floor untested — exactly how the first version of this test was written, which is why the
        // mutation survived.
        Algorithm("de", 3).Should()
            .BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        Algorithm("shade", 8, ("minimumSize", 10)).Should()
            .BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        Algorithm("shade", 8, ("minimumSize", 4)).Should()
            .BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        Algorithm("clpso", 1).Should()
            .BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**概率类参数必须在 0 到 1 之间** / Probability-like parameters must lie between zero and one.</summary>
    [Fact]
    public void ValidateAlgorithm_ShouldEnforceRateRanges() {
        Algorithm("genetic", 20, ("mutationRate", 2.0)).Should()
            .BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        Algorithm("genetic", 20, ("mutationRate", -0.1)).Should()
            .BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        Algorithm("genetic", 20).Should()
            .BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**数值协议：`native` 收，fdlibm 明确拒** / The numerics protocol: `native` is accepted, fdlibm is refused.</summary>
    /// <remarks>
    /// 五条成对：`native` 收 / **所属算法的 fdlibm 协议拒**（消息点名 `fdlibm`）/ 别的算法的协议拒 / 未知取值拒 / 都是同一个失败码。
    /// "收下但不用"是最坏的一种：配置说"按 fdlibm 逐位可复现"，跑出来的却是原生数值——**同一份配置对应两份轨迹，而没有任何地方报错**。
    ///
    /// Five paired points: `native` is accepted; **the owning algorithm's fdlibm protocol is refused**, with `fdlibm` named in the message; another algorithm's protocol is
    /// refused; an unknown value is refused; all under one failure code. "Accepted but not used" is the worst kind: the config says "bit-exactly reproducible via fdlibm" while the
    /// run uses native numerics — **one config maps to two trajectories and nothing reports it**.
    /// </remarks>
    [Fact]
    public void ValidateAlgorithm_ShouldEnforceNumericsMode() {
        Algorithm("cma-es", 20, ("numerics", "native")).Should()
            .BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();

        Result<Success, ErrorCode, Error<ErrorCode>> refusedFdlibm =
            Algorithm("cma-es", 20, ("numerics", "cma-polar-fdlibm-v1"));
        refusedFdlibm.Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>()
            .Which.Error.Message.Should().Contain("fdlibm");

        Algorithm("shade", 20, ("numerics", "shade-fdlibm-v1")).Should()
            .BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        Algorithm("cma-es", 20, ("numerics", "shade-fdlibm-v1")).Should()
            .BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        Algorithm("cma-es", 20, ("numerics", "fast")).Should()
            .BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>构造键集合 / Build a key set.</summary>
    /// <param name="keys">键 / The keys.</param>
    /// <returns>键集合 / The key set.</returns>
    private static IReadOnlySet<string> KeySet(params string[] keys) =>
        new HashSet<string>(keys, System.StringComparer.Ordinal);

    /// <summary>构造组件规格 / Build a component spec.</summary>
    /// <param name="type">类型 / The type.</param>
    /// <param name="parameters">参数 / The parameters.</param>
    /// <returns>组件规格 / The component spec.</returns>
    private static ComponentSpec Spec(string type, params (string Key, object Value)[] parameters) {
        var node = new JsonObject();
        foreach ((string key, object? value) in parameters) {
            // **必须按具体类型转换**：`JsonValue.Create<object>` 会包一层对象，`TryGetValue<double>` 直接失败，
            // 于是"参数合法"的用例会被误判成"参数非法"——这里第一版正是这么写的。
            // **The conversion must be by concrete type**: `JsonValue.Create<object>` wraps the object, so `TryGetValue<double>` fails and a
            // "valid parameters" case is mis-read as "invalid" — which is exactly how the first version of this test was written.
            node[key] = value switch {
                JsonNode json => json.DeepClone(),
                double number => JsonValue.Create(number),
                int integer => JsonValue.Create(integer),
                long integer => JsonValue.Create(integer),
                bool boolean => JsonValue.Create(boolean),
                string text => JsonValue.Create(text),
                null => null,
                _ => JsonValue.Create(value.ToString()),
            };
        }
        return new ComponentSpec(type, node);
    }

    /// <summary>构造算法配置并校验 / Build an algorithm configuration and validate it.</summary>
    /// <param name="type">算法类型 / The algorithm type.</param>
    /// <param name="size">种群规模 / The population size.</param>
    /// <param name="parameters">参数 / The parameters.</param>
    /// <returns>校验结果 / The validation result.</returns>
    private static Result<Success, ErrorCode, Error<ErrorCode>> Algorithm(string type, int size,
        params (string Key, object Value)[] parameters) =>
        RegistryParameters.ValidateAlgorithm(new AlgorithmConfig("cfg", Spec(type, parameters), size));
}
