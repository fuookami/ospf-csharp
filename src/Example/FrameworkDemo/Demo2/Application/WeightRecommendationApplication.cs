#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.PayloadMaximization;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Application;

/// <summary>
/// 建议打板应用。Weight recommendation application: orchestrates the weight recommendation optimization pipeline.
/// Port of Kotlin WeightRecommendationApplication / WeightRecommendationAlgorithmImpl.
/// </summary>
public sealed class WeightRecommendationApplication
{
    private readonly AircraftContext _aircraftContext = new();
    private readonly StowageContext _stowageContext = new();
    private readonly MacContext _macContext = new();
    private readonly AirworthinessSecurityContext _airworthinessSecurityContext = new();
    private readonly PayloadMaximizationContext _payloadMaximizationContext = new();

    /// <summary>
    /// 执行建议打板算法。Execute the weight recommendation algorithm.
    /// </summary>
    /// <param name="request">请求 DTO / Request DTO</param>
    /// <returns>响应 DTO 和渲染 DTO / Response DTO and render DTO</returns>
    public (ResponseDTO Response, RenderDTO? Render) Execute(RequestDTO request)
    {
        var notes = new List<string>();

        // 1. Initialize contexts
        Try r1 = Init(request);
        if (r1 is Failed<Success, ErrorCode, Error<ErrorCode>> failed)
        {
            return (ResponseDTO.NoSolution("InitFailed", notes), null);
        }

        // 2. Feasibility diagnostics
        FeasibilityDiagnostics.AppendCoreFeasibilityDiagnostics(request, notes);
        if (notes.Count > 0)
        {
            return (ResponseDTO.NoSolution("NoSolution", notes), null);
        }

        // 3. Resolve solve mode
        SolveMode solveMode = BendersStrategy.ResolveSolveMode(request, notes);

        // 4. Solve (stub)
        // TODO: Port solveWithMILP and solveWithBendersAlgorithm from Kotlin

        return (ResponseDTO.NoSolution("NotYetPorted", notes), null);
    }

    private Try Init(RequestDTO request)
    {
        Try r1 = _aircraftContext.Init(request);
        if (r1.IsFailed) return r1;

        Try r2 = _stowageContext.Init(_aircraftContext, request);
        if (r2.IsFailed) return r2;

        Try r3 = _macContext.Init(_aircraftContext, _stowageContext, request);
        if (r3.IsFailed) return r3;

        Try r4 = _airworthinessSecurityContext.Init(_aircraftContext, _stowageContext, _macContext, request);
        if (r4.IsFailed) return r4;

        // TODO: RecommendedWeightEqualizationContext
        Try r5 = _payloadMaximizationContext.Init(_aircraftContext, _stowageContext, request);
        if (r5.IsFailed) return r5;

        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
