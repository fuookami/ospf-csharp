#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Core.Solver.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Iis;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Solver.ConstraintProgramming;

/// <summary>
/// MIP 驱动的 CP 求解器 / The MIP-backed CP solver.
/// </summary>
/// <remarks>
/// B1263 实现 B1262 定下的设计。**判据要钉的两件事：拒绝的内容是否正确，以及线性解是否被翻译成 CP 解。**
///
/// B1263 implements the design B1262 laid down. **The two things the criteria must pin: is the refusal's content right, and
/// is the linear solution actually translated into a CP one.**
/// </remarks>
public class MipBackedConstraintProgrammingSolverTests
{
    /// <summary>
    /// **要求原生冲突证据时，它拒绝并点名原因** / It refuses a native-conflict request and names the cause.
    /// </summary>
    /// <remarks>
    /// **这是 B1261 的教训在这里的第二次应用。** **静默忽略会让失败以
    /// "CP 输出缺少结构化 conflict 证据"的形式出现——读起来像求解器的问题。**
    ///
    /// **判据断言信息<b>点名了 MIP 驱动这条路</b>，而不只是"失败了"。**
    /// **因为随便一条错误信息也能让它失败，而那对调用方毫无用处。**
    ///
    /// **B1261's lesson applied a second time.** **The criterion asserts the message <b>names the MIP-backed path</b>, not
    /// merely that it failed — any message would satisfy "it failed", and that would be useless to the caller.**
    /// </remarks>
    [Fact]
    public async Task ItShouldRefuseConflictCollectionByName() {
        var solver = new MipBackedConstraintProgrammingSolver(new ScriptedSolver());

        using ConstraintProgrammingModel model = new();
        Result<
            ConstraintProgrammingSolverOutput,
            ErrorCode,
            Error<ErrorCode>> result = await solver.SolveAsync(
                model,
                new ConstraintProgrammingSolveOptions(CollectConflict: true));

        result.IsOk.Should().BeFalse();
        var failed = (Failed<ConstraintProgrammingSolverOutput, ErrorCode, Error<ErrorCode>>)result;
        failed.Error.Message.Should().Contain("MIP 驱动",
            "the refusal must name this path, or the caller reads it as a solver failure");
        failed.Error.Message.Should().Contain("SCIP",
            "and it must say where native conflict evidence does come from");
    }

    /// <summary>
    /// **线性解被翻译成 CP 解，并且只含 CP 变量** / The linear solution becomes a CP solution with only CP variables.
    /// </summary>
    /// <remarks>
    /// **这条判据的价值在于它<b>同时</b>检查了两件容易各自出错的事：**
    ///
    /// **① 列的定位按引用匹配 `Origin`**（不是按下标）——**下标会被降阶辅助变量挤开。**
    /// **② 结果里<b>只有</b> CP 变量，辅助变量不外露**——**否则调用方会看到一堆它没定义过的变量。**
    ///
    /// **只检查解值的对错会漏掉②：一个把辅助变量也塞进去的解，值可能全对，而形状是错的。**
    ///
    /// **The value here is that it checks two things that fail independently: ① columns are located by <b>reference</b> on
    /// `Origin` (not by index — indices get displaced by lowering auxiliaries), and ② the result contains <b>only</b> CP
    /// variables (otherwise the caller sees variables it never declared).**
    /// **Checking only the values would miss ②: a solution that also carries auxiliaries can have every value right and the
    /// wrong shape.**
    /// </remarks>
    [Fact]
    public async Task ItShouldTranslateTheLinearSolutionIntoACpSolution() {
        var solver = new MipBackedConstraintProgrammingSolver(new ScriptedSolver(feasible: true));

        using ConstraintProgrammingModel model = new();
        Register(model, "x");
        Register(model, "y");

        Result<
            ConstraintProgrammingSolverOutput,
            ErrorCode,
            Error<ErrorCode>> result = await solver.SolveAsync(
                model,
                new ConstraintProgrammingSolveOptions());

        bool ok = result.IsOk;

        ok.Should().BeTrue(result.ToString());
        var output = (ConstraintProgrammingFeasibleOutput)
            ((Ok<ConstraintProgrammingSolverOutput, ErrorCode, Error<ErrorCode>>)result).Value;

        output.Solution.Values.Should().HaveCount(
            2,
            "exactly the two CP variables — no lowering auxiliaries may leak into a CP solution");
        foreach (Fuookami.Ospf.Math.Algebra.Number.Int64 value in output.Solution.Values.Values) {
            value.Should().Be(new Fuookami.Ospf.Math.Algebra.Number.Int64(3));
        }
    }

    /// <summary>
    /// **不可行时不报"没有解"，而报不可行** / Infeasible is reported as infeasible, not as a missing solution.
    /// </summary>
    /// <remarks>
    /// **这两种情况在调用方那里修法相反：** **不可行是模型的性质（要么改模型，要么接受），
    /// "非不可行却拿不到解"是后端或映射的缺陷。**
    ///
    /// **把后者报成前者，会让一个真正的 bug 看起来像一次正常的不可行结论。**
    ///
    /// **The two have opposite fixes on the caller's side: infeasibility is a property of the model, while
    /// "non-infeasible but no solution" is a defect of the backend or the mapping.** **Reporting the latter as the former makes
    /// a real bug look like a normal infeasibility verdict.**
    /// </remarks>
    [Fact]
    public async Task Infeasible_ShouldNotBeReportedAsAMissingSolution() {
        var solver = new MipBackedConstraintProgrammingSolver(new ScriptedSolver(infeasible: true));

        using ConstraintProgrammingModel model = new();
        Register(model, "x");

        Result<
            ConstraintProgrammingSolverOutput,
            ErrorCode,
            Error<ErrorCode>> result = await solver.SolveAsync(
                model,
                new ConstraintProgrammingSolveOptions());

        bool ok = result.IsOk;

        ok.Should().BeTrue(result.ToString());
        ((Ok<ConstraintProgrammingSolverOutput, ErrorCode, Error<ErrorCode>>)result).Value
            .Should().BeOfType<ConstraintProgrammingInfeasibleOutput>();
    }

    private static void Register(ConstraintProgrammingModel model, string name) {
        Result<VariableId, ErrorCode, Error<ErrorCode>> registered = model.RegisterVariable(
            variable: new IntVar(name),
            domain: IntegerDomain.CreateInterval(
                lowerBound: new Fuookami.Ospf.Math.Algebra.Number.Int64(0),
                upperBound: new Fuookami.Ospf.Math.Algebra.Number.Int64(10)).Value);
        bool ok = registered.IsOk;
        ok.Should().BeTrue(registered.ToString());
    }

    private sealed class ScriptedSolver : IAbstractLinearSolver {
        private readonly bool _feasible;
        private readonly bool _infeasible;

        public ScriptedSolver(bool feasible = false, bool infeasible = false) {
            _feasible = feasible;
            _infeasible = infeasible;
        }

        public string Name => "scripted";

        public SolverDescriptor? Descriptor { get; } = new(
            SolverId: "scripted",
            BackendName: "Scripted",
            Capabilities: new SolverCapabilities(ModelTypes: new HashSet<SolverModelType>()));

        public Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeReportAsync(
            ILinearTriadModelView model,
            SolvingStatusCallBack? solvingStatusCallBack = null,
            CancellationToken cancellationToken = default) {
            if (_infeasible) {
                return Task.FromResult(Results.Ok(new SolveReport<Flt64> {
                    ProblemStatus = ProblemStatus.Infeasible,
                    TerminationReason = TerminationReason.Completed,
                    SolutionPresence = SolutionPresence.None,
                }));
            }

            if (!_feasible) {
                return Task.FromResult(Results.Ok(new SolveReport<Flt64> {
                    ProblemStatus = ProblemStatus.Feasible,
                    TerminationReason = TerminationReason.Completed,
                    SolutionPresence = SolutionPresence.Incumbent,
                }));
            }

            var values = new List<Flt64>();
            foreach (ModelViewVariable variable in model.Variables) {
                values.Add(new Flt64(3));
            }
            return Task.FromResult(Results.Ok(new SolveReport<Flt64> {
                ProblemStatus = ProblemStatus.Feasible,
                TerminationReason = TerminationReason.Completed,
                SolutionPresence = SolutionPresence.Incumbent,
                Solution = new SolveSolution<Flt64>(new Solution<Flt64>(values)),
            }));
        }

        public IReadOnlyList<IInfeasibilityAnalyzer<ILinearTriadModelView>> DiagnosticAnalyzers(
            IisConfig config) => Array.Empty<IInfeasibilityAnalyzer<ILinearTriadModelView>>();

        public Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
            ILinearTriadModelView model,
            SolvingStatusCallBack? solvingStatusCallBack = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("本测试未使用 / unused by this test");

        public Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> InvokeAsync(
            LinearTriadModel model,
            SolvingStatusCallBack? solvingStatusCallBack = null,
            IisConfig? iisConfig = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("本测试未使用 / unused by this test");

        public Task<Result<
            (FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions),
            ErrorCode,
            Error<ErrorCode>>> InvokeAsync(
            ILinearTriadModelView model,
            ulong solutionAmount,
            SolvingStatusCallBack? solvingStatusCallBack = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("本测试未使用 / unused by this test");

        public Task<Result<LinearTriadModel, ErrorCode, Error<ErrorCode>>> DumpAsync(
            LinearMechanismModel<Flt64> model,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("本测试未使用 / unused by this test");

        public Task<Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>> DumpAsync(
            LinearMetaModel<Flt64> model,
            RegistrationStatusCallBack? registrationStatusCallBack,
            MechanismModelDumpingStatusCallBack? dumpingStatusCallBack,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("本测试未使用 / unused by this test");
    }
}
