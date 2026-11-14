#if GUROBI_INTEGRATION
#nullable enable

using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Gurobi.Tests;

/// <summary>
/// **扰动在真实 Gurobi 上跑通一次** / Perturbation runs once on real Gurobi.
/// </summary>
/// <remarks>
/// **B1283 补的是基线 `gurobiPerturbationBackend` 的对应物。而这条判据比工厂本身更重要：**
/// **工厂只是"给一个配置好的求解器"，而<b>扰动这条路到底能不能在真实后端上走完，
/// 此前没有任何证据**。
///
/// **断言两件事：** **① 报告回得来，且阶段列表非空（不是"全都跳过"）；
/// ② 求解次数大于零（扰动就是"改 RHS 再解"，零次意味着什么都没试）。**
///
/// **The factory is only "hand back a configured solver"; whether the perturbation path can run to completion on a real backend had
/// no evidence at all before this.** **Two assertions: a report comes back with a non-empty stage list (not "everything skipped"),
/// and the solve count is positive — perturbation *is* re-solving with a displaced RHS, so zero means nothing was tried.**
/// </remarks>
public class GurobiPerturbationIntegrationTest
{
    [Fact]
    public async Task ThePerturbationPipeline_ShouldRunOnRealGurobi() {
        ConstraintProgrammingModelSnapshot snapshot = BuildSnapshot();
        var solver = GurobiPerturbationBackend.Create();

        var report = await ConstraintPerturbationPipeline.AnalyzeAsync(
            snapshot: snapshot,
            constraintId: snapshot.Constraints.First().Id,
            baselineSolution: null,
            baselineObjective: null,
            // **能力矩阵从求解器自己的描述符来，而不是手写一份。**
            // **手写一份等于声明"我认为这个后端能做什么"，而判据要验的正是"它实际做得到"。**
            //
            // **The matrix comes from the solver's own descriptor rather than being written by hand: a hand-written one would
            // declare "what I think this backend can do", while the criterion is about whether it actually can.**
            capabilityMatrix: CapabilityMatrix.From(solver.Descriptor),
            policy: new ConstraintPerturbationPolicy(),
            solver: solver);

        report.IsOk.Should().BeTrue("a real Gurobi solve must be able to answer a perturbation question");
        // **扰动就是"把 RHS 挪一挪再解一次"，所以观测列表非空才是"真的试过了"。**
        // **空列表不是"敏感度为零"，而是"什么都没试"——两者在报告里必须分得清。**
        //
        // **Perturbation is "displace the RHS and solve again", so a non-empty observation list is what "it really tried" means.**
        // **An empty list is not "sensitivity is zero" but "nothing was attempted", and the two must be distinguishable in a report.**
        report.Value!.ObservationList.Should().NotBeEmpty(
            "a displacement was requested, so the backend must have tried at least one");

        report.Value.ObservationList.Count(observation => observation.PerturbedRhs is not null)
            .Should().BeGreaterThan(
                0,
                "each observation has to state the RHS it tried, otherwise the report says nothing about what was solved");
    }

    private static ConstraintProgrammingModelSnapshot BuildSnapshot() {
        using var model = new ConstraintProgrammingModel(
            name: "b1283", objectCategory: ObjectCategory.Minimum);
        var variable = new IntVar("x");
        IntegerDomain domain = IntegerDomain.CreateInterval(Int64.Zero, new Int64(10)).Value;
        var expression = new ConstraintProgrammingExpression.Variable(variable, domain, X);

        model.RegisterVariable(id: X, variable: variable, domain: domain,
            scope: ModelElementScope.Stable, origin: "fixture/b1283").Value.Should().NotBeNull();
        model.RegisterExpression("x", expression).Value.Should().NotBeNull();
        model.AddConstraint(
            ConstraintProgrammingConstraint.CreateGreaterOrEqual(expression, Int64.Zero).Value,
            "constraint:b1283").Value.Should().NotBeNull();
        model.Minimize(expression: expression, id: new ObjectiveId("objective:main")).Value.Should().NotBeNull();

        return model.Snapshot().Value;
    }

    private static readonly VariableId X = new("source:x");
}
#endif
