// 受控搜索会话测试 / Tests for the controlled search session.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator.MultiObjective;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator.Mutation;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 受控搜索会话 / The controlled search session.
/// </summary>
public sealed class ControlledSearchSessionTests {
    /// <summary>**第一次决策不产生任何转移** / The first decision produces no transitions.</summary>
    /// <remarks>
    /// 第一次决策时算子池还没被选中过任何算子，此时 `choose` 会凭空产生一条决策而 `usedAction` 仍是 `null`；
    /// **反馈一条从未执行的决策会让策略从空执行里学习**。
    /// On the first decision no operator has been chosen yet; calling `choose` fabricates a decision while `usedAction` stays `null` —
    /// **feeding back a decision that never executed teaches the policy from nothing**.
    /// </remarks>
    [Fact]
    public async Task Step_ShouldNotRecordTransitionsBeforeInitialization() {
        ControlledSearchSession<double, Flt64> session = BuildSession(
            out ScriptedSession _, stepsPerCall: 1, useOperator: true);

        await session.Step(Slice(1));

        session.DrainTransitions().Should().BeEmpty(
            "第一次低层步进之前不得产生转移 / no transition may appear before the first low-level step");
        session.Initialized.Should().BeTrue();
    }

    /// <summary>**`initialized` 在第一次真正走步之前保持 `false`** / `initialized` stays `false` until a real step happens.</summary>
    /// <remarks>
    /// 只断言"没有转移"是弱断言：`choose` 之后 `usedAction` 仍为 `null`，池会走 `discard` 分支，因此**去掉 `_initialized` 守卫也测不出来**。
    /// 变异测试实测：把守卫改成 `if (true)`，首版测试**仍然通过**。因此必须直接钉住 `initialized` 这个可观测状态。
    /// Asserting only "no transitions" is a weak assertion: after `choose` the `usedAction` is still `null`, so the pool takes the `discard`
    /// branch and **removing the `_initialized` guard is undetectable**. The mutation test confirmed this: changing the guard to
    /// `if (true)` **still passed** the first version. The observable state `initialized` must be pinned directly instead.
    /// </remarks>
    [Fact]
    public async Task Step_ShouldKeepUninitializedUntilRealStep() {
        ControlledSearchSession<double, Flt64> session = BuildSession(
            out ScriptedSession _, stepsPerCall: 0, useOperator: true);

        // 低层返回 0 步 → 没有真正走步 → 仍未初始化。
        // The low level reports zero steps → nothing actually ran → still uninitialized.
        await session.Step(Slice(1));
        session.Initialized.Should().BeFalse(
            "零步进不算初始化 / a zero-step run does not count as initialization");
    }

    /// <summary>**第二次决策产生一条转移** / The second decision produces one transition.</summary>
    [Fact]
    public async Task Step_ShouldRecordTransitionAfterInitialization() {
        // **必须让脚本会话真的使用算子池**：否则 `usedAction` 恒为 `null`，池会走 `discard` 分支，永远不产生转移。
        // **The scripted session must actually use the pool**: otherwise `usedAction` stays `null`, the pool takes the `discard` branch,
        // and no transition is ever produced.
        ControlledSearchSession<double, Flt64> session = BuildSession(
            out ScriptedSession _, stepsPerCall: 1, useOperator: true);

        await session.Step(Slice(1));
        await session.Step(Slice(1));

        session.DrainTransitions().Should().ContainSingle();
    }

    /// <summary>**未用过的池被 `discard` 而不是记录反馈** / Unused pools are discarded instead of fed back.</summary>
    /// <remarks>
    /// 对未用过的池调 `observe` 会返回成功但什么也不记，看起来像正常反馈。
    /// Calling `observe` on an unused pool returns success while recording nothing, which looks like normal feedback.
    /// </remarks>
    [Fact]
    public async Task Step_ShouldDiscardUnusedPools() {
        // 变异算子池只有一个候选且从不被真正使用（`ScriptedSession` 不调算子），
        // 因此每次都必须走 `discard` 分支。
        // The mutation pool has one candidate that is never actually used (the scripted session never calls operators), so every decision
        // must take the `discard` branch.
        ControlledSearchSession<double, Flt64> session = BuildSession(out ScriptedSession _, stepsPerCall: 1);

        await session.Step(Slice(1));
        await session.Step(Slice(1));

        // 若走了 `observe` 分支，转移数会与池数一样多；这里只有一个池且它没被用过。
        // Had it taken the `observe` branch, the transition count would equal the pool count; here there is one pool and it was unused.
        session.DrainTransitions().Should().BeEmpty();
    }

    /// <summary>**奖励无效时记为 0 并写诊断，不中止搜索** / An invalid reward is recorded as 0 with a diagnostic, not an abort.</summary>
    /// <remarks>
    /// 中止会让一次奖励函数的数值意外毁掉整个搜索；记 0 之后策略仍能继续，诊断里留下"这次奖励是假的"的证据。
    /// Aborting lets one numeric accident in the reward function destroy the whole search; recording 0 keeps the policy going while the
    /// diagnostic preserves the evidence that this reward was fake.
    /// </remarks>
    [Fact]
    public async Task Step_ShouldTolerateInvalidReward() {
        ControlledSearchSession<double, Flt64> session = BuildSession(
            out ScriptedSession _, stepsPerCall: 1, reward: new BrokenReward(), useOperator: true);

        // **必须先让会话初始化**：第一次步进之前不产生任何反馈，奖励函数根本不会被调用。
        // 第一版直接跑一步就断言诊断，实测 `Diagnostics` 为空。
        // **The session must be initialized first**: no feedback happens before the first step, so the reward function is never called.
        // The first version asserted the diagnostic after a single step and measured an empty `Diagnostics`.
        await session.Step(Slice(1));
        Result<SearchStepResult<double, Flt64>, ErrorCode, Error<ErrorCode>> result =
            await session.Step(Slice(2));

        result.Should().BeOfType<Ok<SearchStepResult<double, Flt64>, ErrorCode, Error<ErrorCode>>>();
        SearchStepResult<double, Flt64> report =
            ((Ok<SearchStepResult<double, Flt64>, ErrorCode, Error<ErrorCode>>)result).Value;
        report.Diagnostics.Should().Contain(line => line.Contains("算子奖励无效"),
            "无效奖励必须留下诊断 / an invalid reward must leave a diagnostic");
    }

    /// <summary>**低层失败被转成失败结果而不是冒泡异常** / A low-level failure becomes a failed result rather than a bubbling exception.</summary>
    [Fact]
    public async Task Step_ShouldConvertLowLevelFailure() {
        ControlledSearchSession<double, Flt64> session = BuildSession(out ScriptedSession inner, stepsPerCall: 1);
        inner.Fail = true;

        Result<SearchStepResult<double, Flt64>, ErrorCode, Error<ErrorCode>> result =
            await session.Step(Slice(1));

        result.Should().BeOfType<Ok<SearchStepResult<double, Flt64>, ErrorCode, Error<ErrorCode>>>();
        SearchStepResult<double, Flt64> report =
            ((Ok<SearchStepResult<double, Flt64>, ErrorCode, Error<ErrorCode>>)result).Value;
        report.Termination.Should().Be(SearchTermination.Failed);
    }

    /// <summary>**快照与恢复往返一致** / Snapshots round-trip.</summary>
    [Fact]
    public async Task Snapshot_ShouldRoundTrip() {
        ControlledSearchSession<double, Flt64> session = BuildSession(out ScriptedSession _, stepsPerCall: 1);
        await session.Step(Slice(2));

        Result<SearchSessionSnapshot, ErrorCode, Error<ErrorCode>> snapshot = session.Snapshot();
        snapshot.Should().BeOfType<Ok<SearchSessionSnapshot, ErrorCode, Error<ErrorCode>>>();
        SearchSessionSnapshot saved =
            ((Ok<SearchSessionSnapshot, ErrorCode, Error<ErrorCode>>)snapshot).Value;

        ControlledSearchSession<double, Flt64> restored = BuildSession(out ScriptedSession _, stepsPerCall: 1);
        restored.Restore(saved).Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        restored.Decisions.Should().Be(session.Decisions);
        restored.Stagnation.Should().Be(session.Stagnation);
        restored.Initialized.Should().Be(session.Initialized);
    }

    /// <summary>**槽位集合变化时拒绝恢复** / A changed slot set is rejected on restore.</summary>
    /// <remarks>
    /// 少一个池会让它保留旧状态而搜索继续，多一个池会让它的状态无处可去；两种情况下都会返回成功。
    /// A missing pool keeps its old state while the search continues, and an extra pool has nowhere to put its state; both would otherwise
    /// return success.
    /// </remarks>
    [Fact]
    public async Task Restore_ShouldRejectChangedSlots() {
        ControlledSearchSession<double, Flt64> source = BuildSession(out ScriptedSession _, stepsPerCall: 1);
        await source.Step(Slice(2));
        SearchSessionSnapshot saved =
            ((Ok<SearchSessionSnapshot, ErrorCode, Error<ErrorCode>>)source.Snapshot()).Value;

        ControlledSearchSession<double, Flt64> target = BuildSession(
            out ScriptedSession _, stepsPerCall: 1, extraParameterPool: true);

        target.Restore(saved).Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**停滞计数在无改进时增长、有改进时归零** / Stagnation grows without improvement and resets on improvement.</summary>
    [Fact]
    public async Task Step_ShouldTrackStagnation() {
        ControlledSearchSession<double, Flt64> session = BuildSession(out ScriptedSession inner, stepsPerCall: 1);

        // 第一次步进提交一个解 → 档案为空时算改进 → 停滞保持 0。
        // The first step offers one solution → an improvement against the empty archive → stagnation stays 0.
        await session.Step(Slice(1));
        session.Stagnation.Should().Be(0L);

        // 第二次步进提交更差的解 → 不算改进 → 停滞变为 1。
        // The second step offers a worse solution → no improvement → stagnation becomes 1.
        inner.ObjectiveValue = 9.0;
        await session.Step(Slice(1));
        session.Stagnation.Should().Be(1L);
    }

    /// <summary>**策略载体暴露各池快照** / The policy carrier exposes the per-pool snapshots.</summary>
    [Fact]
    public void Policies_ShouldExposePoolSnapshots() {
        ControlledSearchSession<double, Flt64> session = BuildSession(out ScriptedSession _, stepsPerCall: 1);

        session.Policies().Should().ContainKey("mutation");
    }

    /// <summary>构造一个片段预算 / Build a slice budget.</summary>
    /// <param name="steps">步数 / The step count.</param>
    /// <returns>片段预算 / The slice budget.</returns>
    private static SearchSlice Slice(long steps) => new(new SearchBudget(long.MaxValue), steps, 1L);

    /// <summary>构造一个受控会话 / Build a controlled session.</summary>
    /// <param name="inner">脚本化低层会话 / The scripted low-level session.</param>
    /// <param name="stepsPerCall">每次低层调用步数 / The steps per low-level call.</param>
    /// <param name="reward">奖励策略 / The reward policy.</param>
    /// <param name="extraParameterPool">是否额外加一个参数池 / Whether to add an extra parameter pool.</param>
    /// <returns>受控会话 / The controlled session.</returns>
    private static ControlledSearchSession<double, Flt64> BuildSession(
        out ScriptedSession inner,
        long stepsPerCall,
        IRewardPolicy? reward = null,
        bool extraParameterPool = false,
        bool useOperator = false) {
        inner = new ScriptedSession(stepsPerCall);
        var dominance = new ParetoDominance(new[] { true });
        var policy = new TabularSelectionPolicy("fixed", new[] { "a" });
        var mutationPool = new OperatorPool<IMutationOperator>(
            "mutation",
            new Dictionary<string, IMutationOperator>(StringComparer.Ordinal) { ["a"] = new NoopMutation() },
            policy, new SearchRandom(1), new[] { "a" });
        var parameters = new Dictionary<string, OperatorPool<IParameterSource>>(StringComparer.Ordinal);
        if (extraParameterPool) {
            parameters["rate"] = new OperatorPool<IParameterSource>(
                "rate",
                new Dictionary<string, IParameterSource>(StringComparer.Ordinal) {
                    ["p"] = new ConstantSource(new Flt64(0.5)),
                },
                new TabularSelectionPolicy("fixed", new[] { "p" }), new SearchRandom(2), Array.Empty<string>());
        }
        // **`useOperator` 让脚本会话真的把算子池标记为已使用**：否则 `usedAction` 恒为 `null`，
        // 所有池都会走 `discard` 分支，奖励与反馈路径根本不会被触达。
        // **`useOperator` makes the scripted session actually mark the pool used**: otherwise `usedAction` stays `null`, every pool takes
        // the `discard` branch, and the reward/feedback paths are never reached at all.
        if (useOperator) {
            inner.UseOperator = () => mutationPool.Current();
        }
        var bindings = new OperatorBindings(null, mutationPool, null, parameters);
        return new ControlledSearchSession<double, Flt64>(
            inner, bindings, reward ?? ImprovementRewardPolicy.Instance, dominance,
            objective => new[] { new Flt64(objective) });
    }

    /// <summary>脚本化的低层会话 / A scripted low-level session.</summary>
    private sealed class ScriptedSession : ISearchSession<double, Flt64> {
        private readonly long _stepsPerCall;

        /// <summary>
        /// 构造会话 / Construct the session.
        /// </summary>
        /// <param name="stepsPerCall">每次步进数 / The steps per call.</param>
        public ScriptedSession(long stepsPerCall) => _stepsPerCall = stepsPerCall;

        /// <summary>下一个提交的目标值 / The next offered objective value.</summary>
        public double ObjectiveValue { get; set; } = 1.0;

        /// <summary>是否失败 / Whether it fails.</summary>
        public bool Fail { get; set; }

        /// <summary>步进时标记算子池为"已使用" / Mark the operator pool as used on each step.</summary>
        public Action? UseOperator { get; set; }

        /// <inheritdoc/>
        public Task<Result<SearchStepResult<double, Flt64>, ErrorCode, Error<ErrorCode>>> Step(
            SearchSlice budget) {
            UseOperator?.Invoke();
            if (Fail) {
                return Task.FromResult(Results.Failed<SearchStepResult<double, Flt64>>(
                    new Err<ErrorCode>(ErrorCode.ApplicationError, "scripted failure")));
            }
            var candidate = new SolutionWithFitness<double, Flt64>(
                new[] { new Flt64(ObjectiveValue) }, ObjectiveValue, ObjectiveValue);
            return Task.FromResult(Results.Ok(new SearchStepResult<double, Flt64>(
                new[] { candidate }, 1L, _stepsPerCall, SearchTermination.SliceComplete)));
        }

        /// <inheritdoc/>
        public Task<Result<SearchStepResult<double, Flt64>, ErrorCode, Error<ErrorCode>>> ImportSolutions(
            IReadOnlyList<Solution<Flt64>> solutions, SearchSlice budget) =>
            Step(budget);

        /// <inheritdoc/>
        public Result<SearchSessionSnapshot, ErrorCode, Error<ErrorCode>> Snapshot() =>
            Results.Ok(new SearchSessionSnapshot(
                false, 0L, 0L, JsonSerializer.SerializeToElement(0),
                // **`Archive` 也必须是真正的 JSON `null`**，不能是 `default(JsonElement)`。
                // **`Archive` likewise must be a real JSON `null`**, not `default(JsonElement)`.
                JsonDocument.Parse("null").RootElement.Clone()));

        /// <inheritdoc/>
        public Result<Success, ErrorCode, Error<ErrorCode>> Restore(SearchSessionSnapshot snapshot) =>
            Results.Ok(new Success());
    }

    /// <summary>一个从不被调用的变异算子 / A mutation operator that is never invoked.</summary>
    private sealed class NoopMutation : IMutationOperator {
        /// <inheritdoc/>
        public Solution<V> Invoke<V, TObj, TObjValue, T>(
            Iteration iteration,
            T individual,
            IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
            IIntoValue<V> converter,
            Func<Flt64> entropy,
            Flt64 mutationRate)
            where V : struct, IRealNumber<V>, INumberField<V>
            where T : IIndividual<TObjValue, V> =>
            throw new NotSupportedException("the scripted session never invokes operators");
    }

    /// <summary>一个总是返回非有限值的奖励策略 / A reward policy that always returns a non-finite value.</summary>
    private sealed class BrokenReward : IRewardPolicy {
        /// <inheritdoc/>
        public Flt64 Reward(RewardContext context) => new(double.NaN);
    }
}
