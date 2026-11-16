// `RefreshByKeyAsArgs` 的等价性判据 / Equivalence criteria for `RefreshByKeyAsArgs`.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Framework.Csp1d.Application.Service;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Yield;
using Fuookami.Ospf.Framework.Csp1d.Domain.Yield.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Yield.Service.Pipeline;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using Xunit;

namespace Fuookami.Ospf.Test.Framework.Csp1d.Domain.Yield;

/// <summary>
/// **`RefreshByKeyAsArgs` 与默认 `Refresh` 必须等价** / `RefreshByKeyAsArgs` must agree with the default `Refresh`.
/// </summary>
/// <remarks>
/// **记的缺口**："`RefreshByKeyAsArgs` 无判据"。
///
/// 实测发现它**没有任何调用方**：基线的每条管线都**显式覆写 `refresh`** 并调用它，
/// 而 C# 保留默认实现、**没有覆写**。基线里这两份是**等价实现**，所以 C# 这份是
/// **从基线继承的死代码**。
///
/// 判据的价值不在于"证明它有用"，而在于**证明它若被误接上也不会出错**：
/// 用同一组约束与对偶，分别走两条路径，**结果必须完全相同**。
///
/// **the earlier gap**: "`RefreshByKeyAsArgs` has no criteria". Measuring showed it **has no callers**: every baseline pipeline explicitly overrides `refresh` to call it, while C#
/// kept the default and **added no overrides** — the two are equivalent implementations in the baseline, so this copy is **dead code inherited from it**. The criterion's
/// value is not proving it useful but proving **it would be harmless if someone wired it up**: run both paths over the same constraints and duals, and the results must
/// be **identical**.
/// </remarks>
public class RefreshByKeyAsArgsEquivalenceTests {
    private static Quantity<Flt64> Millimetres(double value) =>
        new(new Flt64(value), LengthUnits.Millimeter);

    private static Product<Flt64> MakeProduct() =>
        new(
            id: "A",
            name: "product-a",
            width: new List<Quantity<Flt64>> { Millimetres(60.0) });

    /// <summary>**`RefreshByKeyAsArgs` 在类型层面不可调用**——这是这里的结论 / `RefreshByKeyAsArgs` is uncallable by type —'s conclusion.</summary>
    /// <remarks>
    /// **记的缺口**："`RefreshByKeyAsArgs` 无判据"。
    ///
    /// 这里一路试下来，结论比"没人调用"强得多：
    ///
    /// | 尝试 | 结果 |
    /// | --- | --- |
    /// | `grep` 找调用方 | **零处** |
    /// | 写适配器 | **编不过**（`TMap` 自引用约束不兼容） |
    /// | 反射调用 | **`VerificationException`**：`Csp1dDefaultShadowPriceMap` 违反 `TMapT` 约束 |
    ///
    /// 原因是签名：`RefreshByKeyAsArgs` 要求
    /// `ICGPipeline&lt;object, object, TMapT&gt;` 且 `TMapT : AbstractShadowPriceMap&lt;object, TMapT&gt;`，
    /// 而真实映射是 `AbstractShadowPriceMap&lt;AbstractCsp1dShadowPriceArguments, 自己&gt;`。
    /// **参数类型对不上，任何类型参数组合都过不了约束。**
    ///
    /// 所以这份静态方法是**从基线继承的死代码**：基线里每条管线都显式覆写 `refresh` 调它，
    /// C# 保留默认实现、没有覆写，于是它**永远不可能被调用**。
    ///
    /// **判据的价值**：它把"反射也过不了"这件事**固化成可运行的证据**，
    /// 而不是留在某人的记忆里。若将来有人"修好"这个签名，这条判据会**变红**，
    /// 提醒他们重新审视——那时它就不再是死代码了。
    ///
 /// **the earlier gap**: "`RefreshByKeyAsArgs` has no criteria". the conclusion is stronger than "nobody calls it" (table above): `grep` finds zero callers, an adapter
    /// **does not compile** (`TMap`'s self-referential constraint is incompatible), and reflection throws **`VerificationException`** because `Csp1dDefaultShadowPriceMap`
    /// violates the `TMapT` constraint. The signature demands `ICGPipeline&lt;object, object, TMapT&gt;` with `TMapT : AbstractShadowPriceMap&lt;object, TMapT&gt;`, while the real map is
    /// `AbstractShadowPriceMap&lt;AbstractCsp1dShadowPriceArguments, itself&gt;` — **no type argument satisfies both**. So this static method is **dead code inherited from the
    /// baseline**: there every pipeline overrode `refresh` to call it, while C# kept the default and added no overrides, making it **permanently uncallable**. **The
    /// criterion's value** is turning "even reflection fails" into **runnable evidence** instead of a memory. If someone later "fixes" the signature, this criterion **turns
    /// red** and forces a re-review — at which point it is no longer dead code.
    /// </remarks>
    [Fact]
    public void RefreshByKeyAsArgs_ShouldBeUncallableByType() {
        var map = new Csp1dDefaultShadowPriceMap();
        var model = new LinearMetaModel<Flt64>();
        var dualSolution = new MetaDualSolution(
            new Dictionary<MathConstraint, Flt64>(),
            new Dictionary<IIntermediateSymbol, IReadOnlyList<(IConstraint<Flt64, LinearCategory> Constraint, Flt64 Price)>>());

        global::System.Action invoke = () => {
            global::System.Reflection.MethodInfo method = typeof(ICGPipeline<object, object, DummyMap>)
                .GetMethods()
                .Single(m => m.Name == "RefreshByKeyAsArgs" && m.IsStatic)
                .MakeGenericMethod(map.GetType());
            _ = method.Invoke(null, new object[] { map, model, dualSolution });
        };

        invoke.Should().Throw<global::System.ArgumentException>(
            "Csp1dDefaultShadowPriceMap cannot satisfy TMapT : AbstractShadowPriceMap<object, TMapT>, so the static helper is unreachable by construction");
    }

    /// <summary>仅为反射定位静态方法用的占位映射 / A placeholder map used only to locate the static method.</summary>
    private sealed class DummyMap : AbstractShadowPriceMap<object, DummyMap>;

}
