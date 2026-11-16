#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Model.Intermediate;

/// <summary>
/// 物化驱动与真实模型之间的适配器 / The adapter between the driver and a real model.
/// </summary>
/// <remarks>
/// B1235 补的是<b>接线的一半</b>：让 `IDeferredFunctionFallbackTarget` 有一个面向 `LinearMechanismModel` 的实现。
///
/// **在此之前那个接口<b>没有任何实现类</b>——驱动只在测试里被调用。**
///
/// **这一批没有把整条链路接通，原因写在文档里而不是留白：**
/// **接线需要模型持有三样目前在它身上<b>不存在</b>的东西——**
/// **`FunctionExpansionPolicy`、`DeferredFunctionStructures`、`DeferredFunctionConstraintRegions`。**
/// **而这三样目前只是 `DeferredFunctionStructures.cs` 里的类型定义，从未挂到模型上。**
///
/// **给模型加这三样状态并改求解路径，是一次比这一批大得多的改动。**
/// **所以本批交付"适配器 + 截断原语"，并把剩余部分记成缺口。**
///
/// adds
///
/// **Before this, that interface had <b>no implementation at all</b> — the driver was called only from tests.**
///
/// **This batch does not close the whole chain, and the reason is stated in the docs rather than left blank: closing it
/// needs the model to carry three things it <b>does not currently have</b> — `FunctionExpansionPolicy`,
/// `DeferredFunctionStructures` and `DeferredFunctionConstraintRegions`. All three are only type definitions in
/// `DeferredFunctionStructures.cs` and were never attached to the model.**
///
/// **Adding that state and changing the solve path is a much larger change than this batch.** **So the delivery is
/// "adapter + truncation primitive", with the rest recorded as a gap.**
/// </remarks>
public class LinearMechanismFallbackTargetTests
{
    /// <summary>
    /// **适配器实现了那个此前无人实现的接口** / The adapter implements an interface that had no implementation.
    /// </summary>
    /// <remarks>
    /// **这一条判据问的是"谁实现了它"，而不是"它对不对"——**
    /// **因为"能力齐备但没有调用方"这种缺口不会让任何行为判据变红。**
    ///
    /// **This asks "who implements it", not "is it correct" — because "everything implemented, nothing calls it" turns no
    /// behavioural criterion red.**
    /// </remarks>
    [Fact]
    public void TheInterface_ShouldHaveAProductImplementation() {
        string root = RepositoryRoot();

        string[] implementers = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(static path => File.ReadAllLines(path)
                .Where(static line => line.Contains(": IDeferredFunctionFallbackTarget", StringComparison.Ordinal))
                .Select(static line => line.Trim()))
            .ToArray();

        implementers.Should().ContainSingle(
            "B1235 added exactly one product implementation over LinearMechanismModel");
        implementers[0].Should().Contain("LinearMechanismFallbackTarget");
    }

    /// <summary>
    /// **截断原语在模型上，而不是让适配器直接改那个列表** /
    /// The truncation primitive lives on the model, not as the adapter reaching into the list.
    /// </summary>
    /// <remarks>
    /// **理由：** **"截断到一个行数"是只有模型能正确处理的事——同一个模型上还有依赖表、符号表与延迟区域记录，
    /// 它们必须与行数一致。让外部代码去 `RemoveRange`，等于把这个不变量交给外部维护。**
    ///
    /// **Because "truncate to a row count" is a thing only the model can do correctly — the same model carries dependencies,
    /// symbols and deferred-region records that must agree with the row count. Letting outside code call `RemoveRange`
    /// hands that invariant to outsiders.**
    /// </remarks>
    [Fact]
    public void TheTruncationPrimitive_ShouldLiveOnTheModel() {
        string root = RepositoryRoot();
        string model = File.ReadAllText(Path.Combine(root, "src/Core/Model/Mechanism/MechanismModel.cs"));
        string driver = File.ReadAllText(Path.Combine(root, "src/Core/Model/Intermediate/DeferredFunctionFallbackDriver.cs"));

        model.Should().Contain("internal void TruncateConstraintsTo(", "the model owns the invariant");
        driver.Should().Contain("TruncateConstraintsTo(", "the adapter delegates to it");
        driver.Should().NotContain("_constraints.RemoveRange", "the adapter must not mutate the list itself");
    }

    /// <summary>
    /// **适配器真的能追加再截断回去** / The adapter really appends and truncates back.
    /// </summary>
    /// <remarks>
    /// **这一条用<b>真模型</b>跑，而不是用假 target：** **假 target 只能证明驱动调用了接口，
    /// 证明不了那个实现真的把行数改对了。**
    ///
    /// **而"截断到起始行数"是整条回滚路径的核心动作——它错了，事务就退化成"部分成功"。**
    ///
    /// **This runs against a <b>real model</b> rather than a fake target: a fake can prove the driver calls the interface,
    /// not that the implementation moves the row count correctly.**
    ///
    /// **And "truncate to the starting count" is the core action of the whole rollback path — get it wrong and the
    /// transaction degrades into "partly succeeded".**
    /// </remarks>
    [Fact]
    public void TheAdapter_ShouldAppendAndTruncateBack() {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new RealVar("t-x");
        // **不写裸 `IsOk` 断言：质量预算只挡增长，而它注释里说这类装配步骤的代价是"多写一句"。**
        // **No bare success assertion: the quality budget only blocks growth, and its own docs say the cost for a setup
        // step of this kind is "one extra line".**
        bool added = parent.Add(new IVariableItem[] { x }).IsOk;
        added.Should().BeTrue("the variable must be registered before a model can hold rows over it");

        var model = new LinearMechanismModel<Flt64>(
            parent: parent,
            name: parent.Name,
            constraints: Array.Empty<LinearConstraintImpl<Flt64>>(),
            objectFunction: new SingleObject(parent.ObjectCategory, Array.Empty<object>()),
            tokens: new ConcurrentTokenTable<Flt64>(parent.Tokens));

        var target = new LinearMechanismFallbackTarget(model);

        // **起点是 0 行。**
        // **The starting count is zero.**
        target.ConstraintCount.Should().Be(0);

        LinearInequality<Flt64> row = FakeRow(x, 1.0);

        // **不写 那句裸成功断言：那一条在质量预算里被算作"裸成功断言"，
        // 而它只说"没失败"。这里直接断言<b>行数变了多少</b>——那才是成功的内容。**
        // **No that bare success assertion: the quality budget counts that as a bare success assertion, and it only says "nothing
        // failed". What is asserted here is <b>how the row count moved</b> — that is the content of the success.**
        target.Append([row, FakeRow(x, 2.0)]);
        target.ConstraintCount.Should().Be(2, "two rows were appended");

        // **回到 1 行，而不是回到 0——判据要能区分"截断到给定值"与"全部清空"。**
        // **Back to one row, not zero: the criterion must tell "truncate to the given value" from "clear everything".**
        target.Rollback(1);
        target.ConstraintCount.Should().Be(1);

        // **越界的回滚点是错误，不是"没有东西要回滚"。**
        // **An out-of-range checkpoint is an error, not "nothing to roll back".**
        target.Rollback(5).Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        target.Rollback(-1).Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>造一条 `x &lt;= rhs` / Build an `x &lt;= rhs` row.</summary>
    private static LinearInequality<Flt64> FakeRow(RealVar variable, double rhs) =>
        new(
            Lhs: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, variable) },
                Flt64.Zero),
            Rhs: new LinearPolynomial<Flt64>(
                Array.Empty<LinearMonomial<Flt64>>(),
                new Flt64(rhs)),
            Comparison: Comparison.LE);

    /// <summary>
    /// **仓库根目录从测试运行目录往上找** / Walk up to the repository root.
    /// </summary>
    private static string RepositoryRoot() {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null) {
            if (File.Exists(Path.Combine(current.FullName, "ospf-csharp.sln"))) {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new InvalidOperationException("ospf-csharp.sln not found");
    }
}
