// 默认长度边界派生的判据 / Criteria for default length-bound derivation.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Framework.Csp1d.Application.Model;
using Fuookami.Ospf.Framework.Csp1d.Application.Service;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.LengthAssignment.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **未显式指定上下界时，动态产品的边界必须被推导出来** / When bounds are unspecified, dynamic products must get derived ones.
/// </summary>
/// <remarks>
/// **记的缺口**："`ResolveDefaultLengthBounds` 的派生逻辑无判据——它是 `private static`，
/// 要走完整求解路径才能观察。"
///
/// 这里走 `SolveAsync` + mock 求解器，从 `MilpResult.Model` 里**看变量在不在**——
/// 派生成功 ⇒ `assigned_length_*` 与 `over_length_*` 变量被建出来并被约束；
/// 派生失败 ⇒ 变量根本不出现（因为 `BuildAssignedLength` 要求 `hasBound || hasPenalty`）。
///
/// **the earlier gap**: "`ResolveDefaultLengthBounds` has no criterion — it is `private static` and needs the full solve path to observe." drives
/// `SolveAsync` with the mock solver and inspects `MilpResult.Model` for **whether the variables exist**: a successful derivation creates and constrains
/// `assigned_length_*` / `over_length_*`; a failed one leaves them absent entirely (because `BuildAssignedLength` requires `hasBound || hasPenalty`).
/// </remarks>
public class Csp1dLengthBoundDerivationTests {
    private static Quantity<Flt64> Millimetres(double value) =>
        new(new Flt64(value), LengthUnits.Millimeter);

    private static Material<Flt64> MakeMaterial() =>
        new(
            id: "M1",
            name: "material",
            widthRange: new WidthRange<Flt64>(
                Width: new QuantityRange<Flt64>(
                    LowerBound: Millimetres(100.0),
                    UpperBound: Millimetres(200.0)),
                Step: Millimetres(1.0)));

    /// <summary>带 `maxOverProduceLength` 的产品，派生上界的来源 / A product carrying `maxOverProduceLength`, the source of the derived upper bound.</summary>
    private static Product<Flt64> MakeProduct() =>
        new(
            id: "A",
            name: "product-a",
            width: new List<Quantity<Flt64>> { Millimetres(60.0) },
            maxOverProduceLength: Millimetres(500.0));

    private static ProduceInput<Flt64> MakeInput() {
        Material<Flt64> material = MakeMaterial();
        Product<Flt64> product = MakeProduct();
        var plan = new CuttingPlan<Flt64>(
            id: "p1",
            material: material,
            slices: new List<CuttingPlanSlice<Flt64>> { new(product, Millimetres(60.0)) },
            demandContributions: new List<CuttingPlanDemandContribution<Flt64>> {
                new(product, Millimetres(60.0)),
            });
        return new ProduceInput<Flt64>(
            cuttingPlans: new List<CuttingPlan<Flt64>> { plan },
            demands: new List<ProductDemand<Flt64>> { new(product, Millimetres(60.0)) },
            materials: new List<Material<Flt64>> { material },
            machines: new List<Machine<Flt64>>());
    }

    /// <summary>**只声明动态产品、不给边界时，变量必须仍然出现** / Declaring only a dynamic product must still produce the variables.</summary>
    /// <remarks>
    /// 配置里**只写 `DynamicProductIds`**，上下界全留空。若不推导，
    /// `BuildAssignedLength` 的 `hasBound || hasPenalty` 为假，**`assigned_length_0` 根本不会建**。
    ///
    /// 判据断言**变量名出现在模型里**，而不是"结果非空"——
    /// 后者在"没有任何长度变量"时也可能非空（因为 `HasAny` 为假时返回 null，但
    /// 这里要区分的是**变量是否被建出来**）。
    ///
    /// The config supplies **only `DynamicProductIds`** with every bound left empty. Without derivation, `BuildAssignedLength`'s `hasBound || hasPenalty` is false and
    /// **`assigned_length_0` is never created**. The criterion asserts **the variable name appears in the model**, not "the result is non-null" — the latter cannot
    /// distinguish whether the variable was created at all.
    /// </remarks>
    [Fact]
    public async Task SolveAsync_ShouldDeriveTheBoundsSoTheVariablesExist() {
        var solver = new MockColumnGenerationSolver();
        var milpSolver = new Csp1dMilpSolver(solver);
        var lengthConfig = new LengthAssignmentModelingConfig<Flt64>(
            DynamicProductIds: new HashSet<string> { "A" });

        Result<Csp1dMilpSolver.MilpResult<Flt64>?, ErrorCode, Error<ErrorCode>> result = await milpSolver.SolveAsync(
            MakeInput(),
            yieldConfig: null,
            wasteConfig: null,
            lengthConfig: lengthConfig,
            extensions: null,
            objectivePolicies: null,
            isFinalMilp: true,
            progressContext: null);

        result.IsOk.Should().BeTrue();
        result.Value.Should().NotBeNull();
        IReadOnlyList<string> variableNames = result.Value!.Model.Tokens
            .TokensInSolver
            .Select(t => t.Name)
            .ToList();
        variableNames.Should().Contain(
            name => name.Contains("assigned_length"),
            "the lower bound must be derived (from the demand) so the assigned-length variable is created");
        variableNames.Should().Contain(
            name => name.Contains("over_length"),
            "the upper bound must be derived from maxOverProduceLength so the over-length variable is created");

        // **只断言"变量存在"不够。** 实测：把上界的 `maxOverProduceLength` 分支改成恒假，
        // 它会退到"需求最大值"兜底，**变量照样建出来**，判据全绿。同理把下界推导关掉，
        // 上界仍在，`hasBound` 仍为真，变量也照样建出来。
        //
        // **所以必须断言派生出的具体值。** 产品 `maxOverProduceLength = 500mm`、
        // 需求 60mm ⇒ 下界 **0**、上界 **500**。
        //
        // **Asserting "the variable exists" is not enough.** Measured: making the upper bound's `maxOverProduceLength` branch always false falls back to "max demand" and
        // **the variable is still created**, leaving the criterion green. Likewise, disabling the lower-bound derivation leaves the upper bound in place, `hasBound` stays
        // true, and the variable still appears. **So the derived values themselves must be asserted.** The product's `maxOverProduceLength` is 500mm and the demand is
        // 60mm, so the lower bound is **0** and the upper bound is **500**.
        // **上下界是约束，不是变量的 `LowerBound` / `UpperBound` 属性。**
        //
        // `BuildAssignedLength` 只建变量；边界由 `LengthConstraintPipeline` 加成约束
        // （`assigned_length_i >= lower` / `<= upper`）。所以判据要看**约束名与右端常数**。
        //
        // **The bounds are constraints, not the variable's `LowerBound` / `UpperBound` properties.** `BuildAssignedLength` only creates the variable; the bounds are added
        // as constraints by `LengthConstraintPipeline`. So the criterion inspects **the constraint names and right-hand constants**.
        IReadOnlyList<string> constraintNames = result.Value!.Model.Constraints
            .Select(c => c.Name)
            .ToList();
        constraintNames.Should().Contain(
            name => name.Contains("assigned_length") && name.Contains("lower"),
            "the derived lower bound must be emitted as a constraint");
        constraintNames.Should().Contain(
            name => name.Contains("assigned_length") && name.Contains("upper"),
            "the derived upper bound must be emitted as a constraint");

        // **只看约束名不够。** 实测：把 `maxOverProduceLength` 分支改成恒假，它会退到
        // "需求最大值"兜底，**约束照样被加上**，只查名字的判据全绿。
        //
        // 所以还要看**右端常数**：下界约束的右端是 0（基线用 `zero`），上界约束的右端是 500
        // （`maxOverProduceLength`），不是 60（需求兜底）。
        //
        // **Checking the names alone is not enough.** Measured: making the `maxOverProduceLength` branch always false falls back to "max demand" and **the constraint is still
        // Added
        // the upper bound's is 500 (`maxOverProduceLength`), not 60 (the demand fallback).
        MathConstraint lowerConstraint = result.Value!.Model.Constraints
            .First(c => c.Name.Contains("assigned_length") && c.Name.Contains("lower"));
        MathConstraint upperConstraint = result.Value!.Model.Constraints
            .First(c => c.Name.Contains("assigned_length") && c.Name.Contains("upper"));
        ExtractRightHandSide(lowerConstraint).Should().BeApproximately(
            0.0,
            1e-9,
            "the baseline derives the lower bound as the additive identity");
        ExtractRightHandSide(upperConstraint).Should().BeApproximately(
            500.0,
            1e-9,
            "the derived upper bound is maxOverProduceLength, 500mm — not the 60mm demand fallback");
    }

    /// <summary>**没有动态产品时不推导，变量也不出现**——反向对照 / With no dynamic products nothing is derived and no variables appear — the reverse control.</summary>
    /// <remarks>
    /// 少了这条，"**无论配置如何都建变量**"的实现也会绿。
    ///
    /// Without this, an implementation that **always** creates the variables regardless of config would also pass.
    /// </remarks>
    [Fact]
    public async Task SolveAsync_ShouldNotCreateTheVariablesWithoutDynamicProducts() {
        var solver = new MockColumnGenerationSolver();
        var milpSolver = new Csp1dMilpSolver(solver);
        var lengthConfig = new LengthAssignmentModelingConfig<Flt64>(
            DynamicProductIds: new HashSet<string>());

        Result<Csp1dMilpSolver.MilpResult<Flt64>?, ErrorCode, Error<ErrorCode>> result = await milpSolver.SolveAsync(
            MakeInput(),
            yieldConfig: null,
            wasteConfig: null,
            lengthConfig: lengthConfig,
            extensions: null,
            objectivePolicies: null,
            isFinalMilp: true,
            progressContext: null);

        result.IsOk.Should().BeTrue();
        result.Value.Should().NotBeNull();
        IReadOnlyList<string> variableNames = result.Value!.Model.Tokens
            .TokensInSolver
            .Select(t => t.Name)
            .ToList();
        variableNames.Should().NotContain(
            name => name.Contains("assigned_length"),
            "no product is dynamic, so no assigned-length variable should exist");
        variableNames.Should().NotContain(
            name => name.Contains("over_length"),
            "no product is dynamic, so no over-length variable should exist");
    }

    /// <remarks>
    /// **`assigned_length` 的"恒建"变异钉不住——这是这里如实记录的等价变异。**
    ///
    /// 原因：`ResolveDefaultLengthBounds` 只要产品是动态的、且有需求，就**一定会推出下界**，
    /// 于是 `hasBound` 恒为真，`isDynamic && (hasBound || hasPenalty)` 与 `isDynamic` **在所有可达状态下等价**。
    ///
    /// 要分开它们，需要构造"动态产品 + 无需求 + 无惩罚"的状态——但那种状态下
    /// `demands` 为空，**变量列表长度为 0，两者都建不出东西**。
    ///
    /// **The "always create" mutant for `assigned_length` cannot be pinned — an equivalent mutant, recorded honestly.** The reason: whenever the product is dynamic and has
    /// a demand, `ResolveDefaultLengthBounds` **always derives a lower bound**, so `hasBound` is always true and `isDynamic && (hasBound || hasPenalty)` is **equivalent to
    /// `isDynamic` in every reachable state**. Separating them needs "dynamic product + no demand + no penalty" — but there `demands` is empty, **the variable list has
    /// length 0, and neither implementation creates anything**.
    /// </remarks>
    /// <summary>**只有 `DynamicProductIds`、无任何惩罚或上界时，超长变量不该被建** / With only `DynamicProductIds` and no penalty or bound, no over-length variable.</summary>
    /// <remarks>
    /// **上一条判据钉不住"恒建"。** 它用的配置带了 `OverLengthPenalty`，
    /// 于是"按条件建"与"恒建"**结果相同**。实测确认：把条件改成 `if (isDynamic)`，**三条全绿**。
    ///
    /// 这条**故意不给任何惩罚、上界、`maxOverProduceLength`**，只声明动态产品：
    /// 正确实现不建超长变量，恒建实现会建。
    ///
    /// **The previous criterion cannot pin "always create".** Its config carries `OverLengthPenalty`, so "conditional" and "always" **agree**. Measured: changing the
    /// condition to `if (isDynamic)` left **all three green**. This one **deliberately supplies no penalty, no bound and no `maxOverProduceLength`**, declaring only a
    /// dynamic product: the correct implementation creates no over-length variable, while an always-create one does.
    /// </remarks>
    [Fact]
    public async Task SolveAsync_ShouldNotCreateTheOverLengthVariableWithoutAnyTrigger() {
        Material<Flt64> material = MakeMaterial();
        Product<Flt64> product = new(
            id: "A",
            name: "product-a",
            width: new List<Quantity<Flt64>> { Millimetres(60.0) });
        var plan = new CuttingPlan<Flt64>(
            id: "p1",
            material: material,
            slices: new List<CuttingPlanSlice<Flt64>> { new(product, Millimetres(60.0)) },
            demandContributions: new List<CuttingPlanDemandContribution<Flt64>> {
                new(product, Millimetres(60.0)),
            });
        var input = new ProduceInput<Flt64>(
            cuttingPlans: new List<CuttingPlan<Flt64>> { plan },
            demands: new List<ProductDemand<Flt64>> { new(product, Millimetres(60.0)) },
            materials: new List<Material<Flt64>> { material },
            machines: new List<Machine<Flt64>>());
        var milpSolver = new Csp1dMilpSolver(new MockColumnGenerationSolver());
        // **只声明动态产品**：没有超长惩罚、没有超长上界、产品也没有 `maxOverProduceLength`。
        // **Only the dynamic product is declared**: no over-length penalty, no over-length bound, and no `maxOverProduceLength` on the product.
        var lengthConfig = new LengthAssignmentModelingConfig<Flt64>(
            DynamicProductIds: new HashSet<string> { "A" });

        Result<Csp1dMilpSolver.MilpResult<Flt64>?, ErrorCode, Error<ErrorCode>> result = await milpSolver.SolveAsync(
            input,
            yieldConfig: null,
            wasteConfig: null,
            lengthConfig: lengthConfig,
            extensions: null,
            objectivePolicies: null,
            isFinalMilp: true,
            progressContext: null);

        result.IsOk.Should().BeTrue();
        result.Value.Should().NotBeNull();
        IReadOnlyList<string> variableNames = result.Value!.Model.Tokens
            .TokensInSolver
            .Select(t => t.Name)
            .ToList();
        variableNames.Should().Contain(
            name => name.Contains("assigned_length"),
            "the derived lower bound is enough to create the assigned-length variable");
        variableNames.Should().NotContain(
            name => name.Contains("over_length"),
            "nothing triggers an over-length variable: no penalty, no bound, no maxOverProduceLength");
    }

    /// <summary>**没有需求时不建长度变量**——与基线一致，不是缺口 / No demand means no length variables — consistent with the baseline, not a gap.</summary>
    /// <remarks>
    /// **这里一开始把这个当成缺口，是错的。**
    ///
    /// 基线 `LengthAggregation` 的 `assignedLength` / `overLength` 都是
    /// **`demands.mapIndexed`** ——**按需求索引**。没有需求 ⇒ 没有变量 ⇒ 没有约束，
    /// 哪怕边界推导阶段能从切割方案的贡献里找到那个产品。
    ///
    /// 所以 `ResolveDefaultLengthBounds` 里"从贡献找产品"那段**只影响推导出的配置值**，
    /// 而配置值在**没有需求时根本用不上**。C# 与基线一致。
    ///
    /// **判据记录这个一致性**：它防止下一个人"顺手"把变量改成按产品建。
    ///
 /// ** first recorded this as a gap — that was wrong.** The baseline's `assignedLength` / `overLength` are both **`demands.mapIndexed`** — **indexed by demand**.
    /// No demand means no variables and no constraints, even though the derivation step can find the product from a plan's contribution. So the "find the product from a
    /// contribution" branch in `ResolveDefaultLengthBounds` **only affects the derived config values**, which are **unused when there is no demand**. C# matches the
    /// baseline, and this criterion records that consistency — it stops the next person from "helpfully" switching the variables to be per-product.
    /// </remarks>
    [Fact]
    public async Task SolveAsync_ShouldNotCreateLengthVariablesWithoutADemand() {
        Material<Flt64> material = MakeMaterial();
        Product<Flt64> product = MakeProduct();
        var plan = new CuttingPlan<Flt64>(
            id: "p1",
            material: material,
            slices: new List<CuttingPlanSlice<Flt64>> { new(product, Millimetres(60.0)) },
            demandContributions: new List<CuttingPlanDemandContribution<Flt64>> {
                new(product, Millimetres(60.0)),
            });
        var input = new ProduceInput<Flt64>(
            cuttingPlans: new List<CuttingPlan<Flt64>> { plan },
            demands: new List<ProductDemand<Flt64>>(),
            materials: new List<Material<Flt64>> { material },
            machines: new List<Machine<Flt64>>());
        var milpSolver = new Csp1dMilpSolver(new MockColumnGenerationSolver());
        var lengthConfig = new LengthAssignmentModelingConfig<Flt64>(
            DynamicProductIds: new HashSet<string> { "A" },
            OverLengthPenalty: new Dictionary<string, Flt64> { ["A"] = new Flt64(1.0) });

        Result<Csp1dMilpSolver.MilpResult<Flt64>?, ErrorCode, Error<ErrorCode>> result = await milpSolver.SolveAsync(
            input,
            yieldConfig: null,
            wasteConfig: null,
            lengthConfig: lengthConfig,
            extensions: null,
            objectivePolicies: null,
            isFinalMilp: true,
            progressContext: null);

        result.IsOk.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Model.Constraints
            .Select(c => c.Name)
            .Should().NotContain(
                name => name.Contains("assigned_length") || name.Contains("over_length"),
                "length variables are indexed by demand, so with no demands there are no length constraints");
    }

    /// <summary>
    /// 取约束右端的常数项 / Extract a constraint's right-hand constant.
    /// </summary>
    /// <param name="constraint">约束 / Constraint.</param>
    /// <returns>右端常数 / The right-hand constant.</returns>
    private static double ExtractRightHandSide(MathConstraint constraint) {
        var inequality = (LinearInequalityConstraint<Flt64>)constraint;
        return inequality.Inequality.Rhs.Monomials.Count == 0
            ? inequality.Inequality.Rhs.Constant.ToDouble()
            : double.NaN;
    }
}
