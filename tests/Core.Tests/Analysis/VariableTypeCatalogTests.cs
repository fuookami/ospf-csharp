#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 变量类型目录 / The variable type catalog.
/// </summary>
/// <remarks>
/// 加这个目录，是因为它是 `ConstraintProgrammingSnapshotBuilder` 的前置件。**
/// 快照把一个变量的类型存成 `TypeKind.ToString`——**一个类名**——而 C# 里没有任何反向解析。
///
/// adds this catalog because it is the prerequisite for `ConstraintProgrammingSnapshotBuilder`.** The snapshot stores a variable's type as `TypeKind.ToString` —
/// **a class name** — and nothing in C# parsed it back before this.
/// </remarks>
public class VariableTypeCatalogTests {
    private static IVariableTypeKind[] AllTypes() => [
        Binary.Instance,
        Ternary.Instance,
        BalancedTernary.Instance,
        Percentage.Instance,
        Integer.Instance,
        UInteger.Instance,
        Continuous.Instance,
        UContinuous.Instance,
    ];

    /// <summary>**存储用的名字与目录里的名字必须一致** / The stored name and the catalog name have to agree.</summary>
    /// <remarks>
    /// 快照编码的是 `TypeKind.ToString`。**目录若与它不一致，存进去的类型就再也读不回来**——
    /// 而且读不回来时不报错，只是解析失败或被当成别的类型。
    ///
    /// The snapshot encodes `TypeKind.ToString`. **If the catalog disagreed with that, a stored type could never be read back** — and the failure would be silent: a failed
    /// resolve or a different type entirely.
    /// </remarks>
    [Fact]
    public void TheCatalog_ShouldMatchWhatTheSnapshotStores() {
        foreach (IVariableTypeKind kind in AllTypes()) {
            VariableTypeCatalog.StableName(kind).Should().Be(kind.ToString(),
                "the snapshot encodes TypeKind.ToString(), so the catalog cannot use a different name");
        }
    }

    /// <summary>**八个类型全部可往返** / All eight types round-trip.</summary>
    [Fact]
    public void TheEightTypes_ShouldRoundTrip() {
        VariableTypeCatalog.Names.Should().HaveCount(8,
            "the concrete type set is closed; a ninth entry means someone added a type without a name");

        foreach (IVariableTypeKind kind in AllTypes()) {
            string? name = VariableTypeCatalog.StableName(kind);
            name.Should().NotBeNull();
            VariableTypeCatalog.TryResolve(name, out IVariableTypeKind resolved).Should().BeTrue();
            resolved.GetType().Should().Be(kind.GetType(),
                "a round trip that changes the type would turn an integer model into an LP");
        }
    }

    /// <summary>**认不出的名字必须失败，且不许兜底** / An unrecognised name has to fail, with no fallback.</summary>
    /// <remarks>
    /// 兜底一个"最像的"类型，正是那种**不报错、只是答案不对**的失效：整数模型被当成 LP 求解，
    /// 解出来了，值不对，而且没有任何一行日志说它换过类型。
    ///
    /// Falling back to the "closest" type is exactly that **no-error-wrong-answer** failure: an integer model solved as an LP, solved, wrong, and nothing logged that the type
    /// was changed.
    /// </remarks>
    [Fact]
    public void TheUnknownName_ShouldNotFallBack() {
        VariableTypeCatalog.TryResolve("Int128", out IVariableTypeKind unknown).Should().BeFalse(
            "an unknown name must not silently become continuous");
        VariableTypeCatalog.TryResolve("", out _).Should().BeFalse();
        VariableTypeCatalog.TryResolve(null, out _).Should().BeFalse();
        VariableTypeCatalog.TryResolve("continuous", out _).Should().BeFalse(
            "the names are ordinal: 'continuous' is a different name, not a spelling of Continuous");

        // **反向对照**：只断言"未知失败"会漏掉"全都不认"。
        // **The reverse control**: asserting only "unknown fails" would miss "nothing resolves at all".
        VariableTypeCatalog.TryResolve("Integer", out IVariableTypeKind known).Should().BeTrue();
        known.IsIntegerType.Should().BeTrue("the resolved type has to carry its own semantics");
    }

    /// <summary>**目录不含任何"看起来像"的近似项** / The catalog holds no near-miss entries.</summary>
    /// <remarks>
    /// 一个近似项（比如把 `Float64` 也映到 `Continuous`）比没有更糟：**它让一个错的映射看起来是支持的。**
    ///
    /// A near-miss entry (mapping `Float64` onto `Continuous`, say) is worse than none: **it makes a wrong mapping look supported.**
    /// </remarks>
    [Fact]
    public void TheCatalog_ShouldHoldNoNearMisses() {
        string[] suspicious = ["Float64", "Double", "Int64", "Long", "Bool", "Real", "Number"];

        foreach (string name in suspicious) {
            VariableTypeCatalog.Names.Should().NotContain(name,
                $"{name} is not one of the eight concrete type names; adding it would fake support");
            VariableTypeCatalog.TryResolve(name, out _).Should().BeFalse();
        }

        VariableTypeCatalog.Names.Should().BeEquivalentTo(
            new[] { "Binary", "Ternary", "BalancedTernary", "Percentage", "Integer", "UInteger", "Continuous", "UContinuous" });
    }

    /// <summary>**整数与连续的分界必须能从解析结果上读出来** / The integer/continuous boundary has to survive the resolve.</summary>
    /// <remarks>
    /// 目录只做名字映射。**如果映射错了，`IsIntegerType` 就是唯一还会说话的地方**——
    /// 它也是重建时决定"这个模型能不能走 LP 证书"的依据。
    ///
    /// The catalog only maps names. **If a mapping is wrong, `IsIntegerType` is the only thing that still speaks** — and it is also what decides whether a rebuilt model may use an
    /// LP certificate.
    /// </remarks>
    [Fact]
    public void TheResolvedTypes_ShouldKeepTheirSemantics() {
        VariableTypeCatalog.TryResolve("Integer", out IVariableTypeKind integer).Should().BeTrue();
        integer.IsIntegerType.Should().BeTrue();

        VariableTypeCatalog.TryResolve("Continuous", out IVariableTypeKind continuous).Should().BeTrue();
        continuous.IsIntegerType.Should().BeFalse();
        continuous.IsContinuousType.Should().BeTrue();

        VariableTypeCatalog.TryResolve("Binary", out IVariableTypeKind binary).Should().BeTrue();
        binary.IsBinaryType.Should().BeTrue();

        VariableTypeCatalog.TryResolve("UInteger", out IVariableTypeKind uinteger).Should().BeTrue();
        uinteger.IsUnsignedIntegerType.Should().BeTrue();
    }

    /// <summary>**名称集合是稳定的、有序枚举的** / The name set is stable and enumerable.</summary>
    [Fact]
    public void TheNames_ShouldBeEnumerableWithoutDuplicates() {
        IReadOnlyCollection<string> names = VariableTypeCatalog.Names;
        names.Should().OnlyHaveUniqueItems();
        names.Should().HaveCount(AllTypes().Distinct().Count(),
            "one name per concrete type, no more and no fewer");
        foreach (string name in names) {
            VariableTypeCatalog.TryResolve(name, out IVariableTypeKind resolved).Should().BeTrue(
                $"every catalog name resolves, and {name} is one of them");
            VariableTypeCatalog.StableName(resolved).Should().Be(name,
                "the name that resolves to a type has to be the name that type reports back");
        }
    }
}

/// <summary>
/// 按名称造变量 / Creating a variable from a name.
/// </summary>
/// <remarks>
/// **`RegisterVariable` 要的是变量实例，不是一个类型对象**——这是 之后重建器差的最后一环。
///
/// **`RegisterVariable` takes a variable instance, not a type object** — the last link the rebuilder was missing after.
/// </remarks>
public class VariableTypeFactoryTests {
    /// <summary>**八个名称各自造出正确类型的变量** / Each of the eight names creates the right variable type.</summary>
    /// <remarks>
    /// 只断言"造出来了"不够：**造出来的类型必须是那一个**，否则整数模型还是会被当成 LP。
    ///
    /// Asserting "it was created" is not enough: **the created type has to be that one**, or an integer model still ends up solved as an LP.
    /// </remarks>
    [Theory]
    [InlineData("Binary", true)]
    [InlineData("Ternary", true)]
    [InlineData("BalancedTernary", true)]
    [InlineData("Percentage", false)]
    [InlineData("Integer", true)]
    [InlineData("UInteger", true)]
    [InlineData("Continuous", false)]
    [InlineData("UContinuous", false)]
    public void TheFactory_ShouldCreateTheNamedType(string name, bool isInteger) {
        VariableTypeCatalog.TryCreate(name, "x", out IVariableItem variable).Should().BeTrue();
        variable.TypeKind.IsIntegerType.Should().Be(isInteger,
            $"{name} must keep its integer/continuous boundary, or a model silently changes class");
        VariableTypeCatalog.StableName(variable.TypeKind).Should().Be(name,
            "the created variable has to report back the name it was created from");
    }

    /// <summary>**认不出的名字不造变量** / An unrecognised name creates nothing.</summary>
    /// <remarks>
    /// 兜底造一个连续型，正是那种**不报错、只是答案不对**的失效。
    ///
    /// Falling back to a continuous variable is exactly that **no-error-wrong-answer** failure.
    /// </remarks>
    [Fact]
    public void TheFactory_ShouldNotFallBack() {
        VariableTypeCatalog.TryCreate(null, "x", out _).Should().BeFalse();
        VariableTypeCatalog.TryCreate("", "x", out _).Should().BeFalse();
        VariableTypeCatalog.TryCreate("Int128", "x", out _).Should().BeFalse();
        VariableTypeCatalog.TryCreate("integer", "x", out _).Should().BeFalse("the names are ordinal");

        VariableTypeCatalog.TryCreate("Integer", "x", out IVariableItem known).Should().BeTrue();
        known.Name.Should().Be("x", "the display name has to be carried through");
    }

    /// <summary>**工厂与目录一一对应，两边都不许漏** / The factory and the catalog correspond one to one.</summary>
    [Fact]
    public void TheFactory_ShouldCoverEveryCatalogName() {
        foreach (string name in VariableTypeCatalog.Names) {
            VariableTypeCatalog.TryCreate(name, "probe", out IVariableItem variable).Should().BeTrue(
                $"the catalog advertises {name}, so the factory has to build it");
            VariableTypeCatalog.StableName(variable.TypeKind).Should().Be(name);
        }
    }
}

/// <summary>
/// 快照里找回的是原变量对象，不是新造一个 / A snapshot yields the original variable objects, not new ones.
/// </summary>
/// <remarks>
/// **更正了 的判断**：`buildModel` 不从 `TypeName` 重建变量，
/// 而是遍历表达式 AST 把 `IVariableItem` 引用**原样收出来**。
///
/// **corrected the claim**: the code does not rebuild variables from `TypeName`; it walks the expression AST and reuses the `IVariableItem` references.**
/// </remarks>
public class SnapshotVariableIdentityTests {
    /// <summary>**快照里的变量就是原来那个对象** / The variable in the snapshot is the original object.</summary>
    [Fact]
    public void TheSnapshot_ShouldCarryTheOriginalVariable() {
        var variable = new IntVar("x");
        var id = new VariableId("source:x");
        IntegerDomain domain = IntegerDomain.CreateInterval(new Int64(0), new Int64(10)).Value;
        var expression = new ConstraintProgrammingExpression.Variable(variable, domain, id);

        var model = new ConstraintProgrammingModel("probe");
        model.RegisterVariable(id: id, variable: variable, domain: domain,
            scope: ModelElementScope.Stable, origin: "fixture/x").Value.Should().NotBeNull();
        model.RegisterExpression("x", expression).Value.Should().NotBeNull();

        ConstraintProgrammingModelSnapshot snapshot = model.Snapshot().Value!;
        ConstraintProgrammingExpression registered = snapshot.Expressions
            .Single(entry => entry.Name == "x").Expression;
        registered.Should().BeOfType<ConstraintProgrammingExpression.Variable>();
        ((ConstraintProgrammingExpression.Variable)registered).VariableItem.Should().BeSameAs(variable,
            "a rebuilder reuses this object; there is no type name on the path");
    }

    /// <summary>**造一个同名的不是"找回"** / Creating one with the same name is not "recovering".</summary>
    /// <remarks>
    /// 这条是 判断的直接反证：如果"造一个"就够了，那 `TryCreate` 就是重建器要的那一环。
    /// **新建的对象 `Identifier` / `Key` 都不同——注册进去就是另一个变量。**
    ///
    /// **A direct counterexample to the judgement**: if "create one" were enough, `TryCreate` would be the link the rebuilder needs. **A new object has a different
    /// `Identifier` / `Key`, so registering it introduces a different variable.**
    /// </remarks>
    [Fact]
    public void TheCreatedVariable_ShouldNotStandInForTheOriginal() {
        var original = new IntVar("x");
        VariableTypeCatalog.TryCreate("Integer", "x", out IVariableItem created).Should().BeTrue();

        created.Name.Should().Be(original.Name, "the display name matches, which is exactly why this is easy to get wrong");
        created.Identifier.Should().NotBe(original.Identifier,
            "identity is what distinguishes a variable, not its name");
        created.Key.Should().NotBe(original.Key);
    }
}
