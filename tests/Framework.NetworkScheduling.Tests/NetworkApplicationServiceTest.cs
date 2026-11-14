// 应用服务层测试 / Tests for the application service layer.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.NetworkScheduling.Application.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Application.Service;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Allocation;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Infrastructure;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Policy;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Service;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Tests;

/// <summary>
/// 应用服务层 / The application service layer.
/// </summary>
public sealed class NetworkApplicationServiceTest {
    /// <summary>**默认空监听器始终成功** / The default no-op trace listener always succeeds.</summary>
    [Fact]
    public void TraceListenerNone_ShouldSucceed() {
        var trace = new BranchAndPriceTrace(
            0, 0, new Flt64(0.0), new Flt64(0.0), new Flt64(0.0), TimeSpan.Zero, 0);

        BranchAndPriceTraceListeners.None(trace).IsOk.Should().BeTrue();
    }

    /// <summary>**求解结果保留报告与业务解两个字段** / The solve result keeps report and business solution separate.</summary>
    [Fact]
    public void NetworkSolveResult_ShouldKeepReportAndSolution() {
        SolveReport<Flt64> report = EmptyReport();
        var result = new NetworkSolveResult<string>(report, "solution");

        result.Report.Should().BeSameAs(report);
        result.Solution.Should().Be("solution");
    }

    /// <summary>**无 incumbent 时业务解为 null** / The business solution is null without an incumbent.</summary>
    [Fact]
    public void NetworkSolveResult_ShouldAllowNullSolution() =>
        new NetworkSolveResult<string>(EmptyReport(), null).Solution.Should().BeNull();

    /// <summary>**enricher 按声明顺序依次作用** / Enrichers apply in declaration order.</summary>
    [Fact]
    public void Enricher_ShouldApplyInDeclarationOrder() {
        var order = new List<string>();
        SolutionEnricher<Flt64> first = result => { order.Add("first"); return result; };
        SolutionEnricher<Flt64> second = result => { order.Add("second"); return result; };
        VrptwSolveResult<Flt64> start = SolveResult(new Flt64(0.0));

        VrptwSolveResult<Flt64> enriched = start;
        foreach (SolutionEnricher<Flt64> enricher in new[] { first, second }) {
            enriched = enricher(enriched);
        }

        order.Should().ContainInOrder("first", "second");
    }

    /// <summary>**enricher 可以改写结果，后一个看到前一个的输出** / An enricher may rewrite the result.</summary>
    [Fact]
    public void Enricher_ShouldBeAbleToRewriteResult() {
        SolutionEnricher<Flt64> bump = result => SolveResult(result.LowerBound.Plus(new Flt64(1.0)));
        VrptwSolveResult<Flt64> start = SolveResult(new Flt64(2.0));

        bump(start).LowerBound.ToDouble().Should().BeApproximately(3.0, 1e-9);
        start.LowerBound.ToDouble().Should().BeApproximately(2.0, 1e-9, "原结果不被改写 / the original is untouched");
    }

    /// <summary>**求解结果默认携带 trace 的相对 gap** / The solve result carries the trace's relative gap.</summary>
    [Fact]
    public void SolveResult_ShouldCarryTraceGap() {
        VrptwSolveResult<Flt64> result = SolveResult(new Flt64(1.0));
        result.Trace.RelativeGap.ToDouble().Should().BeApproximately(0.5, 1e-9);
    }

    /// <summary>**求解器失败时服务返回失败而非抛出** / A solver failure yields a failure result, not an exception.</summary>
    [Fact]
    public async System.Threading.Tasks.Task SolveNetwork_ShouldPropagateSolverFailure() {
        var solver = new CountingSolver();

        Result<NetworkSolveResult<string>, ErrorCode, Error<ErrorCode>> result =
            await NetworkSolver.SolveNetwork<string>(
            "test",
            solver,
            _ => Results.Ok(Results.SuccessInstance),
            _ => Results.Ok("unreachable"));

        result.IsOk.Should().BeFalse();
    }

    /// <summary>**注册失败时不会调用求解器** / A registration failure never reaches the solver.</summary>
    [Fact]
    public async System.Threading.Tasks.Task SolveNetwork_ShouldStopBeforeSolverOnRegistrationFailure() {
        var solver = new CountingSolver();

        Result<NetworkSolveResult<string>, ErrorCode, Error<ErrorCode>> result =
            await NetworkSolver.SolveNetwork<string>(
            "test",
            solver,
            _ => Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.IllegalArgument, "registration failed")),
            _ => Results.Ok("unreachable"));

        result.IsOk.Should().BeFalse();
        solver.Calls.Should().Be(0, "注册失败后不再求解 / no solve after a registration failure");
    }

    /// <summary>
    /// **解维度不匹配时返回失败而不是静默错位**。solver 声称有解却给出与 `TokensInSolver` 长度不符的向量时，逐位赋值会把某个变量的值
    /// 写到另一个变量上——这类错误不会报错，只会让提取结果整体偏移。
    /// **A dimension mismatch fails instead of silently misaligning.** When the solver claims an incumbent but hands back a vector
    /// whose length disagrees with `TokensInSolver`, positional assignment writes one variable's value into another — an error that
    /// never raises, it just shifts the whole extraction.
    /// </summary>
    [Fact]
    public async System.Threading.Tasks.Task SolveNetwork_ShouldRejectSolutionDimensionMismatch() {
        var solver = new MismatchedSolver();
        bool extracted = false;

        Result<NetworkSolveResult<string>, ErrorCode, Error<ErrorCode>> result =
            await NetworkSolver.SolveNetwork<string>(
            "test",
            solver,
            _ => Results.Ok(Results.SuccessInstance),
            _ => { extracted = true; return Results.Ok("extracted"); });

        result.IsOk.Should().BeFalse();
        extracted.Should().BeFalse("维度不符不进入提取 / no extraction on a dimension mismatch");
    }

    /// <summary>构造空的求解报告 / Build an empty solve report.</summary>
    private static SolveReport<Flt64> EmptyReport() =>
        new SolveReport<Flt64> {
            ProblemStatus = ProblemStatus.Infeasible,
            TerminationReason = TerminationReason.Completed,
            SolutionPresence = SolutionPresence.None,
        };

    /// <summary>构造求解结果 / Build a solve result.</summary>
    private static VrptwSolveResult<Flt64> SolveResult(Flt64 lowerBound) =>
        new(
            BranchAndPriceStatus.Feasible,
            null,
            lowerBound,
            new Flt64(lowerBound.ToDouble() * 2.0),
            new Flt64(0.5),
            new BranchAndPriceTrace(
                1, 0, lowerBound, new Flt64(lowerBound.ToDouble() * 2.0), new Flt64(0.5),
                TimeSpan.FromSeconds(1), 1));

    /// <summary>**分配构造失败在装配前返回** / Allocation construction failures return before assembly.</summary>
    [Fact]
    public async Task Allocation_ShouldRejectBeforeAssembly() {
        var problem = new AllocationProblem<Flt64>(
            new[] {
                new AllocationLocation<Flt64>(AllocationPlant),
                new AllocationLocation<Flt64>(AllocationPlant),
            },
            new[] { AllocationServiceOf() },
            new[] { AllocationRequestOf() },
            new Flt64(0.0),
            costUnit: SIBaseUnits.Meter);

        Result<NetworkSolveResult<AllocationSolution<Flt64>>, ErrorCode, Error<ErrorCode>> result =
            await AllocationService().Solve(problem);

        result.IsOk.Should().BeFalse("重复地点在构造阶段就该被拒 / duplicate locations are rejected at construction");
    }

    /// <summary>**分配应用服务要求求解器** / The allocation application service requires a solver.</summary>
    [Fact]
    public void Allocation_ShouldRequireSolver() =>
        ((Func<NetworkAllocationApplicationService<Flt64>>)(() =>
            new NetworkAllocationApplicationService<Flt64>(
                null!, Flt64NetworkSchedulingSolverValueAdapter.Instance)))
            .Should().Throw<ArgumentNullException>();

    /// <summary>**未知起运地点被拒** / An unknown origin is rejected.</summary>
    [Fact]
    public async Task Allocation_ShouldRejectUnknownOrigin() {
        var problem = new AllocationProblem<Flt64>(
            new[] { new AllocationLocation<Flt64>(AllocationStore) },
            new[] { AllocationServiceOf() },
            new[] { AllocationRequestOf() },
            new Flt64(0.0),
            costUnit: SIBaseUnits.Meter);

        (await AllocationService().Solve(problem)).IsOk.Should().BeFalse();
    }

    /// <summary>**非法请求数量被拒** / An illegal request amount is rejected.</summary>
    [Fact]
    public async Task Allocation_ShouldRejectIllegalQuantity() {
        var problem = new AllocationProblem<Flt64>(
            new[] {
                new AllocationLocation<Flt64>(AllocationPlant),
                new AllocationLocation<Flt64>(AllocationStore),
            },
            new[] { AllocationServiceOf() },
            new[] {
                new AllocationRequest<Flt64>(
                    "req", AllocationPlant, AllocationStore, AllocationMeters(-1.0)),
            },
            new Flt64(0.0),
            costUnit: SIBaseUnits.Meter);

        (await AllocationService().Solve(problem)).IsOk.Should().BeFalse();
    }

    /// <summary>**广义流应用服务要求求解器** / The generalized-flow service also requires a solver.</summary>
    [Fact]
    public void GeneralizedFlow_ShouldRequireSolver() =>
        ((Func<NetworkGeneralizedFlowApplicationService<Flt64>>)(() =>
            new NetworkGeneralizedFlowApplicationService<Flt64>(
                null!, Flt64NetworkSchedulingSolverValueAdapter.Instance)))
            .Should().Throw<ArgumentNullException>();

    /// <summary>**VRPTW 服务拒绝空实例** / The VRPTW service rejects a null instance.</summary>
    [Fact]
    public void Vrptw_ShouldRejectNullInstance() =>
        ((Func<VrptwApplicationService<Flt64>>)(() =>
            new VrptwApplicationService<Flt64>(
                null!, new CountingSolver(), VrptwConfiguration(), VrptwPolicy())))
            .Should().Throw<ArgumentNullException>();

    /// <summary>**VRPTW 服务拒绝空求解器** / The VRPTW service rejects a null solver.</summary>
    [Fact]
    public void Vrptw_ShouldRejectNullSolver() =>
        ((Func<VrptwApplicationService<Flt64>>)(() =>
            new VrptwApplicationService<Flt64>(
                VrptwInstance(), null!, VrptwConfiguration(), VrptwPolicy())))
            .Should().Throw<ArgumentNullException>();

    /// <summary>构造 VRPTW 配置 / Build a VRPTW configuration.</summary>
    private static BranchAndPriceAlgorithm<Flt64>.Configuration VrptwConfiguration() =>
        new(TimeSpan.FromSeconds(1), NodeLimit: 1);

    /// <summary>构造 VRPTW 策略 / Build a VRPTW policy.</summary>
    private static BranchAndPriceAlgorithm<Flt64>.Policy VrptwPolicy() =>
        new(
            Flt64NetworkSchedulingSolverValueAdapter.Instance,
            new EuclideanDistanceCalculator<Flt64>(SIBaseUnits.Meter),
            new DistanceAsTravelTimeCalculator<Flt64>(
                new EuclideanDistanceCalculator<Flt64>(SIBaseUnits.Meter),
                new TimeWindow<Flt64>(
                    new TimeRange(VrptwOrigin, VrptwOrigin.AddHours(12)), false, TimeSpan.FromMinutes(1))),
            new DistanceArcCostCalculator<Flt64>(SIBaseUnits.Meter, SIBaseUnits.Meter),
            new FixedPlusArcCostPolicy<Flt64>(SIBaseUnits.Meter));

    /// <summary>VRPTW 时间原点 / The VRPTW time origin.</summary>
    private static readonly DateTimeOffset VrptwOrigin = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>构造合法 VRPTW 实例 / Build a valid VRPTW instance.</summary>
    private static VrptwInstance<Flt64> VrptwInstance() =>
        VrptwInstance<Flt64>.Invoke(
            "vrptw",
            new Depot<Flt64>(VrptwNode("depot"), VrptwWindow()),
            new Depot<Flt64>(VrptwNode("depot-end"), VrptwWindow()),
            new[] { VrptwCustomer("c1", "n1") },
            new[] {
                VehicleType<Flt64>.Invoke(
                    new VehicleTypeId("truck"),
                    AllocationMeters(100.0),
                    AllocationMeters(10.0),
                    1).Value,
            },
            new TimeWindow<Flt64>(
                new TimeRange(VrptwOrigin, VrptwOrigin.AddHours(12)), false, TimeSpan.FromMinutes(1)),
            VrptwUnits.Invoke(SIBaseUnits.Meter, SIBaseUnits.Meter, SIBaseUnits.Meter).Value,
            VrptwTolerances.Default).Value;

    /// <summary>构造客户 / Build a customer.</summary>
    private static Customer<Flt64> VrptwCustomer(string id, string nodeId) =>
        Customer<Flt64>.Invoke(
            new CustomerId(id),
            VrptwNode(nodeId),
            AllocationMeters(1.0),
            VrptwWindow(),
            TimeSpan.FromMinutes(10)).Value;

    /// <summary>构造服务时间窗 / Build a service time window.</summary>
    private static ServiceTimeWindow VrptwWindow() =>
        ServiceTimeWindow.Invoke(VrptwOrigin, VrptwOrigin.AddHours(12)).Value;

    /// <summary>构造带坐标的网络节点 / Build a network node with coordinates.</summary>
    private static NetworkNode<Flt64> VrptwNode(string id) =>
        NetworkNode<Flt64>.Invoke(
            new NetworkNodeId(id),
            new Dictionary<string, Quantity<Flt64>> {
                ["x"] = AllocationMeters(0.0),
                ["y"] = AllocationMeters(0.0),
            }).Value;

    /// <summary>分配地点：起点 / The allocation origin.</summary>
    private static readonly NetworkNodeId AllocationPlant = new("plant");

    /// <summary>分配地点：终点 / The allocation destination.</summary>
    private static readonly NetworkNodeId AllocationStore = new("store");

    /// <summary>分配服务标识 / The allocation service identity.</summary>
    private static readonly NetworkArcId AllocationServiceId = new("arc:plant-store");

    /// <summary>构造分配应用服务 / Build the allocation application service.</summary>
    private static NetworkAllocationApplicationService<Flt64> AllocationService() =>
        new(new CountingSolver(), Flt64NetworkSchedulingSolverValueAdapter.Instance);

    /// <summary>构造运输服务 / Build a transport service.</summary>
    private static AllocationService<Flt64> AllocationServiceOf() =>
        new(
            AllocationServiceId,
            AllocationPlant,
            AllocationStore,
            AllocationMeters(1.0),
            new CapacityBounds<Flt64>(AllocationMeters(0.0), AllocationMeters(10.0)));

    /// <summary>构造运输请求 / Build a shipment request.</summary>
    private static AllocationRequest<Flt64> AllocationRequestOf() =>
        new("req", AllocationPlant, AllocationStore, AllocationMeters(5.0));

    /// <summary>以米为单位的量 / A quantity in metres.</summary>
    private static Quantity<Flt64> AllocationMeters(double value) =>
        new(new Flt64(value), SIBaseUnits.Meter);

    /// <summary>
    /// 求解器桩基类：实现全部六个接口成员，子类只覆写真正关心的那一个。
    /// Solver-stub base: implements all six interface members so a subclass overrides only the one it cares about.
    /// </summary>
    private abstract class SolverStub : IColumnGenerationSolver {
        /// <inheritdoc/>
        public virtual string Name => "stub";

        /// <inheritdoc/>
        public virtual System.Threading.Tasks.Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>
            SolveMILPAsync(
                string name,
                LinearMetaModel<Flt64> metaModel,
                bool toLogModel = false,
                RegistrationStatusCallBack? registrationStatusCallBack = null,
                SolvingStatusCallBack? solvingStatusCallBack = null) =>
            System.Threading.Tasks.Task.FromResult(
                Results.Failed<FeasibleSolverOutput<Flt64>>(
                    new Err<ErrorCode>(ErrorCode.IllegalArgument, "stub")));

        /// <inheritdoc/>
        public System.Threading.Tasks.Task<
            Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> SolutionPool), ErrorCode, Error<ErrorCode>>>
            SolveMILPAsync(
                string name,
                LinearMetaModel<Flt64> metaModel,
                ulong amount,
                bool toLogModel = false,
                RegistrationStatusCallBack? registrationStatusCallBack = null,
                SolvingStatusCallBack? solvingStatusCallBack = null) =>
            throw new NotSupportedException();

        /// <inheritdoc/>
        public System.Threading.Tasks.Task<Result<FeasibleSolverOutput<V>, ErrorCode, Error<ErrorCode>>> SolveMILPAsAsync<V>(
            string name,
            LinearMetaModel<Flt64> metaModel,
            IIntoValue<V> converter,
            bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null)
            where V : struct, IRealNumber<V>, INumberField<V> =>
            throw new NotSupportedException();

        /// <inheritdoc/>
        public System.Threading.Tasks.Task<Result<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>>> SolveLPAsync(
            string name,
            LinearMetaModel<Flt64> metaModel,
            bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null) =>
            throw new NotSupportedException();

        /// <inheritdoc/>
        public System.Threading.Tasks.Task<Result<IColumnGenerationSolver.LpResultOf<V>, ErrorCode, Error<ErrorCode>>> SolveLPAsAsync<V>(
            string name,
            LinearMetaModel<Flt64> metaModel,
            IIntoValue<V> converter,
            bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null)
            where V : struct, IRealNumber<V>, INumberField<V> =>
            throw new NotSupportedException();
    }

    /// <summary>记录调用次数、总是失败的求解器 / A solver that counts calls and always fails.</summary>
    private sealed class CountingSolver : SolverStub {
        /// <summary>调用次数 / Call count.</summary>
        public int Calls { get; private set; }

        /// <inheritdoc/>
        public override string Name => "counting";

        /// <inheritdoc/>
        public override System.Threading.Tasks.Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>
            SolveMILPAsync(
                string name,
                LinearMetaModel<Flt64> metaModel,
                bool toLogModel = false,
                RegistrationStatusCallBack? registrationStatusCallBack = null,
                SolvingStatusCallBack? solvingStatusCallBack = null) {
            Calls++;
            return System.Threading.Tasks.Task.FromResult(
                Results.Failed<FeasibleSolverOutput<Flt64>>(
                    new Err<ErrorCode>(ErrorCode.IllegalArgument, "unexpected call")));
        }
    }

    /// <summary>返回与模型维度不符的解向量的求解器 / A solver returning a solution vector of the wrong length.</summary>
    private sealed class MismatchedSolver : SolverStub {
        /// <inheritdoc/>
        public override string Name => "mismatched";

        /// <inheritdoc/>
        public override System.Threading.Tasks.Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>
            SolveMILPAsync(
                string name,
                LinearMetaModel<Flt64> metaModel,
                bool toLogModel = false,
                RegistrationStatusCallBack? registrationStatusCallBack = null,
                SolvingStatusCallBack? solvingStatusCallBack = null) =>
            System.Threading.Tasks.Task.FromResult(
                Results.Ok(new FeasibleSolverOutput<Flt64>(
                    new Flt64(0.0),
                    new Solution<Flt64>(new[] { new Flt64(1.0) }),
                    TimeSpan.Zero,
                    new Flt64(0.0),
                    new Flt64(0.0))));
    }
}
