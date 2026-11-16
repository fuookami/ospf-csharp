#nullable enable

using FluentAssertions;
using System;
using System.IO;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// "缺什么"这句话必须与实测一致 / The claim about what is missing must match the measurement.
/// </summary>
/// <remarks>
/// **纯文档，无新增源码。** 它改掉了一句被三个批次推翻的话。
///
/// 台账原来把三个 `analyze` 都写成"类本体需要真实后端"。**用假 `IFixedIntegerLpBackend`
/// 跑通了敏感性编排，把目标可达性的整个判定层用不到一个求解器地测通了。**
///
/// **于是那句话从"谨慎"变成了"低估"**，而低估的代价是真实的：
/// **它让后续的人以为必须等许可证，而实际能做的活一直在那里。**
///
/// **No source change.** It removed a sentence that had already been refuted. The ledger used to say all three `analyze` bodies **"need a real backend"**. **A stub run covered the sensitivity
/// orchestration against a stub `IFixedIntegerLpBackend`, and the entire reachability decision layer was tested without a solver at all. The entire reachability decision layer without a solver at all.** **So the sentence went from cautious to an
/// underestimate** — and underestimates cost something real: **they make the next session believe it must wait for a licence while the work sits there.**
///
/// **判据钉的是"两端"**：旧说法必须不在，新说法必须在。
/// **只钉 absence 会让一节被整段删掉也通过；只钉 presence 会让新旧两种说法并存。**
///
/// **The criteria pin both ends**: the old claim must be gone and the new one present. **Pinning only absence lets the whole section be deleted and still pass; pinning only presence lets both claims coexist.**
/// </remarks>
public class BackendClaimHonestyTests
{
    private const string LedgerPath = "docs/kotlin-sync-ledger.md";

    private static string Ledger() {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "ospf-csharp.sln"))) {
            directory = directory.Parent;
        }
        if (directory is null) {
            throw new InvalidOperationException("repository root not found");
        }
        return File.ReadAllText(Path.Combine(directory.FullName, LedgerPath));
    }

}
