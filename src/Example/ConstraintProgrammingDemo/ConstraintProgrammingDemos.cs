#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.ConstraintProgrammingDemo;

/// <summary>
/// 直接使用 Core CP 的示例（确定性 fake 求解器） / Direct core CP demo using a deterministic fake solver.
/// </summary>
/// <remarks>
/// **对应的 `DirectConstraintProgrammingDemo`**。
/// **Mirrors `DirectConstraintProgrammingDemo` from its.**
/// </remarks>
public static class DirectConstraintProgrammingDemo {
    /// <summary>构造并求解一个小型全异模型 / Build and solve a small all-different model.</summary>
    /// <returns>CP 求解输出，或结构化的建模/求解错误 / CP solver output, or a structured modelling/solving error.</returns>
    public static async Task<Result<
        ConstraintProgrammingSolverOutput,
        ErrorCode,
        Error<ErrorCode>>> InvokeAsync(
        CancellationToken cancellationToken = default) {
        var model = new ConstraintProgrammingModel(
            name: "direct-cp-demo",
            objectCategory: ObjectCategory.Minimum);
        IntVar[] values = {
            new("cp-a"),
            new("cp-b"),
            new("cp-c"),
        };
        Result<IntegerDomain.Interval, ErrorCode, Error<ErrorCode>> domain =
            IntegerDomain.CreateInterval(
                new Int64(0),
                new Int64(values.Length - 1));
        if (domain is Failed<IntegerDomain.Interval, ErrorCode, Error<ErrorCode>> domainFailed) {
            return Results.Failed<ConstraintProgrammingSolverOutput>(domainFailed.Error);
        }
        IntegerDomain interval = domain.Value;
        var expressions = new List<ConstraintProgrammingExpression>(values.Length);
        foreach (IntVar variable in values) {
            Result<VariableId, ErrorCode, Error<ErrorCode>> registered =
                model.RegisterVariable(variable, domain: interval);
            if (registered is Failed<VariableId, ErrorCode, Error<ErrorCode>> registerFailed) {
                return Results.Failed<ConstraintProgrammingSolverOutput>(registerFailed.Error);
            }
            Result<ConstraintProgrammingExpression.Variable, ErrorCode, Error<ErrorCode>>
                expression = ConstraintProgrammingExpression.CreateVariable(variable, interval);
            if (expression is Failed<
                    ConstraintProgrammingExpression.Variable,
                    ErrorCode,
                    Error<ErrorCode>> expressionFailed) {
                return Results.Failed<ConstraintProgrammingSolverOutput>(expressionFailed.Error);
            }
            expressions.Add(expression.Value);
        }
        Result<
            ConstraintProgrammingConstraint.AllDifferent,
            ErrorCode,
            Error<ErrorCode>> allDifferent =
            ConstraintProgrammingConstraint.CreateAllDifferent(expressions);
        if (allDifferent is Failed<
                ConstraintProgrammingConstraint.AllDifferent,
                ErrorCode,
                Error<ErrorCode>> allDifferentFailed) {
            return Results.Failed<ConstraintProgrammingSolverOutput>(allDifferentFailed.Error);
        }
        Result<ConstraintId, ErrorCode, Error<ErrorCode>> added = model.AddConstraint(
            allDifferent.Value,
            id: new ConstraintId("direct-all-different"));
        if (added is Failed<ConstraintId, ErrorCode, Error<ErrorCode>> addFailed) {
            return Results.Failed<ConstraintProgrammingSolverOutput>(addFailed.Error);
        }
        var solver = new FakeConstraintProgrammingSolver();
        return await solver
            .SolveAsync(model, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>
/// framework CP 示例的领域聚合 / Domain aggregation for the framework CP demo.
/// </summary>
public sealed class CpDemoAggregation {
    /// <summary>创建聚合 / Create the aggregation.</summary>
    /// <param name="selection">二值选择变量；为空时使用默认两个变量 / Binary selection variables; defaults to two variables when empty.</param>
    public CpDemoAggregation(IEnumerable<BinVar>? selection = null) {
        Selection = Array.AsReadOnly((selection ?? new[] {
            new BinVar("cp-selection-a"),
            new BinVar("cp-selection-b"),
        }).ToArray());
    }

    /// <summary>二值选择变量 / Binary selection variables.</summary>
    public IReadOnlyList<BinVar> Selection { get; }

    /// <summary>注册聚合变量 / Register the aggregation variables.</summary>
    /// <param name="model">接收变量的 CP 模型 / CP model receiving the variables.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public Try Register(ConstraintProgrammingModel model) {
        ArgumentNullException.ThrowIfNull(model);
        foreach (BinVar variable in Selection) {
            Result<VariableId, ErrorCode, Error<ErrorCode>> registered =
                model.RegisterVariable(variable);
            if (registered is Failed<VariableId, ErrorCode, Error<ErrorCode>> failed) {
                return Results.Failed<Success>(failed.Error);
            }
        }
        return TryFactory.Create();
    }
}

/// <summary>
/// 强制恰好选择一个变量的领域管线 / A domain pipeline enforcing exactly one selection.
/// </summary>
public sealed class CpDemoSelectionPipeline : IConstraintProgrammingPipeline {
    private readonly CpDemoAggregation _aggregation;

    /// <summary>创建管线 / Create the pipeline.</summary>
    /// <param name="aggregation">提供变量的示例聚合 / The aggregation supplying the variables.</param>
    public CpDemoSelectionPipeline(CpDemoAggregation aggregation) {
        ArgumentNullException.ThrowIfNull(aggregation);
        _aggregation = aggregation;
    }

    /// <inheritdoc/>
    public string Name => "cp-demo-selection";

    /// <inheritdoc/>
    public Try Register(ConstraintProgrammingModel model) {
        ArgumentNullException.ThrowIfNull(model);
        var literals = _aggregation.Selection
            .Select(variable => (BooleanLiteral)new BooleanLiteral.VariableLiteral(variable))
            .ToList();
        Result<ConstraintProgrammingConstraint.BoolXor, ErrorCode, Error<ErrorCode>> constraint =
            ConstraintProgrammingConstraint.CreateBoolXor(literals);
        if (constraint is Failed<
                ConstraintProgrammingConstraint.BoolXor,
                ErrorCode,
                Error<ErrorCode>> constraintFailed) {
            return Results.Failed<Success>(constraintFailed.Error);
        }
        Result<ConstraintId, ErrorCode, Error<ErrorCode>> added = model.AddConstraint(
            constraint.Value,
            id: new ConstraintId("cp-demo-exactly-one"),
            name: Name);
        return added is Failed<ConstraintId, ErrorCode, Error<ErrorCode>> addFailed
            ? Results.Failed<Success>(addFailed.Error)
            : TryFactory.Create();
    }
}

/// <summary>
/// 持有聚合与管线的 framework 风格 Context / Framework-style context owning the aggregation and pipelines.
/// </summary>
public sealed class CpDemoContext {
    /// <summary>创建 Context / Create the context.</summary>
    /// <param name="aggregation">待注册的示例聚合 / Aggregation to register.</param>
    /// <param name="pipelines">注册管线；为空时使用默认选择管线 / Registration pipelines; defaults to the selection pipeline.</param>
    public CpDemoContext(
        CpDemoAggregation? aggregation = null,
        IEnumerable<IConstraintProgrammingPipeline>? pipelines = null) {
        Aggregation = aggregation ?? new CpDemoAggregation();
        Pipelines = Array.AsReadOnly((pipelines ?? new IConstraintProgrammingPipeline[] {
            new CpDemoSelectionPipeline(Aggregation),
        }).ToArray());
    }

    /// <summary>示例聚合 / The demo aggregation.</summary>
    public CpDemoAggregation Aggregation { get; }

    /// <summary>注册管线 / The registration pipelines.</summary>
    public IReadOnlyList<IConstraintProgrammingPipeline> Pipelines { get; }

    /// <summary>构造并注册 CP 模型 / Build a registered CP model.</summary>
    /// <returns>已注册模型，或结构化注册错误 / A registered model, or a structured registration error.</returns>
    public Result<ConstraintProgrammingModel, ErrorCode, Error<ErrorCode>> BuildModel() {
        var model = new ConstraintProgrammingModel(name: "framework-cp-demo");
        Try registered = Aggregation.Register(model);
        if (registered.IsFailed) {
            return Results.Failed<ConstraintProgrammingModel>(
                ((Failed<Success, ErrorCode, Error<ErrorCode>>)registered).Error);
        }
        Try pipelineResult = Pipelines.RegisterConstraintProgramming(model);
        return pipelineResult.IsFailed
            ? Results.Failed<ConstraintProgrammingModel>(
                ((Failed<Success, ErrorCode, Error<ErrorCode>>)pipelineResult).Error)
            : Results.Ok(model);
    }
}

/// <summary>
/// Benders 示例使用的子问题管线 / Subproblem pipeline used by the Benders demo.
/// </summary>
public sealed class CpDemoSubproblemPipeline : IBendersSubproblemPipeline {
    private readonly BinVar _variable;

    /// <summary>创建子问题管线 / Create the subproblem pipeline.</summary>
    /// <param name="variable">子问题要求的 CP 变量 / The CP variable required by the subproblem.</param>
    public CpDemoSubproblemPipeline(BinVar variable) {
        ArgumentNullException.ThrowIfNull(variable);
        _variable = variable;
    }

    /// <inheritdoc/>
    public Try Register(ConstraintProgrammingModel model, IBendersVariableBinding binding) {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(binding);
        Result<ConstraintProgrammingExpression.Variable, ErrorCode, Error<ErrorCode>> expression =
            ConstraintProgrammingExpression.CreateVariable(_variable);
        if (expression is Failed<
                ConstraintProgrammingExpression.Variable,
                ErrorCode,
                Error<ErrorCode>> expressionFailed) {
            return Results.Failed<Success>(expressionFailed.Error);
        }
        Result<ConstraintProgrammingConstraint.IntegerComparison, ErrorCode, Error<ErrorCode>>
            constraint = ConstraintProgrammingConstraint.CreateEqual(
                expression.Value,
                Int64.One);
        if (constraint is Failed<
                ConstraintProgrammingConstraint.IntegerComparison,
                ErrorCode,
                Error<ErrorCode>> constraintFailed) {
            return Results.Failed<Success>(constraintFailed.Error);
        }
        Result<ConstraintId, ErrorCode, Error<ErrorCode>> added = model.AddConstraint(
            constraint.Value,
            id: new ConstraintId("benders-required-selection"));
        return added is Failed<ConstraintId, ErrorCode, Error<ErrorCode>> addFailed
            ? Results.Failed<Success>(addFailed.Error)
            : TryFactory.Create();
    }
}

/// <summary>
/// 一个两轮收敛的二值主问题 Logic-Based Benders 示例 / A two-iteration binary-master Logic-Based Benders demo.
/// </summary>
public static class LogicBasedBendersDemo {
    /// <summary>构造并求解示例 / Build and solve the demo.</summary>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>Benders 报告，或结构化建模/求解错误 / The Benders report, or a structured modelling/solving error.</returns>
    public static async Task<Result<
        LogicBasedBendersReport,
        ErrorCode,
        Error<ErrorCode>>> InvokeAsync(
        CancellationToken cancellationToken = default) {
        var masterVariable = new BinVar("master-selection");
        using var master = new LinearMetaModel<Flt64>(name: "benders-master");
        Result<Success, ErrorCode, Error<ErrorCode>> masterAdded =
            master.Add(masterVariable);
        if (masterAdded is Failed<Success, ErrorCode, Error<ErrorCode>> addFailed) {
            return Results.Failed<LogicBasedBendersReport>(addFailed.Error);
        }

        var subproblemVariable = new BinVar("subproblem-selection");
        var subproblem = new ConstraintProgrammingModel(name: "benders-subproblem");
        Result<VariableId, ErrorCode, Error<ErrorCode>> registered =
            subproblem.RegisterVariable(subproblemVariable);
        if (registered is Failed<VariableId, ErrorCode, Error<ErrorCode>> registerFailed) {
            return Results.Failed<LogicBasedBendersReport>(registerFailed.Error);
        }
        var binding = new BinaryBendersVariableBinding(new[] {
            new BinaryBendersVariable(
                Key: "selection",
                MasterVariable: masterVariable,
                SubproblemVariable: subproblemVariable),
        });
        Try pipelineResult = new CpDemoSubproblemPipeline(subproblemVariable)
            .Register(subproblem, binding);
        if (pipelineResult.IsFailed) {
            return Results.Failed<LogicBasedBendersReport>(
                ((Failed<Success, ErrorCode, Error<ErrorCode>>)pipelineResult).Error);
        }

        int masterCalls = 0;
        Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> MasterSolver(
            LinearMetaModel<Flt64> model) {
            // **第一轮给 0 触发冲突割，第二轮给 1 收敛**：这正是基线示例的脚本化主问题。
            // **Round one returns 0 to trigger a conflict cut, round two returns 1 to converge**: this is its scripted master.
            Flt64 value = masterCalls++ == 0 ? Flt64.Zero : Flt64.One;
            return Task.FromResult(Results.Ok(new SolveReport<Flt64> {
                ProblemStatus = ProblemStatus.Feasible,
                TerminationReason = TerminationReason.Completed,
                SolutionPresence = SolutionPresence.Optimal,
                Solution = new SolveSolution<Flt64>(
                    Values: new Solution<Flt64>(new[] { value }),
                    Objective: Flt64.Zero),
                Proof = new SolveProof(ProofStatus.Verified, "demo-master"),
                Statistics = new SolveStatistics(
                    SolveTime: TimeSpan.Zero,
                    BestBound: Flt64.Zero),
            }));
        }

        var engine = new LogicBasedBendersEngine(
            masterSolver: MasterSolver,
            subproblemSolver: new FakeConstraintProgrammingSolver(),
            binding: binding,
            options: new LogicBasedBendersOptions {
                ProofMode = BendersProofMode.Exact,
                CancellationToken = cancellationToken,
                MasterSolutionSource = output => Results.Ok<IConstraintProgrammingValueSource>(
                    MapConstraintProgrammingValueSource.Of(
                        new Dictionary<string, Flt64>(StringComparer.Ordinal) {
                            ["selection"] = output.Solution!.Values[0],
                        })),
            });
        return await engine.SolveAsync(master, subproblem).ConfigureAwait(false);
    }
}
