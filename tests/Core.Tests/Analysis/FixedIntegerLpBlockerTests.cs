#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Report;
using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 固定整数 LP 的真实对偶后端到底卡在哪 / Exactly what blocks the real-dual backend.
/// </summary>
/// <remarks>
/// **这两条是"绊线"，不是"保证"。** 它们现在通过，**而它们存在的意义是：一旦解除就会红。**
///
/// 台账里长期写着"只剩真实对偶后端"。**那是一句描述，不是一个可以检查的事实**——
/// **而描述会过期：有人给求解输出加上对偶之后，那句话仍然读着像真的。**
///
/// **These two are tripwires, not guarantees.** They pass now, **and their point is to go red once the blocker lifts.**
/// The ledger has long said "only the real-dual backend is left". **That is a description, not a checkable fact** — **and descriptions expire: after someone adds duals to the solver output, the sentence still reads as
/// true.**
/// </remarks>
public class FixedIntegerLpBlockerTests
{
    /// <summary>**求解输出层不得出现对偶值** / The solver output layer must carry no dual values.</summary>
    /// <remarks>
    /// **`LinearSolverFixedIntegerLpBackend` 搬不过来，卡点就在这里**：它要从线性求解的输出里读对偶，
    /// **而 C# 的求解输出类型一个对偶字段都没有。**
    ///
    /// **这条判据红了，说明卡点没了**——那就该去把那个后端搬过来，
    /// **而不是把这条判据改绿。**
    ///
    /// **This is where `LinearSolverFixedIntegerLpBackend` is stuck**: it reads duals out of the linear solve output, **and no C# solver output type has a dual field at all.**
    /// **When this criterion goes red the blocker is gone** — then go port that backend, **not make this criterion green again.**
    /// </remarks>
    [Fact]
    public void TheSolverOutput_ShouldStillCarryNoDuals()
    {
        string[] dualMembers = typeof(IFixedIntegerLpBackend)
            .Assembly
            .GetTypes()
            .Where(type => type.Namespace?.Contains("Solver.Output", StringComparison.Ordinal) == true)
            .SelectMany(type => type.GetMembers(BindingFlags.Public | BindingFlags.Instance))
            .Where(member => member.Name.Contains("Dual", StringComparison.OrdinalIgnoreCase))
            .Select(member => $"{member.DeclaringType?.Name}.{member.Name}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        dualMembers.Should().BeEmpty(
            "the solver output layer still has no duals, so LinearSolverFixedIntegerLpBackend cannot be ported yet. "
            + "If this fails, duals now exist: port that backend instead of relaxing this criterion");
    }

    /// <summary>**只有降阶适配器实现了后端接口** / Only the lowering adapter implements the backend interface.</summary>
    /// <remarks>
 /// `IFixedIntegerLpBackend` 有两个基线实现：降阶适配器**与求解器直连那个（搬不了）**。
    ///
    /// **数清楚"现在有几个实现"比写"还剩一个"可靠**——**"还剩一个"不会告诉你已经变成了两个。**
    ///
    /// The baseline has two implementations: the lowering adapter (**ported in B1066**) **and the solver-backed one (not portable here)**.
    /// **Counting how many exist is more reliable than writing "one is left"** — **"one is left" never tells you it has become two.**
    /// </remarks>
    [Fact]
    public void TheBackendInterface_ShouldHaveExactlyOneImplementation()
    {
        Type[] implementations = typeof(IFixedIntegerLpBackend)
            .Assembly
            .GetTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false })
            .Where(type => typeof(IFixedIntegerLpBackend).IsAssignableFrom(type))
            .ToArray();

        implementations.Should().ContainSingle(
            "only the lowering adapter exists; the solver-backed one needs duals that the solver layer does not expose");
        implementations[0].Should().Be<LoweredFixedIntegerLpBackend>(
            "the single implementation has to be the lowering adapter, or the counting above is meaningless");
    }
}
