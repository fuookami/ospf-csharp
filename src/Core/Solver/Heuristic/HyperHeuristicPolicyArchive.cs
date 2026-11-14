// 策略档案的导出与恢复 / Exporting and restoring the policy archive.
// 对应固定 Kotlin 基线 `HyperHeuristicSolver.kt`（47241 B）里 `HyperHeuristicRun.exportPolicy()` / `restorePolicy(saved)`。
// Mirrors `HyperHeuristicRun.exportPolicy()` / `restorePolicy(saved)` in the pinned baseline's `HyperHeuristicSolver.kt` (47241 B).

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Heuristic.Persistence;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver.Heuristic;

/// <summary>
/// 策略档案的导出与恢复 / Exporting and restoring the policy archive.
/// </summary>
/// <remarks>
/// **`definition` 是策略定义的指纹，`featureVersion` 是特征版本**——两者都必须参与兼容性校验。
/// 只比 `definition` 会让"特征从 3 维扩到 5 维"这种改动被当成兼容：表宽对不上，而恢复过程**不会报错**，
/// 它只是把前 3 维填进去、后 2 维留零，**于是策略从此按"永远看到零"的状态做决策**。
///
/// **`definition` is the policy-definition digest and `featureVersion` the feature version** — both must take part in the compatibility check.
/// Comparing `definition` alone treats "features grew from 3 to 5 dimensions" as compatible: the table width does not match, restore **does not fail**,
/// it fills the first three and leaves two at zero, **so the policy decides forever on "always zero"**.
/// </remarks>
/// <typeparam name="O">目标类型 / The objective type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
public static class HyperHeuristicPolicyArchive<O, V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    /// <summary>
    /// 导出策略档案 / Export the policy archive.
    /// </summary>
    /// <param name="run">运行 / The run.</param>
    /// <param name="definition">策略定义摘要 / The policy-definition digest.</param>
    /// <returns>档案或失败 / The archive or a failure.</returns>
    public static Result<PolicyArchive, ErrorCode, Error<ErrorCode>> Export(
        HyperHeuristicRun<O, V> run, string definition) {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(definition);
        var operators = new Dictionary<string, IReadOnlyDictionary<string, PolicySnapshot>>(
            StringComparer.Ordinal);
        foreach (HyperHeuristicWorker<O, V> worker in run.Workers) {
            if (worker.Session is not IPolicyCarrier carrier) {
                operators[worker.Id] = new Dictionary<string, PolicySnapshot>(StringComparer.Ordinal);
                continue;
            }
            operators[worker.Id] = carrier.Policies();
        }
        return Results.Ok(new PolicyArchive(
            definition, run.FeatureVersion, run.PolicySnapshot(), operators));
    }

    /// <summary>
    /// 恢复策略档案 / Restore the policy archive.
    /// </summary>
    /// <param name="run">运行 / The run.</param>
    /// <param name="saved">档案 / The archive.</param>
    /// <param name="definition">策略定义摘要 / The policy-definition digest.</param>
    /// <returns>成功或失败 / Success or failure.</returns>
    public static Result<Success, ErrorCode, Error<ErrorCode>> Restore(
        HyperHeuristicRun<O, V> run, PolicyArchive saved, string definition) {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(definition);
        if (saved.Version != 1
            || !string.Equals(saved.Definition, definition, StringComparison.Ordinal)
            || !string.Equals(saved.FeatureVersion, run.FeatureVersion, StringComparison.Ordinal)) {
            return Fail("incompatible policy definitions");
        }
        var expected = new HashSet<string>(StringComparer.Ordinal);
        foreach (HyperHeuristicWorker<O, V> worker in run.Workers) {
            expected.Add(worker.Id);
        }
        if (expected.Count != saved.Operators.Count) {
            return Fail("policy archive operator set does not match the run");
        }
        foreach (string id in expected) {
            if (!saved.Operators.ContainsKey(id)) {
                return Fail("policy archive operator set does not match the run");
            }
        }

        Result<Success, ErrorCode, Error<ErrorCode>> root = run.Policy.Restore(saved.Root);
        if (root is Failed<Success, ErrorCode, Error<ErrorCode>> rootFailure) {
            return Results.Failed<Success>(rootFailure.Error);
        }
        foreach (HyperHeuristicWorker<O, V> worker in run.Workers) {
            IReadOnlyDictionary<string, PolicySnapshot> policies = saved.Operators[worker.Id];
            if (worker.Session is not IPolicyCarrier carrier) {
                // **有策略但会话不支持恢复，必须失败**：静默跳过会让这次恢复"成功"，而算子池的策略还是旧的——
                // 之后所有算子选择都按旧表进行，**与刚恢复的根策略各说各话**。
                // **Policies present but the session cannot restore them must fail**: skipping silently makes the restore "succeed" while the operator
                // pools keep their old policies — every later operator choice then follows the old tables, **disagreeing with the freshly restored root
                // policy**.
                if (policies.Count > 0) {
                    return Fail("missing operator policy carrier");
                }
                continue;
            }
            Result<Success, ErrorCode, Error<ErrorCode>> restored = carrier.RestorePolicies(policies);
            if (restored is Failed<Success, ErrorCode, Error<ErrorCode>> restoreFailure) {
                return Results.Failed<Success>(restoreFailure.Error);
            }
        }
        return Results.Ok(new Success());
    }

    /// <summary>失败 / A failure.</summary>
    /// <param name="message">消息 / The message.</param>
    /// <returns>失败结果 / The failure.</returns>
    private static Result<Success, ErrorCode, Error<ErrorCode>> Fail(string message) =>
        Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.IllegalArgument, message));
}
