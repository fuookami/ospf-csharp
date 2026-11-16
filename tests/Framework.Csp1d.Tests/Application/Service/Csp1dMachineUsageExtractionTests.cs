// 设备用量提取的判据 / Criteria for machine-capacity usage extraction.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.Csp1d.Application.Model;
using Fuookami.Ospf.Framework.Csp1d.Application.Service;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Test.Framework.Csp1d.Application.Service;

/// <summary>
/// **设备产能用量必须按"消耗 × 次数"累加** / Machine capacity usage must accumulate "consumption × amount".
/// </summary>
/// <remarks>
/// `ExtractMachineUsages` 一直是**唯一没有判据**的提取路径。
/// 它内部用 `(V)(dynamic)a * (V)(dynamic)b` 做乘加，**没有编译期类型检查**——
/// 所以判据要同时回答两个问题：
///
/// 1. **算得对不对**（`Flt64` 下的功能正确性）；
/// 2. **单位不一致时会不会静默混加**（两个 `continue` 就是干这个的）。
///
/// `ExtractMachineUsages` has always been **the only extraction path without a criterion** (noted). It multiplies and adds via `(V)(dynamic)a * (V)(dynamic)b`
/// with **no compile-time type checking**, so the criteria must answer two questions: **is the arithmetic right** for `Flt64`, and **does a unit mismatch get silently
/// summed** (its two `continue`s exist for exactly that).
/// </remarks>
public class Csp1dMachineUsageExtractionTests {
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

    private static Product<Flt64> MakeProduct() =>
        new(id: "A", name: "product-a", width: new List<Quantity<Flt64>> { Millimetres(50.0) });

    private static CuttingPlan<Flt64> MakePlan(
        string id,
        Material<Flt64> material,
        Product<Flt64> product,
        string machineId,
        Quantity<Flt64>? capacityConsumption) =>
        new(
            id: id,
            material: material,
            machineId: machineId,
            capacityConsumption: capacityConsumption,
            slices: new List<CuttingPlanSlice<Flt64>> { new(product, Millimetres(50.0)) },
            demandContributions: new List<CuttingPlanDemandContribution<Flt64>> {
                new(product, Millimetres(50.0)),
            });

    private static (Csp1dProduceContext<Flt64> Context, LinearMetaModel<Flt64> Model) MakeContext(
        IReadOnlyList<CuttingPlan<Flt64>> plans,
        IReadOnlyList<Machine<Flt64>> machines,
        Material<Flt64> material) {
        Product<Flt64> product = MakeProduct();
        var input = new ProduceInput<Flt64>(
            cuttingPlans: plans,
            demands: new List<ProductDemand<Flt64>> { new(product, Millimetres(50.0)) },
            materials: new List<Material<Flt64>> { material },
            machines: machines);
        var model = new LinearMetaModel<Flt64>();
        Csp1dProduceContext<Flt64> context = new Csp1dProduceContextBuilder<Flt64>(input)
            .Mode(Csp1dModelingMode.MILP)
            .Build();
        context.Register(model).IsOk.Should().BeTrue();
        return (context, model);
    }

    /// <summary>**设备用量 = 单次消耗 × 使用次数** / Machine usage = per-run consumption × amount.</summary>
    /// <remarks>
    /// 一次消耗 120mm、用了 3 次，用量必须是 **360**。
    /// 断言精确值而不是"非空"，因为 dynamic 路径算错时**不会抛异常**，
    /// 只会静默给出错误数值。
    ///
    /// One run consumes 120mm and the plan is used 3 times, so the usage must be **360**. The assertion is on the exact value rather than "non-empty", because a wrong
    /// dynamic computation **throws nothing** — it silently yields a wrong number.
    /// </remarks>
    [Fact]
    public void ExtractSolution_ShouldMultiplyTheConsumptionByTheAmount() {
        Material<Flt64> material = MakeMaterial();
        Product<Flt64> product = MakeProduct();
        var machine = new Machine<Flt64>(
            id: "K1",
            name: "machine",
            capacity: Millimetres(3600.0));
        CuttingPlan<Flt64> plan = MakePlan("p1", material, product, "K1", Millimetres(120.0));
        (Csp1dProduceContext<Flt64> context, LinearMetaModel<Flt64> model) =
            MakeContext(new List<CuttingPlan<Flt64>> { plan }, new List<Machine<Flt64>> { machine }, material);
        IVariableItem variable = context.Produce[0]!;
        model.Tokens.Find(variable)!.SetResult(new Flt64(3.0));

        Result<Produce<Flt64>, ErrorCode, Error<ErrorCode>> result = context.ExtractSolution(model);

        result.IsOk.Should().BeTrue();
        result.Value.MachineUsages.Should().HaveCount(1);
        result.Value.MachineUsages[0].Used!.Value.ToDouble().Should().BeApproximately(
            360.0,
            1e-9,
            "120mm per run times 3 runs is 360mm");
    }

    /// <summary>**同一设备的多条方案必须累加，不是覆盖** / Multiple plans on one machine must accumulate, not overwrite.</summary>
    /// <remarks>
    /// **上一条判据钉不住累加**：只有一条方案时，"覆盖"与"累加"结果相同。
    /// 实测确认：把累加改成覆盖，上一条**仍然全绿**。
    ///
    /// 两条方案各消耗 100mm 与 250mm、各用 2 次，正确结果是 `200 + 500 = 700`；
    /// 覆盖式实现给 **500**（只留最后一条）。
    ///
    /// **The previous criterion cannot pin accumulation**: with a single plan, "overwrite" and "accumulate" agree. Measured: changing accumulation to overwrite left it
    /// **green**. With two plans consuming 100mm and 250mm, each used twice, the correct total is `200 + 500 = 700`; an overwriting implementation gives **500**.
    /// </remarks>
    [Fact]
    public void ExtractSolution_ShouldAccumulateAcrossMultiplePlansOnTheSameMachine() {
        Material<Flt64> material = MakeMaterial();
        Product<Flt64> product = MakeProduct();
        var machine = new Machine<Flt64>(
            id: "K1",
            name: "machine",
            capacity: Millimetres(3600.0));
        // **两条方案必须内容不同**，否则第二条会被规范键去重掉。
        // **The two plans must differ in content**, or the second is deduped by canonical key.
        CuttingPlan<Flt64> first = MakePlan("p1", material, product, "K1", Millimetres(100.0));
        var second = new CuttingPlan<Flt64>(
            id: "p2",
            material: material,
            machineId: "K1",
            capacityConsumption: Millimetres(250.0),
            slices: new List<CuttingPlanSlice<Flt64>> { new(product, Millimetres(60.0)) },
            demandContributions: new List<CuttingPlanDemandContribution<Flt64>> {
                new(product, Millimetres(60.0)),
            });
        (Csp1dProduceContext<Flt64> context, LinearMetaModel<Flt64> model) = MakeContext(
            new List<CuttingPlan<Flt64>> { first, second },
            new List<Machine<Flt64>> { machine },
            material);
        context.Produce.PlanCount.Should().Be(2, "both plans must survive deduplication");
        model.Tokens.Find(context.Produce[0]!)!.SetResult(new Flt64(2.0));
        model.Tokens.Find(context.Produce[1]!)!.SetResult(new Flt64(2.0));

        Result<Produce<Flt64>, ErrorCode, Error<ErrorCode>> result = context.ExtractSolution(model);

        result.IsOk.Should().BeTrue();
        result.Value.MachineUsages.Should().HaveCount(1);
        result.Value.MachineUsages[0].Used!.Value.ToDouble().Should().BeApproximately(
            700.0,
            1e-9,
            "100x2 plus 250x2 is 700; an overwriting implementation would give 500");
    }

    /// <summary>**单位与设备产能不一致的消耗不得计入** / A consumption whose unit differs from the machine's capacity must not be counted.</summary>
    /// <remarks>
    /// 判定就是 `if (machineCapacity != null && consumption.unit != machineCapacity.unit) return@fold acc`。
    ///
    /// 少了这个判断，**以厘米计的消耗会被直接当成毫米累加**——数值差 10 倍且毫无提示。
    /// 判据断言"该设备没有任何用量记录"，而不是"用量为零"：`mapNotNull`，
    /// 没有匹配项时**整条设备不出现**。
    ///
    /// The guard is `if (machineCapacity != null && consumption.unit != machineCapacity.unit) return@fold acc`. Without it, **a centimetre consumption would be summed as
 /// millimetres** — a factor of 10 with no warning. The criterion asserts "no usage record for this machine" rather than "usage is zero": this implementation uses
    /// `mapNotNull`, so with no matching entry **the machine does not appear at all**.
    /// </remarks>
    [Fact]
    public void ExtractSolution_ShouldSkipAConsumptionWithADifferentUnit() {
        Material<Flt64> material = MakeMaterial();
        Product<Flt64> product = MakeProduct();
        var machine = new Machine<Flt64>(
            id: "K1",
            name: "machine",
            capacity: Millimetres(3600.0));
        CuttingPlan<Flt64> plan = MakePlan(
            "p1",
            material,
            product,
            "K1",
            new Quantity<Flt64>(new Flt64(12.0), LengthUnits.Centimeter));
        (Csp1dProduceContext<Flt64> context, LinearMetaModel<Flt64> model) =
            MakeContext(new List<CuttingPlan<Flt64>> { plan }, new List<Machine<Flt64>> { machine }, material);
        IVariableItem variable = context.Produce[0]!;
        model.Tokens.Find(variable)!.SetResult(new Flt64(3.0));

        Result<Produce<Flt64>, ErrorCode, Error<ErrorCode>> result = context.ExtractSolution(model);

        result.IsOk.Should().BeTrue();
        result.Value.MachineUsages.Should().BeEmpty(
            "a centimetre consumption against a millimetre capacity is skipped, so the machine has no usage record");
    }
}
