#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Plugin.Gurobi;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using SymbolFlatten = Fuookami.Ospf.Core.Symbol.Flatten;

namespace Fuookami.Ospf.Core.Plugin.Gurobi.Tests;

/// <summary>
/// Gurobi 入口的 SDK 无关契约 / SDK-independent Gurobi entry contracts.
/// </summary>
public class GurobiSolverContractTest {
    /// <summary>
    /// 求解器入口保持稳定名称和配置 / Solver entries keep stable names and configuration.
    /// </summary>
    [Fact]
    public void LinearSolver_HasStableNameAndConfig() {
        var solver = new GurobiLinearSolver();

        solver.Name.Should().Be("gurobi");
        solver.Config.Should().BeOfType<Fuookami.Ospf.Core.Solver.Config.SolverConfig>();
    }

    /// <summary>预取消不创建原生环境 / Pre-cancellation does not create a native environment.</summary>
    [Fact]
    public async Task BothReportEntriesReturnPreCancelledBeforeEnvironment() {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        IAbstractLinearSolver linear = new GurobiLinearSolver();
        IAbstractQuadraticSolver quadratic = new GurobiQuadraticSolver();
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> linearReport = await linear.SolveReportAsync(
            model: null!,
            converter: IIntoValue<Flt64>.Identity,
            cancellationToken: cancellation.Token);
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> quadraticReport = await quadratic.SolveReportAsync(
            model: null!,
            converter: IIntoValue<Flt64>.Identity,
            cancellationToken: cancellation.Token);
        foreach (Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result in new[] { linearReport, quadraticReport }) {
            result.IsOk.Should().BeTrue();
            result.Value.ProblemStatus.Should().Be(ProblemStatus.Unknown);
            result.Value.TerminationReason.Should().Be(TerminationReason.Cancelled);
            result.Value.SolutionPresence.Should().Be(SolutionPresence.None);
            result.Value.Solution.Should().BeNull();
        }
    }

    /// <summary>转储和取消不依赖求解器 SDK / Dumping and cancellation do not depend on the solver SDK.</summary>
    [Fact]
    public async Task LinearDump_UsesSharedConversionAndReturnsErrors() {
        using var parent = new LinearMetaModel<Flt64>(objectCategory: ObjectCategory.Minimum);
        var x = new RealVar("dump-x");
        parent.Add(x).IsOk.Should().BeTrue();
        parent.AddObject(
            category: ObjectCategory.Maximum,
            flattenData: new SymbolFlatten.LinearFlattenData<Flt64>(
                new[] { new LinearMonomial<Flt64>(new Flt64(2), x) },
                new Flt64(3)),
            name: "maximum",
            displayName: null
        ).IsOk.Should().BeTrue();
        parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new SymbolFlatten.LinearFlattenData<Flt64>(
                new[] { new LinearMonomial<Flt64>(new Flt64(5), x) },
                new Flt64(4)),
            name: "minimum",
            displayName: null
        ).IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> mechanism = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        IAbstractLinearSolver solver = new GurobiLinearSolver();
        Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> result = await solver.DumpAsync(mechanism);
        result.IsOk.Should().BeTrue();
        using LinearTriadModel model = result.Value;
        model.Objective.Category.Should().Be(ObjectCategory.Minimum);
        model.Objective.Cells.Should().ContainSingle().Which.Coefficient.Should().Be(new Flt64(3));
        model.Objective.Constant.Should().Be(Flt64.One);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        (await solver.DumpAsync(mechanism, cancellation.Token))
            .Should().BeOfType<Failed<LinearTriadModel, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.ApplicationStopped);
        x.Range.SetRange(ValueRange<Flt64>.Of(
            lb: Flt64.Zero,
            ub: Flt64.One,
            lbInterval: new Interval.Open(),
            ubInterval: new Interval.Closed()
        ).Value);
        (await solver.DumpAsync(mechanism))
            .Should().BeOfType<Failed<LinearTriadModel, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.IllegalArgument);
    }

    /// <summary>二次转储和取消不依赖 SDK / Quadratic dumping and cancellation do not depend on the SDK.</summary>
    [Fact]
    public async Task QuadraticDump_UsesSharedConversionAndReturnsErrors() {
        using var parent = new QuadraticMetaModel<Flt64>(objectCategory: ObjectCategory.Minimum);
        var x = new RealVar("quadratic-dump-x");
        parent.Add(x).IsOk.Should().BeTrue();
        parent.AddObject(
            category: ObjectCategory.Maximum,
            flattenData: new SymbolFlatten.QuadraticFlattenData<Flt64>(
                new[] { new QuadraticMonomial<Flt64>(
                    Coefficient: new Flt64(2),
                    Symbol1: x,
                    Symbol2: x
                ) },
                new Flt64(3)),
            name: "maximum",
            displayName: null
        ).IsOk.Should().BeTrue();
        parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new SymbolFlatten.LinearFlattenData<Flt64>(
                new[] { new LinearMonomial<Flt64>(new Flt64(5), x) },
                new Flt64(4)),
            name: "minimum",
            displayName: null
        ).IsOk.Should().BeTrue();
        using QuadraticMechanismModel<Flt64> mechanism = (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        IAbstractQuadraticSolver solver = new GurobiQuadraticSolver();
        Result<QuadraticTetradModel, ErrorCode, Error<ErrorCode>> result = await solver.DumpAsync(mechanism);
        result.IsOk.Should().BeTrue();
        using QuadraticTetradModel model = result.Value;
        model.Objective.Category.Should().Be(ObjectCategory.Minimum);
        model.Objective.Cells.Should().Contain(cell => cell.Col2 == cell.Col1 && cell.Coefficient == new Flt64(-2));
        model.Objective.Cells.Should().Contain(cell => cell.Col2 == -1 && cell.Coefficient == new Flt64(5));
        model.Objective.Constant.Should().Be(Flt64.One);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        (await solver.DumpAsync(mechanism, cancellation.Token))
            .Should().BeOfType<Failed<QuadraticTetradModel, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.ApplicationStopped);
        x.Range.SetRange(ValueRange<Flt64>.Of(
            lb: Flt64.Zero,
            ub: Flt64.One,
            lbInterval: new Interval.Open(),
            ubInterval: new Interval.Closed()
        ).Value);
        (await solver.DumpAsync(mechanism))
            .Should().BeOfType<Failed<QuadraticTetradModel, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.IllegalArgument);
    }

#if GUROBI_AVAILABLE
    /// <summary>编排入口保留转储错误 / Orchestration entries preserve dump failures.</summary>
    [Fact]
    public async Task ColumnGenerationAndBenders_PropagateConversionFailures() {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new RealVar("invalid-bound");
        x.Range.SetRange(ValueRange<Flt64>.Of(
            lb: Flt64.Zero,
            ub: Flt64.One,
            lbInterval: new Interval.Open(),
            ubInterval: new Interval.Closed()
        ).Value);
        parent.Add(x).IsOk.Should().BeTrue();
        var columnGeneration = new GurobiColumnGenerationSolver();
        var benders = new GurobiBendersDecompositionSolver();

        (await columnGeneration.SolveMILPAsync("invalid", parent))
            .Should().BeOfType<Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.IllegalArgument);
        (await columnGeneration.SolveMILPAsync(
            name: "invalid",
            metaModel: parent,
            amount: 2
        )).Should().BeOfType<Failed<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>), ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.IllegalArgument);
        (await columnGeneration.SolveLPAsync("invalid", parent))
            .Should().BeOfType<Failed<Fuookami.Ospf.Framework.Solver.IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.IllegalArgument);
        (await benders.SolveMasterAsync("invalid", parent))
            .Should().BeOfType<Failed<SolverOutput, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.IllegalArgument);
        (await benders.SolveSubAsync(
            name: "invalid",
            metaModel: parent,
            objectVariable: x,
            fixedVariables: new Dictionary<IVariableItem, Flt64>()
        )).Should().BeOfType<Failed<Fuookami.Ospf.Framework.Solver.IBendersDecompositionSolver.LinearSubResult, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.IllegalArgument);
    }
#endif

#if !GUROBI_AVAILABLE
    /// <summary>
    /// 缺少 SDK 时求解返回明确环境错误 / Solving returns an explicit environment error when the SDK is absent.
    /// </summary>
    [Fact]
    public async Task LinearSolver_ReportsMissingSdk() {
        var solver = new GurobiLinearSolver();
        var model = new LinearTriadModel(
            new List<ModelViewVariable>(),
            new LinearConstraintBatch(
                new SparseMatrix(new List<SparseVector>()),
                new List<ConstraintRelation>(),
                new List<Flt64>(),
                new List<string>(),
                new List<ConstraintSource>()),
            new Objective<LinearObjectiveCell>(
                ObjectCategory.Minimum,
                new List<LinearObjectiveCell>(),
                Flt64.Zero),
            "gurobi_missing_sdk");

        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await solver.InvokeAsync((ILinearTriadModelView)model);

        result.Should().BeOfType<Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>();
        ((Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>)result)
            .Error.Code.Should().Be(ErrorCode.OREngineEnvironmentLost);
        IAbstractLinearSolver reportSolver = solver;
        (await reportSolver.InvokeReportAsync(model))
            .Should().BeOfType<Failed<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.OREngineEnvironmentLost);
        (await reportSolver.InvokeReportAsync(model, 2))
            .Should().BeOfType<Failed<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.OREngineEnvironmentLost);
        (await reportSolver.SolveReportAsync(model, IIntoValue<Flt64>.Identity))
            .Should().BeOfType<Failed<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.OREngineEnvironmentLost);

        IAbstractQuadraticSolver quadratic = new GurobiQuadraticSolver();
        (await quadratic.InvokeReportAsync(null!))
            .Should().BeOfType<Failed<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.OREngineEnvironmentLost);
        (await quadratic.InvokeReportAsync(null!, 2))
            .Should().BeOfType<Failed<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.OREngineEnvironmentLost);
    }
#endif
}
