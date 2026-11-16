#nullable enable

using System;
using FluentAssertions;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Chaotic;
using Fuookami.Ospf.Math.Geometry;
using Xunit;

namespace Fuookami.Ospf.Math.Tests.Chaotic;

/// <summary>
/// **两个吸引子回到标准式** / Two attractors back to their standard forms.
/// </summary>
/// <remarks>
/// 同步 Kotlin `966cafbc`（其内部编号 B311）。C# 侧把**修复前**的公式原样搬了过来，于是同一个错误在这里又活了一轮——
/// 五个偏离点全在：Aizawa 的 `dx` 减的是完整 `dy`、`dz` 的线性项用 `alpha*x`、输出把 `dz`/`dy` 置换；
/// Wimol-Banlue 用 `tan` 而不是 `tanh`、用 `|x|` 而不是 `|y|`。
///
/// **判据不复述实现：** **它在测试里按标准式独立算一遍，再与实现的一步结果比较。** 复述实现的判据只能证明"代码没变"，
/// 证明不了"公式对"——而这一批要的正是后者。
///
/// Syncs Kotlin `966cafbc` (its internal id B311). The C# side had copied the **pre-fix** formulas, so the same error lived on here: all five deviations
/// were present. **The criteria do not restate the implementation: they recompute the standard form inside the test and compare it with one step of the
/// implementation.** A criterion that restates the implementation only proves "the code did not change", not "the formula is right" — and the latter is what
/// this batch is about.
/// </remarks>
public class AttractorFormulaSyncTests
{
    /// <summary>**Aizawa 的一步必须等于标准式** / One Aizawa step must equal the standard form.</summary>
    /// <remarks>
    /// 标准式（Kotlin 966cafbc / B311）：
    /// `dx = (z-β)x - δy`、`dy = δx + (z-β)y`、`dz = γ + αz - z³/3 - (x²+y²)(1+εz) + ζzx³`，输出 `(x+h·dx, y+h·dy, z+h·dz)`。
    ///
    /// The standard form: as above, with the outputs in dx, dy, dz order.
    /// </remarks>
    [Fact]
    public void Aizawa_ShouldMatchTheStandardForm() {
        double alpha = 0.95, beta = 0.7, gamma = 0.6, delta = 3.5, epsilon = 0.25, zeta = 0.1, h = 0.01;
        double x = 0.31, y = -0.17, z = 0.53;

        double dx = (z - beta) * x - delta * y;
        double dy = delta * x + (z - beta) * y;
        double dz = gamma + alpha * z - z * z * z / 3.0
                    - (x * x + y * y) * (1.0 + epsilon * z)
                    + zeta * z * x * x * x;

        Point<Dim3, Flt64> next = AizawaAttractor<Flt64>.Create(
            new Flt64(alpha), new Flt64(beta), new Flt64(gamma), new Flt64(delta),
            new Flt64(epsilon), new Flt64(zeta), new Flt64(h)).Invoke(Point(x, y, z));

        next[0].Value.Should().BeApproximately(x + h * dx, 1e-12, "the first component advances along dx");
        next[1].Value.Should().BeApproximately(y + h * dy, 1e-12, "the second along dy");
        next[2].Value.Should().BeApproximately(z + h * dz, 1e-12, "the third along dz; the old code swapped dz and dy here");
    }

    /// <summary>**Aizawa 平局点：输出顺序被置换时这一条必红** / A tie-free point that goes red if the outputs are swapped.</summary>
    /// <remarks>
    /// 取 `x=0`、`y=0`：此时 `dx = 0`、`dy = 0`，而 `dz = γ + αz - z³/3 ≠ 0`。**旧实现把 `dz` 放进第二分量，
    /// 于是第二分量非零而标准式要求它为零——**一个不需要复算整条公式就能分辨的对照点。
    ///
    /// With x = 0 and y = 0: dx = 0 and dy = 0 while dz = gamma + alpha*z - z^3/3 is not zero. The old code put dz in the second component, so the second
    /// component would be non-zero where the standard form demands zero — a discriminator that needs no full recomputation.
    /// </remarks>
    [Fact]
    public void Aizawa_ShouldKeepTheOutputOrder() {
        Point<Dim3, Flt64> next = AizawaAttractor<Flt64>.Create().Invoke(Point(0.0, 0.0, 0.5));

        next[1].Value.Should().BeApproximately(
            0.0,
            1e-12,
            "dy is zero at the origin of the x/y plane, so the second component must not move");
        next[2].Value.Should().NotBeApproximately(
            0.5,
            1e-9,
            "dz is non-zero there, so the third component must move");
    }

    /// <summary>**Wimol-Banlue 的一步必须等于标准式（tanh、|y|）** / One Wimol-Banlue step must equal the standard form.</summary>
    /// <remarks>
    /// 标准式（Kotlin 966cafbc / B311）：`dx = y - x`、`dy = -z·tanh(x)`、`dz = -α + xy + |y|`。
    /// 两处偏离都在这里能分辨：`tan` 与 `tanh` 在 x≈1 已相差数倍；`|x|` 与 `|y|` 在符号相反时直接反号。
    ///
    /// The standard form: dx = y - x, dy = -z*tanh(x), dz = -alpha + x*y + |y|. Both deviations separate here: tan and tanh already differ by a factor at
    /// x ≈ 1, and |x| vs |y| flips sign when the two have opposite signs.
    /// </remarks>
    [Fact]
    public void WimolBanlue_ShouldMatchTheStandardForm() {
        double alpha = 2.0, h = 0.01;
        double x = 1.1, y = -0.4, z = 0.7;

        double dx = y - x;
        double dy = -z * global::System.Math.Tanh(x);
        double dz = -alpha + x * y + global::System.Math.Abs(y);

        Point<Dim3, Flt64> next = WimolBanlueAttractor<Flt64>.Create(new Flt64(alpha), new Flt64(h))
            .Invoke(Point(x, y, z));

        next[0].Value.Should().BeApproximately(x + h * dx, 1e-12);
        next[1].Value.Should().BeApproximately(
            y + h * dy,
            1e-12,
            "tanh keeps the second component bounded; tan would have produced a value several times larger here");
        next[2].Value.Should().BeApproximately(
            z + h * dz,
            1e-12,
            "|y| and |x| have opposite signs at this point, so the old form lands elsewhere");
    }

    /// <summary>**tanh 的有界性是换成它的理由** / Boundedness is why tanh replaced tan.</summary>
    /// <remarks>
    /// 从 `x` 靠近 `π/2`（`tan` 的极点）处走若干步：`tan` 会给出一个巨大的值并很快溢出，`tanh` 始终落在 `(-1, 1)`。
    /// **判据钉的是这条性质，而不是"改成了 tanh"**——前者说明为什么必须改。
    ///
    /// Starting near pi/2 (a pole of tan) and stepping: tan returns a huge value and soon overflows while tanh stays within (-1, 1). The criterion pins the
    /// property rather than "it now says tanh", because the property is why the change was required.
    /// </remarks>
    [Fact]
    public void WimolBanlue_ShouldStayBoundedNearTheTangentPole() {
        var attractor = WimolBanlueAttractor<Flt64>.Create();
        Point<Dim3, Flt64> point = Point(global::System.Math.PI / 2 - 0.01, 0.3, 0.2);

        for (int step = 0; step < 200; ++step) {
            point = attractor.Invoke(point);
        }

        foreach (int axis in new[] { 0, 1, 2 }) {
            double value = point[axis].Value;
            double.IsFinite(value).Should().BeTrue("axis " + axis + " must stay finite over two hundred steps");
            value.Should().BeLessThan(1000.0, "a bounded orbit near the tangent pole cannot run away");
        }
    }

    private static Point<Dim3, Flt64> Point(double x, double y, double z) => new(
        new[] { new Flt64(x), new Flt64(y), new Flt64(z) },
        Dim3.Instance);
}