#nullable enable

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Model.Mechanism;

/// <summary>
/// **目标快照必须展平到变量级** / The objective snapshot must be flattened to variables.
/// </summary>
/// <remarks>
/// 对齐基线的 `MetaModel.objectiveSnapshot()`。**这条判据不需要求解器**，所以它能在
/// "整条链跑通"之前就红——B929 那次是跑到真后端收尾复核才发现缺这一步，绕了很远。
///
/// 判据造一个**目标里含表达式符号**的模型（`stowage_0_0` 那类），再断言：
///
/// 1. `MetaSubObjects` 里**确实**有非变量的单项式——**不造出这个前提，判据就是在测空气**（B928 的教训）；
/// 2. `ObjectiveSnapshot()` 展平后**一个非变量单项式都不剩**；
/// 3. 展平前后的**常量项相同**（展平是代入，不是重算）。
///
/// Mirrors the baseline's `MetaModel.objectiveSnapshot()`. **This criterion needs no solver**, so it reds before "the whole chain runs" — B929 only found the
/// missing step after reaching the real backend's final check, which is a long way around. The fixture builds a model whose **objective contains an expression
/// symbol**, then asserts: (1) `MetaSubObjects` **really does** hold a non-variable monomial — **without that premise the criterion tests nothing** (the B928
/// lesson); (2) after `ObjectiveSnapshot()` **no non-variable monomial remains**; (3) the constant term is **the same** before and after, since flattening
/// substitutes rather than recomputes.
/// </remarks>
public class ObjectiveSnapshotTest {
    /// <summary>**含表达式符号的目标必须能展平到变量级** / An objective holding an expression symbol must flatten to variables.</summary>
    [Fact]
    public void ObjectiveSnapshot_ShouldFlattenExpressionSymbolsToVariables() {
        using var model = new LinearMetaModel<Flt64>(name: "objective-snapshot");
        var x = new RealVar("x");
        var y = new RealVar("y");
        model.Add(x).IsOk.Should().BeTrue();
        model.Add(y).IsOk.Should().BeTrue();

        // **表达式符号 `bundle` = 2x + 3y + 5**：目标写成 `10 * bundle`，于是目标里出现的是**符号**而不是变量。
        // **The expression symbol `bundle` = 2x + 3y + 5**, and the objective is `10 * bundle`, so the objective holds a **symbol**, not variables.
        var bundle = new LinearExpressionSymbol(
            new MutableLinearPolynomial<Flt64>(
                new List<LinearMonomial<Flt64>> {
                    new(new Flt64(2.0), x),
                    new(new Flt64(3.0), y),
                },
                new Flt64(5.0)),
            name: "bundle");

        var polynomial = new LinearPolynomial<Flt64>(
            new List<LinearMonomial<Flt64>> { new(new Flt64(10.0), bundle) },
            Flt64.Zero);
        model.AddObject(ObjectCategory.Minimum, polynomial, "objective", null).IsOk.Should().BeTrue();

        // 1. **前提必须成立**：目标里真的有非变量单项式。少了这一步，判据在"本来就是变量级"的模型上也会绿。
        // **The premise must hold**: the objective really does carry a non-variable monomial. Without this the criterion also passes on a model that is already flat.
        model.MetaSubObjects.Should().ContainSingle();
        model.MetaSubObjects[0].Polynomial.Monomials
            .Count(term => term.Symbol is not IVariableItem)
            .Should().BeGreaterThan(0,
                "the fixture must actually create the shape under test, or the criterion proves nothing");

        // 2. **展平之后一个非变量单项式都不剩**。
        // **After flattening no non-variable monomial remains.**
        IReadOnlyList<MetaSubObject<Flt64>>? snapshot = model.ObjectiveSnapshot();
        snapshot.Should().NotBeNull("flattening an expression symbol with no cycles cannot fail");
        snapshot!.Should().ContainSingle();
        snapshot![0].Polynomial.Monomials
            .Count(term => term.Symbol is not IVariableItem)
            .Should().Be(0,
                "every monomial must end on a variable, or objective evaluation has nothing to look up");
        snapshot![0].Polynomial.Monomials.Should().HaveCount(2, "2x + 3y + 5 scaled by 10 keeps two variable terms");

        // 3. **常量项正是 50**：展平是代入，`10 * 5`。
        // **The constant is exactly 50**: flattening substitutes, `10 * 5`.
        snapshot![0].Polynomial.Constant.ToDouble().Should().Be(50.0);

        // 4. **系数对得上**：x 的系数 20、y 的系数 30。
        // **The coefficients line up**: 20 on x, 30 on y.
        var byName = snapshot![0].Polynomial.Monomials
            .ToDictionary(term => term.Symbol!.Name, term => term.Coefficient.ToDouble());
        byName["x"].Should().Be(20.0);
        byName["y"].Should().Be(30.0);
    }

    /// <summary>**目标里只有变量时展平不改变任何东西** / Flattening is a no-op when the objective already holds variables.</summary>
    /// <remarks>
    /// 少了这条，"永远返回空快照"也会让上一条绿（它的断言只看"没有非变量单项式"）。
    /// **一条能被"什么都不做"满足的判据，等于没有判据。**
    ///
    /// Without this, "always return an empty snapshot" would also keep the criterion above green, since it only asserts "no non-variable monomials".
    /// **A criterion that "do nothing" satisfies is no criterion.**
    /// </remarks>
    [Fact]
    public void ObjectiveSnapshot_ShouldLeaveAPlainObjectiveUntouched() {
        using var model = new LinearMetaModel<Flt64>(name: "objective-plain");
        var x = new RealVar("plain-x");
        model.Add(x).IsOk.Should().BeTrue();
        var polynomial = new LinearPolynomial<Flt64>(
            new List<LinearMonomial<Flt64>> { new(new Flt64(4.0), x) },
            new Flt64(7.0));
        model.AddObject(ObjectCategory.Minimum, polynomial, "plain", null).IsOk.Should().BeTrue();

        IReadOnlyList<MetaSubObject<Flt64>>? snapshot = model.ObjectiveSnapshot();
        snapshot.Should().NotBeNull();
        snapshot!.Should().ContainSingle("one objective in, one objective out");
        snapshot![0].Polynomial.Monomials.Should().ContainSingle("no terms are invented or dropped");
        snapshot![0].Polynomial.Monomials[0].Coefficient.ToDouble().Should().Be(4.0);
        snapshot![0].Polynomial.Constant.ToDouble().Should().Be(7.0);
        snapshot![0].Name.Should().Be("plain");
        snapshot![0].Category.Should().Be(ObjectCategory.Minimum);
    }
}
