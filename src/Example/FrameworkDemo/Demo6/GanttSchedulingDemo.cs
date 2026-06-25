#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo6;
/// <summary>
/// 调度任务：具有处理时间和截止日期。Scheduling task with processing time and deadline.
/// </summary>
public sealed class ScheduleTask {
    public int Index { get; }
    public string Name { get; }
    public int ProcessingTime { get; }
    public int Deadline { get; }

    public ScheduleTask(int index, string name, int processingTime, int deadline) {
        Index = index;
        Name = name;
        ProcessingTime = processingTime;
        Deadline = deadline;
    }
}

/// <summary>
/// 甘特调度演示：单机调度问题。
/// Gantt scheduling demo: single-machine scheduling problem.
///
/// 甘特调度框架尚未在 C# 端实现，此演示使用核心建模 API 展示调度建模模式。
/// The Gantt scheduling framework is not yet implemented on the C# side;
/// this demo uses the core modeling API to show scheduling modeling patterns.
/// </summary>
public sealed class GanttSchedulingDemo {
    private static readonly List<ScheduleTask> Tasks = new()
    {
        new ScheduleTask(0, "Task-A", 3, 10),
        new ScheduleTask(1, "Task-B", 2, 8),
        new ScheduleTask(2, "Task-C", 4, 15),
        new ScheduleTask(3, "Task-D", 1, 6),
        new ScheduleTask(4, "Task-E", 5, 20)
    };

    private readonly List<List<BinVar>> _x = new();
    private readonly List<UIntVar> _start = new();
    private UIntVar? _makespanVar;
    private LinearExpressionSymbol? _makespan;
    private readonly LinearMetaModel<Flt64> _metaModel = new("demo6-gantt-scheduling", ObjectCategory.Minimum);

    public LinearMetaModel<Flt64> MetaModel => _metaModel;
    public IReadOnlyList<ScheduleTask> ScheduleTasks => Tasks;

    /// <summary>
    /// 构建调度模型（最小化 makespan）。
    /// Build the scheduling model (minimize makespan).
    /// </summary>
    public Result<Success, ErrorCode, Error<ErrorCode>> BuildModel() {
        Result<Success, ErrorCode, Error<ErrorCode>> r1 = InitVariables();
        if (r1.IsFailed) {
            return r1;
        }

        Result<Success, ErrorCode, Error<ErrorCode>> r2 = InitObjective();
        if (r2.IsFailed) {
            return r2;
        }

        Result<Success, ErrorCode, Error<ErrorCode>> r3 = InitConstraints();
        if (r3.IsFailed) {
            return r3;
        }

        return Results.Ok(Results.SuccessInstance);
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitVariables() {
        // start[i] = start time of task i
        foreach (ScheduleTask task in Tasks) {
            var s = new UIntVar($"start_{task.Index}");
            _start.Add(s);
            Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.Add(s);
            if (result.IsFailed) {
                return result;
            }
        }

        // makespan = max over all tasks of (start[i] + processingTime[i])
        _makespanVar = new UIntVar("makespan");
        Result<Success, ErrorCode, Error<ErrorCode>> makespanResult = _metaModel.Add(_makespanVar);
        if (makespanResult.IsFailed) {
            return makespanResult;
        }

        // x[i,j] = 1 if task i precedes task j
        for (int i = 0; i < Tasks.Count; i++) {
            var row = new List<BinVar>();
            for (int j = 0; j < Tasks.Count; j++) {
                var x = new BinVar($"x_{i}_{j}");
                row.Add(x);
                if (i != j) {
                    Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.Add(x);
                    if (result.IsFailed) {
                        return result;
                    }
                }
            }
            _x.Add(row);
        }

        return Results.Ok(Results.SuccessInstance);
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitObjective() {
        // minimize makespan = max over all tasks of (start[i] + processingTime[i])
        // The makespan variable is bounded below by each task's completion time
        // via the completion constraints added in InitConstraints().
        var objPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
        objPoly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, _makespanVar!));
        _makespan = new LinearExpressionSymbol(objPoly, name: "makespan");
        _metaModel.Add(_makespan);

        return _metaModel.AddObject(
            ObjectCategory.Minimum,
            objPoly.ToLinearPolynomial(),
            "makespan",
            "Makespan");
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitConstraints() {
        // Precedence: if x[i,j] = 1, then start[j] >= start[i] + processingTime[i]
        // Big-M: start[j] >= start[i] + p[i] - M * (1 - x[i,j])
        var bigM = new Flt64(1000.0);

        for (int i = 0; i < Tasks.Count; i++) {
            for (int j = 0; j < Tasks.Count; j++) {
                if (i == j) {
                    continue;
                }

                // start[j] - start[i] + M * x[i,j] >= p[i]
                // => start[j] - start[i] + M * x[i,j] - p[i] >= 0
                var poly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
                poly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, _start[j]));
                poly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One.Negate(), _start[i]));
                poly.AddMonomial(new LinearMonomial<Flt64>(bigM, _x[i][j]));
                poly.AddConstant(new Flt64(-Tasks[i].ProcessingTime));

                LinearInequality<Flt64> constraint = poly.ToLinearPolynomial().Ge(Flt64.Zero);
                Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.AddConstraint(constraint, group: null,
                    name: $"precedence_{i}_{j}");
                if (result.IsFailed) {
                    return result;
                }
            }
        }

        // Deadline constraints: start[i] + processingTime[i] <= deadline[i]
        foreach (ScheduleTask task in Tasks) {
            var poly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
            poly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, _start[task.Index]));
            poly.AddConstant(new Flt64(task.ProcessingTime));

            LinearInequality<Flt64> constraint = poly.ToLinearPolynomial().Le(new Flt64(task.Deadline));
            Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.AddConstraint(constraint, group: null,
                name: $"deadline_{task.Index}");
            if (result.IsFailed) {
                return result;
            }
        }

        // Completion constraints: makespan >= start[i] + processingTime[i] for each task i
        // => start[i] + processingTime[i] - makespan <= 0
        foreach (ScheduleTask task in Tasks) {
            var poly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
            poly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, _start[task.Index]));
            poly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One.Negate(), _makespanVar!));
            poly.AddConstant(new Flt64(task.ProcessingTime));

            LinearInequality<Flt64> constraint = poly.ToLinearPolynomial().Le(Flt64.Zero);
            Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.AddConstraint(constraint, group: null,
                name: $"completion_{task.Index}");
            if (result.IsFailed) {
                return result;
            }
        }

        return Results.Ok(Results.SuccessInstance);
    }
}
