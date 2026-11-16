#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Copt.Tests;

/// <summary>
/// COPT 原生 PWL 的校验与写入 / COPT's native PWL validation and writing.
/// </summary>
/// <remarks>
/// 补写侧。**这里的判断：读侧全绿而写侧不存在，这种半成品不会自我报告。**
///
/// **报告能列出 PWL 结构、能算指纹、能校验列，
/// 而求解器从来没收到过任何一条 PWL 约束——于是那些报告描述的是一份<b>从未生效</b>的模型。**
///
/// **这也是为什么它不报错：读侧读的是自己的数据，写侧缺失不产生任何信号。**
///
/// **The report can list PWL structures, fingerprint them and validate their columns, while the solver
/// never received a single PWL constraint — so those reports describe a model that <b>never took effect</b>.**
///
/// **That is why it stays silent: the read side reads its own data, and a missing write side emits no signal.**
/// </remarks>
public class CoptNativePiecewiseContractTests
{
    /// <summary>建一个变量键 / Build a variable key.</summary>
    private static VariableItemKey Key(ulong identifier, int index = 0) =>
        new(new Fuookami.Ospf.Math.Algebra.Number.UInt64(identifier), index);

    /// <summary>一份合法的 PWL / A valid PWL.</summary>
    private static NativePiecewiseData Valid(VariableItemKey input, VariableItemKey result, string name = "pwl") =>
        new(input, result, [0.0, 1.0, 2.0], [0.0, 2.0, 0.0], name);

    /// <summary>
    /// **无穷哨兵必须有限且为正** / The infinity sentinel must be finite and positive.
    /// </summary>
    /// <remarks>
    /// **它列为第一条校验，而它是唯一<b>与具体 PWL 无关</b>的一条：** **它检查的是求解器常量本身。**
    ///
    /// **为什么它值得一条独立判据：** **λ 的上下界由它推导，哨兵无效会让每条 PWL 的边界一起失效——
    /// 而一条 PWL 的边界失效不会抛异常，只会让 λ 变成无界。**
    ///
 /// **The implementation lists this first, and it is the only check <b>independent of any particular PWL</b>: it inspects the
    /// solver constant itself.**
    ///
    /// **Why it deserves its own criterion: the λ bounds derive from it, so an invalid sentinel invalidates every PWL's
    /// bounds at once — and an invalid λ bound throws nothing, it just makes λ unbounded.**
    /// </remarks>
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void AnInvalidInfinitySentinel_ShouldBeRejected(double infinity) {
        VariableItemKey input = Key(1);
        VariableItemKey result = Key(2);

        Result<Success, ErrorCode, Error<ErrorCode>> outcome = CoptNativePiecewise.Validate(
            new[] { input, result },
            [Valid(input, result)],
            infinity);

        outcome.Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>
    /// **输入键与结果键不得相同** / Input and result keys must differ.
    /// </summary>
    [Fact]
    public void TheSameInputAndResultKey_ShouldBeRejected() {
        VariableItemKey key = Key(1);

        Result<Success, ErrorCode, Error<ErrorCode>> outcome = CoptNativePiecewise.Validate(
            new[] { key },
            [Valid(key, key)],
            1e20);

        outcome.Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>
    /// **两个 PWL 不得抢同一个结果变量** / Two PWLs must not claim the same result variable.
    /// </summary>
    /// <remarks>
    /// **这一条比"键不同"更隐蔽：单看每一条 PWL 都合法，冲突只在<b>它们之间</b>。**
    /// **而后果是两条折线同时决定一个 `y`——模型可解，但那个 `y` 满足的不是任何一条折线。**
    ///
    /// **This is subtler than "keys differ": each PWL is individually valid, and the conflict exists only <b>between</b>
    /// them.** **The consequence is two polylines both pinning one `y` — the model solves, and that `y` satisfies neither.**
    /// </remarks>
    [Fact]
    public void TwoPiecewiseSharingAResultKey_ShouldBeRejected() {
        VariableItemKey inputA = Key(1);
        VariableItemKey inputB = Key(2);
        VariableItemKey shared = Key(3);

        Result<Success, ErrorCode, Error<ErrorCode>> outcome = CoptNativePiecewise.Validate(
            new[] { inputA, inputB, shared },
            [Valid(inputA, shared, "a"), Valid(inputB, shared, "b")],
            1e20);

        outcome.Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>
    /// **断点至少两个且与函数值等长** / At least two breakpoints, same length as the values.
    /// </summary>
    /// <remarks>
    /// **一个断点无法构成线段：凸组合会退化成"λ₁ = 1"，于是 `y` 被钉死在一个值上，
    /// 而那条 PWL 事实上变成了一条常数约束。**
    ///
    /// **长度不等则是另一回事：** **`x` 与 `y` 的 λ 个数不同，两条 linking 行会索引到不同的 λ，
    /// 而循环按 `x` 的长度跑——于是 `y` 那一行会漏掉最后几个系数。不抛异常，只是错。**
    ///
    /// **One breakpoint cannot form a segment: the convex combination degenerates to "λ₁ = 1", pinning `y` to one value,
    /// so the PWL becomes a constant constraint in disguise.**
    ///
    /// **A length mismatch is another matter: `x` and `y` would index different λ, the loop runs over `x`'s length, and
    /// `y`'s row silently loses its last coefficients. No exception, just wrong.**
    /// </remarks>
    [Fact]
    public void FewerThanTwoBreakpoints_ShouldBeRejected() {
        VariableItemKey input = Key(1);
        VariableItemKey result = Key(2);

        Result<Success, ErrorCode, Error<ErrorCode>> outcome = CoptNativePiecewise.Validate(
            new[] { input, result },
            [new NativePiecewiseData(input, result, [0.0], [0.0], "single")],
            1e20);

        outcome.Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>
    /// **函数值数量必须与断点一致** / The value count must match the breakpoint count.
    /// </summary>
    [Fact]
    public void MismatchedValueCount_ShouldBeRejected() {
        VariableItemKey input = Key(1);
        VariableItemKey result = Key(2);

        Result<Success, ErrorCode, Error<ErrorCode>> outcome = CoptNativePiecewise.Validate(
            new[] { input, result },
            [new NativePiecewiseData(input, result, [0.0, 1.0, 2.0], [0.0, 2.0], "mismatch")],
            1e20);

        outcome.Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>
    /// **未映射的变量键被拒** / An unmapped variable key is rejected.
    /// </summary>
    [Fact]
    public void AnUnmappedVariableKey_ShouldBeRejected() {
        VariableItemKey input = Key(1);
        VariableItemKey result = Key(2);

        Result<Success, ErrorCode, Error<ErrorCode>> outcome = CoptNativePiecewise.Validate(
            new[] { input },
            [Valid(input, result)],
            1e20);

        outcome.Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>
    /// **一份合法的数据通过** / A valid datum passes.
    /// </summary>
    /// <remarks>
    /// **这一条是上面六条的反面：** **没有它，把 `Validate` 写成"永远失败"也能让上面全绿。**
    ///
    /// **This is the counterpart to the six above: without it, a `Validate` that always fails would keep them all
    /// green.**
    /// </remarks>
    [Fact]
    public void AValidDatum_ShouldPass() {
        VariableItemKey input = Key(1);
        VariableItemKey result = Key(2);

        Result<Success, ErrorCode, Error<ErrorCode>> outcome = CoptNativePiecewise.Validate(
            new[] { input, result },
            [Valid(input, result)],
            1e20);

        outcome.Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>
    /// **写入先校验，且每条 PWL 都写 SOS2** / Writing validates first, and every PWL gets an SOS2.
    /// </summary>
    /// <remarks>
    /// **结构判据，因为"写进去了但 COPT 没接受"在这台机器上没有真跑 COPT 时无法行为化。**
    ///
    /// **而 SOS2 这一条特别值得钉：** **没有它，那四条行描述的是断点的<b>凸包</b>，不是折线——
    /// λ 可以是任意凸组合，`y` 能取到凸包内部的任意值。模型仍然可解，结果却是错的。**
    ///
    /// **Structural, because "written but not accepted" cannot be made behavioural without really running COPT.**
    ///
    /// **The SOS2 line is worth pinning hardest: without it those four rows describe the <b>convex hull</b> of the
    /// breakpoints, not the polyline — λ may be any convex combination and `y` lands anywhere inside. The model still
    /// solves and the answer is wrong.**
    /// </remarks>
    [Fact]
    public void TheWritePath_ShouldValidateFirstAndWriteAnSos2() {
        string path = Path.Combine(
            RepositoryRoot(),
            "src/Core.Plugin.Copt/CoptNativePiecewise.cs");
        string text = File.ReadAllText(path);

        text.Should().Contain("internal static Try Validate(", "the up-front validation entry must exist");
        text.Should().Contain("internal static Try Add(", "the write entry must exist");
        text.Should().Contain("AddSos(", "the SOS2 call must exist");
        text.Should().Contain("SOS_TYPE2", "it must be an SOS of type 2");

        int validateCall = text.IndexOf("Validate(variables.Keys", StringComparison.Ordinal);
        int firstAdd = text.IndexOf("model.AddVar(", StringComparison.Ordinal);
        validateCall.Should().BeGreaterThan(0, "Add must call Validate");
        firstAdd.Should().BeGreaterThan(0, "Add must create variables");
        validateCall.Should().BeLessThan(firstAdd, "validation must come before the model is touched");
    }

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
