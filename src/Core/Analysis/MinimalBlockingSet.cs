#nullable enable

using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Core.Analysis;

/// <summary>
/// 最小阻塞集合 / A minimal blocking set.
/// </summary>
/// <remarks>
/// **这是 `ConflictAnalyzer.kt` 里 `MinimalBlockingSet` 的移植。**
///
/// **它是一个独立类型，不是一个属性。** C# 侧此前只有 `ConflictExplanation.MinimalBlockingSet`
/// ——那只是一个 `Members` 的别名，**而别名丢掉了"最小性"这件事本身。**
///
/// **It is a type, not a property.** The C# side previously had only `ConflictExplanation.MinimalBlockingSet` — a plain alias for `Members`, **and an alias throws away the minimality itself.**
///
/// | 成员 | 为什么必须带出来 | Why it has to come along |
/// | --- | --- | --- |
/// | `Members` | 阻塞集合本身 | the blocking set itself |
/// | `Status` | **"提取到"与"只提取了一部分"是两种结论** | **"extracted" and "only partly extracted" are two different conclusions** |
/// | `Minimality` | **`Verified` 的判据** | the basis for `Verified` |
/// | `Validity` | 这个集合在什么前提下成立 | the premise under which it holds |
/// | `Verifications` | 逐条证据 | the per-item evidence |
///
/// **`Verified` 只认 `Irreducible`。** 把 `NotChecked` 也当成"已验证"会让**没验过的东西看起来验过了**；
/// 把 `Reducible` 当成"已验证"更糟——**它已经被证明还能再小。**
///
/// **`Verified` accepts only `Irreducible`.** Treating `NotChecked` as verified makes **unchecked look checked**; treating `Reducible` as verified is worse — **it has already been proven to shrink further.**
/// </remarks>
public sealed record MinimalBlockingSet(
    ObjectiveTarget Target,
    IReadOnlyList<DiagnosticSource> Members,
    AnalysisStatus Status,
    ConflictMinimality Minimality,
    ConflictValidity Validity,
    IReadOnlyList<ConflictVerification> Verifications)
{
    /// <summary>
    /// 是否已证明不可再小 / Whether irreducibility is proven.
    /// </summary>
    /// <remarks>
    /// **只认 `Irreducible`。** `NotChecked` 是"没验过"，`Reducible` 是"已证明还能更小"——
    /// **两者都不能被读成"已验证"。**
    ///
    /// **Only `Irreducible`.** `NotChecked` means "never checked", `Reducible` means "proven to shrink further" — **neither may be read as verified.**
    /// </remarks>
    public bool Verified => Minimality == ConflictMinimality.Irreducible;

    /// <summary>
    /// 从完整解释投影出最小阻塞集合 / Project a minimal blocking set out of a full explanation.
    /// </summary>
    /// <param name="explanation">完整解释 / The full explanation.</param>
    /// <returns>最小阻塞集合 / The minimal blocking set.</returns>
    /// <remarks>
    /// **投影保留 `Minimality` 与 `Validity` 原值，不做任何"顺手修正"。**
    /// 一个把 `NotChecked` 升格成 `Irreducible` 的投影会让**每一份报告都自称已验证**。
    ///
    /// **The projection keeps `Minimality` and `Validity` as they are, with no helpful repair.** A projection that promotes `NotChecked` to `Irreducible` would make **every report claim to be verified.**
    /// </remarks>
    public static MinimalBlockingSet From(ConflictExplanation explanation) {
        ArgumentNullException.ThrowIfNull(explanation);

        return new MinimalBlockingSet(
            Target: explanation.Target,
            Members: explanation.Members,
            Status: explanation.Status,
            // **原样带出，不升格。** / **Carried through as-is, never promoted.**
            Minimality: explanation.Minimality,
            Validity: explanation.Validity,
            Verifications: explanation.Verifications ?? Array.Empty<ConflictVerification>());
    }
}
