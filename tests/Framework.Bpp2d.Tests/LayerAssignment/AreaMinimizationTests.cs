// 面积最小化目标的判据 / Criteria for the area-minimisation objective.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Framework.Bpp2d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.Model;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.Service.Limits;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp2d.Tests.LayerAssignment;

/// <summary>
/// **面积最小化目标** / The area-minimisation objective.
/// </summary>
/// <remarks>
/// 判据的重点：**未注册面积符号时直接失败**（不是加一个空目标）、**目标名与分类如此**、
/// 以及**系数真的乘进目标里**（不是记下来但没用）。
///
/// The criteria focus on **failing outright when the area symbol is unregistered** rather than adding an empty objective, **the objective name and category
/// **the coefficient actually multiplying into the objective** rather than being stored and ignored.
/// </remarks>
public class AreaMinimizationTests {
    private static ImpreciseAssignment Assignment() =>
        new(new Dictionary<ItemId, ulong> { [new ItemId("I0")] = 1UL });

    /// <summary>
    /// **未注册面积符号就失败，注册后目标才在模型上** / It fails without a registered area symbol, and only then puts the objective on the model.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 未注册 → **失败**（不是"成功但目标为空"）。
    /// 2. 注册后 → 成功，且模型上**恰好一个**目标。
    /// 3. 目标**名与分类**如此（`area_minimization` / `Minimum`）。
    /// 4. 目标多项式里**挂的是面积符号本身**，不是某个空单项。
    ///
    /// Four paired points: before registration it **fails** rather than "succeeding with an empty objective"; after registration it succeeds with **exactly one** objective on the model; the
 /// objective's **name and category** match the implementation; and the polynomial carries **the area symbol itself** rather than an empty monomial.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldFailUntilTheAreaSymbolIsRegistered() {
        using var model = new LinearMetaModel<Flt64>();
        ImpreciseAssignment assignment = Assignment();
        // **用非默认名**：默认名下"名字没被用上"与"名字正确"无法区分，变异会存活。
        // **A non-default name**: under the default name, "the name was ignored" and "the name is right" look identical and the mutant survives.
        var objective = new AreaMinimization(assignment, name: "my_area");

        objective.Invoke(model).IsOk.Should().BeFalse("the area symbol is not registered yet");
        model.MetaSubObjects.Should().BeEmpty("a failed invoke must not leave an objective behind");

        assignment.Register(model).IsOk.Should().BeTrue();
        objective.Invoke(model).IsOk.Should().BeTrue();

        model.MetaSubObjects.Should().HaveCount(1);
        model.MetaSubObjects[0].Name.Should().Be("my_area", "the caller-supplied name reaches the model");
        model.MetaSubObjects[0].Category.Should().Be(ObjectCategory.Minimum);
        model.MetaSubObjects[0].Polynomial.Monomials.Should().HaveCount(1);
        model.MetaSubObjects[0].Polynomial.Monomials[0].Symbol.Name.Should().Be("area");
    }

    /// <summary>
    /// **系数乘进目标，平局权重走同一条路** / The coefficient multiplies into the objective, and a tie-break weight takes the same path.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 系数 1 → 单项式系数 1。
    /// 2. 系数 `1e-6` → **单项式系数也变成 `1e-6`**（不是"记下系数但按 1 加"）。
    /// 3. 系数为 1 与系数为 `1e-6` 的两次调用**目标名相同、系数不同**——名字是契约，系数是调参。
    ///
    /// Three paired points: a coefficient of 1 yields monomial coefficient 1; a coefficient of `1e-6` makes **the monomial coefficient `1e-6`** rather than storing the value and adding 1; and the
    /// two calls share **the same objective name but different coefficients**, since the name is a contract and the coefficient is a knob.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldScaleByTheCoefficient() {
        ImpreciseAssignment assignment = Assignment();
        using var unitModel = new LinearMetaModel<Flt64>();
        assignment.Register(unitModel).IsOk.Should().BeTrue();
        new AreaMinimization(assignment, new FltX(1.0)).Invoke(unitModel).IsOk.Should().BeTrue();
        unitModel.MetaSubObjects[0].Polynomial.Monomials[0].Coefficient.ToFlt64().ToDouble()
            .Should().Be(1.0);

        using var tinyModel = new LinearMetaModel<Flt64>();
        ImpreciseAssignment other = Assignment();
        other.Register(tinyModel).IsOk.Should().BeTrue();
        var tiny = new AreaMinimization(other, new FltX(1e-6));
        tiny.Invoke(tinyModel).IsOk.Should().BeTrue();
        tiny.Name.Should().Be("area_minimization");
        tinyModel.MetaSubObjects.Single().Polynomial.Monomials[0].Coefficient.ToFlt64().ToDouble()
            .Should().Be(1e-6, "a tie-break weight must reach the objective, not stop at the property");
    }

    /// <summary>
    /// **自定义名与系数可读回** / A custom name and coefficient read back.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 构造时给名字 → `Name` 读回同一个字符串。
    /// 2. 给系数 → `Coefficient` 读回同一个数。
    /// 3. 不给系数 → 默认 1（不是 0）。
    ///
    /// Three paired points: a supplied name reads back unchanged; a supplied coefficient reads back unchanged; and an omitted coefficient defaults to 1 rather than 0.
    /// </remarks>
    [Fact]
    public void Constructor_ShouldKeepNameAndCoefficient() {
        ImpreciseAssignment assignment = Assignment();
        var named = new AreaMinimization(assignment, new FltX(2.0), "my_area");
        named.Name.Should().Be("my_area");
        named.Coefficient.ToFlt64().ToDouble().Should().Be(2.0);

        var defaulted = new AreaMinimization(assignment);
        defaulted.Name.Should().Be("area_minimization");
        defaulted.Coefficient.ToFlt64().ToDouble().Should().Be(1.0);
    }
}
