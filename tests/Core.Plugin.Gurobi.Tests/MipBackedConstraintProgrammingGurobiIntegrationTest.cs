#nullable enable
#if GUROBI_INTEGRATION

using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;

namespace Fuookami.Ospf.Core.Plugin.Gurobi.Tests;

/// <summary>
/// **MIP 驱动的 CP 在真实 Gurobi 后端上端到端跑通** /
/// The MIP-backed CP path runs end to end on a real Gurobi backend.
/// </summary>
/// <remarks>
/// **这里只用一个脚本化假求解器证明了"线性解被翻译成 CP 解"。那证明的是翻译那一层，不证明整条路。**
///
/// **本仓有一条反复学到的教训：** **逐层绿什么也证明不了，直到有一次真实的端到端运行。**
/// **假求解器返回的是我自己构造的 `SolveReport`，它不可能暴露
/// "降阶出来的模型 Gurobi 解不了"或"列索引对不上"这类问题。**
///
/// **所以这一条判据的价值不是再加一个测试，而是<b>把 那句话变成实测过的</b>：**
/// **"任意 MIP 后端都能当 CP 求解器"——`任意` 这个词，只有在一个真实后端上跑过之后才敢说。**
///
/// **The translation was proved with a scripted fake; a fake returns a `SolveReport` I built myself and cannot expose
/// "Gurobi cannot solve the lowered model" or "the column indices do not line up".** **So this criterion's value is turning
/// the earlier sentence into something measured: the word "any" is only earned on a real backend.**
/// </remarks>
public class MipBackedConstraintProgrammingGurobiIntegrationTest
{
    /// <summary>
    /// **一个真实 CP 模型经 MIP 驱动求解后拿到正确的解** /
    /// A real CP model solved through the MIP-backed path yields the right solution.
    /// </summary>
    /// <remarks>
    /// **模型刻意简单，断言刻意具体：** **`x + y = 7`、`x = 3`、两者都在 `0..10`，唯一解是 `x = 3, y = 4`。**
    ///
    /// **为什么不用"能求出解就行"这种断言：** **那对"列索引错位"完全免疫——**
    /// **错位后仍然能解出某个解，只是把值安到了别的变量上。按 ID 核对到具体值，才让错位暴露出来。**
    ///
    /// ### 第一版这条判据写错了，而错的方式值得记
    ///
    /// **第一版用的是 `x ≤ 3` 而不是 `x = 3`，并断言 `x == 3`。**
    /// **它红了，报 `x = 0`。我一度以为"约束被静默丢掉了"。**
    ///
    /// **实际上 `x = 0, y = 7` 是完全合法的解：** **它满足 `x + y = 7`，也满足 `x ≤ 3`。**
    /// **没有目标函数时，求解器返回其中任意一个可行点都是对的。**
    ///
    /// **所以红的是<b>我的断言</b>，不是代码。** **而这条判据当时没有说清它需要唯一性。**
    /// **把约束改成等式，判据才真正在测我想测的东西——**
    /// **"约束被降阶进了模型"这件事，只有在解必须唯一时才能由一个具体值证明。**
    ///
    /// **The first version of this criterion was wrong, and the way it was wrong is worth recording: it used `x &lt;= 3` and
    /// asserted `x == 3`, went red reporting `x = 0`, and I briefly believed "the constraint is silently dropped". In fact
    /// `x = 0, y = 7` is a perfectly legal solution — it satisfies both `x + y = 7` and `x &lt;= 3`, and with no objective any
    /// feasible point is a correct answer.** **So the red was <b>my assertion</b>, not the code, and the criterion had failed to
    /// state that it needs uniqueness.** **Making the bound an equality is what finally tests the intended thing: "the
    /// constraint reached the model" can only be proven by a specific value when the solution must be unique.**
    /// </remarks>
    [Fact]
    public async Task ARealCpModel_ShouldBeSolvedThroughTheMipBackedPath() {
        var x = new IntVar("x");
        var y = new IntVar("y");
        using ConstraintProgrammingModel model = new();
        VariableId xId = Register(model, x);
        VariableId yId = Register(model, y);

        var sum = ConstraintProgrammingExpression.CreateLinear(new[] {
            new ConstraintProgrammingExpression.Term(x, new Int64(1), xId),
            new ConstraintProgrammingExpression.Term(y, new Int64(1), yId),
        }).Value;
        var onlyX = ConstraintProgrammingExpression.CreateLinear(new[] {
            new ConstraintProgrammingExpression.Term(x, new Int64(1), xId),
        }).Value;

        bool addedSum = model.AddConstraint(
            ConstraintProgrammingConstraint.CreateEqual(sum, new Int64(7)).Value).IsOk;
        addedSum.Should().BeTrue("x + y = 7 must be accepted");
        bool pinnedX = model.AddConstraint(
            ConstraintProgrammingConstraint.CreateEqual(onlyX, new Int64(3)).Value).IsOk;
        pinnedX.Should().BeTrue("x = 3 must be accepted");

        var solver = new MipBackedConstraintProgrammingSolver(new GurobiLinearSolver());
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

        output.Solution.Values.Should().HaveCount(2,
            "exactly the two CP variables — no lowering auxiliaries may leak out");
        output.Solution.Values[xId].Should().Be(
            new Int64(3), "x is pinned to 3 by its own constraint");
        output.Solution.Values[yId].Should().Be(
            new Int64(4), "and x + y = 7 then forces y = 4");
    }

    private static VariableId Register(ConstraintProgrammingModel model, IntVar variable) {
        Result<VariableId, ErrorCode, Error<ErrorCode>> registered = model.RegisterVariable(
            variable: variable,
            domain: IntegerDomain.CreateInterval(
                lowerBound: new Int64(0),
                upperBound: new Int64(10)).Value);
        bool ok = registered.IsOk;
        ok.Should().BeTrue(registered.ToString());
        return ((Ok<VariableId, ErrorCode, Error<ErrorCode>>)registered).Value;
    }
}
#endif
