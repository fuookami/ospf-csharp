# Framework.GanttScheduling

[中文](README_ch.md)

Gantt-style task scheduling on top of the OSPF framework: tasks, executors, time slots, bunch
generation, and the scheduling lifecycle.

Baseline: `ospf-kotlin-framework-gantt-scheduling` at Kotlin `12fdb587`.

## Layout

A single assembly with `Application/`, `Domain/`, and `Infrastructure/`. The baseline splits this into
many sub-modules (26 READMEs); **this project does not mirror that split**, so those READMEs are not a
checklist against these three directories.

## What it provides

| Capability | Where |
| --- | --- |
| Task and executor models | `Domain/Task/` |
| Bunch generation (slot-based) | `Domain/BunchGeneration/Service/SlotBasedBunchGenerator.cs` |
| Productivity models | `Infrastructure/Productivity.cs`, `QuantityProductivity.cs` |
| Scheduling application services | `Application/` |

## Known gaps

**Still to be audited.** Earlier batches recorded that the Gantt task-bundle compilation, executor/task
constraints, and cancellation targets still contain empty implementations, and that Demo4 does not register
the bundle cost pipeline. Those notes have not been re-verified in this session, so treat them as
**unconfirmed** rather than current.

The `Framework.GanttNetworkScheduling` bridge (Batch 278) is a separate project, because
`Framework.NetworkScheduling` already references this module.

Demo4's `FlightTaskBunchGenerator` runs its own hand-written label search rather than the shared path
search kernel. That is a **structural divergence from the baseline, not a missing call** — see
`docs/kotlin-capability-matrix.md`.

## Verification

```bash
dotnet test tests/Framework.GanttScheduling.Tests/Framework.GanttScheduling.Tests.csproj -c Release
```

No claim of cross-language equivalence: only `SerializedSolution`, `RemoteTaskSubmitRequest` and the
`configuration`/`sha256` digest have been measured across the two implementations.
