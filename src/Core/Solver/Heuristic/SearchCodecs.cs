// 无损 JSON 编码器组合 / Lossless JSON codec combinators.
// 与固定 Kotlin 基线 `core/solver/heuristic/SearchCodecs.kt`（5855 字节）对应。
// Mirrors the pinned baseline's `core/solver/heuristic/SearchCodecs.kt`.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver.Heuristic;

/// <summary>
/// 结果串联与失败构造 / Result chaining and failure construction.
/// </summary>
public static class SearchCodecSupport {
    /// <summary>
    /// 成功时继续，失败时短路 / Continue on success, short-circuit on failure.
    /// </summary>
    /// <typeparam name="T">输入类型 / The input type.</typeparam>
    /// <typeparam name="R">输出类型 / The output type.</typeparam>
    /// <param name="result">上游结果 / The upstream result.</param>
    /// <param name="block">成功时的下一步 / The next step on success.</param>
    /// <returns>串联结果 / The chained result.</returns>
    public static Result<R, ErrorCode, Error<ErrorCode>> Then<T, R>(
        this Result<T, ErrorCode, Error<ErrorCode>> result,
        Func<T, Result<R, ErrorCode, Error<ErrorCode>>> block) {
        ArgumentNullException.ThrowIfNull(block);
        if (result is Ok<T, ErrorCode, Error<ErrorCode>> ok) {
            return block(ok.Value);
        }
        return Results.Failed<R>(((Failed<T, ErrorCode, Error<ErrorCode>>)result).Error);
    }

    /// <summary>
    /// 构造"配置或状态无效"失败 / Build an "invalid configuration or state" failure.
    /// </summary>
    /// <typeparam name="T">结果类型 / The result type.</typeparam>
    /// <param name="message">失败细节 / The failure detail.</param>
    /// <returns>失败结果 / The failure result.</returns>
    public static Result<T, ErrorCode, Error<ErrorCode>> SearchInvalid<T>(string message) =>
        Results.Failed<T>(new Err<ErrorCode>(
            ErrorCode.IllegalArgument,
            $"搜索配置或状态无效 / Invalid search configuration or state: {message}"));
}

/// <summary>
/// 无损 JSON 编码器组合 / Lossless JSON codec combinators.
/// </summary>
/// <remarks>
/// **`Flt64` 编码器拒绝非有限值**。`NaN` 与 `±Infinity` 能通过 `ToString` 写出文本，但回到解析端时得到的是"看起来像数字"的东西或者
/// 直接抛错；更糟的是它们会一路毒化目标比较（`NaN` 与任何值比较都为假），让档案悄悄变得无序。
/// **The `Flt64` codec rejects non-finite values.** `NaN` and `±Infinity` stringify to text fine, but parsing them back yields
/// something that merely looks like a number or throws outright. Worse, they poison objective comparison — `NaN` compares false
/// against everything — silently leaving the archive unordered.
/// **列表编码遇到首个失败立即整体失败**。返回部分结果会让调用方拿到一个"短了一截"的集合，而没有任何标记说明它不完整。
/// **The list codec fails as a whole on the first element failure.** Returning partial results would hand the caller a truncated
/// collection with no marker that it is incomplete.
/// **个体解码必须重新校验维度与目标**。只校验 JSON 形状会让一个"形状正确但维度不符"的状态恢复成功，随后在搜索里以越界或错位的形式暴露。
/// **Individual decoding must revalidate dimensions and the objective.** Checking only the JSON shape would let a correctly shaped
/// but dimensionally wrong state restore successfully, then fail later inside the search as an index error or a silent mix-up.
/// </remarks>
public static class SearchCodecs {
    /// <summary>有限 `Flt64` 编码器 / The finite `Flt64` codec.</summary>
    public static ISearchCodec<Flt64> Flt64 { get; } = new FiniteFlt64Codec();

    /// <summary>
    /// 由序列化选项创建编码器 / Create a codec from serialization options.
    /// </summary>
    /// <typeparam name="T">值类型 / The value type.</typeparam>
    /// <param name="options">序列化选项 / The serialization options.</param>
    /// <returns>编码器 / The codec.</returns>
    public static ISearchCodec<T> Serialized<T>(JsonSerializerOptions? options = null) =>
        new SerializedCodec<T>(options ?? new JsonSerializerOptions());

    /// <summary>
    /// 列表编码器 / The list codec.
    /// </summary>
    /// <typeparam name="T">元素类型 / The element type.</typeparam>
    /// <param name="element">元素编码 / The element codec.</param>
    /// <returns>列表编码 / The list codec.</returns>
    public static ISearchCodec<List<T>> List<T>(ISearchCodec<T> element) => new ListCodec<T>(element);

    /// <summary>
    /// 可空值编码器 / The nullable codec.
    /// </summary>
    /// <typeparam name="T">元素类型 / The element type.</typeparam>
    /// <param name="element">元素编码 / The element codec.</param>
    /// <returns>可空编码 / The nullable codec.</returns>
    public static ISearchCodec<T?> Nullable<T>(ISearchCodec<T> element) where T : class =>
        new NullableCodec<T>(element);

    /// <summary>
    /// 个体编码器，业务数值不经 `double` 中转 / The individual codec, with values never routed through `double`.
    /// </summary>
    /// <typeparam name="O">目标类型 / The objective type.</typeparam>
    /// <typeparam name="V">解值类型 / The solution-value type.</typeparam>
    /// <param name="problem">问题编码定义 / The problem codec definition.</param>
    /// <returns>个体编码 / The individual codec.</returns>
    public static ISearchCodec<IIndividual<O, V>> Individual<O, V>(SearchProblem<O, V> problem)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        new IndividualCodec<O, V>(problem);

    /// <summary>
    /// 有限 `Flt64` 编码 / The finite `Flt64` codec.
    /// </summary>
    private sealed class FiniteFlt64Codec : ISearchCodec<Flt64> {
        /// <inheritdoc/>
        public Result<JsonElement, ErrorCode, Error<ErrorCode>> Encode(Flt64 value) =>
            double.IsFinite(value.ToDouble())
                ? Results.Ok(JsonSerializer.SerializeToElement(value.ToDouble()))
                : SearchCodecSupport.SearchInvalid<JsonElement>("non-finite value");

        /// <inheritdoc/>
        public Result<Flt64, ErrorCode, Error<ErrorCode>> Decode(JsonElement value) {
            double number;
            if (value.ValueKind == JsonValueKind.Number) {
                number = value.GetDouble();
            } else if (value.ValueKind == JsonValueKind.String
                       && double.TryParse(value.GetString(), out double parsed)) {
                number = parsed;
            } else {
                return SearchCodecSupport.SearchInvalid<Flt64>("invalid Flt64");
            }

            return double.IsFinite(number)
                ? Results.Ok(new Flt64(number))
                : SearchCodecSupport.SearchInvalid<Flt64>("invalid Flt64");
        }
    }

    /// <summary>
    /// 基于序列化协议的编码 / The serialization-based codec.
    /// </summary>
    /// <typeparam name="T">值类型 / The value type.</typeparam>
    private sealed class SerializedCodec<T> : ISearchCodec<T> {
        private readonly JsonSerializerOptions _options;

        public SerializedCodec(JsonSerializerOptions options) => _options = options;

        /// <inheritdoc/>
        public Result<JsonElement, ErrorCode, Error<ErrorCode>> Encode(T value) =>
            SearchBoundary.Execute(() => JsonSerializer.SerializeToElement(value, _options));

        /// <inheritdoc/>
        public Result<T, ErrorCode, Error<ErrorCode>> Decode(JsonElement value) =>
            SearchBoundary.Execute(() => JsonSerializer.Deserialize<T>(value.GetRawText(), _options)!);
    }

    /// <summary>
    /// 列表编码 / The list codec.
    /// </summary>
    /// <typeparam name="T">元素类型 / The element type.</typeparam>
    private sealed class ListCodec<T> : ISearchCodec<List<T>> {
        private readonly ISearchCodec<T> _element;

        public ListCodec(ISearchCodec<T> element) {
            ArgumentNullException.ThrowIfNull(element);
            _element = element;
        }

        /// <inheritdoc/>
        public Result<JsonElement, ErrorCode, Error<ErrorCode>> Encode(List<T> value) {
            ArgumentNullException.ThrowIfNull(value);
            var encoded = new List<JsonElement>(value.Count);
            foreach (T item in value) {
                Result<JsonElement, ErrorCode, Error<ErrorCode>> one = _element.Encode(item);
                if (!one.IsOk) {
                    return Results.Failed<JsonElement>(((Failed<JsonElement, ErrorCode, Error<ErrorCode>>)one).Error);
                }
                encoded.Add(((Ok<JsonElement, ErrorCode, Error<ErrorCode>>)one).Value);
            }
            return Results.Ok(JsonSerializer.SerializeToElement(encoded));
        }

        /// <inheritdoc/>
        public Result<List<T>, ErrorCode, Error<ErrorCode>> Decode(JsonElement value) {
            if (value.ValueKind != JsonValueKind.Array) {
                return SearchCodecSupport.SearchInvalid<List<T>>("expected array");
            }

            var decoded = new List<T>(value.GetArrayLength());
            foreach (JsonElement item in value.EnumerateArray()) {
                Result<T, ErrorCode, Error<ErrorCode>> one = _element.Decode(item);
                if (!one.IsOk) {
                    return Results.Failed<List<T>>(((Failed<T, ErrorCode, Error<ErrorCode>>)one).Error);
                }
                decoded.Add(((Ok<T, ErrorCode, Error<ErrorCode>>)one).Value);
            }
            return Results.Ok(decoded);
        }
    }

    /// <summary>
    /// 可空值编码 / The nullable codec.
    /// </summary>
    /// <typeparam name="T">元素类型 / The element type.</typeparam>
    private sealed class NullableCodec<T> : ISearchCodec<T?> where T : class {
        private readonly ISearchCodec<T> _element;

        public NullableCodec(ISearchCodec<T> element) {
            ArgumentNullException.ThrowIfNull(element);
            _element = element;
        }

        /// <inheritdoc/>
        public Result<JsonElement, ErrorCode, Error<ErrorCode>> Encode(T? value) =>
            value is null ? Results.Ok(JsonSerializer.SerializeToElement<object?>(null)) : _element.Encode(value);

        /// <inheritdoc/>
        public Result<T?, ErrorCode, Error<ErrorCode>> Decode(JsonElement value) {
            if (value.ValueKind == JsonValueKind.Null) {
                return Results.Ok<T?>(null);
            }
            Result<T, ErrorCode, Error<ErrorCode>> decoded = _element.Decode(value);
            return decoded.IsOk
                ? Results.Ok<T?>(((Ok<T, ErrorCode, Error<ErrorCode>>)decoded).Value)
                : Results.Failed<T?>(((Failed<T, ErrorCode, Error<ErrorCode>>)decoded).Error);
        }
    }
}

/// <summary>
/// 个体编码 / The individual codec.
/// </summary>
/// <typeparam name="O">目标类型 / The objective type.</typeparam>
/// <typeparam name="V">解值类型 / The solution-value type.</typeparam>
/// <remarks>
/// **业务数值不经 `double` 中转**。解值与目标都走各自的编解码器，避免"精确整数目标被 `double` 截断"这类只在特定取值上出现的偏差。
/// **Values are never routed through `double`.** Both the solution values and the objective go through their own codecs, avoiding
/// deviations such as an exact integer objective truncated by `double` that only appear at particular values.
/// </remarks>
internal sealed class IndividualCodec<O, V> : ISearchCodec<IIndividual<O, V>>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly SearchProblem<O, V> _problem;
    private readonly ISearchCodec<List<V>> _values;

    /// <summary>
    /// 构造个体编码 / Construct the individual codec.
    /// </summary>
    /// <param name="problem">问题编码定义 / The problem codec definition.</param>
    public IndividualCodec(SearchProblem<O, V> problem) {
        ArgumentNullException.ThrowIfNull(problem);
        _problem = problem;
        _values = SearchCodecs.List(problem.ValueCodec);
    }

    /// <inheritdoc/>
    public Result<JsonElement, ErrorCode, Error<ErrorCode>> Encode(IIndividual<O, V> value) {
        ArgumentNullException.ThrowIfNull(value);
        Result<JsonElement, ErrorCode, Error<ErrorCode>> solution = _values.Encode(value.Solution.ToList());
        if (!solution.IsOk) {
            return Results.Failed<JsonElement>(((Failed<JsonElement, ErrorCode, Error<ErrorCode>>)solution).Error);
        }
        Result<JsonElement, ErrorCode, Error<ErrorCode>> objective = _problem.ObjectiveCodec.Encode(value.Objective);
        if (!objective.IsOk) {
            return Results.Failed<JsonElement>(((Failed<JsonElement, ErrorCode, Error<ErrorCode>>)objective).Error);
        }

        var record = new Dictionary<string, JsonElement> {
            ["solution"] = ((Ok<JsonElement, ErrorCode, Error<ErrorCode>>)solution).Value,
            ["fitness"] = ((Ok<JsonElement, ErrorCode, Error<ErrorCode>>)objective).Value,
        };
        return Results.Ok(JsonSerializer.SerializeToElement(record));
    }

    /// <inheritdoc/>
    public Result<IIndividual<O, V>, ErrorCode, Error<ErrorCode>> Decode(JsonElement value) {
        if (value.ValueKind != JsonValueKind.Object) {
            return SearchCodecSupport.SearchInvalid<IIndividual<O, V>>("expected individual");
        }
        if (!value.TryGetProperty("solution", out JsonElement solutionElement)) {
            return SearchCodecSupport.SearchInvalid<IIndividual<O, V>>("missing solution");
        }
        if (!value.TryGetProperty("fitness", out JsonElement objectiveElement)) {
            return SearchCodecSupport.SearchInvalid<IIndividual<O, V>>("missing fitness");
        }

        Result<List<V>, ErrorCode, Error<ErrorCode>> solution = _values.Decode(solutionElement);
        if (!solution.IsOk) {
            return Results.Failed<IIndividual<O, V>>(((Failed<List<V>, ErrorCode, Error<ErrorCode>>)solution).Error);
        }
        Result<O, ErrorCode, Error<ErrorCode>> objective = _problem.ObjectiveCodec.Decode(objectiveElement);
        if (!objective.IsOk) {
            return Results.Failed<IIndividual<O, V>>(((Failed<O, ErrorCode, Error<ErrorCode>>)objective).Error);
        }

        List<V> decoded = ((Ok<List<V>, ErrorCode, Error<ErrorCode>>)solution).Value;
        O obj = ((Ok<O, ErrorCode, Error<ErrorCode>>)objective).Value;
        if (!_problem.ValidSolution(decoded) || !_problem.Valid(obj)) {
            return SearchCodecSupport.SearchInvalid<IIndividual<O, V>>("invalid individual dimensions or objective");
        }
        return Results.Ok<IIndividual<O, V>>(new EvaluatedIndividual<O, V>(decoded, obj));
    }
}
