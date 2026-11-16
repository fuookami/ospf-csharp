#nullable enable

using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.Tests.Solver;

/// <summary>
/// 主/子划分的两道守卫，必须**按名字**被拒绝 / The two partition guards must be rejected **by name**.
/// </summary>
/// <remarks>
/// `LinearBendersPartition.BuildAsync` 里有两条 `Invalid(...)`：
///
/// ```csharp
/// return Invalid($"分解阶段引用未知变量 / Stage references an unknown variable: {variable.Name}");
/// return Invalid($"整数变量不能作为连续追索 / Integer variable cannot be continuous recourse: {variable.Name}");
/// ```
///
/// 第一条**零判据**；第二条此前只在 `Partition_ShouldPlaceStagedContinuousObjectiveInRecourse` 里被断言成 `invalid.IsFailed`——
/// 那个断言**什么都能通过**：划分因为别的原因失败、甚至 Fatal，它同样绿。这里把它改成**按名字**拒绝，
/// 于是"拒绝"这件事从此有了内容：拒绝的是**这一条**理由。
///
/// Two `Invalid(...)` guards sit inside `LinearBendersPartition.BuildAsync` (snippet above). The first had **no criteria**; the second was only asserted as
/// `invalid.IsFailed` elsewhere — an assertion **anything can satisfy**, including a failure for an unrelated reason or a fatal error. turns the
/// rejection into one **by name**, so "rejected" finally has content: rejected **for this reason**.
/// </remarks>
public class LinearBendersPartitionGuardTests
{
    /// <summary>**阶段引用了一个模型里没有的变量 → 点名拒绝** / A stage naming a variable the model does not contain is rejected by name.</summary>
    /// <remarks>
    /// 这条守卫挡的是**拼错变量名**：阶段表按变量身份匹配，拼错一个名字不会报错，只会让那个变量**悄悄留在错误的子问题里**，
    /// 解出来仍然"可行"，只是主/子分解从此不对。
    ///
    /// It guards against **a misspelt variable name**: stages match by variable identity, so a wrong name produces no error — the variable just **quietly stays in the
    /// wrong subproblem**, the solution still comes out "feasible", and only the decomposition is wrong.
    /// </remarks>
    [Fact]
    public async Task AStageNamingAnUnknownVariable_ShouldBeRejectedByName() {
        var placed = new BinVar("b842-placed");
        using var source = new LinearMetaModel<Flt64>(name: "b842-unknown-stage");
        source.Add(placed).IsOk.Should().BeTrue();
        source.AddObject(
            category: ObjectCategory.Minimum,
            polynomial: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, placed) },
                Flt64.Zero),
            name: "objective",
            displayName: null).IsOk.Should().BeTrue();

        // **这个变量从来没进过模型** / This variable was never added to the model.
        var neverAdded = new URealVar("b842-never-added");
        Result<LinearBendersModels, ErrorCode, Error<ErrorCode>> result =
            await LinearBendersPartition.BuildAsync(
                source: source,
                stages: new Dictionary<IVariableItem, LinearBendersStage> {
                    [neverAdded] = LinearBendersStage.ContinuousRecourse,
                });

        result.Should().BeOfType<Failed<LinearBendersModels, ErrorCode, Error<ErrorCode>>>();
        ((Failed<LinearBendersModels, ErrorCode, Error<ErrorCode>>)result).Error.Message
            .Should().Contain("未知变量", "the rejection must name the guard, not read like a generic invalid argument");
    }

    /// <summary>**整数变量被声明成连续追索 → 点名拒绝** / An integer variable declared as continuous recourse is rejected by name.</summary>
    /// <remarks>
    /// 这个组合**在数学上不成立**：连续追索要求子问题对绑定变量是线性的，整数变量做不到。
    /// 放过去的话，子问题会按"连续"解释一个二元变量，解出来的值仍然满足所有约束——**但那是另一个问题**。
    ///
    /// This combination **does not hold mathematically**: continuous recourse requires the subproblem to be linear in the bound variables, which an integer variable
    /// cannot be. Letting it through would make the subproblem read a binary variable as continuous; the values still satisfy every constraint — **of a different problem**.
    /// </remarks>
    [Fact]
    public async Task AnIntegerVariableDeclaredAsRecourse_ShouldBeRejectedByName() {
        var staged = new BinVar("b842-integer-recourse");
        using var source = new LinearMetaModel<Flt64>(name: "b842-integer-stage");
        source.Add(staged).IsOk.Should().BeTrue();
        source.AddObject(
            category: ObjectCategory.Minimum,
            polynomial: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, staged) },
                Flt64.Zero),
            name: "objective",
            displayName: null).IsOk.Should().BeTrue();

        Result<LinearBendersModels, ErrorCode, Error<ErrorCode>> result =
            await LinearBendersPartition.BuildAsync(
                source: source,
                stages: new Dictionary<IVariableItem, LinearBendersStage> {
                    [staged] = LinearBendersStage.ContinuousRecourse,
                });

        result.Should().BeOfType<Failed<LinearBendersModels, ErrorCode, Error<ErrorCode>>>();
        ((Failed<LinearBendersModels, ErrorCode, Error<ErrorCode>>)result).Error.Message
            .Should().Contain("连续追索", "the rejection must name the guard");
    }

    /// <summary>
    /// **按行归属：只有决策变量的行进主问题，含追索变量的行进子问题** /
    /// **Row ownership: a row of decisions only goes to master, a row touching recourse goes to sub.**
    /// </summary>
    /// <remarks>
    /// 这是基线 `LinearBendersPartition.build` 的分行规则（12fdb587）：`target = if (cells.all { it.token.variable in decisions }) master else sub`。
    /// **变量一律镜像进子问题**，于是"收窄追索"靠的是行，不是变量——本仓 曾把这一点读成"结构上限"，其实**两边的规则一致**。
    /// 这条判据把规则钉在行为上：主问题的行数必须等于"只含决策变量的行"条数。
    ///
    /// This is the baseline's row rule (`LinearBendersPartition.build`, 12fdb587): `target = if (cells.all { it.token.variable in decisions }) master else sub`.
    /// **Every variable is mirrored into the sub**, so narrowing the recourse is done by **rows**, not variables — misread this as a structural ceiling, while
    /// **both sides follow the same rule**. This criterion pins the rule behaviourally: the master row count must equal the number of decisions-only rows.
    /// </remarks>
    [Fact]
    public async Task RowsOfDecisionsOnly_ShouldGoToMaster_RowsTouchingRecourse_ShouldGoToSub() {
        var decision = new BinVar("b878-decision");
        var recourse = new URealVar("b878-recourse");
        using var source = new LinearMetaModel<Flt64>(name: "b878-row-ownership");
        source.Add(decision).IsOk.Should().BeTrue();
        source.Add(recourse).IsOk.Should().BeTrue();
        source.AddObject(
            category: ObjectCategory.Minimum,
            polynomial: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, decision) },
                Flt64.Zero),
            name: "objective",
            displayName: null).IsOk.Should().BeTrue();

        // 只含决策变量的行 / a row over decisions only
        source.AddConstraint(
            new LinearInequality<Flt64>(
                new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, decision) }, Flt64.Zero),
                new LinearPolynomial<Flt64>([], Flt64.Zero),
                Comparison.LE,
                "decisions_only"),
            group: null,
            name: "decisions_only").IsOk.Should().BeTrue();

        // 含追索变量的行 / a row touching the recourse variable
        source.AddConstraint(
            new LinearInequality<Flt64>(
                new LinearPolynomial<Flt64>(
                    new[] {
                        new LinearMonomial<Flt64>(Flt64.One, decision),
                        new LinearMonomial<Flt64>(Flt64.One, recourse),
                    },
                    Flt64.Zero),
                new LinearPolynomial<Flt64>([], Flt64.Zero),
                Comparison.LE,
                "touches_recourse"),
            group: null,
            name: "touches_recourse").IsOk.Should().BeTrue();

        Result<LinearBendersModels, ErrorCode, Error<ErrorCode>> result =
            await LinearBendersPartition.BuildAsync(
                source: source,
                stages: new Dictionary<IVariableItem, LinearBendersStage> {
                    [recourse] = LinearBendersStage.ContinuousRecourse,
                });
        result.IsOk.Should().BeTrue(result.ToString());

        LinearBendersModels models = ((Ok<LinearBendersModels, ErrorCode, Error<ErrorCode>>)result).Value;
        try {
            models.Master.Constraints.Should().HaveCount(1,
                "only the decisions-only row belongs to the master; the other row touches a recourse variable");
            models.Sub.Constraints.Should().HaveCountGreaterThanOrEqualTo(1);
        } finally {
            models.Dispose();
        }
    }
}
