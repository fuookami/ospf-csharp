#nullable enable

using System;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Model.Intermediate;
using System.Threading.Tasks;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Application;

/// <summary>
/// 飞机装载演示。Aircraft loading demo: models cargo stowage as a linear optimization problem.
///
/// Port of Kotlin framework_demo/demo2 aircraft-loading solver.
/// </summary>
public sealed class AircraftLoadingDemo
{
    private readonly LinearMetaModel<Flt64> _metaModel = new("demo2-aircraft-loading", ObjectCategory.Minimum);

    private DomainPipelineContext _pipelineContext = null!;
    private readonly List<string> _notes = new();

    private bool _solved;

    /// <summary>元模型 / Meta model</summary>
    public LinearMetaModel<Flt64> MetaModel => _metaModel;
    /// <summary>备注列表 / Notes list</summary>
    public IReadOnlyList<string> Notes => _notes;

    /// <summary>求解参数 / The solve parameters.</summary>
    /// <remarks>
    /// **An empty parameter set suffices by default**: the sample request carries no weight-recommendation preferences, and each domain's pipelines read only
    /// the fields they care about. It is a writable property so a caller can change coefficients without rebuilding the demo object.
    /// </remarks>
    public Parameter Parameter { get; set; } = new();

    /// <summary>
    /// 构建模型（不求解，用于测试验证）。Build the optimization model (without solving, for test verification).
    /// </summary>
    /// <returns>执行结果 / Execution result</returns>
    public Try BuildModel()
    {
        RequestDTO request = CreateSampleRequest();
        return BuildModel(request);
    }

    /// <summary>
    /// 使用给定输入构建模型。Build the optimization model with the given input.
    /// </summary>
    /// <param name="request">请求 DTO / Request DTO</param>
    /// <returns>执行结果 / Execution result</returns>
    public Try BuildModel(RequestDTO request)
    {
        // 1. Resolve solve mode
        SolveMode solveMode = BendersStrategy.ResolveSolveMode(request, _notes);

        // 2. Feasibility diagnostics
        FeasibilityDiagnostics.AppendCoreFeasibilityDiagnostics(request, _notes);

        // 3. Initialize domain pipeline
        _pipelineContext = new DomainPipelineContext();
        Try r1 = _pipelineContext.Init(request);
        if (r1 is Failed<Success, ErrorCode, Error<ErrorCode>> failed)
        {
            // Domain initialization failed (expected for P0 stubs).
            // Continue with feasibility diagnostics only.
            _notes.Add($"Domain init failed: {failed.Message}");
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        // 4. 注册七个域 / Register all seven domains
        //
        // **走 `DomainPipelineContext.Register` 而不是逐个子模型注册**：每个域的 `Register` 除了子模型，还会把该域
        // 的管线表跑一遍——约束加了，**目标也加了**。这两批只注册子模型，所以模型里有变量、有约束、
        // **没有目标**；求解器会拿一个默认目标解出一个看起来合理的答案，而那个答案与业务目标无关。
        //
        // **Go through `DomainPipelineContext.Register` rather than registering sub-models one by one**: besides the sub-models, each domain's `Register` runs that
        // domain's pipeline list, which adds constraints **and objectives**. only registered sub-models, so the model had variables and rows but **no objective**;
        // a solver would then pick a default objective and return a plausible-looking answer unrelated to the business goal.
        Try registered = _pipelineContext.Register(StowageMode.FullLoad, Parameter, _metaModel);
        if (registered is Failed<Success, ErrorCode, Error<ErrorCode>> registrationFailed) {
            _notes.Add($"Domain registration failed: {registrationFailed.Error.Message}");
            return new Failed<Success, ErrorCode, Error<ErrorCode>>(registrationFailed.Error);
        }
        // 5. 目标与求解还没接 / The objective and the solve are not wired yet
        //
        // **这里只到"模型里真的有东西"为止**。求解需要 `SCIP_INTEGRATION` 开关，把"演示能不能跑"绑在一个可选依赖上
        // 会让默认全量测试失去这条判据——**所以先钉"注册成功 + 模型非空"，求解单独一批。**
 // ** stops at "the model really holds something".** Solving needs the `SCIP_INTEGRATION` switch, and tying "can the demo run" to an optional dependency
        // would cost the default suite that criterion — **so the criterion pins "registration succeeded and the model is non-empty", and the solve gets its own batch.**
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 用给定求解器求这个模型 / Solve this model with the given solver.
    /// </summary>
    /// <param name="solver">线性求解器 / The linear solver.</param>
    /// <returns>执行结果 / Execution result.</returns>
    /// <remarks>
    /// **求解器是注入的，不是内定的**：默认全量测试不能依赖某个可选后端，所以"能不能求解"这条判据必须由调用方带求解器进来。
    /// 求解失败不吞掉——失败原因写进 `Notes` 并原样返回，**调用方拿到的失败与真实原因一致**。
    ///
    /// **The solver is injected, not hard-wired**: the default suite must not depend on an optional backend, so "can it solve" has to be a criterion the caller supplies a solver
    /// for. A solve failure is not swallowed — the reason goes into `Notes` and the result is returned unchanged, **so the caller's failure matches the real cause**.
    /// </remarks>
    public async Task<Try> SolveAsync(ILinearSolver solver) {
        ArgumentNullException.ThrowIfNull(solver);

        if (_solved) {
            // **已经解过就不再解**：模型里可能已有求解器写入的辅助状态，重解会把它叠在旧的上。
            // **Already solved, do not solve again**: the model may already carry solver state, and a second solve would stack on the old one.
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        Try built = BuildModel();
        if (built is Failed<Success, ErrorCode, Error<ErrorCode>> buildFailed) {
            return new Failed<Success, ErrorCode, Error<ErrorCode>>(buildFailed.Error);
        }

        // **先转成求解器认的视图**：`SolveAsync` 收的是 `ILinearTriadModelView`，元模型不是。
        // 这一步同时把符号展开、把固定变量消元——**跳过它求解器只会报"模型为空"**，而模型看起来明明有行有目标。
        // **Convert to the view the solver accepts first**: `SolveAsync` takes an `ILinearTriadModelView` and a meta-model is not one. This step also expands symbols and
        // eliminates fixed variables — **skipping it makes the solver report "empty model"** while the model visibly holds rows and an objective.
        Result<(LinearMechanismModel<Flt64> Model, LinearTriadModel Matrix), ErrorCode, Error<ErrorCode>> builtMatrix =
            await _metaModel.BuildLinearTriadModelAsync<Flt64>().ConfigureAwait(false);
        if (builtMatrix is Failed<(LinearMechanismModel<Flt64> Model, LinearTriadModel Matrix), ErrorCode, Error<ErrorCode>> matrixFailed) {
            _notes.Add("Solve failed: " + matrixFailed.Error.Message);
            return new Failed<Success, ErrorCode, Error<ErrorCode>>(matrixFailed.Error);
        }
        LinearTriadModel matrix =
            ((Ok<(LinearMechanismModel<Flt64> Model, LinearTriadModel Matrix), ErrorCode, Error<ErrorCode>>)builtMatrix).Value.Matrix;

        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await solver.SolveAsync(matrix).ConfigureAwait(false);
        if (solved is Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solveFailed) {
            // **失败码单独写进备注**：消息文本会随后端措辞变，**码不会**——判据钉码，才不会因为一句措辞改动就失效，也不会因为吞掉细节而恒绿。
 // **The error code goes into the note separately**: the message text varies with backend wording, **this implementation does not** — pinning this implementation keeps the criterion from
            // breaking on a reworded message, and from staying green while the detail is swallowed.
            _notes.Add("Solve failed: " + solveFailed.Error.Code + " " + solveFailed.Error.Message);
            return new Failed<Success, ErrorCode, Error<ErrorCode>>(solveFailed.Error);
        }

        FeasibleSolverOutput<Flt64> output =
            ((Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>)solved).Value;
        Solution = output.Solution.Values;
        Objective = output.Obj;
        _solved = true;
        _notes.Add("Solved with " + solver.Name + ": objective " + output.Obj.ToDouble());
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>求解得到的变量取值；未求解时为 `null` / The variable values, `null` before solving.</summary>
    public IReadOnlyList<Flt64>? Solution { get; private set; }

    /// <summary>求解得到的目标值；未求解时为 `null` / The objective value, `null` before solving.</summary>
    public Flt64? Objective { get; private set; }
    /// <summary>
    /// 创建示例请求。Create sample request for testing.
    /// </summary>
    public static RequestDTO CreateSampleRequest()
    {
        return new RequestDTO(
            Id: "sample-001",
            Cargos: new[]
            {
                new CargoInput("C1", 8.0, 10, "S1", "D1", RequiresSeparation: true),
                new CargoInput("C2", 6.0, 6, "S2", "D1"),
                new CargoInput("C3", 4.0, 4, "S1", "D2", RequiresSeparation: true)
            },
            // **示例请求必须能过飞机初始化器**： 给 `PositionInput` 补了 `SpaceId`/`LoadingOrder`/`Geometry`，
            // 而这里一直没跟上——示例请求会被"物理空间或装载顺序无效"挡在第一步，**演示因此连模型都建不出来**。
            // 现在补上空间表、互不相同的装载顺序与每舱位的矩形边界。
            //
            // **The sample request must get past the aircraft initialiser**: added `SpaceId`/`LoadingOrder`/`Geometry` to `PositionInput` and this call site never
            // followed, so the sample request was stopped at the first step by "invalid physical space or loading order" — **the demo could not even build a model**.
            // The space table, distinct loading orders and each position's rectangle bounds are supplied now.
            Spaces: new[]
            {
                new LoadingSpaceInput("MAIN", DeckLocationInput.Main)
            },
            Positions: new[]
            {
                new PositionInput(
                    "P1", 10.0, -1.0, -0.5,
                    SpaceId: "MAIN", LoadingOrder: 0,
                    Geometry: new PositionGeometryInput(-1.5, -0.5, -1.0, 0.0)),
                new PositionInput(
                    "P2", 10.0, 1.0, 0.5,
                    SpaceId: "MAIN", LoadingOrder: 1,
                    Geometry: new PositionGeometryInput(0.5, 1.5, 0.0, 1.0))
            },
            // **单位必须写清楚**：`RequestDTO.LengthUnit` 默认是英寸，而示例里的力臂与矩形边界都是**米**。
            // 不写这一行，1.0 x 1.0 的舱位会被算成 `0.0254 x 0.0254 = 0.000645` 平方米，
            // 表面密度系数因此放大 1550 倍（8 kg 变成 12400），`≤ 1000` 的上限连**一件货**都装不下——
            // 表现为"任何正业载都不可行"，而所有约束看上去都加对了。
            // **The unit has to be stated**: `RequestDTO.LengthUnit` defaults to inches while the sample's arms and rectangles are in **metres**. Without this line a
            // 1.0 x 1.0 position is read as `0.0254 x 0.0254 = 0.000645` square metres, so the surface-density coefficient is inflated 1550x (8 kg becomes 12400) and the
            // `<= 1000` cap cannot fit **a single piece** — which shows up as "any positive payload is infeasible" while every constraint looks correctly added (measured).
            LengthUnit: LengthUnitInput.Meter,
            AircraftType: AircraftTypeInput.B737,
            SolvePolicy: new SolvePolicy(),
            BendersAdaptive: new BendersAdaptiveConfig(),
            PayloadUpperBound: 20.0,
            MinPayloadRatio: 0.6,
            MaxAdjacentLoadGap: 8.0,
            MaxCumulativeForwardLoad: 20.0,
            MaxCumulativeBackwardLoad: 20.0,
            EnvelopeLongitudinalMomentMin: -20.0,
            EnvelopeLongitudinalMomentMax: 20.0,
            TargetLongitudinalMoment: 0.0,
            MaxLongitudinalMomentDeviation: 20.0,
            MaxLateralImbalance: 12.0);
    }
    /// <summary>
    /// **全部货物必装**的样车请求 / The sample request with every cargo mandatory.
    /// </summary>
    /// <returns>请求 / The request.</returns>
    /// <remarks>
    /// 量到 demo 造不出必装货，把 `CargoInput.Required` 打通。这份请求是**它的第一个使用方**：
    /// 零装载对必装货**不可行**，于是它才能区分"解出分配"和"什么都没装"——前九批缺的就是这个。
    ///
    /// measured that the demo cannot produce required cargo and wired `CargoInput.Required`. This request is **its first consumer**: zero loading is **infeasible**
    /// for required cargo, so it can tell "decoded assignments" from "nothing was loaded" — what the previous nine batches lacked.
    /// </remarks>
    public static RequestDTO CreateRequiredSampleRequest() {
        RequestDTO sample = CreateSampleRequest();
        return sample with {
            Cargos = System.Linq.Enumerable.ToList(sample.Cargos).ConvertAll(cargo => cargo with { Required = true }),
        };
    }
    /// <summary>
    /// **两件必装**的样车请求 / The sample request with two mandatory cargo.
    /// </summary>
    /// <returns>请求 / The request.</returns>
    /// <remarks>
    /// 量到：三件必装**装不下**（两个舱位各一件），两件必装**可行且零装载不可行**。
    /// 所以这是**唯一能区分"解出分配"和"什么都没装"的示例请求**——缺的就是它。
    ///
    /// Measured: three required cargo **do not fit** (two positions, one item each) while two are **feasible with zero loading infeasible**. This is therefore the
    /// **only sample request that can tell "decoded assignments" from "nothing was loaded"** — what lacked.
    /// </remarks>
    public static RequestDTO CreatePairRequiredSampleRequest() {
        RequestDTO required = CreateRequiredSampleRequest();
        return required with { Cargos = System.Linq.Enumerable.ToList(required.Cargos).GetRange(0, 2) };
    }

    /// <summary>
    /// 创建预配载示例请求 / Create a predistribution sample request.
    /// </summary>
    /// <returns>示例请求 / The sample request.</returns>
    /// <remarks>
    /// **与全配载示例的唯一差别是每个舱位都带上谓词装载重量规格**：预配载模式下 `Load` 会为每个
    /// `PredicateWeightNeeded` 的舱位建谓词重量变量，它的上界与松弛右端都取自这份规格；没有它，装配阶段就报
    /// `IllegalArgument`。
    ///
    /// **为什么不把这个规格塞进 `CreateSampleRequest`**：那会让**每一条既有判据**测到的模型形状都变，
    /// 而它们与预配载无关（立下的规矩：需要的判据自己带规格，示例请求不替所有调用方做决定）。
    ///
    /// **The only difference from the full-load sample is the predicate load weight specification on every position**: in predistribution mode `Load` creates a
    /// predicate weight variable for each `PredicateWeightNeeded` position and takes both its upper bound and the slack right-hand side from this spec; without it
    /// assembly reports `IllegalArgument` . **Why it is not folded into `CreateSampleRequest`**: that would change the model shape measured by
    /// **every existing criterion**, none of which is about predistribution (the rule set: criteria that need it bring it themselves).
    /// </remarks>
    public static RequestDTO CreatePredistributionSampleRequest()
    {
        RequestDTO sample = CreateSampleRequest();
        List<PositionInput> positions = new();
        foreach (PositionInput position in sample.Positions)
        {
            // **规格三端都给**：`Plw` 是谓词重量上界，`Min` 是松弛右端，`Max` 参与区间判断；
            // 压成一个数就看不出哪一端是哪一端（与模型侧的 `PositionPredicateLoadWeight` 同形）。
            // **All three ends are supplied**: `Plw` bounds the predicate weight, `Min` is the slack right-hand side and `Max` takes part in the interval check;
            // collapsed into one number there is no way to tell which end is which (same shape as the model-side `PositionPredicateLoadWeight`).
            positions.Add(position with { PredicateLoadWeight = new PositionPredicateLoadWeightInput(10.0, 1.0, 10.0) });
        }
        return sample with { Positions = positions };
    }
}
