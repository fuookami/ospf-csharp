#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Linq;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Framework.Solver.Remote.Domain;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Solver.Remote.Client;

/// <summary>
/// 远程 wire 结果到本地报告的映射 / Mapping from remote wire results to local reports.
/// </summary>
/// <remarks>
/// 最优判定：<c>solutionPresence == OPTIMAL</c> 且 <c>proofStatus != NONE</c>。
/// 间隙为 0 不足以声明最优；未知枚举一律按未知处理，绝不推断为成功。
/// Optimality follows the reference: <c>solutionPresence == OPTIMAL</c> and <c>proofStatus != NONE</c>.
/// A zero gap is insufficient to claim optimality, and unknown enum values stay unknown rather than becoming success.
/// </remarks>
public static class RemoteReportMapping {
    /// <summary>可读取的线性/二次结果 schema 主版本 / Readable linear/quadratic result schema majors.</summary>
    private static readonly int[] SupportedLinearQuadraticSchemaMajors = { 1, 2 };

    /// <summary>
    /// 校验线性/二次远程结果 envelope / Validate the backend-neutral linear/quadratic result envelope.
    /// </summary>
    /// <remarks>
    /// 版本 1 保持旧生产端可读；版本 2 要求完整的正交状态元组并拒绝未来主版本。
    /// Version 1 stays readable for legacy producers; version 2 requires the complete orthogonal state
    /// tuple and rejects future major versions.
    /// </remarks>
    /// <param name="solution">wire 解 / Wire solution.</param>
    /// <returns>校验结果 / Validation result.</returns>
    public static Try ValidateLinearQuadraticResult(Domain.SerializedSolution solution) {
        ArgumentNullException.ThrowIfNull(solution);
        string majorText = solution.SchemaVersion.Split('.')[0];
        if (!int.TryParse(majorText, out int major)) {
            return Invalid("远程结果 schema 无效 / Invalid remote result schema");
        }
        if (System.Array.IndexOf(SupportedLinearQuadraticSchemaMajors, major) < 0) {
            return Invalid(
                $"不支持的远程结果 schema 主版本：{solution.SchemaVersion} / Unsupported remote result schema major: {solution.SchemaVersion}");
        }
        bool strict = major >= 2;
        if (strict && solution.SchemaVersion != "2.0") {
            return Invalid(
                $"不支持的远程结果 schema：{solution.SchemaVersion} / Unsupported remote result schema: {solution.SchemaVersion}");
        }
        if (solution.Feasible &&
            solution.ProblemStatus is not null &&
            solution.ProblemStatus != Domain.RemoteProblemStatus.Feasible) {
            return Invalid("远程结果问题状态与可行性不一致 / Remote result problem status disagrees with feasibility");
        }
        if (!solution.Feasible && solution.ProblemStatus == Domain.RemoteProblemStatus.Feasible) {
            return Invalid("不可行远程结果不得标记为 FEASIBLE / Infeasible remote result must not be FEASIBLE");
        }
        if (solution.Optimal &&
            (!solution.Feasible ||
             (strict && (solution.ProofStatus is null || solution.ProofStatus == Domain.RemoteProofStatus.None)))) {
            return Invalid("最优远程结果必须可行且带证明状态 / Optimal remote result must be feasible and carry proof status");
        }
        Domain.RemoteSolutionPresence expected = solution.Optimal
            ? Domain.RemoteSolutionPresence.Optimal
            : solution.Feasible
                ? Domain.RemoteSolutionPresence.Incumbent
                : Domain.RemoteSolutionPresence.None;
        if (solution.SolutionPresence is not null && solution.SolutionPresence != expected) {
            return Invalid(
                "远程结果解存在性与可行性/最优性不一致 / Remote result solution presence disagrees with feasibility or optimality");
        }
        if (!solution.Feasible &&
            (solution.ObjectiveValue is not null || solution.ObjectiveValueInt64 is not null)) {
            return Invalid("无解远程结果不得携带目标值 / A remote result without an incumbent must not carry an objective");
        }
        if (strict && solution.Feasible && solution.ObjectiveValue is null) {
            return Invalid("严格可行远程结果缺少目标值 / Strict feasible remote result is missing an objective");
        }
        if (solution.ObjectiveValue is not null && solution.ObjectiveValueInt64 is not null) {
            return Invalid("远程结果不得同时携带浮点和 Int64 目标 / Remote result must not carry both floating and Int64 objectives");
        }
        if (solution.ObjectiveValueInt64 is not null) {
            return Invalid("线性/二次远程结果不得使用 Int64 目标字段 / Linear/quadratic results must not use the Int64 objective field");
        }
        if (strict) {
            if (solution.Fingerprints.Count != solution.FingerprintSchemas.Count ||
                solution.FingerprintSchemas.Values.Any(string.IsNullOrWhiteSpace)) {
                return Invalid("严格远程结果的指纹 schema 不完整 / Strict remote result fingerprint schemas are incomplete");
            }
            if (string.IsNullOrWhiteSpace(solution.RunId) || string.IsNullOrWhiteSpace(solution.AttemptId)) {
                return Invalid("严格远程结果缺少 run/attempt 标识 / Strict remote result is missing run/attempt identifiers");
            }
        }
        return Results.OkInstance;
    }

    /// <summary>
    /// 解析带索引的远程 issue 字段 / Parse indexed remote issue fields.
    /// </summary>
    /// <remarks>
    /// 字段形如 <c>&lt;prefix&gt;.&lt;index&gt;.code|category|message|details.&lt;key&gt;</c>；
    /// 索引按数值升序；缺失 code/category/message 或类别无法识别都返回 `ORSolutionInvalid`；
    /// 任何以 <c>&lt;prefix&gt;.</c> 开头但不匹配该形状的键都判为畸形字段，不静默忽略。
    /// fields look like
    /// <c>&lt;prefix&gt;.&lt;index&gt;.code|category|message|details.&lt;key&gt;</c>, indexes sort
    /// numerically, a missing code/category/message or an unrecognized category yields
    /// `ORSolutionInvalid`, and any key starting with <c>&lt;prefix&gt;.</c> that does not match the shape is
    /// reported as malformed rather than silently ignored.
    /// </remarks>
    /// <param name="fields">扁平字段表 / Flat field map.</param>
    /// <param name="prefix">字段前缀 / Field prefix.</param>
    /// <returns>结构化问题列表或协议错误 / Structured issues or a protocol error.</returns>
    public static Result<
        IReadOnlyList<Fuookami.Ospf.Core.Solver.Report.SolveIssue>,
        ErrorCode,
        Error<ErrorCode>> ToRemoteIssues(
        IReadOnlyDictionary<string, string> fields,
        string prefix) {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(prefix);
        var indexPattern = new System.Text.RegularExpressions.Regex(
            "^" + System.Text.RegularExpressions.Regex.Escape(prefix) + @"\.(\d+)\.code$");
        var fieldPattern = new System.Text.RegularExpressions.Regex(
            "^" + System.Text.RegularExpressions.Regex.Escape(prefix) + @"\.\d+\.(code|category|message|details\..+)$");
        foreach (string key in fields.Keys) {
            if (key.StartsWith(prefix + ".", StringComparison.Ordinal) && !fieldPattern.IsMatch(key)) {
                return InvalidIssues("远程 issue 字段畸形 / Remote issue fields are malformed: " + key);
            }
        }
        var indexes = new List<int>();
        foreach (string key in fields.Keys) {
            System.Text.RegularExpressions.Match match = indexPattern.Match(key);
            if (match.Success && int.TryParse(match.Groups[1].Value, out int index) && !indexes.Contains(index)) {
                indexes.Add(index);
            }
        }
        indexes.Sort();
        var issues = new List<Fuookami.Ospf.Core.Solver.Report.SolveIssue>(indexes.Count);
        foreach (int index in indexes) {
            string prefixOfIndex = $"{prefix}.{index}";
            if (!fields.TryGetValue(prefixOfIndex + ".code", out string? code) || string.IsNullOrEmpty(code)) {
                return InvalidIssues($"远程 issue 缺少 code / Remote issue code is missing: {prefixOfIndex}");
            }
            if (!fields.TryGetValue(prefixOfIndex + ".category", out string? categoryName) ||
                string.IsNullOrEmpty(categoryName)) {
                return InvalidIssues($"远程 issue 缺少 category / Remote issue category is missing: {prefixOfIndex}");
            }
            if (!TryParseIssueCategory(categoryName, out Fuookami.Ospf.Core.Solver.Report.SolveIssueCategory category)) {
                return InvalidIssues($"远程 issue 类别无效 / Remote issue category is invalid: {categoryName}");
            }
            if (!fields.TryGetValue(prefixOfIndex + ".message", out string? message) || string.IsNullOrEmpty(message)) {
                return InvalidIssues($"远程 issue 缺少 message / Remote issue message is missing: {prefixOfIndex}");
            }
            string detailsPrefix = prefixOfIndex + ".details.";
            var details = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach ((string key, string value) in fields) {
                if (key.StartsWith(detailsPrefix, StringComparison.Ordinal)) {
                    details[key[detailsPrefix.Length..]] = value;
                }
            }
            issues.Add(new Fuookami.Ospf.Core.Solver.Report.SolveIssue(
                Code: code,
                Category: category,
                Message: message,
                Details: details));
        }
        return Results.Ok<IReadOnlyList<Fuookami.Ospf.Core.Solver.Report.SolveIssue>>(issues);
    }

    /// <summary>
    /// 解析远程诊断 / Parse remote diagnostics.
    /// </summary>
    /// <remarks>
    /// 只允许 <c>infeasibility.</c>／<c>warning.</c>／<c>error.</c> 前缀，未知前缀返回
    /// `ORSolutionInvalid`；警告与错误分别按带索引形式解析。
    /// only <c>infeasibility.</c>/<c>warning.</c>/<c>error.</c> prefixes are
    /// allowed and an unknown prefix yields `ORSolutionInvalid`; warnings and errors are parsed from their
    /// indexed forms.
    /// </remarks>
    /// <param name="fields">扁平字段表 / Flat field map.</param>
    /// <returns>诊断摘要或协议错误 / Diagnostics summary or a protocol error.</returns>
    public static Result<
        Fuookami.Ospf.Core.Solver.Report.SolveDiagnostics<Flt64>,
        ErrorCode,
        Error<ErrorCode>> ToRemoteDiagnostics(IReadOnlyDictionary<string, string> fields) {
        ArgumentNullException.ThrowIfNull(fields);
        string[] allowed = { "infeasibility.", "warning.", "error." };
        foreach (string key in fields.Keys) {
            if (!allowed.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal))) {
                return Results.Failed<Fuookami.Ospf.Core.Solver.Report.SolveDiagnostics<Flt64>>(
                    new Err<ErrorCode>(
                        ErrorCode.ORSolutionInvalid,
                        "远程诊断字段未知 / Remote diagnostic field is unknown: " + key));
            }
        }
        Result<IReadOnlyList<Fuookami.Ospf.Core.Solver.Report.SolveIssue>, ErrorCode, Error<ErrorCode>> warnings =
            ToRemoteIssues(fields, "warning");
        if (!warnings.IsOk) {
            return warnings switch {
                Failed<IReadOnlyList<Fuookami.Ospf.Core.Solver.Report.SolveIssue>, ErrorCode, Error<ErrorCode>> failed =>
                    Results.Failed<Fuookami.Ospf.Core.Solver.Report.SolveDiagnostics<Flt64>>(failed.Error),
                Fatal<IReadOnlyList<Fuookami.Ospf.Core.Solver.Report.SolveIssue>, ErrorCode, Error<ErrorCode>> fatal =>
                    new Fatal<Fuookami.Ospf.Core.Solver.Report.SolveDiagnostics<Flt64>, ErrorCode, Error<ErrorCode>>(fatal.Errors),
                _ => InvalidDiagnostics("远程警告解析失败 / Remote warnings cannot be parsed"),
            };
        }
        Result<IReadOnlyList<Fuookami.Ospf.Core.Solver.Report.SolveIssue>, ErrorCode, Error<ErrorCode>> errors =
            ToRemoteIssues(fields, "error");
        if (!errors.IsOk) {
            return errors switch {
                Failed<IReadOnlyList<Fuookami.Ospf.Core.Solver.Report.SolveIssue>, ErrorCode, Error<ErrorCode>> failed =>
                    Results.Failed<Fuookami.Ospf.Core.Solver.Report.SolveDiagnostics<Flt64>>(failed.Error),
                Fatal<IReadOnlyList<Fuookami.Ospf.Core.Solver.Report.SolveIssue>, ErrorCode, Error<ErrorCode>> fatal =>
                    new Fatal<Fuookami.Ospf.Core.Solver.Report.SolveDiagnostics<Flt64>, ErrorCode, Error<ErrorCode>>(fatal.Errors),
                _ => InvalidDiagnostics("远程错误解析失败 / Remote errors cannot be parsed"),
            };
        }
        Result<Fuookami.Ospf.Core.Solver.Report.InfeasibilityEvidence?, ErrorCode, Error<ErrorCode>>
            evidence = ToRemoteEvidence(fields);
        if (!evidence.IsOk) {
            return evidence switch {
                Failed<Fuookami.Ospf.Core.Solver.Report.InfeasibilityEvidence?, ErrorCode, Error<ErrorCode>> failed =>
                    Results.Failed<Fuookami.Ospf.Core.Solver.Report.SolveDiagnostics<Flt64>>(failed.Error),
                Fatal<Fuookami.Ospf.Core.Solver.Report.InfeasibilityEvidence?, ErrorCode, Error<ErrorCode>> fatal =>
                    new Fatal<Fuookami.Ospf.Core.Solver.Report.SolveDiagnostics<Flt64>, ErrorCode, Error<ErrorCode>>(fatal.Errors),
                _ => InvalidDiagnostics("远程不可行证据解析失败 / Remote infeasibility evidence cannot be parsed"),
            };
        }
        return Results.Ok(new Fuookami.Ospf.Core.Solver.Report.SolveDiagnostics<Flt64>(
            InfeasibilityEvidence: evidence.Value,
            Warnings: warnings.Value,
            Errors: errors.Value));
    }

    /// <summary>
    /// 解析不可行证据 / Parse infeasibility evidence.
    /// </summary>
    /// <remarks>
    /// 没有任何 <c>infeasibility.</c> 字段时返回 null；五个枚举字段全部必填且必须逐字匹配，
    /// 五个列表字段是 JSON 数组编码的字符串，畸形即失败。`variableBoundRefs` 与 `members` 里的
    /// <c>bound:</c> 形式共用 <c>id:Side</c> 编码，用最后一个冒号切分，因此变量标识本身可以含冒号。
    /// no <c>infeasibility.</c> field yields null; all five enum fields are
    /// required and must match verbatim, and the five list fields are JSON-array-encoded strings where
    /// malformed content fails. The <c>variableBoundRefs</c> and <c>bound:</c> member forms share the
    /// <c>id:Side</c> encoding and split on the last colon, so a variable identifier may contain colons.
    /// </remarks>
    /// <param name="fields">diagnostics 字段 / Diagnostics fields.</param>
    /// <returns>证据或 null / Evidence or null.</returns>
    public static Result<
        Fuookami.Ospf.Core.Solver.Report.InfeasibilityEvidence?,
        ErrorCode,
        Error<ErrorCode>> ToRemoteEvidence(IReadOnlyDictionary<string, string> fields) {
        ArgumentNullException.ThrowIfNull(fields);
        if (!fields.Keys.Any(key => key.StartsWith("infeasibility.", StringComparison.Ordinal))) {
            return Results.Ok<Fuookami.Ospf.Core.Solver.Report.InfeasibilityEvidence?>(null);
        }
        string Required(string name) =>
            fields.TryGetValue("infeasibility." + name, out string? value) ? value : string.Empty;

        if (!TryParseWireEnum(Required("source"),
            out Fuookami.Ospf.Core.Solver.Report.InfeasibilityEvidenceSource source)) {
            return InvalidEvidence("远程不可行证据来源无效 / Remote infeasibility source is invalid");
        }
        if (!TryParseWireEnum(Required("exactness"),
            out Fuookami.Ospf.Core.Solver.Report.EvidenceExactness exactness)) {
            return InvalidEvidence("远程不可行证据精度无效 / Remote infeasibility exactness is invalid");
        }
        if (!TryParseWireEnum(Required("completeness"),
            out Fuookami.Ospf.Core.Solver.Report.EvidenceCompleteness completeness)) {
            return InvalidEvidence("远程不可行证据完整度无效 / Remote infeasibility completeness is invalid");
        }
        if (!TryParseWireEnum(Required("validity"),
            out Fuookami.Ospf.Core.Solver.Report.EvidenceValidity validity)) {
            return InvalidEvidence("远程不可行证据有效性无效 / Remote infeasibility validity is invalid");
        }
        if (!TryParseWireEnum(Required("minimality"),
            out Fuookami.Ospf.Core.Solver.Report.EvidenceMinimality minimality)) {
            return InvalidEvidence("远程不可行证据最小性无效 / Remote infeasibility minimality is invalid");
        }

        Result<IReadOnlyList<string>, ErrorCode, Error<ErrorCode>> constraintIds =
            ReadStringList(fields, "constraintIds");
        Result<IReadOnlyList<string>, ErrorCode, Error<ErrorCode>> bounds =
            ReadStringList(fields, "variableBoundRefs");
        Result<IReadOnlyList<string>, ErrorCode, Error<ErrorCode>> domains =
            ReadStringList(fields, "variableDomainRefs");
        Result<IReadOnlyList<string>, ErrorCode, Error<ErrorCode>> members =
            ReadStringList(fields, "members");
        Result<IReadOnlyList<string>, ErrorCode, Error<ErrorCode>> assumptions =
            ReadStringList(fields, "assumptionIds");
        foreach (Result<IReadOnlyList<string>, ErrorCode, Error<ErrorCode>> list in
            new[] { constraintIds, bounds, domains, members, assumptions }) {
            if (!list.IsOk) {
                return InvalidEvidence("远程不可行证据列表格式错误 / Remote infeasibility list is malformed");
            }
        }

        var parsedBounds = new HashSet<Fuookami.Ospf.Core.Solver.Report.VariableBoundRef>();
        foreach (string value in bounds.Value) {
            int separator = value.LastIndexOf(':');
            if (separator <= 0) {
                return InvalidEvidence("远程变量边界引用格式错误 / Remote variable bound reference is malformed");
            }
            if (!TryParseWireEnum(value[(separator + 1)..],
                out Fuookami.Ospf.Core.Solver.Report.BoundSide side)) {
                return InvalidEvidence("远程变量边界方向无效 / Remote variable bound side is invalid");
            }
            _ = parsedBounds.Add(new Fuookami.Ospf.Core.Solver.Report.VariableBoundRef(
                VariableId: new Fuookami.Ospf.Core.Solver.Report.VariableId(value[..separator]),
                Side: side));
        }

        var parsedDomains = new HashSet<Fuookami.Ospf.Core.Solver.Report.VariableDomainRef>();
        foreach (string value in domains.Value) {
            _ = parsedDomains.Add(new Fuookami.Ospf.Core.Solver.Report.VariableDomainRef(
                VariableId: new Fuookami.Ospf.Core.Solver.Report.VariableId(value)));
        }

        var parsedMembers = new HashSet<Fuookami.Ospf.Core.Solver.Report.InfeasibilityMember>();
        foreach (string value in members.Value) {
            if (value.StartsWith("constraint:", StringComparison.Ordinal)) {
                _ = parsedMembers.Add(new Fuookami.Ospf.Core.Solver.Report.InfeasibilityMember.ConstraintMember(
                    new Fuookami.Ospf.Core.Solver.Report.ConstraintId(value["constraint:".Length..])));
            }
            else if (value.StartsWith("domain:", StringComparison.Ordinal)) {
                _ = parsedMembers.Add(new Fuookami.Ospf.Core.Solver.Report.InfeasibilityMember.VariableDomainMember(
                    new Fuookami.Ospf.Core.Solver.Report.VariableDomainRef(
                        VariableId: new Fuookami.Ospf.Core.Solver.Report.VariableId(value["domain:".Length..]))));
            }
            else if (value.StartsWith("bound:", StringComparison.Ordinal)) {
                string encoded = value["bound:".Length..];
                int separator = encoded.LastIndexOf(':');
                if (separator <= 0) {
                    return InvalidEvidence("远程不可行成员格式错误 / Remote infeasibility member is malformed");
                }
                if (!TryParseWireEnum(encoded[(separator + 1)..],
                    out Fuookami.Ospf.Core.Solver.Report.BoundSide side)) {
                    return InvalidEvidence("远程不可行成员边界方向无效 / Remote infeasibility member bound side is invalid");
                }
                _ = parsedMembers.Add(new Fuookami.Ospf.Core.Solver.Report.InfeasibilityMember.VariableBoundMember(
                    new Fuookami.Ospf.Core.Solver.Report.VariableBoundRef(
                        VariableId: new Fuookami.Ospf.Core.Solver.Report.VariableId(encoded[..separator]),
                        Side: side)));
            }
            else {
                return InvalidEvidence("远程不可行成员类型未知 / Remote infeasibility member type is unknown");
            }
        }

        // 尾段字段（耗时、不可用原因、外部引用、复验次数、终止原因）各自可选。
        // The tail fields (elapsed, unavailable reason, external reference, verification checks and
 // termination reason) are each optional in the implementation.
        TimeSpan? elapsed = null;
        if (fields.TryGetValue("infeasibility.elapsedMs", out string? elapsedMs)) {
            if (!long.TryParse(elapsedMs, out long elapsedValue) || elapsedValue < 0) {
                return InvalidEvidence("远程证据耗时无效 / Remote infeasibility elapsed is invalid");
            }
            elapsed = TimeSpan.FromMilliseconds(elapsedValue);
        }

        Fuookami.Ospf.Core.Solver.Report.SolveIssue? unavailable = null;
        if (fields.TryGetValue("infeasibility.unavailable.code", out string? unavailableCode)) {
            if (!fields.TryGetValue("infeasibility.unavailable.category", out string? categoryName) ||
                !TryParseIssueCategory(categoryName, out Fuookami.Ospf.Core.Solver.Report.SolveIssueCategory category)) {
                return InvalidEvidence("远程不可用诊断类别无效 / Remote unavailable diagnostic category is invalid");
            }
            string message = Required("unavailable.message");
            if (string.IsNullOrWhiteSpace(message)) {
                return InvalidEvidence("远程不可用诊断缺少消息 / Remote unavailable diagnostic message is missing");
            }
            unavailable = new Fuookami.Ospf.Core.Solver.Report.SolveIssue(
                Code: unavailableCode,
                Category: category,
                Message: message);
        }

        Fuookami.Ospf.Core.Solver.Report.TerminationReason? termination = null;
        if (fields.TryGetValue("infeasibility.terminationReason", out string? terminationName)) {
            if (!TryParseWireEnum(terminationName,
                out Fuookami.Ospf.Core.Solver.Report.TerminationReason parsedTermination)) {
                return InvalidEvidence("远程诊断终止原因无效 / Remote diagnostic termination is invalid");
            }
            termination = parsedTermination;
        }

        ulong? verificationChecks = null;
        if (fields.TryGetValue("infeasibility.verificationChecks", out string? checks)) {
            if (!ulong.TryParse(checks, out ulong checksValue)) {
                return InvalidEvidence("远程证据复验次数无效 / Remote verification checks is invalid");
            }
            verificationChecks = checksValue;
        }

        return Results.Ok<Fuookami.Ospf.Core.Solver.Report.InfeasibilityEvidence?>(
            new Fuookami.Ospf.Core.Solver.Report.InfeasibilityEvidence {
                Source = source,
                Exactness = exactness,
                Completeness = completeness,
                Validity = validity,
                Minimality = minimality,
                ConstraintIds = new HashSet<Fuookami.Ospf.Core.Solver.Report.ConstraintId>(
                    constraintIds.Value.Select(id => new Fuookami.Ospf.Core.Solver.Report.ConstraintId(id))),
                VariableBoundIds = new HashSet<Fuookami.Ospf.Core.Solver.Report.VariableId>(
                    parsedBounds.Select(reference => reference.VariableId)),
                VariableBoundRefs = parsedBounds,
                VariableDomainRefs = parsedDomains,
                Members = parsedMembers,
                AssumptionIds = new HashSet<Fuookami.Ospf.Core.Solver.Report.VariableId>(
                    assumptions.Value.Select(id => new Fuookami.Ospf.Core.Solver.Report.VariableId(id))),
                Elapsed = elapsed,
                UnavailableReason = unavailable,
                Reference = fields.TryGetValue("infeasibility.reference", out string? reference)
                    ? reference
                    : null,
                VerificationChecks = verificationChecks,
                TerminationReason = termination,
            });
    }

    private static bool TryParseWireEnum<TEnum>(string name, out TEnum value)
        where TEnum : struct, Enum {
        foreach (TEnum candidate in Enum.GetValues<TEnum>()) {
            if (string.Equals(
                RemoteWireEnumNames.ToWireName(candidate),
                name,
                StringComparison.Ordinal)) {
                value = candidate;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static Result<IReadOnlyList<string>, ErrorCode, Error<ErrorCode>> ReadStringList(
        IReadOnlyDictionary<string, string> fields,
        string name) {
        if (!fields.TryGetValue("infeasibility." + name, out string? encoded)) {
            return Results.Ok<IReadOnlyList<string>>(Array.Empty<string>());
        }
        try {
            using var document = JsonDocument.Parse(encoded);
            if (document.RootElement.ValueKind != JsonValueKind.Array) {
                return InvalidList();
            }
            var values = new List<string>();
            foreach (JsonElement element in document.RootElement.EnumerateArray()) {
                if (element.ValueKind != JsonValueKind.String) {
                    return InvalidList();
                }
                values.Add(element.GetString() ?? string.Empty);
            }
            return Results.Ok<IReadOnlyList<string>>(values);
        }
        catch (JsonException) {
            return InvalidList();
        }

        static Result<IReadOnlyList<string>, ErrorCode, Error<ErrorCode>> InvalidList() =>
            Results.Failed<IReadOnlyList<string>>(new Err<ErrorCode>(
                ErrorCode.ORSolutionInvalid,
                "远程不可行证据列表格式错误 / Remote infeasibility list is malformed"));
    }

    private static Result<
        Fuookami.Ospf.Core.Solver.Report.InfeasibilityEvidence?,
        ErrorCode,
        Error<ErrorCode>> InvalidEvidence(string message) =>
        Results.Failed<Fuookami.Ospf.Core.Solver.Report.InfeasibilityEvidence?>(
            new Err<ErrorCode>(ErrorCode.ORSolutionInvalid, message));

    private static bool TryParseIssueCategory(
        string name,
        out Fuookami.Ospf.Core.Solver.Report.SolveIssueCategory category) {
        foreach (Fuookami.Ospf.Core.Solver.Report.SolveIssueCategory candidate in
            Enum.GetValues<Fuookami.Ospf.Core.Solver.Report.SolveIssueCategory>()) {
            if (string.Equals(candidate.ToString(), name, StringComparison.Ordinal)) {
                category = candidate;
                return true;
            }
        }
        category = default;
        return false;
    }

    private static Result<
        IReadOnlyList<Fuookami.Ospf.Core.Solver.Report.SolveIssue>,
        ErrorCode,
        Error<ErrorCode>> InvalidIssues(string message) =>
        Results.Failed<IReadOnlyList<Fuookami.Ospf.Core.Solver.Report.SolveIssue>>(
            new Err<ErrorCode>(ErrorCode.ORSolutionInvalid, message));

    private static Result<
        Fuookami.Ospf.Core.Solver.Report.SolveDiagnostics<Flt64>,
        ErrorCode,
        Error<ErrorCode>> InvalidDiagnostics(string message) =>
        Results.Failed<Fuookami.Ospf.Core.Solver.Report.SolveDiagnostics<Flt64>>(
            new Err<ErrorCode>(ErrorCode.ORSolutionInvalid, message));

    private static Try Invalid(string message) =>
        Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.ORSolutionInvalid, message));
    /// <summary>
    /// 将 wire 解映射为求解结果 / Map a wire solution to a solve result.
    /// </summary>
    /// <param name="solution">wire 解 / Wire solution.</param>
    /// <param name="checkpointRef">检查点引用 / Checkpoint reference.</param>
    /// <returns>求解结果 / Solve result.</returns>
    public static SolveResult ToSolveResult(
        Domain.SerializedSolution solution,
        Domain.ObjectRef? checkpointRef) {
        ArgumentNullException.ThrowIfNull(solution);
        return new SolveResult(
            Feasible: solution.Feasible,
            Optimal: solution.ClaimsExactOptimum,
            ObjectiveValue: ObjectiveOf(solution),
            Gap: solution.Gap is { } gap ? new Flt64(gap) : null,
            Elapsed: TimeSpan.FromMilliseconds(solution.ElapsedMs),
            CheckpointRef: checkpointRef,
            Message: solution.Message);
    }

    /// <summary>远程 CP 默认线程数 / Default remote CP thread count.</summary>
    public const int DefaultRemoteCpThreads = 8;

    /// <summary>
    /// 计算模型指纹 / Compute the model fingerprint.
    /// </summary>
    /// <remarks>
    /// 对 snapshot 的规范化编码取 SHA-256，输出小写十六进制。指纹绑定的是**编码后的字节**，
    /// 因此它同时约束了模型内容与编码方式——任一处不同都会得到不同的指纹。
    /// SHA-256 over the snapshot's canonical encoding, as lower-case hex. The
    /// fingerprint binds the **encoded bytes**, so it constrains both the model content and the encoding;
    /// any difference in either yields a different value.
    /// </remarks>
    /// <param name="snapshot">不含 native 句柄的 CP snapshot / CP snapshot without native handles.</param>
    /// <returns>模型指纹 / Model fingerprint.</returns>
    public static Result<string, ErrorCode, Error<ErrorCode>> ModelFingerprint(
        ConstraintProgrammingModelSnapshot snapshot) {
        ArgumentNullException.ThrowIfNull(snapshot);
        Result<string, ErrorCode, Error<ErrorCode>> encoded =
            ConstraintProgrammingSnapshotCodec.Encode(snapshot);
        if (!encoded.IsOk) {
            return encoded;
        }
        byte[] digest = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(encoded.Value));
        return Results.Ok(Convert.ToHexStringLower(digest));
    }

    /// <summary>
    /// 计算配置指纹 / Compute the configuration fingerprint.
    /// </summary>
    /// <remarks>
    /// 把有效配置拼成"长度前缀 + 键 + 长度前缀 + 值"的规范化串后取 SHA-256。长度前缀让拼接
    /// 无歧义（否则 <c>a</c>+<c>bc</c> 与 <c>ab</c>+<c>c</c> 会撞车），空值编码为长度 <c>-1</c>，
    /// <c>solverParams</c> 按 key 排序后逐对展开。
    ///
    /// <c>threads</c> 缺失时取 <see cref="DefaultRemoteCpThreads"/>，因此"未指定线程数"与
    /// "指定为 8"得到相同指纹——这与后端实际行为一致（未指定就用 8）。
    ///
    /// the effective configuration is flattened into length-prefixed key/value pairs
    /// and hashed with SHA-256. Length prefixes keep the concatenation unambiguous (otherwise <c>a</c>+
    /// <c>bc</c> and <c>ab</c>+<c>c</c> would collide), a null value encodes as length <c>-1</c>, and
    /// <c>solverParams</c> is expanded in key order. A missing <c>threads</c> becomes
    /// <see cref="DefaultRemoteCpThreads"/>, so "unspecified" and "explicitly 8" hash identically — which
    /// matches what the backend actually does.
    /// </remarks>
    /// <param name="config">远程求解配置 / Remote solver config.</param>
    /// <returns>配置指纹 / Configuration fingerprint.</returns>
    public static string ConfigurationFingerprint(SolverConfig config) {
        ArgumentNullException.ThrowIfNull(config);
        var canonical = new System.Text.StringBuilder();
        AppendCanonicalConfiguration(canonical, "timeLimitMs", config.TimeLimitMs?.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
        AppendCanonicalConfiguration(canonical, "solutionLimit", config.SolutionLimit?.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
        AppendCanonicalConfiguration(canonical, "mipGapTolerance", config.MipGapTolerance?.ToString(
            "R",
            System.Globalization.CultureInfo.InvariantCulture));
        AppendCanonicalConfiguration(canonical, "threads", (config.Threads ?? DefaultRemoteCpThreads)
            .ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (config.SolverParams is { Count: > 0 } solverParams) {
            foreach (KeyValuePair<string, string> entry in solverParams
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)) {
                AppendCanonicalConfiguration(canonical, "solverParam.key", entry.Key);
                AppendCanonicalConfiguration(canonical, "solverParam.value", entry.Value);
            }
        }
        byte[] digest = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(canonical.ToString()));
        return Convert.ToHexStringLower(digest);
    }

    /// <inheritdoc cref="AppendCanonicalConfiguration(System.Text.StringBuilder, string, string?)"/>
    private static void AppendCanonicalConfiguration(
        System.Text.StringBuilder builder,
        string name,
        string? value) {
        builder.Append(name.Length)
            .Append(':')
            .Append(name)
            .Append(value is null ? -1 : value.Length)
            .Append(':')
            .Append(value ?? string.Empty);
    }

    /// <summary>
    /// 校验结果确实绑定回本次请求 / Validate that the result is actually bound to this request.
    /// </summary>
    /// <remarks>
    /// 这是整条远程链路里最关键的防串台检查：一个"看起来正确"的结果可能来自别的任务、别的切片或别的
    /// 模型。四道门槛：
    /// 1. **task／slice 绑定**：v2+ 必须精确等于本次的 task／slice；v1 若给出则必须一致（历史后端可能
    ///    不下发这两个字段，但一旦下发就不能是错的）。
    /// 2. **模型指纹**：v2+ 只要结果带了任何观测（可行、有结果引用、状态不是 `UNKNOWN`、终态不是
    ///    `BACKEND_FAILURE`），就必须携带与本次 snapshot 编码一致的模型指纹。这样"后端把别的模型的结果
    ///    发回来"会被拦住。
    /// 3. **配置指纹**：v2+ 且带观测时，配置指纹必须与本次请求一致——否则无法把结果归因于这组超参数。
    /// 4. v1 结果允许缺模型指纹，但一旦给出就必须一致。
    ///
    /// 上述"是否带观测"的判定决定了要求的严格程度，因此纯后端故障（无观测）不会被误判为
    /// 串台。
    ///
    /// This is the critical cross-talk guard of the whole remote path: a plausible-looking result may
    /// belong to another task, slice or model. Four gates apply — exact task/slice binding under v2+ and
    /// non-null-but-consistent under v1; a model fingerprint matching this snapshot's encoding whenever the
    /// result carries any observation; a configuration fingerprint matching this request under the same
    /// condition; and, for v1, an optional model fingerprint that must match if present. The "carries an
    /// observation" test sets the required strictness, so a pure backend failure without observations is not
    /// mistaken for cross-talk.
    /// </remarks>
    /// <param name="slice">切片结果 / Slice result.</param>
    /// <param name="snapshot">本次请求的 snapshot / The snapshot of this request.</param>
    /// <param name="expectedTaskId">期望任务标识 / Expected task identifier.</param>
    /// <param name="expectedSliceId">期望切片标识 / Expected slice identifier.</param>
    /// <param name="expectedConfigurationFingerprint">期望配置指纹 / Expected configuration fingerprint.</param>
    /// <returns>校验结果 / Validation result.</returns>
    public static Try ValidateExecutionBinding(
        SliceResult slice,
        ConstraintProgrammingModelSnapshot snapshot,
        string expectedTaskId,
        string expectedSliceId,
        string expectedConfigurationFingerprint) {
        ArgumentNullException.ThrowIfNull(slice);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(expectedTaskId);
        ArgumentNullException.ThrowIfNull(expectedSliceId);
        ArgumentNullException.ThrowIfNull(expectedConfigurationFingerprint);

        bool strict = ParseMajorVersion(slice.SchemaVersion) is { } major && major >= 2;
        if (strict && (slice.RunId != expectedTaskId || slice.AttemptId != expectedSliceId)) {
            return Invalid(
                $"远程 CP 结果未绑定本次 task/slice：run={slice.RunId}, attempt={slice.AttemptId} / "
                + "Remote CP result is not bound to this task/slice");
        }
        if (!strict &&
            ((slice.RunId is not null && slice.RunId != expectedTaskId) ||
                (slice.AttemptId is not null && slice.AttemptId != expectedSliceId))) {
            return Invalid(
                $"legacy 远程 CP 结果的 task/slice 绑定不一致：run={slice.RunId}, "
                + $"attempt={slice.AttemptId} / Legacy remote CP result has conflicting task/slice binding");
        }

        Result<string, ErrorCode, Error<ErrorCode>> expectedModel = ModelFingerprint(snapshot);
        if (!expectedModel.IsOk) {
            // 编码失败时无法建立指纹绑定，直接拒绝而不是放行。
            // A failed encoding means the fingerprint binding cannot be established, so reject rather than
            // let it through.
            return Invalid(
                "无法计算本次请求的模型指纹 / Cannot compute the model fingerprint of this request");
        }

        bool requiresArtifactBinding =
            slice.Feasible ||
            slice.ResultRef is not null ||
            (slice.ProblemStatus is { } problemStatus &&
                problemStatus != Domain.RemoteProblemStatus.Unknown) ||
            (slice.TerminationReason is { } termination &&
                termination != Domain.RemoteTerminationReason.BackendFailure);
        string? actualModel = slice.Fingerprints is { } fingerprints &&
            fingerprints.TryGetValue("model", out string? model)
                ? model
                : null;
        if (strict && requiresArtifactBinding && actualModel is null) {
            return Invalid(
                "严格远程 CP 结果缺少模型指纹 / "
                + "Strict remote CP result is missing the model fingerprint");
        }
        if (actualModel is not null && actualModel != expectedModel.Value) {
            return Invalid(
                "远程 CP 结果模型指纹与请求不一致 / "
                + "Remote CP result model fingerprint disagrees with the request");
        }
        if (strict && requiresArtifactBinding) {
            string? actualConfiguration =
                slice.Fingerprints is { } all &&
                all.TryGetValue("configuration", out string? configuration)
                    ? configuration
                    : null;
            if (actualConfiguration is null
                || actualConfiguration != expectedConfigurationFingerprint) {
                return Invalid(
                    "远程 CP 结果配置指纹与请求不一致 / "
                    + "Remote CP result configuration fingerprint disagrees with the request");
            }
        }
        return Results.OkInstance;
    }

    /// <summary>
    /// 校验稳定解工件与原始结果一致 / Validate that the stable-solution artifact agrees with the raw result.
    /// </summary>
    /// <remarks>
    /// 这里有两个真相源：后端直接返回的原始结果，以及存储里的稳定解工件。**两者必须一致**——否则无法判断
    /// 最终交给用户的是哪一份。因此宁可拒绝，也不静默择一。
    ///
    /// 的宽严规则：v2+ 要求工件字段**逐一相等**；v1 工件允许缺省（字段为 null），但一旦给出就
    /// 必须相等。这反映了历史后端可能不下发全部字段，而新后端必须完整。
    ///
    /// 一处字段差异（不是放宽）：工件与结果两边都有 optimal 布尔时可直接比较；C# 的
    /// <see cref="SliceResult"/> 没有独立 optimal 布尔，最优性由 <see cref="SliceResult.SolutionPresence"/>
    /// 编码，因此改为比较「工件的 optimal」与「原始结果的存在性是否为 OPTIMAL」，语义等价。
    ///
    /// There are two sources of truth here: the raw result returned by the backend and the stable-solution
    /// artifact in storage. They **must** agree, otherwise it is impossible to tell which one the caller is
    /// being handed, so a disagreement is rejected rather than silently resolved either way. For v2+ this
    /// requires field-by-field equality while a v1 artifact may omit fields (null) but must
    /// match on any field it does carry. One field differs without being loosened: the artifact and the raw result
    /// each carry an `optimal` boolean elsewhere, while C#'s <see cref="SliceResult"/> has no separate optimal flag because
    /// <see cref="SliceResult.SolutionPresence"/> encodes it, so the artifact's optimal flag is compared
    /// against "presence is OPTIMAL" — semantically the same check.
    /// </remarks>
    /// <param name="serialized">稳定解工件 / Stable-solution artifact.</param>
    /// <param name="slice">原始切片结果 / Raw slice result.</param>
    /// <returns>校验结果 / Validation result.</returns>
    public static Try ValidateSerializedCpResult(SerializedSolution serialized, SliceResult slice) {
        ArgumentNullException.ThrowIfNull(serialized);
        ArgumentNullException.ThrowIfNull(slice);

        bool strict = ParseMajorVersion(serialized.SchemaVersion) is { } major && major >= 2;
        bool resultOptimal = slice.SolutionPresence == Domain.RemoteSolutionPresence.Optimal;

        if (serialized.Feasible != slice.Feasible) {
            return Invalid(
                "远程 CP raw result 与稳定解可行性不一致 / "
                + "Remote CP raw result disagrees with serialized feasibility");
        }
        if (serialized.Optimal != resultOptimal) {
            return Invalid(
                "远程 CP raw result 与稳定解最优性不一致 / "
                + "Remote CP raw result disagrees with serialized optimality");
        }
        if (Disagrees(serialized.ProofStatus, slice.ProofStatus, strict)) {
            return Invalid(
                "远程 CP raw result 与稳定解证明状态不一致 / "
                + "Remote CP raw result disagrees with serialized proof status");
        }
        if (!string.Equals(serialized.SchemaVersion, slice.SchemaVersion, StringComparison.Ordinal)) {
            return Invalid(
                "远程 CP raw result 与稳定解 schema 版本不一致 / "
                + "Remote CP raw result disagrees with serialized schema version");
        }
        if (Disagrees(serialized.TerminationReason, slice.TerminationReason, strict)) {
            return Invalid(
                "远程 CP raw result 与稳定解终止原因不一致 / "
                + "Remote CP raw result disagrees with serialized termination reason");
        }
        if (!serialized.Feasible && serialized.Optimal) {
            return Invalid(
                "稳定解将不可行结果标记为最优 / "
                + "Serialized solution marks an infeasible result as optimal");
        }
        Domain.RemoteSolutionPresence inferredPresence = serialized.Optimal
            ? Domain.RemoteSolutionPresence.Optimal
            : serialized.Feasible
                ? Domain.RemoteSolutionPresence.Incumbent
                : Domain.RemoteSolutionPresence.None;
        if (Disagrees(serialized.SolutionPresence, inferredPresence, strict)) {
            return Invalid(
                "稳定解的解存在性与自身可行性/最优性不一致 / "
                + "Serialized solution presence disagrees with its feasibility or optimality");
        }
        if (inferredPresence != slice.SolutionPresence) {
            return Invalid(
                "远程 CP raw result 与稳定解解存在性不一致 / "
                + "Remote CP raw result disagrees with serialized solution presence");
        }
        if (serialized.ProblemStatus == Domain.RemoteProblemStatus.Feasible && !serialized.Feasible) {
            return Invalid(
                "稳定解将不可行结果标记为 FEASIBLE / "
                + "Serialized solution marks an infeasible result as FEASIBLE");
        }
        if (Disagrees(serialized.ProblemStatus, slice.ProblemStatus, strict)) {
            return Invalid(
                "远程 CP raw result 与稳定解问题状态不一致 / "
                + "Remote CP raw result disagrees with serialized problem status");
        }
        if (serialized.ObjectiveValueInt64 != slice.ObjectiveValueInt64 ||
            !NullableDoubleEquals(serialized.ObjectiveValue, slice.ObjectiveValue?.ToDouble())) {
            return Invalid(
                "远程 CP raw result 与稳定解目标值不一致 / "
                + "Remote CP raw result disagrees with serialized objective");
        }
        if (serialized.ObjectiveValueInt64 is not null && serialized.ObjectiveValue is not null) {
            return Invalid(
                "远程 CP artifact 的精确目标不得同时使用浮点目标字段 / "
                + "Remote CP artifacts must not duplicate exact objectives in the floating-point field");
        }
        if (Disagrees(serialized.Gap, slice.Gap?.ToDouble(), strict)) {
            return Invalid(
                "远程 CP raw result 与稳定解间隙不一致 / "
                + "Remote CP raw result disagrees with serialized gap");
        }
        if (!MapEquals(serialized.Provenance, slice.Provenance) ||
            !MapEquals(serialized.Fingerprints, slice.Fingerprints) ||
            !MapEquals(serialized.FingerprintSchemas, slice.FingerprintSchemas)) {
            return Invalid(
                "远程 CP raw result 与稳定解来源或指纹不一致 / "
                + "Remote CP raw result disagrees with serialized provenance or fingerprints");
        }
        if (!MapEquals(serialized.Statistics, slice.Statistics) ||
            !MapEquals(serialized.Diagnostics, slice.Diagnostics)) {
            return Invalid(
                "远程 CP raw result 与稳定解统计或诊断不一致 / "
                + "Remote CP raw result disagrees with serialized statistics or diagnostics");
        }
        if (strict) {
            if (!string.Equals(serialized.RunId, slice.RunId, StringComparison.Ordinal) ||
                !string.Equals(serialized.AttemptId, slice.AttemptId, StringComparison.Ordinal)) {
                return Invalid(
                    "远程 CP raw result 与稳定解 run/attempt 不一致 / "
                    + "Remote CP raw result disagrees with serialized run/attempt");
            }
        }
        else if ((serialized.RunId is not null && serialized.RunId != slice.RunId) ||
            (serialized.AttemptId is not null && serialized.AttemptId != slice.AttemptId)) {
            return Invalid(
                "远程 CP raw result 与稳定解 run/attempt 不一致 / "
                + "Remote CP raw result disagrees with serialized run/attempt");
        }
        if ((serialized.ArtifactDigest is null) != (slice.ArtifactDigest is null)) {
            return Invalid(
                "远程 CP raw result 与稳定解 artifact 摘要缺失状态不一致 / "
                + "Remote CP raw result and serialized artifact disagree about digest presence");
        }
        if (serialized.ArtifactDigest is not null &&
            !string.Equals(serialized.ArtifactDigest, slice.ArtifactDigest, StringComparison.Ordinal)) {
            return Invalid(
                "远程 CP raw result 与稳定解 artifact 摘要不一致 / "
                + "Remote CP raw result disagrees with serialized artifact digest");
        }
        return Results.OkInstance;
    }

    /// <summary>
    /// 严格模式下要求相等；宽松模式下仅当工件给出值时才要求相等。
    /// Strict mode requires equality; lenient mode requires it only when the artifact supplies a value.
    /// </summary>
    private static bool Disagrees<T>(T? serialized, T? raw, bool strict)
        where T : struct =>
        strict
            ? !EqualityComparer<T?>.Default.Equals(serialized, raw)
            : serialized is not null && !EqualityComparer<T?>.Default.Equals(serialized, raw);

    private static bool NullableDoubleEquals(double? left, double? right) =>
        left is null ? right is null : right is not null && left.Value.Equals(right.Value);

    private static bool MapEquals(
        IReadOnlyDictionary<string, string>? left,
        IReadOnlyDictionary<string, string>? right) {
        if (left is null || left.Count == 0) {
            return right is null || right.Count == 0;
        }
        if (right is null || left.Count != right.Count) {
            return false;
        }
        foreach (KeyValuePair<string, string> entry in left) {
            if (!right.TryGetValue(entry.Key, out string? value) ||
                !string.Equals(entry.Value, value, StringComparison.Ordinal)) {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// 合法化历史 1.0 结果的证明状态 / Normalize a legacy 1.0 result's proof status.
    /// </summary>
    /// <remarks>
    /// <c>1.0</c> 结果若是"不可行 + 已完成 + 未给证明状态"，说明它来自一个早于证明字段的
    /// 后端，此时记为 <c>CLAIMED</c> 而不是 <c>NONE</c>——否则"明确没有证明"与"该字段不存在"会被混为
    /// 一谈。这里不加强任何结论，只补足缺失的语义。
    /// a <c>1.0</c> result that is infeasible, completed and carries no proof status
    /// came from a backend older than the proof field, so it becomes <c>CLAIMED</c> rather than <c>NONE</c>
    /// — otherwise "explicitly unproven" and "field absent" would be conflated. No conclusion is
    /// strengthened; only the missing semantics is filled in.
    /// </remarks>
    /// <param name="slice">切片结果 / Slice result.</param>
    /// <returns>合法化后的结果 / The normalized result.</returns>
    public static SliceResult NormalizeLegacyCpResult(SliceResult slice) {
        ArgumentNullException.ThrowIfNull(slice);
        bool legacyInfeasible =
            string.Equals(slice.SchemaVersion, "1.0", StringComparison.Ordinal) &&
            !slice.Feasible &&
            slice.ProblemStatus == Domain.RemoteProblemStatus.Infeasible &&
            slice.TerminationReason == Domain.RemoteTerminationReason.Completed &&
            slice.ProofStatus == Domain.RemoteProofStatus.None;
        return legacyInfeasible
            ? slice with { ProofStatus = Domain.RemoteProofStatus.Claimed }
            : slice;
    }

    /// <summary>
    /// 校验远端 CP 原始结果自相一致 / Validate that a raw remote CP result is self-consistent.
    /// </summary>
    /// <remarks>
    /// 逐条校验。这些检查的共同目的是：**宁可拒绝一个矛盾的结果，也不要
    /// 把一个自相矛盾的结论交给下游**。
    /// - schema 主版本必须是 1 或 2（未来主版本一律拒绝，不做"尽力解读"）。
    /// - 可行性必须与问题状态、最优性、证明状态、解存在性一致：可行 ⇔ `FEASIBLE`；不可行不得声称最优；
    ///   声称最优必须给出证明状态。
    /// - v2 结果的指纹键集必须与 schema 键集一致，且 schema 值不得空白。
    /// - 精确整数目标与浮点目标不得同时出现。
    /// - `bestBound`／`gap` 统计字段必须可解析为数值。
    /// - 结果引用与 artifact 摘要必须成对出现。
    ///
    /// 本方法作用于 C# 的 <see cref="SliceResult"/>，它恰好携带全部相关字段；
    /// <c>SolveResult</c> 与 C# 的同名精简类型不是一回事，因此不按名对齐而按字段对齐。
    ///
    /// Corresponds rule for rule to its <c>validateRawResult</c>. The shared purpose is to
    /// reject a contradictory result rather than hand a self-contradictory conclusion downstream: the
    /// schema major must be 1 or 2 (future majors are rejected, not best-effort interpreted); feasibility
    /// must agree with problem status, optimality, proof status and solution presence; an infeasible result
    /// may not claim optimality and an optimal claim requires a proof status; v2 fingerprint keys must
    /// match the schema keys with non-blank values; exact and floating objectives may not coexist;
    /// <c>bestBound</c>/<c>gap</c> must parse as numbers; and a result reference must be paired with an
    /// artifact digest. This operates on C#'s <see cref="SliceResult"/>, which carries every relevant
    /// field, so it aligns by field rather than by the (differently shaped) type name.
    /// </remarks>
    /// <param name="slice">切片结果 / Slice result.</param>
    /// <returns>校验结果 / Validation result.</returns>
    public static Try ValidateRawCpResult(SliceResult slice) {
        ArgumentNullException.ThrowIfNull(slice);

        int? majorVersion = ParseMajorVersion(slice.SchemaVersion);
        if (majorVersion is null || majorVersion is not (1 or 2)) {
            return Invalid(
                $"不支持的远程 CP schema 主版本：{slice.SchemaVersion} / "
                + $"Unsupported remote CP schema major version: {slice.SchemaVersion}");
        }
        if (slice.Feasible && slice.ProblemStatus != Domain.RemoteProblemStatus.Feasible) {
            return Invalid(
                "远程 CP raw result 的问题状态与可行性不一致 / "
                + "Remote CP raw problem status disagrees with feasibility");
        }
        if (!slice.Feasible && slice.ProblemStatus == Domain.RemoteProblemStatus.Feasible) {
            return Invalid(
                "远程 CP raw result 将不可行结果标记为 FEASIBLE / "
                + "Remote CP raw result marks an infeasible result as FEASIBLE");
        }
        if (!slice.Feasible && slice.SolutionPresence == Domain.RemoteSolutionPresence.Optimal) {
            return Invalid(
                "远程 CP raw result 将不可行结果标记为最优 / "
                + "Remote CP raw result marks an infeasible result as optimal");
        }
        if (slice.SolutionPresence == Domain.RemoteSolutionPresence.Optimal &&
            slice.ProofStatus == Domain.RemoteProofStatus.None) {
            return Invalid(
                "远程 CP raw result 声称最优但没有证明状态 / "
                + "Remote CP raw result claims optimality without a proof status");
        }
        bool isVersioned = !string.Equals(slice.SchemaVersion, "1.0", StringComparison.Ordinal);
        if (isVersioned && slice.Fingerprints is { } fingerprints) {
            if (slice.FingerprintSchemas is not { } schemas) {
                return Invalid(
                    "远程 CP 指纹与 fingerprint schema 清单不一致 / "
                    + "Remote CP fingerprints and fingerprint schemas disagree");
            }
            if (!fingerprints.Keys.OrderBy(key => key, StringComparer.Ordinal).SequenceEqual(
                schemas.Keys.OrderBy(key => key, StringComparer.Ordinal), StringComparer.Ordinal)) {
                return Invalid(
                    "远程 CP 指纹与 fingerprint schema 清单不一致 / "
                    + "Remote CP fingerprints and fingerprint schemas disagree");
            }
            foreach (KeyValuePair<string, string> entry in schemas) {
                if (string.IsNullOrWhiteSpace(entry.Value)) {
                    return Invalid(
                        "远程 CP 指纹 schema 不能为空 / "
                        + "Remote CP fingerprint schemas must not be blank");
                }
            }
        }
        if (slice.ObjectiveValueInt64 is not null && slice.ObjectiveValue is not null) {
            return Invalid(
                "远程 CP 精确目标不得同时使用浮点目标字段 / "
                + "Remote CP exact objectives must not be duplicated in the floating-point field");
        }
        if (slice.Statistics is { } statistics) {
            foreach (string key in new[] { "bestBound", "gap" }) {
                if (statistics.TryGetValue(key, out string? value) &&
                    !double.TryParse(
                        value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out _)) {
                    return Invalid(
                        $"远程 CP 统计字段无效：{key}={value} / Invalid remote CP statistic: {key}={value}");
                }
            }
        }
        if ((slice.ResultRef is null) != (slice.ArtifactDigest is null)) {
            return Invalid(
                "远程 CP 结果引用与 artifact 摘要必须成对出现 / "
                + "Remote CP result references and artifact digests must be provided together");
        }
        // expectedPresence 由独立的 optimal／feasible 两个布尔推导；C# 的 SliceResult 没有独立的
        // optimal 布尔，presence 本身就是最优性的编码，因此这里只能做非循环的那部分校验：有解必须可行，
        // 无解必须不可行。
 // The implementation derives expectedPresence from independent optimal/feasible booleans; C#'s
        // SliceResult has no separate optimal flag because presence encodes it, so only the non-circular
        // half of the check remains: a present solution must be feasible and an absent one must not be.
        bool claimsSolution = slice.SolutionPresence is
            Domain.RemoteSolutionPresence.Optimal or Domain.RemoteSolutionPresence.Incumbent;
        if (claimsSolution != slice.Feasible) {
            return Invalid(
                "远程 CP raw result 的解存在性与可行性不一致 / "
                + "Remote CP raw solution presence disagrees with feasibility");
        }
        return Results.OkInstance;
    }

    private static int? ParseMajorVersion(string? schemaVersion) {
        if (string.IsNullOrWhiteSpace(schemaVersion)) {
            return null;
        }
        int separator = schemaVersion.IndexOf('.', StringComparison.Ordinal);
        string major = separator < 0 ? schemaVersion : schemaVersion[..separator];
        return int.TryParse(
            major,
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out int parsed)
            ? parsed
            : null;
    }

    /// <summary>
    /// 解析 CP 诊断（目标值类型为 <c>Int64</c>）/ Parse CP diagnostics with an <c>Int64</c> objective type.
    /// </summary>
    /// <remarks>
    /// `SolveDiagnostics&lt;V&gt;` 的 `V` 只出现在 `ConstraintEvaluations`／`VariableBoundEvaluations` 两个
    /// 列表里，而当前的解析**只填充** `Warnings` 与 `Errors`——这两个列表保持为空。因此这里把同一份解析结果
    /// 重新装进 `Int64` 版本**不丢失任何内容**；一旦将来开始解析带数值的评估项，这条转换就必须重新审视，
    /// 因为那时光标值类型的差别才会真正生效。
    /// `SolveDiagnostics&lt;V&gt;` only uses `V` in `ConstraintEvaluations`/`VariableBoundEvaluations`, and the
    /// current parser **only fills** `Warnings` and `Errors` — those two lists stay empty. Rewrapping the same
    /// parse into the `Int64` variant therefore **loses nothing**; once value-carrying evaluations are parsed,
    /// this conversion must be revisited, because only then does the value type actually matter.
    /// </remarks>
    /// <param name="diagnostics">原始诊断映射 / Raw diagnostics map.</param>
    /// <returns>CP 诊断 / CP diagnostics.</returns>
    public static Result<
        Fuookami.Ospf.Core.Solver.Report.SolveDiagnostics<Fuookami.Ospf.Math.Algebra.Number.Int64>,
        ErrorCode,
        Error<ErrorCode>> ToCpDiagnostics(IReadOnlyDictionary<string, string>? diagnostics) {
        if (diagnostics is null || diagnostics.Count == 0) {
            return Results.Ok(
                new Fuookami.Ospf.Core.Solver.Report.SolveDiagnostics<
                    Fuookami.Ospf.Math.Algebra.Number.Int64>());
        }
        Result<
            Fuookami.Ospf.Core.Solver.Report.SolveDiagnostics<Flt64>,
            ErrorCode,
            Error<ErrorCode>> parsed = ToRemoteDiagnostics(diagnostics);
        if (!parsed.IsOk) {
            return parsed switch {
                Failed<
                    Fuookami.Ospf.Core.Solver.Report.SolveDiagnostics<Flt64>,
                    ErrorCode,
                    Error<ErrorCode>> failed =>
                    Results.Failed<
                        Fuookami.Ospf.Core.Solver.Report.SolveDiagnostics<
                            Fuookami.Ospf.Math.Algebra.Number.Int64>>(failed.Error),
                Fatal<
                    Fuookami.Ospf.Core.Solver.Report.SolveDiagnostics<Flt64>,
                    ErrorCode,
                    Error<ErrorCode>> fatal =>
                    new Fatal<
                        Fuookami.Ospf.Core.Solver.Report.SolveDiagnostics<
                            Fuookami.Ospf.Math.Algebra.Number.Int64>,
                        ErrorCode,
                        Error<ErrorCode>>(fatal.Errors),
                _ => Results.Failed<
                    Fuookami.Ospf.Core.Solver.Report.SolveDiagnostics<
                        Fuookami.Ospf.Math.Algebra.Number.Int64>>(new Err<ErrorCode>(
                            ErrorCode.ApplicationFailed,
                            "诊断解析失败 / Diagnostics cannot be parsed")),
            };
        }
        return Results.Ok(new Fuookami.Ospf.Core.Solver.Report.SolveDiagnostics<
            Fuookami.Ospf.Math.Algebra.Number.Int64>(
            Warnings: parsed.Value.Warnings,
            Errors: parsed.Value.Errors));
    }

    /// <summary>
    /// 取目标值：精确整数优先，浮点值仅在线性或二次结果使用。
    /// Read the objective: the exact integer wins; the floating value only applies to linear or quadratic results.
    /// </summary>
    /// <param name="solution">wire 解 / Wire solution.</param>
    /// <returns>目标值；缺失为 null / Objective value, or null when absent.</returns>
    public static Flt64? ObjectiveOf(Domain.SerializedSolution solution) {
        ArgumentNullException.ThrowIfNull(solution);
        if (solution.ObjectiveValueInt64 is { } exact) {
            return new Flt64(exact);
        }
        return solution.ObjectiveValue is { } floating ? new Flt64(floating) : null;
    }
}
