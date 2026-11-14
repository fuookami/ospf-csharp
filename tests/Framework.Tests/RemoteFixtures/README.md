# 远程 wire fixture / Remote wire fixtures

这些文件用于远程协议的跨语言契约测试。**来源必须标注**，因为"由固定 Kotlin 基线产出"和"由 C#
产出"是两种不同强度的证据。

These files back the cross-language contract tests for the remote protocol. **Provenance must be
recorded**, because "produced by the pinned Kotlin baseline" and "produced by C#" are two different
strengths of evidence.

| File | Provenance |
|---|---|
| `remote-cp-result-v1.json` | 从固定 Kotlin `12fdb587` 逐字节复制 / Copied byte-for-byte from pinned Kotlin `12fdb587` |
| `remote-cp-result-v2.json` | 同上 / Same |
| `remote-linear-model-v2.json` | 同上 / Same |
| `remote-linear-result-v2.json` | 同上 / Same |
| `solution-kotlin-encoded-v2.json` | 由固定 Kotlin `12fdb587` 侧真实 `SerializedSolution` 编码产出 / Produced by a real `SerializedSolution` encode on the pinned Kotlin `12fdb587` side |
| `submit-request-csharp.json` | 由 C# 产出，并已用固定 Kotlin 侧 `ignoreUnknownKeys = false` 严格解码验证 / Produced by C#, then strictly decoded by the pinned Kotlin side with `ignoreUnknownKeys = false` |
| `configuration-digest-kotlin.txt` | C# 与固定 Kotlin 侧对同一输入的配置指纹**实测一致** / C# and the pinned Kotlin side produced the identical configuration fingerprint for the same input |

## 已知边界 / Known boundaries

- 只有 `SerializedSolution`、`RemoteTaskSubmitRequest` 与配置指纹做过真实跨语言往返；
  调度模型、能力摘要与 CP checkpoint envelope 尚未对跑。
- 不因为这些 fixture 通过就宣称整体互通。
