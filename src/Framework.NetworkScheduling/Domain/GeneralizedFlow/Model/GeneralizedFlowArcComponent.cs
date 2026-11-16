// 广义流弧变量组件 / Generalized-flow arc variable component.
// 与固定 Kotlin （2697 字节）对应。
//
// `GeneralizedFlowPipelineFactory` 是 `fun interface`，在 C# 里直接由
// `Func<GeneralizedFlowExpressions, IPipeline<LinearMetaModel<Flt64>>>` 表达，不再单独声明一个只有单一方法的接口。
// `GeneralizedFlowPipelineFactory` is a `fun interface`; in C# it is expressed directly as
// `Func<GeneralizedFlowExpressions, IPipeline<LinearMetaModel<Flt64>>>` without declaring another single-method interface.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.GeneralizedFlow.Model;

/// <summary>
/// 已在 solver 数值边界完成单位、有限性和系数校验的弧配置 / Arc configuration validated at the solver numeric boundary.
/// </summary>
/// <remarks>
/// 所有系数都在这里**一次性**从物理量归一化成 `Flt64`：之后再进入模型装配就只处理数，不再做单位换算。这样做的理由是换算失败
/// 只有在这里才有足够上下文报出"哪条弧的哪个量不兼容"，到了约束装配阶段只剩一堆数字，出错也无从定位。
/// Every coefficient is normalised from a physical quantity into an `Flt64` **once, here**: model assembly then deals only with
/// numbers and never converts units again. The reason is that only at this point is there enough context to report "which quantity
/// of which arc is incompatible"; inside constraint assembly there is nothing but numbers left, and a failure is untraceable.
/// </remarks>
/// <typeparam name="V">领域数值类型 / Domain numeric type.</typeparam>
/// <param name="Arc">弧 / The arc.</param>
/// <param name="LowerInput">投入下界 / Lower input bound.</param>
/// <param name="UpperInput">投入上界 / Upper input bound.</param>
/// <param name="UnitConversionCoefficient">终点单位／起点单位换算系数 / Destination-per-origin conversion coefficient.</param>
/// <param name="OutputCoefficient">投入到达产出系数（换算 × 产率）/ Input-to-output coefficient (conversion × yield).</param>
/// <param name="CostCoefficient">每起点单位投入资费 / Tariff per origin-unit input.</param>
public sealed record PreparedGeneralizedFlowArc<V>(
    GeneralizedFlowArc<V> Arc,
    Flt64 LowerInput,
    Flt64 UpperInput,
    Flt64 UnitConversionCoefficient,
    Flt64 OutputCoefficient,
    Flt64 CostCoefficient) where V : struct, IRealNumber<V>;

/// <summary>
/// 弧变量和输入／输出线性表达式的模型组件 / Model component for arc variables and their input/output linear expressions.
/// </summary>
/// <typeparam name="V">领域数值类型 / Domain numeric type.</typeparam>
public sealed class GeneralizedFlowArcComponent<V> where V : struct, IRealNumber<V> {
    private readonly IReadOnlyList<PreparedGeneralizedFlowArc<V>> _prepared;
    private readonly string _name;
    private readonly Dictionary<NetworkArcId, URealVar> _variables = new();
    private readonly Dictionary<NetworkArcId, LinearPolynomial<Flt64>> _inputs = new();
    private readonly Dictionary<NetworkArcId, LinearPolynomial<Flt64>> _outputs = new();

    /// <summary>
    /// 构造弧变量组件 / Construct the arc variable component.
    /// </summary>
    /// <param name="prepared">已校验并归一化的弧数据 / Validated and normalised arc data.</param>
    /// <param name="name">变量命名空间 / Variable namespace.</param>
    public GeneralizedFlowArcComponent(IReadOnlyList<PreparedGeneralizedFlowArc<V>> prepared, string name) {
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(name);
        _prepared = prepared;
        _name = name;
    }

    /// <summary>弧投入变量 / Arc input variables.</summary>
    public IReadOnlyDictionary<NetworkArcId, URealVar> Variables => _variables;

    /// <summary>起点单位下的投入表达式 / Input expressions in origin units.</summary>
    public IReadOnlyDictionary<NetworkArcId, LinearPolynomial<Flt64>> Inputs => _inputs;

    /// <summary>终点单位下的到达产出表达式 / Arrival-output expressions in destination units.</summary>
    public IReadOnlyDictionary<NetworkArcId, LinearPolynomial<Flt64>> Outputs => _outputs;

    /// <summary>
    /// 注册弧投入变量并构建广义产出表达式 / Register arc-input variables and build generalized output expressions.
    /// </summary>
    /// <remarks>
    /// **产出表达式由投入乘以产出系数得到，不是独立变量**。把产出建成独立变量会多出一整组自由度：求解器可以让投入与产出各自取
    /// 任意值，节点平衡照样"满足"，而物理上那条弧根本不存在对应的投入产出关系。
    /// **The output expression is the input scaled by the output coefficient, not an independent variable.** Modelling the output as
    /// its own variable adds a whole set of extra degrees of freedom: the solver may give input and output arbitrary values, node
    /// balances still "hold", and physically that arc has no input-output relation at all.
    /// </remarks>
    /// <param name="model">元模型 / Meta model.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public Try Register(LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        foreach (PreparedGeneralizedFlowArc<V> item in _prepared) {
            NetworkArcId arcId = item.Arc.NetworkArc.Id;
            var variable = new URealVar($"{_name}_input_{arcId.Value}");
            if (model.Add(variable) is Failed<Success, ErrorCode, Error<ErrorCode>> addFailure) {
                return Results.Failed<Success>(addFailure.Error);
            }
            var input = new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, variable) }, Flt64.Zero);
            _variables[arcId] = variable;
            _inputs[arcId] = input;
            _outputs[arcId] = Scale(item.OutputCoefficient, input);
        }
        return Results.Ok(Results.SuccessInstance);
    }

    /// <summary>按标量缩放表达式 / Scale an expression by a scalar.</summary>
    /// <param name="factor">标量 / The scalar.</param>
    /// <param name="polynomial">表达式 / The expression.</param>
    /// <returns>缩放后的表达式 / The scaled expression.</returns>
    private static LinearPolynomial<Flt64> Scale(Flt64 factor, LinearPolynomial<Flt64> polynomial) =>
        new(
            polynomial.Monomials
                .Select(term => new LinearMonomial<Flt64>(factor.Times(term.Coefficient), term.Symbol))
                .ToList(),
            factor.Times(polynomial.Constant));
}
