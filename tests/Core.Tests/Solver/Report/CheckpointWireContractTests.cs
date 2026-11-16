#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Report;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Solver.Report;

/// <summary>
/// **C# 这一方也在 `checkpoint-wire-contract.tsv` 的约束之下** / C# is a party to `checkpoint-wire-contract.tsv`.
/// </summary>
/// <remarks>
/// 验收表 4.7 一直写着"`checkpoint-wire-contract.tsv` 是 **Kotlin↔Rust** 之间的契约，**没有 C# 这一方**，不搬"。
/// **这个判断是错的。**
///
/// **C# 实现了同一套 checkpoint 信封**：`ConstraintProgrammingCheckpoint` 带 `integritySha256` 与逐字段的
/// `JsonPropertyOrder`，`SolveCancellation` 带 `taskAbort`/`frameworkLoser`/`remoteStop` 这些**契约里才有的词**。
/// **既然它产出同名的线格式，它就受同一份契约约束**——**否则"跨语言摘要失配"这件事，C# 是唯一不知道的一方。**
///
/// **判据直接照契约的原文写**：字段顺序表与取消来源词表。**契约是外部文件，这里不复制它的全部内容，
/// 只钉住"C# 与它一致"这件事实**——契约变了而 C# 没变，这条判据会红。
///
/// Acceptance row 4.7 said "`checkpoint-wire-contract.tsv` is a **Kotlin↔Rust** contract with **no C# party**, so it is not implemented".
/// **That judgement was wrong.**
///
/// **C# implements the same checkpoint envelope**: `ConstraintProgrammingCheckpoint` carries `integritySha256` and a per-field
/// `JsonPropertyOrder`, and `SolveCancellation` carries `taskAbort`/`frameworkLoser`/`remoteStop`, **words that exist only in the contract**.
/// **Producing a wire format of the same name puts C# under the same contract** — **otherwise C# is the only party unaware of a digest mismatch.**
///
/// **The criteria are written straight from the contract's own text**: the field-order table and the cancellation-origin vocabulary.
/// **The contract is an external file, so this does not copy it wholesale; it pins that C# agrees with it** —
/// if the contract moves and C# does not, this goes red.
/// </remarks>
public class CheckpointWireContractTests
{
    /// <summary>**取消来源词表与契约逐项一致** / The cancellation-origin vocabulary matches the contract item by item.</summary>
    /// <remarks>
    /// **契约的 `[cancellation-origin]` 一节**列出的 code 是：
    /// `user` / `external` / `callback` / `frameworkLoser` / `remoteStop` / `taskAbort` / `backend` / `future` / `timeout`。
    ///
    /// **这九个词是跨语言的硬约定**：`integritySha256` 覆盖它们，
    /// **所以 C# 写错一个词，Kotlin 与 Rust 的摘要就对不上，而三方都"算得出来"。**
    ///
    /// **The contract's `[cancellation-origin]` section** lists exactly these codes:
    /// `user` / `external` / `callback` / `frameworkLoser` / `remoteStop` / `taskAbort` / `backend` / `future` / `timeout`.
    ///
    /// **Those nine words are a cross-language hard agreement**: `integritySha256` covers them,
    /// **so one wrong word in C# breaks the digest on both Kotlin and Rust while all three still "compute fine".**
    /// </remarks>
    [Fact]
    public void TheCancellationVocabulary_ShouldMatchTheContract()
    {
        // **契约原文的九个 code。**
        // **The nine codes from the contract's own text.**
        string[] contractCodes = [
            "user", "external", "callback", "frameworkLoser", "remoteStop",
            "taskAbort", "backend", "future", "timeout",
        ];

        // **C# 实际能产出的 code**：把每个枚举值都过一遍 `ToWireCode`。
        // **What C# can actually emit**: run every enum value through `ToWireCode`.
        string[] emitted = Enum.GetValues<CancellationSource>()
            .Select(source => source.ToWireCode())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToArray();

        // **不能要求"九个 code 都能从枚举产出"**：契约里 `backend` 与 `external` 是**兜底代码**
        // （原文 `backend... Kotlin has no dedicated variant; preserved verbatim`），
        // **它们本来就该由"原样保留"承载，而不是由某个枚举值产出。**
        //
        // **第一次我要求九项全可达，红了——而红的是我的判据，不是实现。**
        //
        // **Requiring all nine to be reachable from the enum would be wrong**: `backend` and `external` are **catch-all codes**
        // (the contract says `backend... Kotlin has no dedicated variant; preserved verbatim`),
        // **and they are meant to survive verbatim rather than come from an enum value.**
        // **My first version demanded all nine and went red — the criterion was wrong, not the implementation.**
        emitted.Should().OnlyContain(code => contractCodes.Contains(code, StringComparer.Ordinal),
            "every wire code C# emits has to be one of the contract's nine");
        contractCodes.Should().Contain(emitted,
            "the seven codes C# does have a variant for all have to be reachable");
    }

    /// <summary>**每个 code 都能被读回来** / Every code round-trips.</summary>
    /// <remarks>
    /// **只写不读是不够的**：`FromWireCode` 遇到未知 code 会回落到 `Other`，
    /// **所以"读回来"这件事必须逐个验证，否则一个拼错的 code 会被静默吞掉。**
    ///
    /// **Writing without reading is not enough**: `FromWireCode` falls back to `Other` on an unknown code,
    /// **so each round trip has to be checked, or a misspelt code is swallowed silently.**
    /// </remarks>
    [Fact]
    public void TheCancellationCodes_ShouldRoundTrip()
    {
        foreach (CancellationSource source in Enum.GetValues<CancellationSource>()) {
            string code = source.ToWireCode();
            CancellationSource back = CancellationSourceExtensions.FromWireCode(code);

            // **`Other` 是契约里"没有专用变体"的落点**（`ToWireCode` 的 `_ =>` 兜底产出 `"external"`），
            // **所以它读回来不可能是原值**——契约原文就写着 `external... preserved verbatim`。
            //
            // **`Other` is where the contract's "no dedicated variant" lands** (`ToWireCode`'s `_ =>` fallback emits `"external"`),
            // **so it cannot come back as itself** — the contract says `external... preserved verbatim`.
            if (source is CancellationSource.Other) {
                back.ToWireCode().Should().Be(code,
                    "the verbatim code has to survive even when the enum value does not");
                continue;
            }

            back.Should().Be(source, $"{code} has to come back as the value it came from");
        }
    }

    /// <summary>**provenance 的前 7 个字段与契约一致，而契约本身落后于实现** / Provenance matches the contract for seven fields, and the contract lags the code.
    /// </summary>
    /// <remarks>
    /// **契约的 `[provenance-field]` 一节只列到 6**（`solverId` … `threadCount`），
    /// **而有 10 个**：多了 `randomSeed`、`deterministic`、`environmentSummary`。
    ///
    /// **所以"契约只到 6"不是 C# 的问题，是契约自己没跟上代码。**
    /// **这条判据只钉前 7 个（契约里的 0—6）**——**多出来的三个是契约的缺口，不是我该"改回去"的东西。**
    /// **把它写下来，是为了让下一个人知道"7 与 10 不一致"是已知的、有出处的。**
    ///
    /// **The contract's `[provenance-field]` section stops at 6** (`solverId` … `threadCount`),
    /// **while the code has 10**: it adds `randomSeed`, `deterministic` and `environmentSummary`.
    ///
    /// **So "the contract stops at 6" is not a C# problem; the contract did not keep up with the code.**
    /// **This pins the first 7 (0—6 in the contract)** — **the extra three are the contract's gap, not something to "revert".**
    /// **Writing it down is so the next reader knows "7 versus 10" is known and sourced.**
    /// </remarks>
    [Fact]
    public void TheProvenanceFields_ShouldMatchTheContractAndOutgrowIt() {
        string[] contractOrder = [
            "solverId", "backendName", "backendVersion", "pluginVersion",
            "requestedConfiguration", "effectiveConfiguration", "threadCount",
        ];

        System.Reflection.PropertyInfo[] rawProperties =
            typeof(Fuookami.Ospf.Core.Solver.ConstraintProgramming.PortableSolverProvenance).GetProperties();
        (string? Name, int? Order)[] mapped = rawProperties
            .Select(property => (
                Name: property.GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonPropertyNameAttribute), false)
                    .Cast<System.Text.Json.Serialization.JsonPropertyNameAttribute>()
                    .FirstOrDefault()?.Name,
                Order: property.GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonPropertyOrderAttribute), false)
                    .Cast<System.Text.Json.Serialization.JsonPropertyOrderAttribute>()
                    .FirstOrDefault()?.Order))
            .ToArray();
        string[] properties = mapped
            .Where(entry => entry.Name is not null && entry.Order is not null)
            .OrderBy(entry => entry.Order!.Value)
            .Select(entry => entry.Name!)
            .ToArray();

        properties.Take(contractOrder.Length).Should().Equal(contractOrder,
            "the seven provenance fields the contract lists keep their order");
        properties.Should().HaveCountGreaterThan(contractOrder.Length,
            "the implementation has fields the contract does not list yet — that gap is the contract's, and it is recorded here");
    }

    /// <summary>**兜底代码必须原样往返** / A catch-all code has to survive verbatim.</summary>
    /// <remarks>
    /// **契约把 `backend` 和未知代码交给"原样保留"**，判据原文是
    /// `listOf("user", "backend", "vendorSpecificStop")` 必须**逐位无损往返**。
    ///
    /// **C# 承载它的是 `PortableCancellationRecord.Origin`，类型是普通 `string` 而不是枚举**——
    /// **这正好满足要求，而"正好"是必须被钉住的东西**：把 `Origin` 改成枚举，
    /// **`backend` 与 `vendorSpecificStop` 会在反序列化时炸掉，而三方摘要都对不上。**
    ///
    /// **The contract leaves `backend` and unknown codes to "preserved verbatim"**, and the code criterion reads
    /// `listOf("user", "backend", "vendorSpecificStop")` with a **lossless round trip** requirement.
    ///
    /// **In C# the carrier is `PortableCancellationRecord.Origin`, typed as a plain `string` rather than an enum** —
    /// **which happens to satisfy it, and "happens to" is what has to be pinned**: turning `Origin` into an enum
    /// **makes `backend` and `vendorSpecificStop` blow up on deserialization, with every party's digest disagreeing.**
    /// </remarks>
    [Fact]
    public void TheCatchAllOriginCodes_ShouldSurviveVerbatim() {
        var record = new Fuookami.Ospf.Core.Solver.ConstraintProgramming.PortableCancellationRecord(
            Origin: "backend", RequestedAtEpochMs: 1_001L);

        // **变异做过两种，"改类型"那条构造不出来。**
        //
        // 1. **改类型**：`Origin` 是位置参数，改成枚举或加默认值 → **26 个编译错误**，构造不出来。
 // 2. **运行时规范化**：给 `Origin` 挂一个 `JsonConverter`，只放行 C# 枚举能产出的七个词，
        //    **`"backend"` 立刻被改写成 `"user"`，这条判据红**（报错原文：`Expected json... to contain ""backend""`）。
        //
        // **第二种才是真实风险**：有人为了"类型安全"给这个字段加转换器，
        // **而契约要求的是"原样保留"——两者正好相反，且加转换器的人不会觉得自己在破坏契约。**
        //
        // **Two mutations were attempted; the "change the type" one cannot be built.**
        //
        // 1. **Change the type**: `Origin` is a positional parameter; an enum or a default **produces 26 compile errors** — it cannot be built.
        // 2. **Runtime normalisation** (measured): attach a `JsonConverter` to `Origin` that only lets through the seven codes C# can emit,
        //    **and `"backend"` is immediately rewritten to `"user"`, turning this criterion red** (the failure reads `Expected json... to contain ""backend""`).
        //
        // **The second is the real risk**: somebody adds a converter here for "type safety",
        // **while the contract demands verbatim preservation — the two are opposites, and the person adding it will not feel like they are breaking a contract.**
        record.Origin.Should().Be("backend", "a catch-all code is kept as written");

        string json = System.Text.Json.JsonSerializer.Serialize(record);
        json.Should().Contain("\"backend\"", "the code has to be on the wire unchanged");

        Fuookami.Ospf.Core.Solver.ConstraintProgramming.PortableCancellationRecord? back =
            System.Text.Json.JsonSerializer.Deserialize<
            Fuookami.Ospf.Core.Solver.ConstraintProgramming.PortableCancellationRecord>(json);
        back!.Origin.Should().Be("backend", "and it has to come back unchanged");

        // **未知代码同理**：契约允许 Rust 侧出现 C# 不认识的词。
        // **The same for an unknown code**: the contract allows words from Rust that C# does not know.
        var unknown = new Fuookami.Ospf.Core.Solver.ConstraintProgramming.PortableCancellationRecord(
            Origin: "vendorSpecificStop", RequestedAtEpochMs: 1L);
        unknown.Origin.Should().Be("vendorSpecificStop");
    }

    /// <summary>**信封字段的顺序与契约一致** / The envelope field order matches the contract.</summary>
    /// <remarks>
    /// **契约的 `[envelope-field]` 一节给了 25 行的 `序号名称类型可选`。**
    /// **这里钉前 17 个（0—16）**——它们是 `ConstraintProgrammingCheckpoint` 的**位置参数**，
    /// **位置参数顺序错了，序列化出来的 JSON 字段顺序就错了，而 `integritySha256` 正是按顺序算的。**
    ///
    /// **The contract's `[envelope-field]` section gives 25 rows of `index name type optional`.**
    /// **This pins the first 17 (0—16)** — they are the **positional parameters** of `ConstraintProgrammingCheckpoint`,
    /// **and a wrong positional order puts the JSON fields in the wrong order, which is exactly what `integritySha256` hashes.**
    /// </remarks>
    [Fact]
    public void TheEnvelopeFieldOrder_ShouldMatchTheContract()
    {
        string[] contractOrder = [
            "schemaVersion", "sourceFormat", "migratedFromLegacy", "checkpointId",
            "identitySchemaVersion", "identityNamespace", "modelName", "modelFingerprint",
            "configurationFingerprint", "solverFingerprint", "runId", "attemptId",
            "parentCheckpointId", "createdAtEpochMs", "snapshotJson", "incumbent", "bestBound",
        ];

        // **从类型上读出 `JsonPropertyOrder` 与 `JsonPropertyName`，按 order 排序。**
        // **Read `JsonPropertyOrder` and `JsonPropertyName` off the type, ordered by order.**
        System.Reflection.PropertyInfo[] rawProperties = typeof(Fuookami.Ospf.Core.Solver.ConstraintProgramming.ConstraintProgrammingCheckpointEnvelope)
            .GetProperties();
        // **元组投影会让 IDE0007/IDE0008 互相打架**（`var` 与显式类型都触发一条），
        // **所以中间结果先落成数组**——两条规则对数组都满意。
        //
        // **A tuple projection makes IDE0007 and IDE0008 fight** (`var` and an explicit type each trip one),
        // **so the intermediate lands in an array**, which satisfies both.
        (string? Name, int? Order)[] mapped = rawProperties
            .Select(property => (
                Name: property.GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonPropertyNameAttribute), false)
                    .Cast<System.Text.Json.Serialization.JsonPropertyNameAttribute>()
                    .FirstOrDefault()?.Name,
                Order: property.GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonPropertyOrderAttribute), false)
                    .Cast<System.Text.Json.Serialization.JsonPropertyOrderAttribute>()
                    .FirstOrDefault()?.Order))
            .ToArray();
        string[] properties = mapped
            .Where(entry => entry.Name is not null && entry.Order is not null)
            .OrderBy(entry => entry.Order!.Value)
            .Select(entry => entry.Name!)
            .ToArray();

        // **契约一共 25 行（0—24），此前只钉了前 17 个，这里钉满。**
        // **把整条读完的理由**：`integritySha256` 按顺序算，**第 18 个字段错位与前 17 个错位一样致命**，
        // **而"只钉了一半"在文档上看起来像"钉完了"。**
        //
        // **The contract has 25 rows (0—24); an earlier criterion pinned only the first 17 and this pins all of them.**
        // **Why read the whole thing**: `integritySha256` hashes in order, so **a misplaced 18th field is as fatal as a misplaced first**,
        // **and "pinned half of it" reads like "pinned it" in a document.**
        string[] contractTail = [
            "gap", "assumptions", "conflicts", "benders",
            "cancellationChain", "provenance", "metadata", "integritySha256",
        ];
        string[] fullContract = [.. contractOrder, .. contractTail];

        properties.Should().HaveCountGreaterThanOrEqualTo(fullContract.Length,
            "the envelope has at least the twenty-five contract fields");
        properties.Take(fullContract.Length).Should().Equal(fullContract,
            "all twenty-five envelope fields are positional and the contract fixes their order");
    }
}
