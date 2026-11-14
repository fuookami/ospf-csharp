// 注册表的参数校验 / The registry's parameter validation.
// 与固定 Kotlin 基线 `configuration/SearchRegistry.kt`（43259 B）的 `emptyParameters` / `numericParameters` /
// `validateAlgorithm` 三个纯校验对应。
// Mirrors the three pure validators `emptyParameters`, `numericParameters` and `validateAlgorithm` from the pinned baseline's
// `configuration/SearchRegistry.kt` (43259 B).

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver.Heuristic.Configuration;

/// <summary>
/// 注册表的参数校验 / The registry's parameter validation.
/// </summary>
/// <remarks>
/// **这一组校验的全部意义是"让写错的配置变不出去"**。配置的最大危险不是报错，而是**悄悄生效一半**：
/// 键名打错一个字母、数值写成字符串、概率写成 2.0——这些都不会让算法失败，
/// **只会让它按省略后的默认值跑，而配置文件看起来完全正常**。
///
/// **The whole point of this group is "make a mistyped configuration fail loudly".** The real danger of configuration is not an error but
/// **taking effect halfway**: a key with one letter wrong, a number written as a string, a probability written as 2.0 — none of these make the
/// algorithm fail, **they merely make it run on the omitted defaults while the configuration file looks perfectly normal**.
///
/// 三条纪律：
/// 1. **未知键一律拒绝**（不是忽略）。忽略未知键等于让拼写错误永远沉默。
/// 2. **数值必须是有限数**。`NaN` 与 `Infinity` 能通过 JSON 解析，却会让后续每一次比较都得出"不更好"。
/// 3. **范围与规模必须在这里判**。把 `mutationRate = 2.0` 留到运行期，它会安静地把每一步都变成"必然变异"。
///
/// Three rules:
/// 1. **Unknown keys are rejected, not ignored.** Ignoring them keeps every typo silent forever.
/// 2. **Numbers must be finite.** `NaN` and `Infinity` parse fine from JSON and then make every comparison say "not better".
/// 3. **Ranges and sizes are decided here.** A `mutationRate` of 2.0 left to run time quietly turns every step into "always mutate".
/// </remarks>
public static class RegistryParameters {
    /// <summary>
    /// 必须为正整数的键 / The keys that must be positive integers.
    /// </summary>
    private static readonly HashSet<string> PositiveIntegerKeys = new(StringComparer.Ordinal) {
        "states", "stagnationLimit", "amount", "period", "limit", "window", "length", "horizon",
        "sweeps", "maxTrials",
    };

    /// <summary>
    /// 必须为正数的键 / The keys that must be positive numbers.
    /// </summary>
    private static readonly HashSet<string> PositiveNumberKeys = new(StringComparer.Ordinal) {
        "sigma", "eta", "ratio",
    };

    /// <summary>
    /// 表格式状态的上限 / The tabular-state ceiling.
    /// </summary>
    private const int MaximumStates = 4096;

    /// <summary>
    /// 无参数组件：任何参数都算错 / A parameterless component: any parameter is an error.
    /// </summary>
    /// <param name="spec">组件规格 / The component spec.</param>
    /// <returns>校验结果 / The validation result.</returns>
    public static Result<Success, ErrorCode, Error<ErrorCode>> Empty(ComponentSpec spec) =>
        Numeric(spec, EmptyAllowed);

    private static readonly HashSet<string> EmptyAllowed = new(StringComparer.Ordinal);

    /// <summary>
    /// 校验一组数值参数 / Validate a set of numeric parameters.
    /// </summary>
    /// <param name="spec">组件规格 / The component spec.</param>
    /// <param name="allowed">允许的键 / The allowed keys.</param>
    /// <returns>校验结果 / The validation result.</returns>
    public static Result<Success, ErrorCode, Error<ErrorCode>> Numeric(
        ComponentSpec spec, IReadOnlySet<string> allowed) {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(allowed);
        if (spec.Version != 1) {
            return Invalid(spec.Type);
        }
        JsonObject? parameters = spec.Parameters;
        if (parameters is not null) {
            foreach ((string key, JsonNode? value) in parameters) {
                if (!allowed.Contains(key)) {
                    return Invalid(spec.Type);
                }
                if (!TryGetFiniteDouble(value, out double number)) {
                    return Invalid(spec.Type);
                }
                if (PositiveIntegerKeys.Contains(key)) {
                    if (!TryGetInt32(value, out int integer) || integer <= 0) {
                        return Invalid(spec.Type);
                    }
                }
                if (PositiveNumberKeys.Contains(key) && number <= 0.0) {
                    return Invalid(spec.Type);
                }
            }
            if (Integer(parameters, "states", 64L) > MaximumStates) {
                return Failed("too many tabular states");
            }
        }
        return Results.Ok(new Success());
    }

    /// <summary>
    /// 校验算法级参数 / Validate the algorithm-level parameters.
    /// </summary>
    /// <param name="config">算法配置 / The algorithm configuration.</param>
    /// <returns>校验结果 / The validation result.</returns>
    public static Result<Success, ErrorCode, Error<ErrorCode>> ValidateAlgorithm(
        AlgorithmConfig config) {
        ArgumentNullException.ThrowIfNull(config);
        string type = config.Algorithm.Type;
        IReadOnlySet<string>? allowed = AllowedAlgorithmKeys(type);
        if (allowed is null) {
            return Results.Ok(new Success());
        }
        JsonObject? parameters = config.Algorithm.Parameters;
        if (parameters is not null) {
            foreach ((string key, JsonNode? value) in parameters) {
                if (!allowed.Contains(key)) {
                    return InvalidAlgorithm(type);
                }
                if (key == "numerics") {
                    string protocol = type == "cma-es" ? "cma-polar-fdlibm-v1" : "shade-fdlibm-v1";
                    if (!TryGetString(value, out string text)
                        || (text != "native" && text != protocol)) {
                        return InvalidAlgorithm(type);
                    }
                    if (text == protocol) {
                        // **fdlibm 协议明确拒绝，不静默按原生数值跑。** 那一支要的是 Java `StrictMath`（fdlibm）的逐位结果——
                        // 历史轨迹可复现的前提就是逐位一致，换成 .NET 的 `Math.tan` 会把"同一份配置、同一段轨迹"拆成两份数据。
                        // **The fdlibm protocol is refused rather than silently run with native numerics.** That branch wants Java `StrictMath` (fdlibm) bit-exact
                        // results: reproducing a historical trajectory *means* bit-exact, and swapping in .NET's `Math.tan` splits "one config, one trajectory" into two
                        // datasets.
                        return Failed(
                            "numerics protocol " + text + " needs Java StrictMath (fdlibm) bit-exact results, which .NET cannot reproduce; use \"native\""
                            + " / 数值协议 " + text + " 需要 Java StrictMath（fdlibm）的逐位结果，.NET 无法复现，请用 \"native\"");
                    }
                    continue;
                }
                if (key == "restartOnImport") {
                    if (!TryGetBoolean(value, out _)) {
                        return InvalidAlgorithm(type);
                    }
                    continue;
                }
                if (!TryGetFiniteDouble(value, out _)) {
                    return InvalidAlgorithm(type);
                }
            }
        }
        // 整数参数必须真的能读出正整数；范围与规模在下面各自判。
        // Integer parameters must really read back as positive integers; ranges and sizes follow below.
        foreach ((string key, JsonNode? value) in parameters ?? new JsonObject()) {
            if (PositiveAlgorithmIntegers.Contains(key)
                && (!TryGetInt32(value, out int integer) || integer <= 0)) {
                return InvalidAlgorithm(type);
            }
        }
        double mutationRate = Number(parameters, "mutationRate", 0.1);
        double pBestRate = Number(parameters, "pBestRate", 0.2);
        double initialSigma = Number(parameters, "initialSigma", 0.3);
        double maxVelocity = Number(parameters, "maxVelocity", 1.0);
        if (mutationRate < 0.0 || mutationRate > 1.0
            || pBestRate < 0.0 || pBestRate > 1.0
            || initialSigma <= 0.0
            || maxVelocity <= 0.0) {
            return InvalidAlgorithm(type);
        }
        if (type is "shade" or "l-shade" or "de" && config.Size < 4) {
            return Failed(type + " requires at least four individuals");
        }
        if (type is "shade" or "l-shade") {
            int minimumSize = (int)Integer(parameters, "minimumSize", 4L);
            if (minimumSize > config.Size || minimumSize < 4) {
                return Failed("invalid minimum SHADE population");
            }
        }
        if (type == "clpso" && config.Size < 2) {
            return Failed("CLPSO requires at least two particles");
        }
        return Results.Ok(new Success());
    }

    /// <summary>算法级整数参数键 / The algorithm-level integer parameter keys.</summary>
    private static readonly HashSet<string> PositiveAlgorithmIntegers = new(StringComparer.Ordinal) {
        "memorySize", "minimumSize", "refreshInterval", "stagnationLimit", "divisions",
        "sizeReductionSteps", "migrationPeriod", "phaseSteps",
    };

    /// <summary>
    /// 每种算法允许的键；未知算法返回 null 表示不校验 / The allowed keys per algorithm; null means "not validated".
    /// </summary>
    /// <param name="type">算法类型 / The algorithm type.</param>
    /// <returns>允许的键 / The allowed keys.</returns>
    private static IReadOnlySet<string>? AllowedAlgorithmKeys(string type) {
        if (type is "pso" or "apso") {
            return Set("maxVelocity");
        }
        if (type == "clpso") {
            return Set("maxVelocity", "learningWeight", "refreshInterval", "stagnationLimit");
        }
        if (type == "shade") {
            return Set("memorySize", "pBestRate", "minimumSize", "numerics");
        }
        if (type == "l-shade") {
            return Set("memorySize", "pBestRate", "minimumSize", "sizeReductionSteps", "numerics");
        }
        if (type == "cma-es") {
            return Set("initialSigma", "restartOnImport", "numerics");
        }
        if (type is "nsga-ii" or "nsga-iii") {
            return Set("mutationRate", "divisions");
        }
        if (type is "island" or "island-ga") {
            return Set("migrationPeriod");
        }
        if (type == "memetic") {
            return Set("phaseSteps");
        }
        if (type is "hill-climbing" or "ns" or "random-walk" or "simulated-annealing") {
            return Set();
        }
        foreach (string update in BuiltInAlgorithms.PopulationUpdateTypes) {
            if (update == type) {
                return Set("mutationRate");
            }
        }
        return null;
    }

    /// <summary>构造键集合 / Build a key set.</summary>
    /// <param name="keys">键 / The keys.</param>
    /// <returns>键集合 / The key set.</returns>
    private static IReadOnlySet<string> Set(params string[] keys) =>
        new HashSet<string>(keys, StringComparer.Ordinal);

    /// <summary>读一个数值，缺省时返回默认值 / Read a number, falling back to the default.</summary>
    /// <param name="parameters">参数 / The parameters.</param>
    /// <param name="key">键 / The key.</param>
    /// <param name="fallback">默认值 / The fallback.</param>
    /// <returns>数值 / The number.</returns>
    private static double Number(JsonObject? parameters, string key, double fallback) =>
        parameters is not null && parameters.TryGetPropertyValue(key, out JsonNode? node)
            && TryGetFiniteDouble(node, out double value)
                ? value
                : fallback;

    /// <summary>读一个整数，缺省时返回默认值 / Read an integer, falling back to the default.</summary>
    /// <param name="parameters">参数 / The parameters.</param>
    /// <param name="key">键 / The key.</param>
    /// <param name="fallback">默认值 / The fallback.</param>
    /// <returns>整数 / The integer.</returns>
    private static long Integer(JsonObject? parameters, string key, long fallback) =>
        parameters is not null && parameters.TryGetPropertyValue(key, out JsonNode? node)
            && TryGetInt32(node, out int value)
                ? value
                : fallback;

    /// <summary>读有限数值 / Read a finite number.</summary>
    /// <remarks>
    /// **不能直接用 `JsonValue.TryGetValue&lt;double&gt;`**：它要求节点里存的就是 `double`，
    /// 而配置里的 `3` 常常是整数——于是"合法配置"被判成非法。这里先转成 `JsonElement` 再读，
    /// **让整数与小数字面量都算数，字符串一律不算**（与基线的 `doubleOrNull` 一致）。
    /// **`JsonValue.TryGetValue&lt;double&gt;` cannot be used directly**: it demands a node that literally stores a `double`, while a configuration
    /// often writes `3` as an integer — so a valid configuration reads as invalid. Converting to `JsonElement` first **accepts both integer and
    /// fractional literals and still rejects strings**, matching the baseline's `doubleOrNull`.
    /// </remarks>
    /// <param name="node">JSON 节点 / The JSON node.</param>
    /// <param name="value">数值 / The number.</param>
    /// <returns>是否成功 / Whether it succeeded.</returns>
    private static bool TryGetFiniteDouble(JsonNode? node, out double value) {
        value = 0.0;
        if (node is null) {
            return false;
        }
        using var document = JsonDocument.Parse(node.ToJsonString());
        if (document.RootElement.ValueKind != JsonValueKind.Number
            || !document.RootElement.TryGetDouble(out double parsed)
            || !double.IsFinite(parsed)) {
            return false;
        }
        value = parsed;
        return true;
    }

    /// <summary>读整数 / Read an integer.</summary>
    /// <param name="node">JSON 节点 / The JSON node.</param>
    /// <param name="value">整数 / The integer.</param>
    /// <returns>是否成功 / Whether it succeeded.</returns>
    private static bool TryGetInt32(JsonNode? node, out int value) {
        value = 0;
        if (node is null) {
            return false;
        }
        using var document = JsonDocument.Parse(node.ToJsonString());
        return document.RootElement.ValueKind == JsonValueKind.Number
            && document.RootElement.TryGetInt32(out value);
    }

    /// <summary>读字符串 / Read a string.</summary>
    /// <param name="node">JSON 节点 / The JSON node.</param>
    /// <param name="value">字符串 / The string.</param>
    /// <returns>是否成功 / Whether it succeeded.</returns>
    private static bool TryGetString(JsonNode? node, out string value) {
        value = string.Empty;
        if (node is null) {
            return false;
        }
        using var document = JsonDocument.Parse(node.ToJsonString());
        if (document.RootElement.ValueKind != JsonValueKind.String) {
            return false;
        }
        value = document.RootElement.GetString() ?? string.Empty;
        return true;
    }

    /// <summary>读布尔 / Read a boolean.</summary>
    /// <param name="node">JSON 节点 / The JSON node.</param>
    /// <param name="value">布尔 / The boolean.</param>
    /// <returns>是否成功 / Whether it succeeded.</returns>
    private static bool TryGetBoolean(JsonNode? node, out bool value) {
        value = false;
        if (node is null) {
            return false;
        }
        using var document = JsonDocument.Parse(node.ToJsonString());
        if (document.RootElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) {
            return false;
        }
        value = document.RootElement.GetBoolean();
        return true;
    }

    /// <summary>参数无效 / Invalid parameters.</summary>
    /// <param name="type">组件类型 / The component type.</param>
    /// <returns>失败结果 / The failure.</returns>
    private static Result<Success, ErrorCode, Error<ErrorCode>> Invalid(string type) =>
        Failed("invalid parameters for " + type);

    /// <summary>算法参数无效 / Invalid algorithm parameters.</summary>
    /// <param name="type">算法类型 / The algorithm type.</param>
    /// <returns>失败结果 / The failure.</returns>
    private static Result<Success, ErrorCode, Error<ErrorCode>> InvalidAlgorithm(string type) =>
        Failed("invalid algorithm parameters: " + type);

    /// <summary>构造失败结果 / Build a failure.</summary>
    /// <param name="message">消息 / The message.</param>
    /// <returns>失败结果 / The failure.</returns>
    private static Result<Success, ErrorCode, Error<ErrorCode>> Failed(string message) =>
        Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.IllegalArgument, message));
}
