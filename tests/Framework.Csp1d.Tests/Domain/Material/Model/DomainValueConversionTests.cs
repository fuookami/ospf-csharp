// 领域数值转换的判据 / Criteria for domain value conversion.

using FluentAssertions;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Test.Framework.Csp1d.Domain.Material.Model;

/// <summary>
/// **`ToFlt64` 必须能转换所有受支持的数值类型，且不静默归零** / `ToFlt64` must convert every supported numeric type and must not silently return zero.
/// </summary>
/// <remarks>
/// **B383 发现的偏离**：`LengthObjectivePipeline` 与 `TopKCuttingPlans` 各有一份**手写的**
/// `ToFlt64`，对非 `Flt64` / `FltX` 的 `V` **静默返回 `Flt64.Zero`**。
///
/// 后果都是**静默的**：
///
/// | 位置 | 归零的后果 |
/// | --- | --- |
/// | 长度目标项 | **惩罚项直接消失**，优化目标少了一整块 |
/// | `TopK` 排序 | **所有方案得分都是 0**，退化成"取前 K 个" |
///
/// 而 `DomainValueConversion.ToFlt64` **本来就在**，用 `System.Convert.ToDouble` 兜底。
/// **两份手写版本是重复实现，且都是错的。**
///
/// **B383's deviation**: `LengthObjectivePipeline` and `TopKCuttingPlans` each carried a **hand-written** `ToFlt64` that **silently returned `Flt64.Zero`** for any `V` other
/// than `Flt64` / `FltX`. Both failures are **silent** (table above), while `DomainValueConversion.ToFlt64` **already existed** with a `System.Convert.ToDouble` fallback.
/// **The two hand-written copies were duplicates, and both were wrong.**
/// </remarks>
public class DomainValueConversionTests {
    /// <summary>**静态调用必须走 `DomainValueConversion` 的分支，而不是实例方法** / A static call must use `DomainValueConversion`'s branches, not an instance method.</summary>
    /// <remarks>
    /// **本批在这里栽了三次，最后才发现根因。**
    ///
    /// 前两版判据都写成 `value.ToFlt64()`。**`FltX` 自己有一个实例方法 `ToFlt64()`**
    /// （`Floating.cs:461`），C# **优先绑定实例方法而不是扩展方法**——
    /// 所以判据**从来没调用到 `DomainValueConversion.ToFlt64`**，
    /// 于是删掉它的 `FltX` / `Int64` / `Flt64` 分支，判据**全部存活**。
    ///
    /// **这不是"变异等价"，是判据压根没测到目标。** 判据里必须**静态调用**。
    ///
    /// 顺带说明为什么本批前两次变异"完整重建后仍存活"——原因就在这里，
    /// 不是增量编译的问题（那个方向当时也怀疑过，查了产物时间才排除）。
    ///
    /// **This batch failed three times here before finding the root cause.** The first two versions called `value.ToFlt64()`, but **`FltX` has its own instance method
    /// `ToFlt64()`** (`Floating.cs:461`), and C# **binds the instance method before the extension method** — so the criteria **never reached `DomainValueConversion.ToFlt64`**
    /// and removing its `FltX` / `Int64` / `Flt64` branches left them **all green**. **That is not an equivalent mutant; the criterion simply never tested the target.** The
    /// criterion must call it **statically**.
    /// </remarks>
    /// <summary>**`ConvertSolverValue` 对 `Int64` / `Flt32` 必须返回 `Failed`，不是静默成功** / It must return `Failed` for `Int64` / `Flt32`, not silently succeed.</summary>
    /// <remarks>
    /// **B392 记的缺口**："`ConvertSolverValue` 失败时抛 `InvalidOperationException`，本批未测"。
    /// B392 当时猜它"需要不受支持的 `V`"，而本仓四个 `V` 里前三个都被支持——
    /// **这个猜测是错的，本批实测推翻了它**：
    ///
    /// | `V` | `ConvertSolverValue` 结果 |
    /// | --- | --- |
    /// | `Flt64` | **`Ok`** |
    /// | `FltX` | `Ok`（走 `ToFltX`） |
    /// | **`Int64`** | **`Failed`** |
    /// | **`Flt32`** | **`Failed`** |
    ///
    /// 所以这条失败路径**完全可达**——只要有人用 `Int64` 或 `Flt32` 实例化 CSP1D。
    ///
    /// 下游 `Csp1dShadowPriceLifecycle.ConvertSolverValue` 遇到 `Failed` 会**抛
    /// `InvalidOperationException`**。**这比静默返回零好**（B383 修的就是静默归零），
    /// 但意味着**用 `Int64` 实例化 CSP1D 会在运行期炸**，而不是在类型层面被挡住。
    ///
    /// **B392's gap**: "`ConvertSolverValue` throws on failure, untested". B392 guessed it needed an unsupported `V` and that the first three of the four `V`s were
    /// supported — **that guess was wrong, as measured this batch** (table above). `Int64` and `Flt32` **both return `Failed`**, so the failure path is **fully
    /// reachable**: anyone instantiating CSP1D with `Int64` or `Flt32` hits it. `Csp1dShadowPriceLifecycle.ConvertSolverValue` then **throws
    /// `InvalidOperationException`** — **better than silently returning zero** (what B383 fixed), but it means **`Int64`/`Flt32` instances blow up at runtime** instead of
    /// being rejected at the type level.
    /// </remarks>
    [Fact]
    public void ConvertSolverValue_ShouldFailForInt64AndFlt32() {
        DomainValueConversion.ConvertSolverValue(new Int64(0), new Flt64(3.5))
            .IsFailed.Should().BeTrue("Int64 has no conversion path");
        DomainValueConversion.ConvertSolverValue(new Flt32(0.0f), new Flt64(3.5))
            .IsFailed.Should().BeTrue("Flt32 has no conversion path");
        DomainValueConversion.ConvertSolverValue(new Flt64(0.0), new Flt64(3.5))
            .IsOk.Should().BeTrue("Flt64 is the supported path");
    }

    [Fact]
    public void ToFlt64_ShouldConvertThroughTheSharedBranches() {
        DomainValueConversion.ToFlt64(new FltX(3.25)).ToDouble()
            .Should().BeApproximately(3.25, 1e-9, "the FltX branch must convert");
        DomainValueConversion.ToFlt64(new Int64(42)).ToDouble()
            .Should().BeApproximately(42.0, 1e-9, "the Int64 branch must convert");
        DomainValueConversion.ToFlt64(new Flt64(7.5)).ToDouble()
            .Should().BeApproximately(7.5, 1e-9, "the Flt64 branch must convert");
    }
}
