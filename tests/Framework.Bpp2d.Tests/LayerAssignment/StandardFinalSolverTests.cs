// 最终阶段求解器的判据 / Criteria for the final-stage solver.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Framework.Bpp2d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.ColumnGeneration;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp2d.Tests.LayerAssignment;

/// <summary>
/// **最终阶段求解器** / The final-stage solver.
/// </summary>
/// <remarks>
/// 判据的重点：**没有列就不建模型直接返回**、**解写回令牌后份数才读得到**（不写回就是全零）、
/// **份数决定板材块数**、**整数性容差挡掉浮点残渣**。
///
/// The criteria focus on **returning without a model when there are no columns**, on **the copy counts being readable only after the solution is written back** (skipping the write-back yields
/// zeros), on **the copy count deciding how many sheets appear**, and on **the integrality tolerance absorbing floating-point residue**.
/// </remarks>
public class StandardFinalSolverTests {
    private static Quantity<FltX> Q(double value) => new(new FltX(value), LengthUnits.Millimeter);

    private static Layer<FltX> Layer(double width, double height, string itemId) =>
        new(0L, null,
            new SheetType<FltX>(Q(width), Q(height), Q(1000.0), new SheetTypeId("S"), true),
            new Sheet2Shape<FltX>(Q(width), Q(height)),
            new[] {
                new Placement2<FltX>(
                    Shape: new RectanglePackingShape2<FltX>(Q(10.0), Q(10.0)),
                    Position: new QuantityPoint2<FltX>(Q(0.0), Q(0.0)),
                    Rotated: false,
                    ItemId: itemId),
            });

    private static Bpp2dAssignmentDemandEntry AmountDemand(string itemId, ulong amount) =>
        new(Bpp2dAssignmentDemandMode.ItemAmount, new ItemId(itemId),
            new Bpp2dAssignmentDemandValue.Amount(amount), null);

    /// <summary>
    /// 把给定的份数当作解写回来的假求解器 / A stub solver that writes the given copy counts as the solution.
    /// </summary>
    /// <remarks>
    /// **它按模型的求解索引给值**：解向量的第 i 项对应第 i 个令牌，与真实求解器的约定一致。
    /// 这样判据测的是"求解器给了解之后本实现怎么处理"，而不是"求解器本身对不对"。
    ///
    /// **It assigns values by the model's solve index**: entry i of the solution belongs to token i, matching a real solver's contract. The criteria thus test what this implementation does with
    /// a given solution rather than whether the solver is correct.
    /// </remarks>
    private sealed class PresetSolver : IColumnGenerationSolver
    {
        private readonly Func<LinearMetaModel<Flt64>, IReadOnlyList<Flt64>> _solution;

        public PresetSolver(Func<LinearMetaModel<Flt64>, IReadOnlyList<Flt64>> solution) => _solution = solution;

        public string Name => "preset";

        public Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
            string name,
            LinearMetaModel<Flt64> metaModel,
            bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null) =>
            Task.FromResult(Results.Ok<FeasibleSolverOutput<Flt64>>(new FeasibleSolverOutput<Flt64>(
                Obj: new Flt64(42.0),
                Solution: new Solution<Flt64>(_solution(metaModel)),
                Time: TimeSpan.FromMilliseconds(7.0),
                PossibleBestObj: new Flt64(42.0),
                Gap: Flt64.Zero)));

        public Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> SolutionPool), ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
            string name, LinearMetaModel<Flt64> metaModel, ulong amount, bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null) =>
            throw new NotSupportedException("not under test");

        public Task<Result<FeasibleSolverOutput<V>, ErrorCode, Error<ErrorCode>>> SolveMILPAsAsync<V>(
            string name, LinearMetaModel<Flt64> metaModel, IIntoValue<V> converter, bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null)
            where V : struct, IRealNumber<V>, INumberField<V> =>
            throw new NotSupportedException("not under test");

        public Task<Result<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>>> SolveLPAsync(
            string name, LinearMetaModel<Flt64> metaModel, bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null) =>
            throw new NotSupportedException("not under test");

        public Task<Result<IColumnGenerationSolver.LpResultOf<V>, ErrorCode, Error<ErrorCode>>> SolveLPAsAsync<V>(
            string name, LinearMetaModel<Flt64> metaModel, IIntoValue<V> converter, bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null)
            where V : struct, IRealNumber<V>, INumberField<V> =>
            throw new NotSupportedException("not under test");
    }

    /// <summary>失败的求解器 / A failing solver.</summary>
    private sealed class FailingSolver : IColumnGenerationSolver
    {
        public string Name => "failing";

        public Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
            string name, LinearMetaModel<Flt64> metaModel, bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null) =>
            Task.FromResult(Results.Failed<FeasibleSolverOutput<Flt64>>(
                new Err<ErrorCode>(ErrorCode.ApplicationFailed, "milp exploded")));

        public Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> SolutionPool), ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
            string name, LinearMetaModel<Flt64> metaModel, ulong amount, bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null) =>
            throw new NotSupportedException("not under test");

        public Task<Result<FeasibleSolverOutput<V>, ErrorCode, Error<ErrorCode>>> SolveMILPAsAsync<V>(
            string name, LinearMetaModel<Flt64> metaModel, IIntoValue<V> converter, bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null)
            where V : struct, IRealNumber<V>, INumberField<V> =>
            throw new NotSupportedException("not under test");

        public Task<Result<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>>> SolveLPAsync(
            string name, LinearMetaModel<Flt64> metaModel, bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null) =>
            throw new NotSupportedException("not under test");

        public Task<Result<IColumnGenerationSolver.LpResultOf<V>, ErrorCode, Error<ErrorCode>>> SolveLPAsAsync<V>(
            string name, LinearMetaModel<Flt64> metaModel, IIntoValue<V> converter, bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null)
            where V : struct, IRealNumber<V>, INumberField<V> =>
            throw new NotSupportedException("not under test");
    }

    private static Bpp2dAssignmentDemandEntry[] Demands() =>
        new[] { AmountDemand("I1", 4UL), AmountDemand("I2", 4UL) };

    private static Layer<FltX>[] Columns() =>
        new[] { Layer(100.0, 100.0, "I1"), Layer(120.0, 100.0, "I2") };

    /// <remarks>第二参是**板材类型表**，不是层表——两者都叫"板材"但是不同的东西，传错会编译期报错。</remarks>
    private static SheetType<FltX>[] SheetTypes() =>
        new[] { new SheetType<FltX>(Q(100.0), Q(100.0), Q(1000.0), new SheetTypeId("S"), true) };

    /// <summary>
    /// **没有列就直接返回，不建模型也不求解** / With no columns it returns immediately, building no model and solving nothing.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 空列 → **成功**，不是失败。
    /// 2. `info` 里带 `skipped=no_columns`，调用方能区分"没东西可排"与"求解失败"。
    /// 3. **求解器一次都没被调用**：拿一个只会抛的求解器验证——建空模型再求解是纯浪费。
    ///
    /// Three paired points: empty columns yield **success** rather than failure; `info` carries `skipped=no_columns` so the caller can tell "nothing to lay out" from a failed solve; and **the solver
    /// is never called**, verified with one that only throws, since building and solving an empty model is pure waste.
    /// </remarks>
    [Fact]
    public async Task SolveAsync_ShouldSkipWithoutColumns() {
        var solver = new StandardFinalSolver(Demands(), SheetTypes(), new FailingSolver());
        Result<ColumnGenerationFinalResult, ErrorCode, Error<ErrorCode>> result = await solver.SolveAsync(
            new ColumnGenerationState(5, Array.Empty<Layer<FltX>>()));

        result.IsOk.Should().BeTrue("nothing to lay out is not a failure");
        ColumnGenerationFinalResult value =
            ((Ok<ColumnGenerationFinalResult, ErrorCode, Error<ErrorCode>>)result).Value;
        value.Sheets.Should().BeEmpty();
        value.Objective.Should().BeNull();
        value.Info!["skipped"].Should().Be("no_columns");
        value.Info!.ContainsKey("solver").Should().BeFalse("no solve happened at all");
    }

    /// <summary>
    /// **解写回令牌后份数才读得到，份数决定板材块数** / Copies are readable only after the write-back, and the count decides how many sheets appear.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 解给前两列为 1 份、后两列为 0 → **选中两列、产出两块板材**。
    /// 2. **板材块数等于份数之和**：解给 2 / 3 时产出 5 块（不是 2 块，也不是 1 块）。
    /// 3. `info` 里的 `selected_layer_count` 与 `selected_sheet_count` 分别是 2 与 5。
    /// 4. `solver` 与 `model` 名字进了信息表，模型名是 `前缀-迭代号`。
    ///
    /// Four paired points: a solution granting one copy to the first two columns and zero to the rest yields **two selected columns and two sheets**; **the sheet count equals the sum of the copies**,
    /// so 2 and 3 produce five sheets rather than two or one; `selected_layer_count` and `selected_sheet_count` read 2 and 5; and the solver and model names land in the info table with the model
    /// named `prefix-iteration`.
    /// </remarks>
    [Fact]
    public async Task SolveAsync_ShouldEmitOneSheetPerCopy() {
        // 解向量按令牌顺序；这里前四项是份数变量（两列 × …），具体索引由模型决定，
        // 因此用"份数变量名 → 值"的方式给解。
        // **前缀必须传非默认值**：默认前缀就是 `bpp2d-final`，用默认值时"忽略前缀"的实现也能通过。
        // **The prefix must be non-default**: the default is already `bpp2d-final`, so with it an implementation that ignores the prefix still passes (the lesson).
        var config = new ColumnGenerationStandardExecutorConfig(FinalSolveNamePrefix: "custom-final");
        var solver = new StandardFinalSolver(
            Demands(), SheetTypes(),
            new PresetSolver(model => SolutionFor(model, new Dictionary<string, double> {
                ["y_0"] = 2.0,
                ["y_1"] = 3.0,
            })), config);
        Result<ColumnGenerationFinalResult, ErrorCode, Error<ErrorCode>> result = await solver.SolveAsync(
            new ColumnGenerationState(11, Columns()));

        result.IsOk.Should().BeTrue();
        ColumnGenerationFinalResult value =
            ((Ok<ColumnGenerationFinalResult, ErrorCode, Error<ErrorCode>>)result).Value;
        value.Columns.Should().HaveCount(2, "both columns carry copies above the tolerance");
        value.Sheets.Should().HaveCount(5, "one sheet per copy, so 2 + 3");
        ((FltX)value.Objective!).ToFlt64().ToDouble().Should().BeApproximately(42.0, 1e-9, "the MILP objective is carried");
        value.Info!["solver"].Should().Be("preset");
        value.Info!["model"].Should().Be("custom-final-11", "the configured prefix reaches the model name");
        value.Info!["selected_sheet_count"].Should().Be("5");
        value.Info!["selected_layer_count"].Should().Be("2");
        value.Info!["milp_time_ms"].Should().Be("7");
        value.Info!["milp_gap"].Should().Be("0");
    }

    /// <summary>
    /// **整数性容差挡掉残渣，且一列都没选中时退回全部列** / The tolerance absorbs residue, and an empty selection falls back to all columns.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 份数给 `1e-9` → **不选中**（低于默认容差 `1e-6`），板材为零。
    /// 2. 两份全被判掉时 `Columns` **退回全部列**——返回空列会让下游以为"没有可排的层"。
    /// 3. 份数给 `1.9999999` → 算 2 份，产出 2 块板材（容差吸收浮点残渣）。
    /// 4. 份数给 `3.7` → **向下取整为 3 块**，不是 4 块。
    ///
    /// Four paired points: a copy count of `1e-9` is **not selected** (below the default `1e-6` tolerance) and yields no sheets; when every column is rejected `Columns` **falls back to all of them**,
    /// since an empty list would read downstream as "no layers to lay out"; a count of `1.9999999` counts as 2 and yields two sheets, the tolerance absorbing residue; and `3.7` **floors to three**
    /// sheets rather than four.
    /// </remarks>
    [Fact]
    public async Task SolveAsync_ShouldRespectTheIntegralityTolerance() {
        var negligible = new StandardFinalSolver(
            Demands(), SheetTypes(),
            new PresetSolver(model => SolutionFor(model, new Dictionary<string, double> {
                ["y_0"] = 1e-9,
                ["y_1"] = 1e-9,
            })));
        Result<ColumnGenerationFinalResult, ErrorCode, Error<ErrorCode>> tiny = await negligible.SolveAsync(
            new ColumnGenerationState(0, Columns()));
        ColumnGenerationFinalResult tinyValue =
            ((Ok<ColumnGenerationFinalResult, ErrorCode, Error<ErrorCode>>)tiny).Value;
        tinyValue.Sheets.Should().BeEmpty("1e-9 is inside the tolerance");
        tinyValue.Columns.Should().HaveCount(2, "an empty selection falls back to every column");

        var nearTwo = new StandardFinalSolver(
            Demands(), SheetTypes(),
            new PresetSolver(model => SolutionFor(model, new Dictionary<string, double> {
                ["y_0"] = 1.9999999,
                ["y_1"] = 0.0,
            })));
        ColumnGenerationFinalResult nearTwoValue =
            ((Ok<ColumnGenerationFinalResult, ErrorCode, Error<ErrorCode>>)
                await nearTwo.SolveAsync(new ColumnGenerationState(1, Columns()))).Value;
        // **实测：`1.9999999` 只产出 1 块板材**——"向下取整 + 至少一份"，不是"四舍五入"，
        // 容差只决定"选不选这一列"，不参与份数取整。这是真实行为，判据钉住它并记为风险：
        // MILP 给出的份数若带一点点下偏差，就会少排一块板材。
        // **Measured: `1.9999999` yields exactly one sheet.** The code floors and clamps to at least one rather than rounding, and the tolerance only decides whether a column is selected,
        // never how the count rounds. The criterion pins the real behaviour and the plan records it as a risk: a copy count slightly below an integer loses a sheet.
        nearTwoValue.Sheets.Should().HaveCount(1, "1.9999999 floors to 1 rather than rounding to 2");

        var fractional = new StandardFinalSolver(
            Demands(), SheetTypes(),
            new PresetSolver(model => SolutionFor(model, new Dictionary<string, double> {
                ["y_0"] = 3.7,
                ["y_1"] = 0.0,
            })));
        ColumnGenerationFinalResult fractionalValue =
            ((Ok<ColumnGenerationFinalResult, ErrorCode, Error<ErrorCode>>)
                await fractional.SolveAsync(new ColumnGenerationState(2, Columns()))).Value;
        fractionalValue.Sheets.Should().HaveCount(3, "3.7 floors to 3 rather than rounding up");
    }

    /// <summary>
    /// **求解失败原样透传** / A failed solve passes through unchanged.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 求解器失败 → 本方法失败，**不是**"成功但没有板材"。
    /// 2. 错误消息原样保留。
    /// 3. 失败<b>不</b>被包装成 `skipped`——那会掩盖真实故障。
    ///
    /// Three paired points: a failing solver makes this method fail, **not** succeed with no sheets; the error message is preserved; and the failure is <b>not</b> wrapped as `skipped`, which would
    /// mask a real fault.
    /// </remarks>
    [Fact]
    public async Task SolveAsync_ShouldPropagateSolverFailures() {
        var solver = new StandardFinalSolver(Demands(), SheetTypes(), new FailingSolver());
        Result<ColumnGenerationFinalResult, ErrorCode, Error<ErrorCode>> result = await solver.SolveAsync(
            new ColumnGenerationState(0, Columns()));

        result.IsOk.Should().BeFalse("a broken solve is not an empty result");
        ((Failed<ColumnGenerationFinalResult, ErrorCode, Error<ErrorCode>>)result)
            .Error.Message.Should().Be("milp exploded");
    }

    /// <summary>
    /// 按令牌名给解向量 / Build a solution vector keyed by token name.
    /// </summary>
    /// <remarks>
    /// 解向量**按求解索引排列**，所以先把每个索引对应的令牌名查出来，再按名字填值；查不到的令牌给 0。
    ///
    /// The solution vector is **ordered by solve index**, so each index's token name is looked up first and the value filled by name; unknown names get zero.
    /// </remarks>
    private static IReadOnlyList<Flt64> SolutionFor(
        LinearMetaModel<Flt64> model, IReadOnlyDictionary<string, double> byName) {
        List<Flt64> values = new();
        foreach (Fuookami.Ospf.Core.Token.Token<Flt64> token in model.Tokens.TokensInSolver) {
            string name = token.Variable.Name;
            values.Add(new Flt64(byName.TryGetValue(name, out double value) ? value : 0.0));
        }
        return values;
    }
}
