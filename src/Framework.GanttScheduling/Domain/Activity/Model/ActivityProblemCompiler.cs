#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Activity.Service;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.Activity.Model;

/// <summary>
/// 把问题编译成求解输入 / Compile a problem into solver input.
/// </summary>
/// <remarks>
/// 与基线 `prepareActivityProblem` 对齐，固定为 `Flt64`（理由同 <see cref="ActivitySchedulingAggregation"/>）。
///
/// **这一层的职责是"在建模之前把不合法的问题挡掉"，不是"尽量修好它"**：所有检查都返回
/// <see cref="ErrorCode.IllegalArgument"/> 加一句点名哪个身份不合法的消息，而不是抛异常。
/// 一条检查都不省略，因为每一条对应一种"求解器会给出一份看起来正常的错解"的输入。
///
/// Aligned with the baseline's `prepareActivityProblem`, fixed to `Flt64` (for the same reason as <see cref="ActivitySchedulingAggregation"/>).
///
/// **This layer's job is to reject an illegal problem before modelling, not to repair it**: every check returns <see cref="ErrorCode.IllegalArgument"/> with a message naming the offending identity
/// rather than throwing, and none is omitted, because each one corresponds to an input on which the solver would return a plausible-looking wrong answer.
/// </remarks>
public static class ActivityProblemCompiler {
    /// <summary>
    /// 编译 / Compile.
    /// </summary>
    /// <param name="problem">问题 / The problem.</param>
    /// <returns>编译结果或失败 / The compiled problem, or a failure.</returns>
    public static Result<CompiledActivityProblem<Flt64>, ErrorCode, Error<ErrorCode>> Prepare(
        ActivitySchedulingProblem<Flt64> problem) {
        ArgumentNullException.ThrowIfNull(problem);

        if (problem.Horizon.End <= problem.Horizon.Start) {
            return FailOf<CompiledActivityProblem<Flt64>>("the scheduling horizon must be finite and nonempty");
        }

        var tasks = new Dictionary<string, Activity<Flt64>>(StringComparer.Ordinal);
        foreach (Activity<Flt64> activity in problem.Activities) {
            if (!tasks.TryAdd(activity.Id, activity)) {
                return FailOf<CompiledActivityProblem<Flt64>>("duplicate task, resource or execution identity");
            }
        }
        var resources = new Dictionary<string, ActivityResource<Flt64>>(StringComparer.Ordinal);
        foreach (ActivityResource<Flt64> resource in problem.Resources) {
            if (!resources.TryAdd(resource.Id, resource)) {
                return FailOf<CompiledActivityProblem<Flt64>>("duplicate task, resource or execution identity");
            }
        }
        var facts = new Dictionary<string, ActivityExecution<Flt64>>(StringComparer.Ordinal);
        foreach (ActivityExecution<Flt64> execution in problem.ExecutionsValue) {
            if (!facts.TryAdd(execution.Placement.Key.Task, execution)) {
                return FailOf<CompiledActivityProblem<Flt64>>("duplicate task, resource or execution identity");
            }
        }
        foreach (string task in facts.Keys) {
            if (tasks.ContainsKey(task)) {
                return FailOf<CompiledActivityProblem<Flt64>>("duplicate task, resource or execution identity");
            }
        }

        Result<Dictionary<string, CompiledActivityMaterial<Flt64>>, ErrorCode, Error<ErrorCode>> materials =
            CompileMaterials(problem);
        if (materials is not Ok<Dictionary<string, CompiledActivityMaterial<Flt64>>, ErrorCode, Error<ErrorCode>> okMaterials) {
            return Propagate<CompiledActivityProblem<Flt64>, Dictionary<string, CompiledActivityMaterial<Flt64>>>(materials);
        }

        Result<List<CompiledActivityMaterialEvent>, ErrorCode, Error<ErrorCode>> events =
            CompileMaterialEvents(problem, okMaterials.Value);
        if (events is not Ok<List<CompiledActivityMaterialEvent>, ErrorCode, Error<ErrorCode>> okEvents) {
            return Propagate<CompiledActivityProblem<Flt64>, List<CompiledActivityMaterialEvent>>(events);
        }

        Result<Dictionary<string, CompiledActivityResource<Flt64>>, ErrorCode, Error<ErrorCode>> capacities =
            CompileResources(problem);
        if (capacities is not Ok<Dictionary<string, CompiledActivityResource<Flt64>>, ErrorCode, Error<ErrorCode>> okCapacities) {
            return Propagate<CompiledActivityProblem<Flt64>, Dictionary<string, CompiledActivityResource<Flt64>>>(capacities);
        }

        var fixedReservations = new List<CompiledActivityReservation>();
        foreach (ActivityExecution<Flt64> execution in problem.ExecutionsValue) {
            ActivityPlacement<Flt64> placement = execution.Placement;
            bool invalid = execution.Status == ActivityExecutionStatus.Completed
                ? placement.Time.End > problem.Horizon.Start
                : placement.Time.Start > problem.Horizon.Start
                    || placement.Time.End <= problem.Horizon.Start;
            if (invalid) {
                return FailOf<CompiledActivityProblem<Flt64>>("execution fact inconsistent with the horizon; running reservations must describe the remainder explicitly");
            }
            Result<List<CompiledActivityReservation>, ErrorCode, Error<ErrorCode>> normalized =
                NormalizeReservations(placement, resources, okCapacities.Value);
            if (normalized is not Ok<List<CompiledActivityReservation>, ErrorCode, Error<ErrorCode>> okNormalized) {
                return Propagate<CompiledActivityProblem<Flt64>, List<CompiledActivityReservation>>(normalized);
            }
            fixedReservations.AddRange(okNormalized.Value);
        }

        Result<Dictionary<string, (Flt64 Start, Flt64 Mode)>, ErrorCode, Error<ErrorCode>> baselines =
            CompileBaselines(problem, tasks);
        if (baselines is not Ok<Dictionary<string, (Flt64 Start, Flt64 Mode)>, ErrorCode, Error<ErrorCode>> okBaselines) {
            return Propagate<CompiledActivityProblem<Flt64>, Dictionary<string, (Flt64 Start, Flt64 Mode)>>(baselines);
        }

        Result<List<CompiledActivityChoice<Flt64>>, ErrorCode, Error<ErrorCode>> choices =
            CompileChoices(problem, resources, okCapacities.Value, okMaterials.Value, okBaselines.Value);
        if (choices is not Ok<List<CompiledActivityChoice<Flt64>>, ErrorCode, Error<ErrorCode>> okChoices) {
            return Propagate<CompiledActivityProblem<Flt64>, List<CompiledActivityChoice<Flt64>>>(choices);
        }

        var data = new CompiledActivityProblem<Flt64>(
            problem, okChoices.Value, okCapacities.Value, fixedReservations,
            Array.Empty<CompiledActivityRow>(), okMaterials.Value, okEvents.Value);

        foreach (ActivityBaseline<Flt64> baseline in problem.BaselinesValue) {
            if (baseline.Frozen && !data.Indices.ContainsKey(baseline.Choice)) {
                return FailOf<CompiledActivityProblem<Flt64>>("a frozen baseline lies outside the candidate domain");
            }
        }

        foreach (ActivityDependency dependency in problem.DependenciesValue) {
            bool knownPredecessor = tasks.ContainsKey(dependency.Predecessor) || facts.ContainsKey(dependency.Predecessor);
            bool knownSuccessor = tasks.ContainsKey(dependency.Successor) || facts.ContainsKey(dependency.Successor);
            if (!knownPredecessor || !knownSuccessor) {
                return FailOf<CompiledActivityProblem<Flt64>>("invalid dependency references or lags");
            }
            if (dependency.Maximum is { } maximum && maximum < dependency.MinimumLag) {
                return FailOf<CompiledActivityProblem<Flt64>>("invalid dependency references or lags");
            }
        }

        Result<List<CompiledActivityRow>, ErrorCode, Error<ErrorCode>> rows = CompileRows(problem, data);
        if (rows is not Ok<List<CompiledActivityRow>, ErrorCode, Error<ErrorCode>> okRows) {
            return Propagate<CompiledActivityProblem<Flt64>, List<CompiledActivityRow>>(rows);
        }
        if (okRows.Value.Count == 0) {
            return Results.Ok(data);
        }
        return Results.Ok(new CompiledActivityProblem<Flt64>(
            problem, data.Choices, data.Resources, data.FixedReservations, okRows.Value,
            data.Materials, data.MaterialEvents));
    }

    private static Result<Dictionary<string, CompiledActivityMaterial<Flt64>>, ErrorCode, Error<ErrorCode>> CompileMaterials(
        ActivitySchedulingProblem<Flt64> problem) {
        var materials = new Dictionary<string, CompiledActivityMaterial<Flt64>>(StringComparer.Ordinal);
        foreach (ActivityMaterial<Flt64> material in problem.MaterialsValue) {
            if (string.IsNullOrWhiteSpace(material.Id) || materials.ContainsKey(material.Id)) {
                return FailOf<Dictionary<string, CompiledActivityMaterial<Flt64>>>(
                    "blank or duplicate material identity");
            }
            Result<Flt64, ErrorCode, Error<ErrorCode>> initial =
                ActivityValueBoundary.Value(material.Initial, material.Initial.Unit, "material " + material.Id);
            if (initial is not Ok<Flt64, ErrorCode, Error<ErrorCode>> okInitial) {
                return Propagate<Dictionary<string, CompiledActivityMaterial<Flt64>>, Flt64>(initial);
            }
            var minimum = new Flt64(0.0);
            if (material.Minimum is { } given) {
                Result<Flt64, ErrorCode, Error<ErrorCode>> converted =
                    ActivityValueBoundary.Value(given, material.Initial.Unit, "material " + material.Id);
                if (converted is not Ok<Flt64, ErrorCode, Error<ErrorCode>> okMinimum) {
                    return Propagate<Dictionary<string, CompiledActivityMaterial<Flt64>>, Flt64>(converted);
                }
                minimum = okMinimum.Value;
            }
            Flt64? maximum = null;
            if (material.Maximum is { } givenMaximum) {
                Result<Flt64, ErrorCode, Error<ErrorCode>> converted =
                    ActivityValueBoundary.Value(givenMaximum, material.Initial.Unit, "material " + material.Id);
                if (converted is not Ok<Flt64, ErrorCode, Error<ErrorCode>> okMaximum) {
                    return Propagate<Dictionary<string, CompiledActivityMaterial<Flt64>>, Flt64>(converted);
                }
                maximum = okMaximum.Value;
            }

            if (minimum.ToDouble() < 0.0 || (maximum is { } high && high.ToDouble() < minimum.ToDouble())) {
                return FailOf<Dictionary<string, CompiledActivityMaterial<Flt64>>>(
                    "invalid stock bounds; implicit negative inventory is not allowed");
            }
            materials[material.Id] = new CompiledActivityMaterial<Flt64>(material, okInitial.Value, minimum, maximum);
        }
        return Results.Ok(materials);
    }

    private static Result<List<CompiledActivityMaterialEvent>, ErrorCode, Error<ErrorCode>> CompileMaterialEvents(
        ActivitySchedulingProblem<Flt64> problem,
        IReadOnlyDictionary<string, CompiledActivityMaterial<Flt64>> materials) {
        var events = new List<CompiledActivityMaterialEvent>();
        foreach (ActivityMaterialEvent<Flt64> materialEvent in problem.MaterialEventsValue) {
            if (!materials.TryGetValue(materialEvent.Material, out CompiledActivityMaterial<Flt64>? material)) {
                return FailOf<List<CompiledActivityMaterialEvent>>(
                    "a fixed event references an unknown material");
            }
            if (materialEvent.Time < problem.Horizon.Start || materialEvent.Time > problem.Horizon.End) {
                return FailOf<List<CompiledActivityMaterialEvent>>(
                    "a fixed material event lies outside the horizon");
            }
            Result<Flt64, ErrorCode, Error<ErrorCode>> quantity = ActivityValueBoundary.Value(
                materialEvent.Quantity, material.Source.Initial.Unit, "material event " + materialEvent.Material);
            if (quantity is not Ok<Flt64, ErrorCode, Error<ErrorCode>> okQuantity) {
                return Propagate<List<CompiledActivityMaterialEvent>, Flt64>(quantity);
            }
            events.Add(new CompiledActivityMaterialEvent(materialEvent.Material, materialEvent.Time, okQuantity.Value));
        }
        return Results.Ok(events);
    }

    private static Result<Dictionary<string, CompiledActivityResource<Flt64>>, ErrorCode, Error<ErrorCode>> CompileResources(
        ActivitySchedulingProblem<Flt64> problem) {
        var normalized = new Dictionary<string, CompiledActivityResource<Flt64>>(StringComparer.Ordinal);
        foreach (ActivityResource<Flt64> resource in problem.Resources) {
            Result<Flt64, ErrorCode, Error<ErrorCode>> capacity = ActivityValueBoundary.Value(
                resource.Capacity, resource.Capacity.Unit, "capacity " + resource.Id);
            if (capacity is not Ok<Flt64, ErrorCode, Error<ErrorCode>> okCapacity) {
                return Propagate<Dictionary<string, CompiledActivityResource<Flt64>>, Flt64>(capacity);
            }
            if (okCapacity.Value.ToDouble() < 0.0) {
                return FailOf<Dictionary<string, CompiledActivityResource<Flt64>>>(
                    "resource capacity cannot be negative");
            }
            foreach (TimeRange unavailable in resource.UnavailableTimesValue) {
                if (unavailable.End <= unavailable.Start) {
                    return FailOf<Dictionary<string, CompiledActivityResource<Flt64>>>(
                        "invalid resource calendar interval");
                }
            }

            var ordered = new List<ActivityCapacity<Flt64>>(resource.CapacitiesValue);
            ordered.Sort((lhs, rhs) => lhs.Time.Start.CompareTo(rhs.Time.Start));
            for (int index = 1; index < ordered.Count; index++) {
                if (ordered[index - 1].Time.End > ordered[index].Time.Start) {
                    return FailOf<Dictionary<string, CompiledActivityResource<Flt64>>>(
                        "capacity overrides cannot overlap");
                }
            }

            var capacities = new List<KeyValuePair<TimeRange, Flt64>>(ordered.Count);
            foreach (ActivityCapacity<Flt64> over in ordered) {
                if (over.Time.End <= over.Time.Start) {
                    return FailOf<Dictionary<string, CompiledActivityResource<Flt64>>>(
                        "invalid resource calendar interval");
                }
                Result<Flt64, ErrorCode, Error<ErrorCode>> amount = ActivityValueBoundary.Value(
                    over.Capacity, resource.Capacity.Unit, "capacity override " + resource.Id);
                if (amount is not Ok<Flt64, ErrorCode, Error<ErrorCode>> okAmount) {
                    return Propagate<Dictionary<string, CompiledActivityResource<Flt64>>, Flt64>(amount);
                }
                if (okAmount.Value.ToDouble() < 0.0) {
                    return FailOf<Dictionary<string, CompiledActivityResource<Flt64>>>(
                        "a capacity override cannot be negative");
                }
                capacities.Add(new KeyValuePair<TimeRange, Flt64>(over.Time, okAmount.Value));
            }
            normalized[resource.Id] = new CompiledActivityResource<Flt64>(resource, okCapacity.Value, capacities);
        }
        return Results.Ok(normalized);
    }

    private static Result<List<CompiledActivityReservation>, ErrorCode, Error<ErrorCode>> NormalizeReservations(
        ActivityPlacement<Flt64> placement,
        IReadOnlyDictionary<string, ActivityResource<Flt64>> resources,
        IReadOnlyDictionary<string, CompiledActivityResource<Flt64>> capacities) {
        if (placement.Time.End <= placement.Time.Start
            || placement.Key.Start != placement.Time.Start
            || string.IsNullOrWhiteSpace(placement.Key.Mode)) {
            return FailOf<List<CompiledActivityReservation>>("invalid placement time or mode");
        }

        var normalized = new List<CompiledActivityReservation>(placement.Reservations.Count);
        foreach (ActivityReservation<Flt64> reservation in placement.Reservations) {
            if (!resources.TryGetValue(reservation.Resource, out ActivityResource<Flt64>? resource)) {
                return FailOf<List<CompiledActivityReservation>>(
                    "a reservation references an unknown resource");
            }
            if (reservation.Time.End <= reservation.Time.Start
                || reservation.Time.Start < placement.Time.Start
                || reservation.Time.End > placement.Time.End) {
                return FailOf<List<CompiledActivityReservation>>(
                    "a reservation must lie within the activity interval");
            }
            Result<Flt64, ErrorCode, Error<ErrorCode>> amount = ActivityValueBoundary.Value(
                reservation.Quantity, resource.Capacity.Unit, "reservation " + reservation.Resource);
            if (amount is not Ok<Flt64, ErrorCode, Error<ErrorCode>> okAmount) {
                return Propagate<List<CompiledActivityReservation>, Flt64>(amount);
            }
            if (okAmount.Value.ToDouble() < 0.0) {
                return FailOf<List<CompiledActivityReservation>>("a reservation cannot be negative");
            }
            normalized.Add(new CompiledActivityReservation(reservation.Resource, reservation.Time, okAmount.Value));
        }
        _ = capacities;
        return Results.Ok(normalized);
    }

    private static Result<Dictionary<string, (Flt64 Start, Flt64 Mode)>, ErrorCode, Error<ErrorCode>> CompileBaselines(
        ActivitySchedulingProblem<Flt64> problem,
        IReadOnlyDictionary<string, Activity<Flt64>> tasks) {
        var baselines = new Dictionary<string, ActivityBaseline<Flt64>>(StringComparer.Ordinal);
        foreach (ActivityBaseline<Flt64> baseline in problem.BaselinesValue) {
            if (!baselines.TryAdd(baseline.Choice.Task, baseline)) {
                return FailOf<Dictionary<string, (Flt64 Start, Flt64 Mode)>>(
                    "a duplicate baseline or a baseline referencing a non-pending task");
            }
        }
        foreach (string task in baselines.Keys) {
            if (!tasks.ContainsKey(task)) {
                return FailOf<Dictionary<string, (Flt64 Start, Flt64 Mode)>>(
                    "a duplicate baseline or a baseline referencing a non-pending task");
            }
        }

        var costs = new Dictionary<string, (Flt64 Start, Flt64 Mode)>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, ActivityBaseline<Flt64>> entry in baselines) {
            ActivityBaseline<Flt64> baseline = entry.Value;
            if (string.IsNullOrWhiteSpace(baseline.Choice.Mode)) {
                return FailOf<Dictionary<string, (Flt64 Start, Flt64 Mode)>>("invalid baseline identity or time");
            }
            Result<Flt64, ErrorCode, Error<ErrorCode>> start = ActivityValueBoundary.Value(
                baseline.StartChangeCost, problem.CostUnit, "baseline cost");
            if (start is not Ok<Flt64, ErrorCode, Error<ErrorCode>> okStart) {
                return Propagate<Dictionary<string, (Flt64 Start, Flt64 Mode)>, Flt64>(start);
            }
            Result<Flt64, ErrorCode, Error<ErrorCode>> mode = ActivityValueBoundary.Value(
                baseline.ModeChangeCost, problem.CostUnit, "baseline cost");
            if (mode is not Ok<Flt64, ErrorCode, Error<ErrorCode>> okMode) {
                return Propagate<Dictionary<string, (Flt64 Start, Flt64 Mode)>, Flt64>(mode);
            }
            if (okStart.Value.ToDouble() < 0.0 || okMode.Value.ToDouble() < 0.0) {
                return FailOf<Dictionary<string, (Flt64 Start, Flt64 Mode)>>(
                    "baseline change costs cannot be negative");
            }
            costs[entry.Key] = (okStart.Value, okMode.Value);
        }
        return Results.Ok(costs);
    }

    private static Result<List<CompiledActivityChoice<Flt64>>, ErrorCode, Error<ErrorCode>> CompileChoices(
        ActivitySchedulingProblem<Flt64> problem,
        IReadOnlyDictionary<string, ActivityResource<Flt64>> resources,
        IReadOnlyDictionary<string, CompiledActivityResource<Flt64>> capacities,
        IReadOnlyDictionary<string, CompiledActivityMaterial<Flt64>> materials,
        IReadOnlyDictionary<string, (Flt64 Start, Flt64 Mode)> baselineCosts) {
        var baselines = new Dictionary<string, ActivityBaseline<Flt64>>(StringComparer.Ordinal);
        foreach (ActivityBaseline<Flt64> baseline in problem.BaselinesValue) {
            baselines[baseline.Choice.Task] = baseline;
        }

        var choices = new List<CompiledActivityChoice<Flt64>>();
        foreach (Activity<Flt64> task in problem.Activities) {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (ActivityMode<Flt64> mode in task.Modes) {
                if (!seen.Add(mode.Id)) {
                    return FailOf<List<CompiledActivityChoice<Flt64>>>(
                        "duplicate mode identity within an activity");
                }
            }

            foreach (ActivityMode<Flt64> mode in task.Modes) {
                if (string.IsNullOrWhiteSpace(mode.Id) || mode.Duration < TimeSpan.Zero) {
                    return FailOf<List<CompiledActivityChoice<Flt64>>>(
                        "invalid mode duration, starts or resource phases");
                }
                var starts = new HashSet<DateTimeOffset>();
                foreach (DateTimeOffset start in mode.Starts) {
                    if (!starts.Add(start)) {
                        return FailOf<List<CompiledActivityChoice<Flt64>>>(
                            "invalid mode duration, starts or resource phases");
                    }
                }
                foreach (ActivityResourceUse<Flt64> use in mode.Resources) {
                    TimeSpan end = use.Until ?? mode.Duration;
                    if (use.FromValue < TimeSpan.Zero || end < use.FromValue || end > mode.Duration) {
                        return FailOf<List<CompiledActivityChoice<Flt64>>>(
                            "invalid mode duration, starts or resource phases");
                    }
                }

                Result<Flt64, ErrorCode, Error<ErrorCode>> modeCost = ActivityValueBoundary.Value(
                    mode.Cost, problem.CostUnit, "mode cost " + mode.Id);
                if (modeCost is not Ok<Flt64, ErrorCode, Error<ErrorCode>> okModeCost) {
                    return Propagate<List<CompiledActivityChoice<Flt64>>, Flt64>(modeCost);
                }

                foreach (ActivityResourceUse<Flt64> use in mode.Resources) {
                    if (!resources.TryGetValue(use.Resource, out ActivityResource<Flt64>? resource)) {
                        return FailOf<List<CompiledActivityChoice<Flt64>>>(
                            "a mode references an unknown resource");
                    }
                    Result<Flt64, ErrorCode, Error<ErrorCode>> amount = ActivityValueBoundary.Value(
                        use.Quantity, resource.Capacity.Unit, "mode reservation");
                    if (amount is not Ok<Flt64, ErrorCode, Error<ErrorCode>> okAmount) {
                        return Propagate<List<CompiledActivityChoice<Flt64>>, Flt64>(amount);
                    }
                    if (okAmount.Value.ToDouble() < 0.0) {
                        return FailOf<List<CompiledActivityChoice<Flt64>>>("usage cannot be negative");
                    }
                }

                var flows = new List<(ActivityMaterialFlow<Flt64> Flow, Flt64 Quantity)>();
                foreach (ActivityMaterialFlow<Flt64> flow in mode.MaterialFlowsValue) {
                    if (!materials.TryGetValue(flow.Material, out CompiledActivityMaterial<Flt64>? material)) {
                        return FailOf<List<CompiledActivityChoice<Flt64>>>(
                            "a mode references an unknown material");
                    }
                    if (flow.Offset < TimeSpan.Zero || flow.Offset > mode.Duration) {
                        return FailOf<List<CompiledActivityChoice<Flt64>>>(
                            "a material event must lie within the mode duration");
                    }
                    Result<Flt64, ErrorCode, Error<ErrorCode>> amount = ActivityValueBoundary.Value(
                        flow.Quantity, material.Source.Initial.Unit, "mode material " + flow.Material);
                    if (amount is not Ok<Flt64, ErrorCode, Error<ErrorCode>> okAmount) {
                        return Propagate<List<CompiledActivityChoice<Flt64>>, Flt64>(amount);
                    }
                    flows.Add((flow, okAmount.Value));
                }

                foreach (DateTimeOffset start in mode.Starts) {
                    if (start < problem.Horizon.Start || start > problem.Horizon.End
                        || mode.Duration > problem.Horizon.End - start) {
                        return FailOf<List<CompiledActivityChoice<Flt64>>>(
                            "a candidate lies outside the declared horizon");
                    }
                    var reservations = new List<ActivityReservation<Flt64>>(mode.Resources.Count);
                    foreach (ActivityResourceUse<Flt64> use in mode.Resources) {
                        reservations.Add(new ActivityReservation<Flt64>(
                            use.Resource,
                            new TimeRange(start + use.FromValue, start + (use.Until ?? mode.Duration)),
                            use.Quantity));
                    }
                    var placement = new ActivityPlacement<Flt64>(
                        new ActivityChoiceKey(task.Id, mode.Id, start),
                        new TimeRange(start, start + mode.Duration),
                        reservations);

                    Result<List<CompiledActivityReservation>, ErrorCode, Error<ErrorCode>> normalized =
                        NormalizeReservations(placement, resources, capacities);
                    if (normalized is not Ok<List<CompiledActivityReservation>, ErrorCode, Error<ErrorCode>> okNormalized) {
                        return Propagate<List<CompiledActivityChoice<Flt64>>, List<CompiledActivityReservation>>(normalized);
                    }

                    Flt64 cost = okModeCost.Value;
                    if (baselines.TryGetValue(task.Id, out ActivityBaseline<Flt64>? baseline)) {
                        (Flt64 Start, Flt64 Mode) change = baselineCosts[task.Id];
                        if (baseline.Choice.Start != start) {
                            cost = cost + change.Start;
                        }
                        if (!string.Equals(baseline.Choice.Mode, mode.Id, StringComparison.Ordinal)) {
                            cost = cost + change.Mode;
                        }
                    }

                    if (problem.AdditionalCost is { } additional) {
                        Result<Quantity<Flt64>, ErrorCode, Error<ErrorCode>> extra = additional(placement);
                        if (extra is not Ok<Quantity<Flt64>, ErrorCode, Error<ErrorCode>> okExtra) {
                            return Propagate<List<CompiledActivityChoice<Flt64>>, Quantity<Flt64>>(extra);
                        }
                        Result<Flt64, ErrorCode, Error<ErrorCode>> amount = ActivityValueBoundary.Value(
                            okExtra.Value, problem.CostUnit, "additional cost");
                        if (amount is not Ok<Flt64, ErrorCode, Error<ErrorCode>> okAmount) {
                            return Propagate<List<CompiledActivityChoice<Flt64>>, Flt64>(amount);
                        }
                        cost = cost + okAmount.Value;
                    }
                    if (!double.IsFinite(cost.ToDouble())) {
                        return FailOf<List<CompiledActivityChoice<Flt64>>>("candidate cost overflow");
                    }

                    var materialEvents = new List<CompiledActivityMaterialEvent>(flows.Count);
                    foreach ((ActivityMaterialFlow<Flt64> flow, Flt64 quantity) in flows) {
                        materialEvents.Add(new CompiledActivityMaterialEvent(
                            flow.Material, start + flow.Offset, quantity));
                    }
                    choices.Add(new CompiledActivityChoice<Flt64>(
                        task.Id, placement, okNormalized.Value, cost, materialEvents));
                }
            }

            if (task.CancellationCost is { } cancellation) {
                Result<Flt64, ErrorCode, Error<ErrorCode>> cost = ActivityValueBoundary.Value(
                    cancellation, problem.CostUnit, "cancellation");
                if (cost is not Ok<Flt64, ErrorCode, Error<ErrorCode>> okCost) {
                    return Propagate<List<CompiledActivityChoice<Flt64>>, Flt64>(cost);
                }
                if (okCost.Value.ToDouble() < 0.0) {
                    return FailOf<List<CompiledActivityChoice<Flt64>>>("the cancellation cost cannot be negative");
                }
                choices.Add(new CompiledActivityChoice<Flt64>(
                    task.Id, null, Array.Empty<CompiledActivityReservation>(), okCost.Value));
            }
        }
        return Results.Ok(choices);
    }

    private static Result<List<CompiledActivityRow>, ErrorCode, Error<ErrorCode>> CompileRows(
        ActivitySchedulingProblem<Flt64> problem,
        CompiledActivityProblem<Flt64> data) {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (ActivityRow<Flt64> row in problem.RowsValue) {
            if (!seen.Add(row.Id)) {
                return FailOf<List<CompiledActivityRow>>("duplicate extension row identity");
            }
        }

        var rows = new List<CompiledActivityRow>(problem.RowsValue.Count);
        foreach (ActivityRow<Flt64> row in problem.RowsValue) {
            if (string.IsNullOrWhiteSpace(row.Id) || (row.Lower is null && row.Upper is null)) {
                return FailOf<List<CompiledActivityRow>>(
                    "invalid extension row identity, references or bounds");
            }
            var coefficients = new Dictionary<int, Flt64>();
            foreach (KeyValuePair<ActivityChoiceKey, Quantity<Flt64>> coefficient in row.CoefficientsValue) {
                if (!data.Indices.TryGetValue(coefficient.Key, out int index)) {
                    return FailOf<List<CompiledActivityRow>>(
                        "invalid extension row identity, references or bounds");
                }
                Result<Flt64, ErrorCode, Error<ErrorCode>> amount =
                    ActivityValueBoundary.Value(coefficient.Value, row.Unit, "row " + row.Id);
                if (amount is not Ok<Flt64, ErrorCode, Error<ErrorCode>> okAmount) {
                    return Propagate<List<CompiledActivityRow>, Flt64>(amount);
                }
                coefficients[index] = okAmount.Value;
            }
            foreach (KeyValuePair<string, Quantity<Flt64>> coefficient in row.CancellationCoefficientsValue) {
                if (!data.Cancellations.TryGetValue(coefficient.Key, out int index)) {
                    return FailOf<List<CompiledActivityRow>>(
                        "invalid extension row identity, references or bounds");
                }
                Result<Flt64, ErrorCode, Error<ErrorCode>> amount =
                    ActivityValueBoundary.Value(coefficient.Value, row.Unit, "row " + row.Id);
                if (amount is not Ok<Flt64, ErrorCode, Error<ErrorCode>> okAmount) {
                    return Propagate<List<CompiledActivityRow>, Flt64>(amount);
                }
                coefficients[index] = coefficients.TryGetValue(index, out Flt64 known)
                    ? known + okAmount.Value
                    : okAmount.Value;
            }

            Flt64? lower = null;
            if (row.Lower is { } givenLower) {
                Result<Flt64, ErrorCode, Error<ErrorCode>> converted =
                    ActivityValueBoundary.Value(givenLower, row.Unit, "row " + row.Id);
                if (converted is not Ok<Flt64, ErrorCode, Error<ErrorCode>> okLower) {
                    return Propagate<List<CompiledActivityRow>, Flt64>(converted);
                }
                lower = okLower.Value;
            }
            Flt64? upper = null;
            if (row.Upper is { } givenUpper) {
                Result<Flt64, ErrorCode, Error<ErrorCode>> converted =
                    ActivityValueBoundary.Value(givenUpper, row.Unit, "row " + row.Id);
                if (converted is not Ok<Flt64, ErrorCode, Error<ErrorCode>> okUpper) {
                    return Propagate<List<CompiledActivityRow>, Flt64>(converted);
                }
                upper = okUpper.Value;
            }
            rows.Add(new CompiledActivityRow(row.Id, coefficients, lower, upper));
        }
        return Results.Ok(rows);
    }

    private static Result<T, ErrorCode, Error<ErrorCode>> Fail<T>(string message) =>
        Results.Failed<T>(new Err<ErrorCode>(ErrorCode.IllegalArgument, message));

    private static Result<T, ErrorCode, Error<ErrorCode>> FailOf<T>(string message) => Fail<T>(message);

    private static Result<T, ErrorCode, Error<ErrorCode>> Propagate<T, U>(
        Result<U, ErrorCode, Error<ErrorCode>> source) =>
        source is Failed<U, ErrorCode, Error<ErrorCode>> failed
            ? Results.Failed<T>(failed.Error)
            : new Fatal<T, ErrorCode, Error<ErrorCode>>(
                ((Fatal<U, ErrorCode, Error<ErrorCode>>)source).Errors);
}
