// 主问题重建的确定性判据 / Criteria for the determinism of master-problem rebuilding.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
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
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using Xunit;

namespace Fuookami.Ospf.Test.Framework.Csp1d.Application.Service;

/// <summary>
/// **同样的输入重建主问题必须得到同样的结果**——这是"恢复失败分支不可达"的依据
/// / Rebuilding the master from identical input must give the identical result — the reason the "restore failed" branch is unreachable.
/// </summary>
/// <remarks>
/// **起一直记着"恢复失败（`restored is null`）分支未被触发"。** 这里去查为什么：
///
/// `Csp1dColumnGeneration` 的第二阶段在同一个迭代里用**同一批 `cuttingPlans`、同一个
/// `domainValueSample`、同一份 `extensions`** 重建主问题，而这套输入**刚刚**在进入循环之前
/// 建过一次并且**成功了**（否则 `lpMaster` 为 null，循环根本不会开始）。
///
/// `BuildLpMasterAsync` 只在 `context.Register(model)` 失败时返回 null，所以
/// **"第二次相同调用失败"要求 `Register` 不确定**。
///
/// **按规则，"读代码得出的结论"不算证据**——本判据就是对那个前提的实测：
/// 同样的输入连续建两次，两次 `Register` 都必须成功。
///
/// **If this ever fails**, the "restore failed" branch becomes reachable and must be given its own criterion.
///
/// **onward记录"the restore-failed (`restored is null`) branch was never triggered".** asks why.
///
/// The second phase rebuilds the master **within the same iteration** with **the same `cuttingPlans`, the same `domainValueSample` and the same `extensions`**, and that
/// exact input **already** built successfully before the loop (otherwise `lpMaster` would be null and the loop would never start).
///
/// `BuildLpMasterAsync` returns null only when `context.Register(model)` fails, so **"the identical second call fails" requires `Register` to be nondeterministic**.
///
/// **Per the rules, a conclusion from reading code is not evidence** — this criterion measures that premise: build twice from the same input and both `Register` calls
/// must succeed.
/// </remarks>
public class Csp1dMasterRebuildDeterminismTests {
    private static Quantity<Flt64> Millimetres(double value) =>
        new(new Flt64(value), LengthUnits.Millimeter);

    private static ProduceInput<Flt64> MakeInput() {
        var material = new Material<Flt64>(
            id: "M1",
            name: "material",
            widthRange: new WidthRange<Flt64>(
                Width: new QuantityRange<Flt64>(
                    LowerBound: Millimetres(100.0),
                    UpperBound: Millimetres(200.0)),
                Step: Millimetres(1.0)));
        var product = new Product<Flt64>(
            id: "A",
            name: "product-a",
            width: new List<Quantity<Flt64>> { Millimetres(50.0) });
        var plan = new CuttingPlan<Flt64>(
            id: "p1",
            material: material,
            slices: new List<CuttingPlanSlice<Flt64>> { new(product, Millimetres(50.0)) },
            demandContributions: new List<CuttingPlanDemandContribution<Flt64>> {
                new(product, Millimetres(50.0)),
            });
        return new ProduceInput<Flt64>(
            cuttingPlans: new List<CuttingPlan<Flt64>> { plan },
            demands: new List<ProductDemand<Flt64>> { new(product, Millimetres(50.0)) },
            materials: new List<Material<Flt64>> { material },
            machines: new List<Machine<Flt64>>());
    }

    /// <summary>**同一输入连续注册两次都必须成功** / Registering the same input twice must succeed both times.</summary>
    /// <remarks>
    /// 每一次都**新建模型与上下文**，因为真实路径也是每次都 `new LinearMetaModel` + `new Csp1dProduceContextBuilder`。
    /// 判据只测"结果是否一致"，不测任何缓存。
    ///
    /// Each call **creates a fresh model and context**, because the real path also does `new LinearMetaModel` + `new Csp1dProduceContextBuilder` every time. The criterion only
    /// checks that the outcomes agree; it does not test any cache.
    /// </remarks>
    [Fact]
    public void RegisteringTheSameInputTwice_ShouldSucceedBothTimes() {
        Try first = BuildAndRegister();
        Try second = BuildAndRegister();

        first.IsOk.Should().BeTrue("the first build is the one that already succeeded in production");
        second.IsOk.Should().BeTrue(
            "the restore path rebuilds from identical input, so it must succeed too — otherwise `restored is null` would be reachable");
    }

    private static Try BuildAndRegister() {
        var model = new LinearMetaModel<Flt64>(name: "determinism-probe");
        Csp1dProduceContext<Flt64> context = new Csp1dProduceContextBuilder<Flt64>(MakeInput())
            .Mode(Csp1dModelingMode.LP)
            .Build();
        return context.Register(model);
    }
}
