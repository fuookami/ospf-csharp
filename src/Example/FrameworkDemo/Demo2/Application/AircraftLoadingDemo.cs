#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage;
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

    /// <summary>元模型 / Meta model</summary>
    public LinearMetaModel<Flt64> MetaModel => _metaModel;
    /// <summary>备注列表 / Notes list</summary>
    public IReadOnlyList<string> Notes => _notes;

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

        // 4. Direct MILP path (P0 scope: no Benders yet)
        // TODO: Register stowage context with model
        // TODO: Register MAC context with model
        // TODO: Add pipelines and solve

        return Results.Ok<Success>(Results.SuccessInstance);
    }

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
            Positions: new[]
            {
                new PositionInput("P1", 10.0, -1.0, -0.5),
                new PositionInput("P2", 10.0, 1.0, 0.5)
            },
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
}
