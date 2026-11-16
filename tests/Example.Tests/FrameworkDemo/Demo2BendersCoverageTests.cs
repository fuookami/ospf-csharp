using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// Benders 三套注册接口的覆盖面 / Coverage of the three Benders registration entry points.
/// </summary>
/// <remarks>
/// 基线在 demo2 域里有 **18 个文件**各定义一次 `registerForBendersMP`（聚合 + 上下文各一份），另有对应的 `registerForBendersSP`
/// 与 `flushForBendersSP`，共 **54 个函数**（`git grep` 实测，基线 `12fdb587`）。
///
/// 本仓实测（反射，见下）：**MP 18 / SP 18 / Flush 12**，共 48。
///
/// 时是 14 / 14 / 8：那时 `StowageContext`、`MacContext`、`LoadingEffectivenessAggregation`、`RedundancyAggregation`
/// 四处的 MP/SP 都缺，按基线补齐——**MP 与 SP 现在与基线持平（18 / 18）**。
///
/// 剩下的 6 个是 **聚合层** 的 flush：基线里它们是 `private fun`（`git cat-file` 实测：`loading_effectiveness`、`redundancy`、`mac` 三处都是 `private`），
/// **外部根本调不到，基线自己也不用**。所以本仓**故意不移植**——它们是基线里的死代码，不是缺口。
/// 判据因此钉 **12**，并在下一条里点名这六个聚合，谁"为了整齐"给它们补上公开 flush，判据会红。
///
/// 这条判据钉的是**当前数字**，不是"应该有的数字"：缺口补上时它会红，**届时把它改成新的数字并在这里写明为什么**——
/// 这正是本仓对"记录缺口的判据"的用法。
///
/// The baseline defines these three once in **18 demo2 domain files** (one per aggregation and context), **54 functions** in total (`git grep` on `12fdb587`).
/// This port measures **MP 18 / SP 18 / Flush 12** — 48. At it was 14/14/8; added the four missing sets (`StowageContext`, `MacContext`,
/// `LoadingEffectivenessAggregation`, `RedundancyAggregation`), so **MP and SP now match the baseline**. The remaining six are the **aggregation-level** flushes, which
/// the baseline declares `private` (measured with `git cat-file` in `loading_effectiveness`, `redundancy` and `mac`): **unreachable from outside and unused by the
/// baseline itself**, so this port **deliberately does not port them** — dead code in the baseline, not a gap. The criterion therefore pins **12** and names the six
/// aggregations below, so anyone who adds a public flush "for tidiness" turns it red.
/// </remarks>
public sealed class Demo2BendersCoverageTests
{
    [Fact]
    public void TheThreeEntryPoints_ShouldCoverTheRecordedNumberOfTypes() {
        Assembly assembly = typeof(ResponseDTO).Assembly;

        int mp = CountTypesDeclaring(assembly, "RegisterForBendersMP");
        int sp = CountTypesDeclaring(assembly, "RegisterForBendersSP");
        int flush = CountTypesDeclaring(assembly, "FlushForBendersSP");

        mp.Should().Be(18, "every aggregation and context now carries the master-problem entry — baseline parity");
        sp.Should().Be(18, "same set as above — baseline parity");
        flush.Should().Be(12, "every context has it; the six aggregation-level flushes are private dead code in the baseline and are deliberately not ported");
        (mp + sp + flush).Should().Be(48, "54 minus the six private aggregation flushes the baseline never uses");
    }

    /// <summary>
    /// **六个聚合仍然没有公开的 flush** / The six aggregations still have no public flush.
    /// </summary>
    /// <remarks>
    /// 上面数的是"有多少类型定义了它"，这条数的是"**谁**没定义"——**前者可能因为有人补了别的类型而保持 12**，
    /// 后者才说得清剩下的到底是哪几个。两者一起才等于"覆盖面没有被悄悄挪走"。
    ///
    /// **为什么点名的是"没有公开 flush"而不是"缺口"**：基线那六个是 `private fun`，**外部调不到**。本仓补上它们只会得到六个没人能调的死方法——
    /// 而"看起来补齐了"正是本仓反复记录过的缺陷形态。**判据点名它们，是为了让"为了整齐而补"这个动作变红。**
    ///
    /// The assertion above counts **how many types declare it**; this one names **which ones do not** — **the count can stay at 12 if someone adds an unrelated type**,
    /// while this names exactly what is left. Only together do they mean the coverage has not quietly moved. **Why this names "no public flush" rather than "a gap"**:
    /// the baseline's six are `private fun`, **unreachable from outside**. Porting them would produce six methods nobody can call — and "it looks complete now" is a
    /// defect shape this port has recorded repeatedly. **They are named so that adding them "for tidiness" turns red.**
    /// </remarks>
    [Fact]
    public void TheSixAggregations_ShouldStillLackAPublicFlush() {
        Assembly assembly = typeof(ResponseDTO).Assembly;
        HashSet<string> declaring = TypesDeclaring(assembly, "FlushForBendersSP");

        foreach (string type in new[] {
            "StowageAggregation",
            "MacAggregation",
            "AirworthinessSecurityAggregation",
            "SoftSecurityAggregation",
            "MacOptimizationAggregation",
            "ExpressEffectivenessAggregation",
        }) {
            declaring.Should().NotContain(type,
                type + " is an aggregation; the baseline's flush there is private and unused, so a public one here would be an uncallable method — "
                + "if you really need it, say why in the plan and update this criterion instead of deleting it");
        }
    }

    private static int CountTypesDeclaring(Assembly assembly, string method) =>
        TypesDeclaring(assembly, method).Count;

    private static HashSet<string> TypesDeclaring(Assembly assembly, string method) {
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (Type type in assembly.GetTypes()) {
            foreach (MethodInfo info in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)) {
                if (info.Name == method) {
                    found.Add(type.Name);
                    break;
                }
            }
        }
        return found;
    }
}
