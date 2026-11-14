#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 降阶后端适配器 / The lowering backend adapter.
/// </summary>
/// <remarks>
/// **`IFixedIntegerLpBackend` 和 lowerer 一直都在，缺的是把两者接起来的那一层。**
/// **而那一层的正确性不依赖任何具体求解器**——**这正是它可以被测、也正因此此前从没被测过的原因。**
///
/// **Both `IFixedIntegerLpBackend` and the lowerer have always existed; the layer joining them did not.** **Its correctness depends on no concrete solver** — **which is why it can be tested, and why it never was.**
/// </remarks>
public class LoweredBackendAdapterTests {
    private static readonly VariableId X = new("source:x");

    private static ConstraintProgrammingModelSnapshot BuildSnapshot() {
        using var model = new ConstraintProgrammingModel(name: "lowering", objectCategory: ObjectCategory.Minimum);
        var variable = new IntVar("x");
        IntegerDomain domain = IntegerDomain.CreateInterval(Int64.Zero, new Int64(10)).Value;
        var expression = new ConstraintProgrammingExpression.Variable(variable, domain, X);

        model.RegisterVariable(id: X, variable: variable, domain: domain,
            scope: ModelElementScope.Stable, origin: "fixture/x").Value.Should().NotBeNull();
        model.RegisterExpression("x", expression).Value.Should().NotBeNull();
        model.AddConstraint(
            ConstraintProgrammingConstraint.CreateGreaterOrEqual(expression, Int64.Zero).Value,
            "constraint:x").Value.Should().NotBeNull();
        model.Minimize(expression: expression, id: new ObjectiveId("objective:main")).Value.Should().NotBeNull();

        return model.Snapshot().Value;
    }

    private static FixedIntegerLpRequest Request(ConstraintProgrammingModelSnapshot snapshot) =>
        new(
            Snapshot: snapshot,
            FixedValues: new Dictionary<VariableId, Int64> { [X] = new Int64(3) },
            BaselineSolution: new ConstraintProgrammingSolution(new Dictionary<VariableId, Int64> { [X] = new Int64(3) }),
            Options: new FixedIntegerLpSensitivityOptions());

    /// <summary>记录委托收到的降阶模型 / A solver that records what it was handed.</summary>
    private sealed class RecordingSolver : ILoweredFixedIntegerLpSolver {
        public int Calls { get; private set; }

        public bool ReceivedLoweredModel { get; private set; }

        public Task<Result<FixedIntegerLpSolveResult, ErrorCode, Error<ErrorCode>>> SolveAsync(
            ConstraintProgrammingLoweredLinearModel lowered,
            FixedIntegerLpRequest request,
            CancellationToken cancellationToken = default) {
            Calls++;
            ReceivedLoweredModel = lowered.Model is not null;
            // **返回值必须可区分**：与"忽略委托结果"的变异默认值不同，**否则判据分不清是谁的答案。**
            // **The value has to be distinguishable** from what a mutation ignoring the delegate would invent.
            return Task.FromResult(Results.Ok(new FixedIntegerLpSolveResult(
                AnalysisStatus.Reachable,
                ObjectiveValue: new Flt64(42.0))));
        }
    }

    /// <summary>**降阶成功时委托收到的是降阶模型** / A successful lowering hands the model to the delegate.</summary>
    [Fact]
    public async Task TheDelegate_ShouldReceiveTheLoweredModel() {
        var solver = new RecordingSolver();
        var adapter = new LoweredFixedIntegerLpBackend(solver);

        Result<FixedIntegerLpSolveResult, ErrorCode, Error<ErrorCode>> result =
            await adapter.SolveAsync(Request(BuildSnapshot()));

        result.IsOk.Should().BeTrue("the fixture lowers cleanly");
        result.Value!.ObjectiveValue!.Value.ToDouble().Should().BeApproximately(42.0, 1e-9,
            "the adapter returns the delegate's answer, not one of its own");
        solver.Calls.Should().Be(1, "the delegate runs exactly once per request");
        solver.ReceivedLoweredModel.Should().BeTrue("a lowered model, not the raw snapshot, is what a lowering backend hands over");
    }

    /// <summary>**降阶失败时不得委托** / A failed lowering must not reach the delegate.</summary>
    /// <remarks>
    /// **这条比"成功时调用了"更重要。** 降阶失败还去委托，
    /// **等于把一个没降下来的模型交给一个只会读降阶模型的求解器**——
    /// **而它可能不报错，只是给出一个基于原始 CP 快照的、看起来合法的答案。**
    ///
    /// **失败是构造出来的**：给 lowerer 一个上限为 0 的策略，`Policy.Validate()` 立刻拒绝。
    /// **不去造一个"降阶不了的模型"**——那种 fixture 会随降阶器演进而失效，
    /// **而策略校验失败是这条路径上稳定存在的一道门。**
    ///
    /// **This matters more than "it calls on success".** Delegating after a failed lowering **hands a solver that only reads lowered models something that was never lowered** — **and it may not complain; it may return a
    /// plausible answer computed from the raw snapshot.**
    /// **The failure is constructed**: a policy with a zero limit makes `Policy.Validate()` refuse at once. **Not by building an "unlowerable model"** — that fixture would rot as the lowerer evolves, **while a rejected
    /// policy is a gate that stays on this path.**
    /// </remarks>
    [Fact]
    public async Task TheFailedLowering_ShouldNotReachTheDelegate() {
        var solver = new RecordingSolver();
        var adapter = new LoweredFixedIntegerLpBackend(
            solver,
            new ConstraintProgrammingToLinearModelLowerer(
                new ConstraintProgrammingLoweringPolicy(SparseDomainLimit: 0)));

        Result<FixedIntegerLpSolveResult, ErrorCode, Error<ErrorCode>> result =
            await adapter.SolveAsync(Request(BuildSnapshot()));

        solver.Calls.Should().Be(0, "a model that never lowered must not reach the solver");
        result.IsOk.Should().BeFalse("the lowering failure is reported, not swallowed");
    }

    /// <summary>**委托抛异常时错误照常传出** / A throwing delegate still surfaces its failure.</summary>
    /// <remarks>
    /// **这一条故意只查"异常传出来了"，不查"降阶模型被关掉了"。**
    /// `ConstraintProgrammingLoweredLinearModel.Dispose()` 只是把内部的 `Model` 转给
    /// `LinearMetaModel.Dispose()`，**而那个对象在处置后仍非 `null`、也没有公开的处置状态**——
    /// **于是"关没关"从外部根本观测不到。**
    ///
    /// **与其写一条永远绿的假判据，不如把它写成它真正能证明的东西**：
    /// **`finally` 不会把异常吞掉。** 关闭本身由 `finally` 保证，**而 `finally` 的存在由 `GateScriptContractTests`
    /// 之外的代码评审保证**——这里不假装测到了。
    ///
    /// **This one deliberately checks only that the exception surfaces, not that the model closed.** `Dispose()` merely forwards the inner `Model` to `LinearMetaModel.Dispose()`, **and that object stays non-null with no
    /// public disposal state** — **so "did it close" cannot be observed from outside at all.**
    /// **Rather than a criterion that is green no matter what, it says what it can prove**: **`finally` does not swallow the exception.** The close itself is `finally`'s job, **and that it exists is code review's job** — this
    /// does not pretend to measure it.
    /// </remarks>
    [Fact]
    public async Task TheThrowingDelegate_ShouldSurfaceTheFailure() {
        var solver = new ThrowingSolver();
        var adapter = new LoweredFixedIntegerLpBackend(solver);

        Func<Task> call = async () => await adapter.SolveAsync(Request(BuildSnapshot()));

        await call.Should().ThrowAsync<InvalidOperationException>("a delegate failure is not swallowed by the adapter");
        solver.SawLoweredModel.Should().BeTrue("the delegate did receive a lowered model before throwing");
    }

    /// <summary>一个总是抛异常的委托 / A delegate that always throws.</summary>
    private sealed class ThrowingSolver : ILoweredFixedIntegerLpSolver {
        public bool SawLoweredModel { get; private set; }

        public Task<Result<FixedIntegerLpSolveResult, ErrorCode, Error<ErrorCode>>> SolveAsync(
            ConstraintProgrammingLoweredLinearModel lowered,
            FixedIntegerLpRequest request,
            CancellationToken cancellationToken = default) {
            SawLoweredModel = lowered.Model is not null;
            throw new InvalidOperationException("delegate failed");
        }
    }
}
