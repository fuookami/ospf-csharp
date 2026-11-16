#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Fuookami.Ospf.Utils.Tests.Functional;

/// <summary>
/// **`Variant` 的档位要和基线对齐** / The `Variant` arities have to match the baseline.
/// </summary>
/// <remarks>
/// 按公开类型名扫了 22 个模块，**`utils` 的命中率只有 62.9%**，而未出现的那 36 个里有一整段是
/// `Variant9`—`Variant20`——**基线有 `Variant2` 到 `Variant20`，本仓只到 `Variant8`。**
///
/// **这不是"够用了"**：`Variant` 表示"N 选一"，**它的价值在于"类型层面保证恰好一个分支"**，
/// **少一个档位就少一个能安全表达的元数**——**而少掉的那一档，调用方只能退回 `object` 加 `if`，**
/// **于是"漏一个分支"从编译错误变成运行时"没有反应"。**
///
/// A sweep over 22 modules by public type name, and **`utils` hit only 62.9%**; among the 36 absent names was an
/// entire run of `Variant9`—`Variant20` — **the baseline has `Variant2` through `Variant20`, this repo stopped at `Variant8`.**
///
/// **That is not "good enough"**: `Variant` represents "one of N", **and its value is a type-level guarantee that exactly one branch is taken**,
/// **so one missing arity is one arity that cannot be expressed safely** — **and the caller of that arity falls back to `object` plus `if`,**
/// **turning "a missed branch" from a compile error into a run-time "nothing happens".**
/// </remarks>
public class VariantCoverageTests
{
    /// <summary>**`Variant2`—`Variant20` 都在** / `Variant2` through `Variant20` all exist.</summary>
    /// <remarks>
    /// **上界 20 不是随便定的**：**基线 `Variant.kt` 就写到 20**（实测），
    /// **而"基线有多少档"这件事必须以基线为准，不能以"我们用得到多少"为准**——
    /// **后者会让"用不到"变成"没有"，而下一个需要 12 元的人会发现它不存在。**
    ///
    /// **The upper bound 20 is not arbitrary**: **the baseline's `Variant.kt` stops at 20** (measured),
    /// **and "how many arities the baseline has" has to be answered by the baseline, not by "how many we happen to need"** —
    /// **the latter turns "unused" into "absent", and the next person needing arity 12 finds it missing.**
    /// </remarks>
    [Fact]
    public void TheArities_ShouldReachTwenty()
    {
        // **泛型类型的 `Name` 带反引号后缀**（`Variant12` 的 `Name` 是 `Variant12\`12`），
        // **所以要按反引号截断再比。** 实测报错：`FormatException: The input string '2\`2' was not in a correct format.`
        //
        // **A generic type's `Name` carries a backtick suffix** (`Variant12`'s `Name` is `Variant12\`12`),
        // **so it is truncated at the backtick before comparison.** Measured: `FormatException: The input string '2\`2' was not in a correct format.`
        string[] declared = typeof(Variant2<,>).Assembly.GetTypes()
            .Where(type => type.IsAbstract && type.Name.StartsWith("Variant", StringComparison.Ordinal))
            .Select(type => type.Name.Split('`')[0])
            .Distinct()
            .ToArray();

        for (int arity = 2; arity <= 20; ++arity)
        {
            string name = "Variant" + arity;
            declared.Should().Contain(name,
                $"the baseline declares {name} and a missing arity cannot be expressed safely");
        }
    }

    /// <summary>**每个档位的泛型参数个数就是它的名字里的数字** / Each arity's type-parameter count is the number in its name.</summary>
    /// <remarks>
    /// **这是"名字与形状一致"的判据**：`Variant12` 若有 11 或 13 个参数，
    /// **`Match` 的分支数就与名字不符**——**而调用方是照着名字去数的。**
    ///
    /// **This pins "the name matches the shape"**: if `Variant12` had 11 or 13 parameters,
    /// **`Match`'s branch count would disagree with the name** — **and callers count by the name.**
    /// </remarks>
    [Fact]
    public void TheArity_ShouldMatchTheTypeParameterCount()
    {
        Type[] variants = typeof(Variant2<,>).Assembly.GetTypes()
            .Where(type => type.IsAbstract && type.Name.StartsWith("Variant", StringComparison.Ordinal))
            .ToArray();

        variants.Should().NotBeEmpty();

        foreach (Type variant in variants)
        {
            int expected = int.Parse(variant.Name.Split('`')[0].Substring("Variant".Length));
            variant.GetGenericArguments().Should().HaveCount(expected,
                $"{variant.Name} must take {expected} type parameters, or its Match arity lies");
        }
    }

    /// <summary>**`Match` 的分支数与档位一致** / `Match` takes as many handlers as the arity.</summary>
    /// <remarks>
    /// **这是这个类型存在的全部理由**：**参数个数就是分支个数，漏一个编译不过。**
    /// **如果 `Match` 少一个参数，那一个分支就永远没人处理，而编译器不会说话。**
    ///
    /// **This is the whole reason the type exists**: **the parameter count is the branch count, and missing one fails to compile.**
    /// **If `Match` took one parameter fewer, that branch would never be handled and the compiler would stay silent.**
    /// </remarks>
    [Fact]
    public void TheMatch_ShouldTakeOneHandlerPerBranch()
    {
        Type[] variants = typeof(Variant2<,>).Assembly.GetTypes()
            .Where(type => type.IsAbstract && type.Name.StartsWith("Variant", StringComparison.Ordinal))
            .ToArray();

        foreach (Type variant in variants)
        {
            int expected = int.Parse(variant.Name.Split('`')[0].Substring("Variant".Length));
            // **`GetMethod("Match")` 在泛型类型上会 `AmbiguousMatchException`**——
            // **`Match<R>` 是一个泛型方法，反射按名字取会撞上"多个候选"。** 用 `GetMethods` 过滤。
            //
            // **`GetMethod("Match")` throws `AmbiguousMatchException` on a generic type** —
            // **`Match<R>` is a generic method, and lookup by name alone finds several candidates.** Filter `GetMethods` instead.
            MethodInfo? match = variant
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .FirstOrDefault(method => method.Name == "Match" && method.IsGenericMethodDefinition);
            match.Should().NotBeNull($"{variant.Name} has to offer Match");

            match!.GetParameters().Should().HaveCount(expected,
                $"{variant.Name}.Match must take one handler per branch");
        }
    }

    /// <summary>**一个真实的高元数用例** / A real high-arity case.</summary>
    /// <remarks>
    /// **上面三条都是反射，而反射判据有一个通病：它们能证明"形状对"，不能证明"能用"。**
    /// **所以这里真的构造一个 `Variant12` 并 `Match` 一遍。**
    ///
    /// **The three above are reflection-based, and reflection criteria share one weakness: they prove the shape, not that it works.**
    /// **So this constructs a real `Variant12` and runs `Match` once.**
    /// </remarks>
    [Fact]
    public void TheHighArity_ShouldActuallyWork()
    {
        var value = new Variant12<int, int, int, int, int, int, int, int, int, int, int, string>.V12("twelfth");

        string result = value.Match(
            _ => "1", _ => "2", _ => "3", _ => "4", _ => "5", _ => "6",
            _ => "7", _ => "8", _ => "9", _ => "10", _ => "11", twelfth => twelfth);

        result.Should().Be("twelfth", "the twelfth branch is the one that carries the value");
    }
}
