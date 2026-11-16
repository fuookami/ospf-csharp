// 非零对偶下的影子价格判据 / Criteria for shadow prices under non-zero duals.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Framework.Csp1d.Application.Service;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce.Service.Pipeline;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Test.Framework.Csp1d.Application.Service;

/// <summary>
/// **非零对偶必须被正确提取成物料/设备影子价格** / Non-zero duals must be extracted into material/machine shadow prices.
/// </summary>
/// <remarks>
/// **起一直记着**："物料/设备对偶价在真实 LP 里是否非零未验证——mock 返回全零对偶。"
///
/// **本环境没有进程内真实 LP 求解器**（只有 `RemoteSolverHttpClient`，需要服务器），
/// 所以"真实求解器产出非零对偶"这件事**做不到**，这里**不宣称**。
///
/// 但**能做的另一半**是：拿一份**非零**的对偶字典喂给提取路径，验证
/// `MaterialUsageShadowPriceKey` / `MachineCapacityShadowPriceKey` 真的带着**非零值**出来。
/// 这才是 mock 全零一直掩盖的那段：**提取映射本身**。
///
/// **onward记录**: "whether material/machine duals are non-zero against a real LP is unverified — the mock returns all-zero duals."
///
/// **There is no in-process real LP solver here** (only `RemoteSolverHttpClient`, which needs a server), so "a real solver yields non-zero duals" is **not achievable** and this
/// batch **makes no such claim**.
///
/// The **achievable half** is feeding a **non-zero** dual dictionary into the extraction path and verifying that `MaterialUsageShadowPriceKey` /
/// `MachineCapacityShadowPriceKey` come out carrying **non-zero values** — the very layer the all-zero mock kept hiding (the earlier empty-map defect lived here).
/// </remarks>
public class Csp1dShadowPriceNonZeroTests {
    private static Quantity<Flt64> Millimetres(double value) =>
        new(new Flt64(value), LengthUnits.Millimeter);

    private static (Csp1dProduceContext<Flt64> Context, LinearMetaModel<Flt64> Model) BuildModel() {
        var material = new Material<Flt64>(
            id: "M1",
            name: "material",
            widthRange: new WidthRange<Flt64>(
                Width: new QuantityRange<Flt64>(
                    LowerBound: Millimetres(100.0),
                    UpperBound: Millimetres(200.0)),
                Step: Millimetres(1.0)),
            availableBatches: new UInt64(10UL));
        var machine = new Machine<Flt64>(
            id: "K1",
            name: "machine",
            capacity: Millimetres(3600.0));
        var product = new Product<Flt64>(
            id: "A",
            name: "product-a",
            width: new List<Quantity<Flt64>> { Millimetres(50.0) });
        var plan = new CuttingPlan<Flt64>(
            id: "p1",
            material: material,
            // **必须带 `machineId`，否则设备约束不会被建出来**——这也是 记的
            // "物料/设备对偶从未验证"的一半原因：没有设备 id 就没有设备约束。
            // **The `machineId` is required, or no machine constraint is created** — half the reason recorded "material/machine duals never verified".
            machineId: "K1",
            capacityConsumption: Millimetres(50.0),
            slices: new List<CuttingPlanSlice<Flt64>> { new(product, Millimetres(50.0)) },
            demandContributions: new List<CuttingPlanDemandContribution<Flt64>> {
                new(product, Millimetres(50.0)),
            });
        var input = new ProduceInput<Flt64>(
            cuttingPlans: new List<CuttingPlan<Flt64>> { plan },
            demands: new List<ProductDemand<Flt64>> { new(product, Millimetres(50.0)) },
            materials: new List<Material<Flt64>> { material },
            machines: new List<Machine<Flt64>> { machine });
        var model = new LinearMetaModel<Flt64>(name: "shadow-price-probe");
        Csp1dProduceContext<Flt64> context = new Csp1dProduceContextBuilder<Flt64>(input)
            .Mode(Csp1dModelingMode.LP)
            .Extensions(new List<Csp1dModelingExtension<Flt64>>())
            .Build();
        context.Register(model).IsOk.Should().BeTrue("the probe model must register");
        return (context, model);
    }

    /// <summary>**物料与设备容量约束必须带非零影子价格** / Material and machine-capacity constraints must carry non-zero shadow prices.</summary>
    /// <remarks>
    /// 做法：遍历模型约束，**按其 `Args` 是不是 `MaterialUsageShadowPriceKey` /
    /// `MachineCapacityShadowPriceKey`** 给一个**互不相同的非零对偶值**，再提取。
    ///
    /// 断言取值**等于**赋给它的那个数，而不是"非零"——**"非零"是太弱的判据**
    /// ` 抓不住"接了但全空"）。
    ///
    /// The method: walk the model constraints, assign **distinct non-zero duals** to those whose `Args` is a `MaterialUsageShadowPriceKey` /
    /// `MachineCapacityShadowPriceKey`, then extract. The assertions check the **exact assigned value**, not merely "non-zero" — **"non-zero" is too weak a criterion** (the earlier
    /// lesson: `NotBeNull` cannot catch "wired but empty").
    /// </remarks>
    [Fact]
    public void ExtractFromDualSolution_ShouldCarryTheNonZeroDualValues() {
        (Csp1dProduceContext<Flt64> context, LinearMetaModel<Flt64> model) = BuildModel();
        var duals = new Dictionary<MathConstraint, Flt64>();
        var materialKeys = new List<MaterialUsageShadowPriceKey>();
        var capacityKeys = new List<MachineCapacityShadowPriceKey>();
        // **同一族约束赋同一个值**，再与另一族用**不同**的值区分开。
        // 早先给每条约束赋不同值，结果"哪条约束对应哪个键"的簿记本身就出了错
        // （实测拿到 4.5 而期望 3.5）——**判据的簿记复杂度超过了它要证明的东西**。
        // **One value per family**, distinct across families. An earlier version assigned a different value per constraint and the bookkeeping itself went wrong
        // (measured 4.5 against an expected 3.5) — **the criterion's bookkeeping outgrew what it was proving**.
        const double materialDual = 3.5;
        const double machineDual = 9.5;
        foreach (MathConstraint constraint in model.Constraints) {
            switch (constraint.Args) {
                case MaterialUsageShadowPriceKey materialKey:
                    duals[constraint] = new Flt64(materialDual);
                    materialKeys.Add(materialKey);
                    break;
                case MachineCapacityShadowPriceKey capacityKey:
                    duals[constraint] = new Flt64(machineDual);
                    capacityKeys.Add(capacityKey);
                    break;
                default:
                    break;
            }
        }
        materialKeys.Should().NotBeEmpty("the material usage constraint must exist in the model");

        // **设备容量约束现在由真实路径产生。**
        //
        // 时这里要手工补一条，因为"只由初始方案构成的模型不产生设备约束"——
        // 那一批还据此把"设备符号构造阶段不登记"记成""。
        //
        // **查证那是错的**：`register` 里用 `PlanBatchTerms` 填充设备符号，
        // C# 补齐之后，初始方案就会产生 `machine_capacity_0`。**手工补的那条因此成了重复项**
        // （实测 `19.0 = 9.5 × 2`）。判据现在走**真实路径**，比合成约束强。
        //
        // **The machine capacity constraint now comes from the real path.** In it had to be added by hand because "a model made only of initial plans produces no
        // machine constraint" — and that batch recorded "machine symbols are not populated in the constructor" as "consistent with the code". **proved that wrong**:
        // the code fills them via `PlanBatchTerms` in `register`, and once C# was fixed the initial plans produce `machine_capacity_0`. **The hand-added constraint then
        // became a duplicate** (measured `19.0 = 9.5 × 2`). The criterion now exercises **the real path**, which is stronger than a synthetic constraint.

        var lifecycle = new Csp1dShadowPriceLifecycle<Flt64>(Millimetres(1.0).Value, context.CgPipelines);
        Result<ShadowPriceMap<Flt64>, ErrorCode, Error<ErrorCode>> result =
            lifecycle.ExtractFromDualSolution(model, duals);

        result.IsOk.Should().BeTrue();
        ShadowPriceMap<Flt64> prices = result.Value;
        materialKeys.Should().Contain(
            key => key.MaterialId == "M1",
            "the pipeline must key the constraint by the material id it was built for");
        // **按已知键查，而不是按"约束自己带的那个键"查**：
        // 早先用后者，于是"管线把所有约束都映射成同一个常量键"的变异**存活**——
        // 因为查找用的键跟着变异一起变了。**判据必须写死期望的键。**
        // **Look up by the known key, not by "whatever key the constraint carries"**: the earlier version used the latter, so the mutation "the pipeline keys every
        // constraint with one constant" **survived** because the lookup key moved with the mutation. **The criterion must hard-code the expected key.**
        var expectedMaterialKey = new MaterialUsageShadowPriceKey("M1");
        prices[expectedMaterialKey].Should().NotBeNull("the material usage dual was non-zero and must survive extraction");
        prices[expectedMaterialKey]!.Value.ToDouble().Should().BeApproximately(materialDual, 1e-9);
        var expectedMachineKey = new MachineCapacityShadowPriceKey("K1");
        prices[expectedMachineKey].Should().NotBeNull("the machine capacity dual was non-zero and must survive extraction");
        prices[expectedMachineKey]!.Value.ToDouble().Should().BeApproximately(machineDual, 1e-9);
    }

    /// <summary>**没有管线时提取结果必须为空**——反向对照 / With no pipelines the extraction must be empty — the reverse control.</summary>
    /// <remarks>
    /// 这钉住"**空表缺陷**"：`ExtractFromDualSolution` 在 `_cgPipelines.Count == 0` 时
    /// **直接返回空表**，而不是"返回默认值"。若实现改成"没管线也照样造键"，
    /// 上一条仍会绿，而这条会红——**两条一起才能说明"有管线才提取"**。
    ///
    /// This pins **the empty-map defect**: with `_cgPipelines.Count == 0` it **returns an empty map** rather than "default values". If the implementation instead
    /// invented keys without pipelines, the previous criterion would still pass and this one would fail — **only together do they show "extraction requires pipelines"**.
    /// </remarks>
    [Fact]
    public void ExtractFromDualSolution_ShouldBeEmptyWithoutPipelines() {
        (_, LinearMetaModel<Flt64> model) = BuildModel();
        var duals = new Dictionary<MathConstraint, Flt64>();
        foreach (MathConstraint constraint in model.Constraints) {
            duals[constraint] = new Flt64(7.0);
        }

        var lifecycle = new Csp1dShadowPriceLifecycle<Flt64>(Millimetres(1.0).Value);
        Result<ShadowPriceMap<Flt64>, ErrorCode, Error<ErrorCode>> result =
            lifecycle.ExtractFromDualSolution(model, duals);

        result.IsOk.Should().BeTrue();
        // **按键查空**：`ShadowPriceMap` 没有键集合，所以查一个"若有管线就一定会有"的键。
        // **Look up by key**: `ShadowPriceMap` exposes no key set, so probe a key that would certainly exist if pipelines had run.
        result.Value[new MaterialUsageShadowPriceKey("M1")].Should().BeNull(
            "without pipelines nothing can be mapped, and inventing keys would be worse than an empty map");
    }
}
