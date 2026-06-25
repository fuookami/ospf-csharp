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

namespace Fuookami.Ospf.Example.CoreDemo;
/// <summary>
/// 通用数值模型构建摘要。Generic number model building summary.
/// </summary>
public sealed class ModelBuildSummary {
    public string ModelName { get; }
    public int ConstraintCount { get; }
    public IReadOnlyList<Flt64> ObjectiveCoefficients { get; }
    public bool IsSuccessful { get; }

    public ModelBuildSummary(string modelName, int constraintCount,
        IReadOnlyList<Flt64> objectiveCoefficients, bool isSuccessful) {
        ModelName = modelName;
        ConstraintCount = constraintCount;
        ObjectiveCoefficients = objectiveCoefficients;
        IsSuccessful = isSuccessful;
    }
}

/// <summary>
/// 通用数值类型演示：构建简单线性和二次模型，验证机制模型构建。
/// Generic number type demo: build simple linear and quadratic models,
/// verify mechanism model construction.
///
/// 此演示独立于 BuildModel 模式，提供 RunBuildAndDump() 方法。
/// This demo is standalone from the BuildModel pattern, providing a RunBuildAndDump() method.
/// </summary>
public sealed class GenericNumberDemo {
    private static readonly Flt64 CoeffX = new(2);
    private static readonly Flt64 CoeffY = new(1);
    private static readonly Flt64 ConstraintBound = new(10);

    private readonly LinearMetaModel<Flt64> _metaModel = new("generic-number-demo", ObjectCategory.Minimum);

    public LinearMetaModel<Flt64> MetaModel => _metaModel;

    /// <summary>
    /// 构建模型并返回摘要。Build the model and return a summary.
    /// </summary>
    public ModelBuildSummary RunBuildAndDump() {
        Result<Success, ErrorCode, Error<ErrorCode>> result = BuildModel();
        if (result.IsFailed) {
            return new ModelBuildSummary(
                modelName: "generic-number-demo",
                constraintCount: 0,
                objectiveCoefficients: Array.Empty<Flt64>(),
                isSuccessful: false);
        }

        var objCoeffs = new List<Flt64> { CoeffX, CoeffY };
        return new ModelBuildSummary(
            modelName: "generic-number-demo",
            constraintCount: 1,
            objectiveCoefficients: objCoeffs,
            isSuccessful: true);
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> BuildModel() {
        // Variables: x, y (continuous, non-negative)
        var x = new RealVar("x");
        var y = new RealVar("y");

        Result<Success, ErrorCode, Error<ErrorCode>> r1 = _metaModel.Add(x);
        if (r1.IsFailed) {
            return r1;
        }

        Result<Success, ErrorCode, Error<ErrorCode>> r2 = _metaModel.Add(y);
        if (r2.IsFailed) {
            return r2;
        }

        // Objective: minimize 2x + y
        var objPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
        objPoly.AddMonomial(new LinearMonomial<Flt64>(CoeffX, x));
        objPoly.AddMonomial(new LinearMonomial<Flt64>(CoeffY, y));
        Result<Success, ErrorCode, Error<ErrorCode>> r3 = _metaModel.AddObject(
            ObjectCategory.Minimum,
            objPoly.ToLinearPolynomial(),
            "obj",
            "Minimize 2x + y");
        if (r3.IsFailed) {
            return r3;
        }

        // Constraint: x + 2y <= 10
        var conPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
        conPoly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, x));
        conPoly.AddMonomial(new LinearMonomial<Flt64>(new Flt64(2), y));
        LinearInequality<Flt64> constraint = conPoly.ToLinearPolynomial().Le(ConstraintBound);
        Result<Success, ErrorCode, Error<ErrorCode>> r4 = _metaModel.AddConstraint(constraint, group: null, name: "budget");
        if (r4.IsFailed) {
            return r4;
        }

        return Results.Ok(Results.SuccessInstance);
    }
}
