// P9 约束编程示例测试 / Tests for the constraint-programming demos.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Example.ConstraintProgrammingDemo;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.Tests.ConstraintProgrammingDemo;

/// <summary>
/// 约束编程示例 / The constraint-programming demos.
/// </summary>
public sealed class ConstraintProgrammingDemoTests {
    /// <summary>**直接 CP 示例能求出全异解** / The direct CP demo solves the all-different model.</summary>
    /// <remarks>
    /// 断言**三个变量的值两两不同**，而不是"求解成功"——三变量全异域是 `{0,1,2}`，
    /// 唯一解就是把三个值各占一次；断言成功会让"全异约束根本没注册"照样通过。
    ///
    /// Asserts that **the three values are pairwise distinct**, not that "solving succeeded" — with three variables over `{0,1,2}` the only solution assigns
    /// each value once; asserting success would let "the all-different constraint was never registered" pass.
    /// </remarks>
    [Fact]
    public async Task DirectDemo_ShouldSolveAllDifferent() {
        Result<ConstraintProgrammingSolverOutput, ErrorCode, Error<ErrorCode>> result =
            await DirectConstraintProgrammingDemo.InvokeAsync();

        result.IsOk.Should().BeTrue(result.ToString());
        ConstraintProgrammingFeasibleOutput feasible = result.Value
            .Should().BeOfType<ConstraintProgrammingFeasibleOutput>().Subject;
        IReadOnlyCollection<long> values = feasible.Solution.Values
            .Values.Select(value => value.ToLong()).ToList();
        values.Should().HaveCount(3);
        values.Distinct().Should().HaveCount(3, "三个变量必须两两不同 / the three variables must be pairwise distinct");
    }

    /// <summary>**framework Context 注册出恰好一个约束** / The framework context registers exactly one constraint.</summary>
    [Fact]
    public void Context_ShouldRegisterExactlyOneSelectionConstraint() {
        var context = new CpDemoContext();

        Result<ConstraintProgrammingModel, ErrorCode, Error<ErrorCode>> built =
            context.BuildModel();

        built.IsOk.Should().BeTrue(built.ToString());
        ConstraintProgrammingModel model = built.Value;
        model.ConstraintCount.Should().Be(1);
        ConstraintProgrammingModelSnapshot snapshot = model.Snapshot().Value;
        snapshot.Constraints.Should().ContainSingle();
        snapshot.Constraints[0].Constraint.Should()
            .BeOfType<ConstraintProgrammingConstraint.BoolXor>();
        snapshot.Constraints[0].Constraint.Variables.Should().HaveCount(2);
    }

    /// <summary>**管线中途失败时后面的管线不再执行** / A failing pipeline stops the ones after it.</summary>
    /// <remarks>
    /// 继续执行会让后续管线在缺失的前置注册之上继续建模，**失败点从第一个真实错误漂移到某个无关的"变量不存在"**。
    ///
    /// Continuing would let later pipelines model on top of a missing prerequisite, **moving the failure point from the first real error to an unrelated
    /// "variable does not exist"**.
    /// </remarks>
    [Fact]
    public void Pipelines_ShouldStopAtFirstFailure() {
        var ran = new List<string>();
        var pipelines = new IConstraintProgrammingPipeline[] {
            new RecordingPipeline("first", ran, fail: true),
            new RecordingPipeline("second", ran, fail: false),
        };

        var model = new ConstraintProgrammingModel(name: "pipeline-stop");
        Try result = pipelines.RegisterConstraintProgramming(model);

        result.IsFailed.Should().BeTrue();
        ran.Should().Equal(
            new[] { "first" },
            "后续管线不得执行 / later pipelines must not run");
    }

    /// <summary>**子问题管线的默认解提取是空操作** / The default extraction of a subproblem pipeline is a no-op.</summary>
    /// <remarks>
    /// 默认报"未实现"会让"这个管线不需要提取"和"这个管线忘了实现提取"变成同一个错误。
    ///
    /// Defaulting to "not implemented" would make "this pipeline needs no extraction" and "this pipeline forgot to implement extraction" the same error.
    /// </remarks>
    [Fact]
    public void SubproblemPipeline_ShouldExtractNothingByDefault() {
        IBendersSubproblemPipeline pipeline =
            new CpDemoSubproblemPipeline(new BinVar("cp-default-extract"));

        Try extracted = pipeline.ExtractSolution(new ConstraintProgrammingSolution());

        extracted.IsOk.Should().BeTrue(
            "默认提取必须成功且什么也不做 / the default extraction must succeed and do nothing");
    }

    /// <summary>**Logic-Based Benders 示例两轮收敛并产生一条割** / The Logic-Based Benders demo converges in two rounds with one cut.</summary>
    [Fact]
    public async Task LogicBasedBendersDemo_ShouldConvergeAfterConflictCut() {
        Result<LogicBasedBendersReport, ErrorCode, Error<ErrorCode>> result =
            await LogicBasedBendersDemo.InvokeAsync();

        result.IsOk.Should().BeTrue(result.ToString());
        LogicBasedBendersReport report = result.Value;
        report.Iterations.Should().HaveCount(2);
        report.Cuts.Should().ContainSingle();
        report.Cuts[0].Kind.Should().Be(BendersCutKind.Conflict);
        report.Proof.Status.Should().Be(ProofStatus.Verified);
    }

    private sealed class RecordingPipeline : IConstraintProgrammingPipeline {
        private readonly string _name;
        private readonly List<string> _ran;
        private readonly bool _fail;

        internal RecordingPipeline(string name, List<string> ran, bool fail) {
            _name = name;
            _ran = ran;
            _fail = fail;
        }

        public string Name => _name;

        public Try Register(ConstraintProgrammingModel model) {
            _ran.Add(_name);
            return _fail
                ? Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.IllegalArgument, "boom"))
                : TryFactory.Create();
        }
    }
}
