// 算子池与绑定测试 / Tests for operator pools and bindings.

using System;
using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator.Mutation;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 算子池 / The operator pools.
/// </summary>
public sealed class OperatorPoolTests {
    /// <summary>**未 `Current()` 时不产生转移** / No transition appears before `Current()` is called.</summary>
    /// <remarks>
    /// 否则"选了但没用"的决策会被记成一次真实转移，策略会从一次并未发生的执行里学习。
    /// Otherwise a "selected but never used" decision is recorded as a real transition and the policy learns from an execution that never
    /// happened.
    /// </remarks>
    [Fact]
    public void Observe_ShouldNotRecordBeforeCurrent() {
        OperatorPool<string> pool = BuildPool(new[] { "a", "b" });
        pool.Choose(new SelectionContext()).Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();

        pool.Observe(new SelectionContext(), new Flt64(1.0), 1L, false, 0L, 1L)
            .Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        pool.DrainTransitions().Should().BeEmpty(
            "未使用过的选择不得记录转移 / an unused selection must not record a transition");
    }

    /// <summary>**`Current()` 之后才记录转移** / A transition appears after `Current()`.</summary>
    [Fact]
    public void Observe_ShouldRecordAfterCurrent() {
        OperatorPool<string> pool = BuildPool(new[] { "a", "b" });
        pool.Choose(new SelectionContext()).Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        pool.Current().Should().NotBeNull();
        pool.UsedAction.Should().NotBeNull();

        pool.Observe(new SelectionContext(), new Flt64(1.0), 1L, false, 0L, 1L)
            .Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        pool.DrainTransitions().Should().ContainSingle();
        pool.DrainTransitions().Should().BeEmpty("转移被取走后应清空 / drained transitions are cleared");
    }

    /// <summary>**无状态候选不参与保存** / Stateless candidates are not saved.</summary>
    [Fact]
    public void Save_ShouldSkipStatelessCandidates() {
        // **两个候选都必须实现 `IRecoverableComponent`**，否则"跳过无状态"与"不跳过"都会因"不可恢复"而失败，
        // 无法区分。第一版让两个候选都是 `string`，变异测试显示"删掉跳过逻辑"仍然通过。
        // **Both candidates must implement `IRecoverableComponent`**, otherwise "skip stateless" and "do not skip" both fail on
        // "not recoverable" and nothing is distinguished. The first version used plain strings and the mutation test reported a pass.
        var stateful = new StatefulOperator(1L);
        var statelessCandidate = new StatefulOperator(2L);
        var candidates = new Dictionary<string, StatefulOperator>(StringComparer.Ordinal) {
            ["stateful"] = stateful,
            ["stateless"] = statelessCandidate,
        };
        var policy = new TabularSelectionPolicy("fixed", new[] { "stateful", "stateless" });
        var pool = new OperatorPool<StatefulOperator>("pool", candidates, policy, new SearchRandom(1),
            new[] { "stateless" });

        Result<SlotSnapshot, ErrorCode, Error<ErrorCode>> saved = pool.Save();
        saved.Should().BeOfType<Ok<SlotSnapshot, ErrorCode, Error<ErrorCode>>>();
        SlotSnapshot snapshot = ((Ok<SlotSnapshot, ErrorCode, Error<ErrorCode>>)saved).Value;
        snapshot.Components.Should().ContainKey("stateful");
        snapshot.Components.Should().NotContainKey("stateless",
            "无状态候选不得进入快照 / a stateless candidate must not enter the snapshot");
    }

    /// <summary>**可恢复组件被保存并恢复** / Recoverable components are saved and restored.</summary>
    [Fact]
    public void SaveLoad_ShouldRoundTripComponentState() {
        var stateful = new StatefulOperator(3L);
        var candidates = new Dictionary<string, StatefulOperator>(StringComparer.Ordinal) {
            ["x"] = stateful,
        };
        var policy = new TabularSelectionPolicy("fixed", new[] { "x" });
        var pool = new OperatorPool<StatefulOperator>("pool", candidates, policy, new SearchRandom(7),
            Array.Empty<string>());

        Result<SlotSnapshot, ErrorCode, Error<ErrorCode>> saved = pool.Save();
        saved.Should().BeOfType<Ok<SlotSnapshot, ErrorCode, Error<ErrorCode>>>();
        SlotSnapshot snapshot = ((Ok<SlotSnapshot, ErrorCode, Error<ErrorCode>>)saved).Value;
        snapshot.Random.Should().Be(7L);

        stateful.Value = 99L;
        pool.Load(snapshot).Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        stateful.Value.Should().Be(3L, "组件状态必须被恢复 / component state must be restored");
    }

    /// <summary>**组件集合不匹配时拒绝加载，且不改动任何组件** / A mismatched component set is rejected without touching anything.</summary>
    /// <remarks>
    /// 先加载再校验会让一次失败的恢复把部分算子改到新状态、其余留在旧状态，而调用方只拿到一个失败结果。
    /// Loading first would leave a failed restore with some operators in the new state and the rest in the old one while the caller only
    /// sees a failure.
    /// </remarks>
    [Fact]
    public void Load_ShouldRejectMismatchedComponents() {
        var stateful = new StatefulOperator(3L);
        var candidates = new Dictionary<string, StatefulOperator>(StringComparer.Ordinal) {
            ["x"] = stateful,
        };
        var policy = new TabularSelectionPolicy("fixed", new[] { "x" });
        var pool = new OperatorPool<StatefulOperator>("pool", candidates, policy, new SearchRandom(7),
            Array.Empty<string>());

        Result<SlotSnapshot, ErrorCode, Error<ErrorCode>> saved = pool.Save();
        SlotSnapshot snapshot = ((Ok<SlotSnapshot, ErrorCode, Error<ErrorCode>>)saved).Value;
        stateful.Value = 99L;

        SlotSnapshot tampered = snapshot with {
            Components = new Dictionary<string, JsonElement>(StringComparer.Ordinal) {
                ["y"] = snapshot.Components["x"],
            },
        };
        pool.Load(tampered).Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        stateful.Value.Should().Be(99L,
            "校验失败不得改动组件 / a validation failure must not touch components");
    }

    /// <summary>**恢复随机流** / The random stream is restored.</summary>
    [Fact]
    public void Load_ShouldRestoreRandomStream() {
        var candidates = new Dictionary<string, StatefulOperator>(StringComparer.Ordinal) {
            ["x"] = new StatefulOperator(0L),
        };
        var policy = new TabularSelectionPolicy("fixed", new[] { "x" });
        var random = new SearchRandom(7);
        var pool = new OperatorPool<StatefulOperator>("pool", candidates, policy, random,
            Array.Empty<string>());

        Result<SlotSnapshot, ErrorCode, Error<ErrorCode>> saved = pool.Save();
        SlotSnapshot snapshot = ((Ok<SlotSnapshot, ErrorCode, Error<ErrorCode>>)saved).Value;
        random.NextDouble();
        random.State.Should().NotBe(7L);

        pool.Load(snapshot).Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        random.State.Should().Be(7L);
    }

    /// <summary>**空候选集在构造时即失败** / An empty candidate set fails at construction.</summary>
    [Fact]
    public void Constructor_ShouldRejectEmptyCandidates() {
        var policy = new TabularSelectionPolicy("fixed", new[] { "x" });

        Action act = () => new OperatorPool<string>(
            "pool", new Dictionary<string, string>(), policy, new SearchRandom(1), Array.Empty<string>());

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>构造算子池 / Build an operator pool.</summary>
    /// <param name="actions">动作名 / The action names.</param>
    /// <returns>算子池 / The operator pool.</returns>
    private static OperatorPool<string> BuildPool(string[] actions) {
        var candidates = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string action in actions) {
            candidates[action] = action;
        }
        return new OperatorPool<string>("pool", candidates,
            new TabularSelectionPolicy("fixed", actions), new SearchRandom(1), Array.Empty<string>());
    }

    /// <summary>可保存状态的算子 / An operator with saveable state.</summary>
    private sealed class StatefulOperator : IRecoverableComponent {
        /// <summary>
        /// 构造算子 / Construct the operator.
        /// </summary>
        /// <param name="value">初始值 / The initial value.</param>
        public StatefulOperator(long value) => Value = value;

        /// <summary>当前值 / The current value.</summary>
        public long Value { get; set; }

        /// <inheritdoc/>
        public Result<JsonElement, ErrorCode, Error<ErrorCode>> Save() =>
            Results.Ok(JsonDocument.Parse($"{{\"value\":{Value}}}").RootElement.Clone());

        /// <inheritdoc/>
        public Result<Success, ErrorCode, Error<ErrorCode>> Load(JsonElement state) {
            Value = state.GetProperty("value").GetInt64();
            return Results.Ok(new Success());
        }
    }
}

/// <summary>
/// 算子绑定 / The operator bindings.
/// </summary>
public sealed class OperatorBindingsTests {
    /// <summary>**参数池顺序按名称排序，与字典插入顺序无关** / Parameter pools are ordered by name, not insertion order.</summary>
    /// <remarks>
    /// 顺序不稳定会让同一份种子的随机流消耗顺序随字典遍历顺序变化，**轨迹不可重放而每次运行本身看起来都正常**。
    /// An unstable order makes random-stream consumption follow dictionary iteration order, so **the trajectory is not replayable while
    /// each run looks normal**.
    /// </remarks>
    [Fact]
    public void ParameterPools_ShouldBeSortedByName() {
        var first = new TabularSelectionPolicy("fixed", new[] { "p" });
        var second = new TabularSelectionPolicy("fixed", new[] { "p" });
        var bindings = new OperatorBindings(null, null, null,
            new Dictionary<string, OperatorPool<IParameterSource>>(StringComparer.Ordinal) {
                ["zeta"] = new OperatorPool<IParameterSource>("zeta",
                    new Dictionary<string, IParameterSource> { ["p"] = new ConstantSource(Flt64.Zero) },
                    first, new SearchRandom(1), Array.Empty<string>()),
                ["alpha"] = new OperatorPool<IParameterSource>("alpha",
                    new Dictionary<string, IParameterSource> { ["p"] = new ConstantSource(Flt64.Zero) },
                    second, new SearchRandom(1), Array.Empty<string>()),
            });

        IReadOnlyList<OperatorPool<IParameterSource>> pools = bindings.ParameterPools();
        pools[0].Id.Should().Be("alpha");
        pools[1].Id.Should().Be("zeta");
    }

    /// <summary>**缺少参数池时返回默认源** / A missing parameter pool falls back to the default source.</summary>
    [Fact]
    public void Parameter_ShouldFallBackToDefault() {
        var bindings = new OperatorBindings(null, null, null,
            new Dictionary<string, OperatorPool<IParameterSource>>(StringComparer.Ordinal));

        IParameterSource source = bindings.Parameter("missing", new ConstantSource(new Flt64(4.0)));
        source.Value(new Iteration()).ToDouble().Should().Be(4.0);
    }

    /// <summary>**每次取值都通过池的 `Current()`，不缓存算子** / Every read goes through the pool's `Current()`.</summary>
    /// <remarks>
    /// 把算子本身缓存进适配器会让后续 `Choose` 选出的新算子被忽略，**策略看起来仍在工作但实际一直执行第一个算子**。
    /// Caching the operator inside the adapter makes later `Choose` results ignored, so **the policy appears to work while the first
    /// operator always executes**.
    /// </remarks>
    [Fact]
    public void Parameter_ShouldReadCurrentFromPool() {
        var candidateA = new ConstantSource(new Flt64(1.0));
        var candidateB = new ConstantSource(new Flt64(2.0));
        var policy = new TabularSelectionPolicy("round-robin", new[] { "a", "b" });
        var pool = new OperatorPool<IParameterSource>("p",
            new Dictionary<string, IParameterSource>(StringComparer.Ordinal) {
                ["a"] = candidateA,
                ["b"] = candidateB,
            }, policy, new SearchRandom(1), Array.Empty<string>());
        var bindings = new OperatorBindings(null, null, null,
            new Dictionary<string, OperatorPool<IParameterSource>>(StringComparer.Ordinal) { ["p"] = pool });

        IParameterSource source = bindings.Parameter("p", new ConstantSource(Flt64.Zero));

        // 第一次选 a。
        // The first selection picks a.
        pool.Choose(new SelectionContext()).Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        source.Value(new Iteration()).ToDouble().Should().Be(1.0);

        // 第二次选 b；若适配器缓存了 a，这里仍是 1.0。
        // The second selection picks b; a caching adapter would still return 1.0.
        pool.Choose(new SelectionContext()).Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        source.Value(new Iteration()).ToDouble().Should().Be(2.0);
    }

    /// <summary>**空绑定的池列表为空** / Empty bindings have no pools.</summary>
    [Fact]
    public void Pools_ShouldBeEmptyWhenNothingBound() {
        var bindings = new OperatorBindings(null, null, null,
            new Dictionary<string, OperatorPool<IParameterSource>>(StringComparer.Ordinal));

        bindings.Pools().Should().BeEmpty();
    }
}
