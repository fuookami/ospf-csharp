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
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo9;
/// <summary>
/// 远程求解器演示：构建可序列化模型以供远程求解。
/// Remote solver demo: build a serializable model for remote solving.
///
/// 此演示展示如何构建一个可以导出并发送到远程求解服务的模型。
/// This demo shows how to build a model that can be exported and sent to a remote solving service.
/// </summary>
public sealed class RemoteSolverDemo {
    private readonly List<BinVar> _x = new();
    private LinearExpressionSymbol? _objective;
    private readonly LinearMetaModel<Flt64> _metaModel = new("demo9-remote-solver", ObjectCategory.Maximum);

    public LinearMetaModel<Flt64> MetaModel => _metaModel;
    /// <summary>上次导出的 JSON 字符串 / Last exported JSON string.</summary>
    public string? LastExportedJson { get; private set; }
    /// <summary>上次导入的 JSON 字符串 / Last imported JSON string.</summary>
    public string? LastImportedJson { get; private set; }

    /// <summary>
    /// 构建远程求解模型（简单的资源分配问题）。
    /// Build the remote solver model (simple resource allocation problem).
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
        // 5 binary decision variables
        for (int i = 0; i < 5; i++) {
            var x = new BinVar($"x_{i}");
            _x.Add(x);
            Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.Add(x);
            if (result.IsFailed) {
                return result;
            }
        }
        return Results.Ok(Results.SuccessInstance);
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitObjective() {
        // maximize sum of weighted variables
        double[] profits = new[] { 10.0, 15.0, 8.0, 12.0, 20.0 };
        var objPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
        for (int i = 0; i < _x.Count; i++) {
            objPoly.AddMonomial(new LinearMonomial<Flt64>(new Flt64(profits[i]), _x[i]));
        }
        _objective = new LinearExpressionSymbol(objPoly, name: "profit");
        _metaModel.Add(_objective);

        return _metaModel.AddObject(
            ObjectCategory.Maximum,
            _objective.Polynomial,
            "profit",
            "Total Profit");
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitConstraints() {
        // Resource constraints: sum of weighted selections <= capacity
        double[] weights = new[] { 3.0, 5.0, 2.0, 4.0, 6.0 };
        double capacity = 10.0;

        var constraintPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
        for (int i = 0; i < _x.Count; i++) {
            constraintPoly.AddMonomial(new LinearMonomial<Flt64>(new Flt64(weights[i]), _x[i]));
        }
        LinearInequality<Flt64> constraint = constraintPoly.ToLinearPolynomial().Le(new Flt64(capacity));
        return _metaModel.AddConstraint(constraint, group: null, name: "capacity");
    }

    /// <summary>
    /// 模拟远程导出：将模型序列化为 JSON 字符串。
    /// Simulate remote export: serialize the model to a JSON string.
    ///
    /// In a real implementation this would:
    ///   1. Export the meta-model to a serializable format (JSON/protobuf).
    ///   2. Send to a remote solving service via HTTP/gRPC.
    ///   3. Receive and parse the solution.
    ///
    /// This method demonstrates the export step by serializing model metadata to JSON.
    /// </summary>
    /// <returns>构建结果 / Build result.</returns>
    public Result<Success, ErrorCode, Error<ErrorCode>> SimulateRemoteExport() {
        if (_metaModel.MetaSubObjects.Count == 0) {
            return Results.Failed<Success>(
                new Err<ErrorCode>(ErrorCode.ApplicationError, "Model has no objectives"));
        }

        var exportDto = new ModelExportDto {
            ModelName = _metaModel.Name ?? "unknown",
            ObjectCategory = _metaModel.ObjectCategory.ToString(),
            VariableCount = _x.Count,
            ConstraintCount = _metaModel.RelationConstraints.Count,
            ObjectiveCount = _metaModel.MetaSubObjects.Count,
            Variables = _x.Select((v, i) => new VariableDto {
                Name = v.Name ?? $"x_{i}",
                Type = "Binary"
            }).ToList(),
            Profits = new[] { 10.0, 15.0, 8.0, 12.0, 20.0 },
            Weights = new[] { 3.0, 5.0, 2.0, 4.0, 6.0 },
            Capacity = 10.0
        };

        string json = JsonSerializer.Serialize(exportDto, new JsonSerializerOptions {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        LastExportedJson = json;
        return Results.Ok(Results.SuccessInstance);
    }

    /// <summary>
    /// 模拟远程导入：从远程服务接收求解结果（模拟）。
    /// Simulate remote import: receive solving result from a remote service (mock).
    ///
    /// In a real implementation this would:
    ///   1. Send the serialized model to a remote solver.
    ///   2. Receive the solution response.
    ///   3. Parse and apply the solution to the local model.
    ///
    /// This method creates a mock solution for demonstration purposes.
    /// </summary>
    /// <returns>构建结果 / Build result.</returns>
    public Result<Success, ErrorCode, Error<ErrorCode>> SimulateRemoteImport() {
        // Mock solution: assume the remote solver selected items 0, 3 (profit=22, weight=7 <= 10)
        var solutionDto = new SolutionDto {
            Status = "Optimal",
            ObjectiveValue = 22.0,
            SelectedItems = new[] { 0, 3 },
            VariableValues = new Dictionary<string, double>
            {
                { "x_0", 1.0 },
                { "x_1", 0.0 },
                { "x_2", 0.0 },
                { "x_3", 1.0 },
                { "x_4", 0.0 }
            }
        };

        string json = JsonSerializer.Serialize(solutionDto, new JsonSerializerOptions {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        LastImportedJson = json;
        return Results.Ok(Results.SuccessInstance);
    }
}

/// <summary>
/// 模型导出 DTO。Model export data transfer object.
/// </summary>
public sealed class ModelExportDto {
    public string ModelName { get; set; } = "";
    public string ObjectCategory { get; set; } = "";
    public int VariableCount { get; set; }
    public int ConstraintCount { get; set; }
    public int ObjectiveCount { get; set; }
    public List<VariableDto> Variables { get; set; } = new();
    public double[] Profits { get; set; } = Array.Empty<double>();
    public double[] Weights { get; set; } = Array.Empty<double>();
    public double Capacity { get; set; }
}

/// <summary>
/// 变量 DTO。Variable data transfer object.
/// </summary>
public sealed class VariableDto {
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
}

/// <summary>
/// 求解结果 DTO。Solution data transfer object.
/// </summary>
public sealed class SolutionDto {
    public string Status { get; set; } = "";
    public double ObjectiveValue { get; set; }
    public int[] SelectedItems { get; set; } = Array.Empty<int>();
    public Dictionary<string, double> VariableValues { get; set; } = new();
}
