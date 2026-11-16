#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Solver.Report;

/// <summary>
/// 批量实验运行器 / The batch experiment runner.
/// </summary>
/// <remarks>
/// 对齐补上这块整块缺失的编排层。
///
/// **这一块的价值全在两条确定性保证上，而两条都不能靠"读起来对"来确认：**
/// **输入顺序打乱 → 结果仍按 `Id` 排序；并发 > 1 → 报告顺序与并发无关。**
/// **所以判据必须真的打乱输入并真的并发跑。**
///
/// **Its value is two determinism guarantees, and neither can be confirmed by reading:** **shuffle the input and the results
/// still come back ordered by `Id`; run at concurrency &gt; 1 and the report order is unaffected.**
/// **So the criteria must really shuffle and really run concurrently.**
/// </remarks>
public class BatchSolveExperimentRunnerTests
{
    /// <summary>
    /// **输入顺序不影响结果顺序** / Input order does not affect result order.
    /// </summary>
    [Fact]
    public async Task TheReportOrder_ShouldNotDependOnTheInputOrder() {
        string[] ids = ["c-09", "a-01", "b-05", "d-02"];
        List<SolveExperimentCase<string>> cases = [.. ids.Select(static id => new SolveExperimentCase<string>(id, id))];

        BatchSolveExperimentReport<Flt64> report = await BatchSolveExperimentRunner.RunAsync<string, Flt64>(
            cases,
            concurrencyBudget: 2,
            executor: SuccessAsync);

        report.Results.Should().HaveCount(ids.Length);
        report.Results.Select(static item => item.CaseId).Should().BeInAscendingOrder(
            "the report order must not depend on which case finished first");
        report.Results[0].CaseId.Should().Be("a-01");
    }

    /// <summary>
    /// **并发确实被限住，而且报告里记的是生效值** / Concurrency is bounded and the report records the effective value.
    /// </summary>
    /// <remarks>
    /// **判据同时看两件事：** **峰值并发不超过上限（用计数器实测），以及 `ConcurrencyBudget`
    /// 记的是生效值而不是传入值。**
    ///
    /// **后者不是形式：** **传 0 时若不记生效值，报告会写"并发 0"，
    /// 而下一个读报告的人会据此认为这里是串行跑的。**
    ///
    /// **The criterion watches two things at once: the observed peak never exceeds the budget, and `ConcurrencyBudget` holds
    /// the effective value rather than the argument.** **The latter is not a formality: with budget 0 an unrecorded
    /// effective value would print "concurrency 0", and the next reader would conclude the batch ran serially.**
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public async Task TheConcurrency_ShouldBeBoundedAndRecorded(int budget) {
        int expected = System.Math.Max(1, budget);
        int current = 0;
        int peak = 0;
        var gate = new SemaphoreSlim(0, 1);

        List<SolveExperimentCase<string>> cases =
            [.. Enumerable.Range(0, 6).Select(index => new SolveExperimentCase<string>($"case-{index:00}", "x"))];

        Task<BatchSolveExperimentReport<Flt64>> running = BatchSolveExperimentRunner.RunAsync<string, Flt64>(
            cases,
            concurrencyBudget: budget,
            executor: async (item, cancellationToken) => {
                int now = Interlocked.Increment(ref current);
                InterlockedMax(ref peak, now);
                await Task.Yield();
                Interlocked.Decrement(ref current);
                return Success(item.Input);
            });

        BatchSolveExperimentReport<Flt64> report = await running;

        report.ConcurrencyBudget.Should().Be(expected, "the report must record the effective budget, not the argument");
        peak.Should().BeLessThanOrEqualTo(expected, "the executor must never run more cases at once than the budget");
        _ = gate;
    }

    /// <summary>
    /// **一个场景失败不会中断整批** / One failing case does not abort the batch.
    /// </summary>
    /// <remarks>
    /// **这是这一块最重要的行为：** **批量实验里"一个坏样本中断整批"会让前八个样本的结果全部作废，
    /// 而它们本来是好的。**
    ///
    /// **判据同时断言：** **失败的那条<b>在结果里</b>（不是被丢掉）、它的 `Report` 为 null、
    /// 它的错误变成一条 `SolveIssue`、而其余场景仍然成功。**
    ///
    /// **只断言"没有抛异常"是不够的：** **把失败场景静默丢掉也满足那一条。**
    ///
    /// **This is the layer's most important behaviour: "one bad sample aborts the batch" would discard the eight good results
    /// that came before it.**
    ///
    /// **It asserts all four: the failed case is <b>present</b> in the results (not dropped), its `Report` is null, its error
    /// became a `SolveIssue`, and the other cases still succeeded.**
    ///
    /// **Asserting "it did not throw" is not enough: silently dropping the failed case satisfies that too.**
    /// </remarks>
    [Fact]
    public async Task AFailingCase_ShouldBecomeAResultAndNotAbortTheBatch() {
        List<SolveExperimentCase<string>> cases = [
            new("good-01", "good"),
            new("bad-02", "bad"),
            new("good-03", "good"),
        ];

        BatchSolveExperimentReport<Flt64> report = await BatchSolveExperimentRunner.RunAsync<string, Flt64>(
            cases,
            concurrencyBudget: 3,
            executor: (item, cancellationToken) => Task.FromResult(
                item.Input == "bad"
                    ? Failure("the sample blew up")
                    : Success(item.Input)));

        report.Results.Should().HaveCount(3, "a failed case is still a case, and must appear in the report");
        SolveExperimentResult<Flt64> failed = report.Results.Single(static item => item.CaseId == "bad-02");
        failed.IsSuccess.Should().BeFalse();
        failed.Report.Should().BeNull();
        failed.Errors.Should().ContainSingle();
        failed.Errors[0].Message.Should().Be("the sample blew up");
        failed.Errors[0].Category.Should().Be(SolveIssueCategory.Backend);
        report.Results.Count(static item => item.IsSuccess).Should().Be(2);
    }

    private static Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> SuccessAsync(
        SolveExperimentCase<string> item,
        CancellationToken cancellationToken) =>
        Task.FromResult(Success(item.Input));

    private static Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> Success(string input) =>
        new Ok<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>(new SolveReport<Flt64> {
            ProblemStatus = ProblemStatus.Feasible,
            TerminationReason = TerminationReason.Completed,
            SolutionPresence = SolutionPresence.Incumbent,
        });

    private static Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> Failure(string message) =>
        new Failed<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>(ErrorCode.IllegalArgument, message);

    private static void InterlockedMax(ref int target, int value) {
        int observed = Volatile.Read(ref target);
        while (value > observed) {
            int previous = Interlocked.CompareExchange(ref target, value, observed);
            if (previous == observed) {
                return;
            }
            observed = previous;
        }
    }
}
