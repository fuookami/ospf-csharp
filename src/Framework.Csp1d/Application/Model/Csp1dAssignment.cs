#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.MultiArray;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Csp1d.Application.Model;
/// <summary>
/// CSP1D 赋值变量 / CSP1D assignment variable.
///
/// 封装方案选择的整数决策变量 x[j]，表示第 j 个切割方案的批次数。
/// Wraps the integer decision variable x[j] for plan selection, representing the batch count of the j-th cutting plan.
/// </summary>
public sealed class Csp1dAssignment {
    /// <summary>决策变量数组 / Decision variable array.</summary>
    public UIntVariable1 X { get; }

    /// <summary>方案数量 / Plan count.</summary>
    public int PlanCount { get; }

    private Csp1dAssignment(UIntVariable1 x, int planCount) {
        X = x;
        PlanCount = planCount;
    }

    /// <summary>
    /// 按索引获取变量 / Get variable by index.
    /// </summary>
    /// <param name="index">索引 / Index.</param>
    /// <returns>变量 / Variable.</returns>
    public AbstractVariableItem<UInt64, UInteger> this[int index] => X[index];

    /// <summary>
    /// 创建赋值变量 / Create assignment variable.
    /// </summary>
    /// <param name="planCount">方案数量 / Plan count.</param>
    /// <returns>CSP1D 赋值变量 / CSP1D assignment variable.</returns>
    public static Csp1dAssignment Create(int planCount) {
        return new Csp1dAssignment(
            new UIntVariable1("x", Shape1.Invoke(planCount)),
            planCount
        );
    }

    /// <summary>
    /// 注册变量到模型 / Register variables to model.
    /// </summary>
    /// <param name="model">线性元模型 / Linear meta model.</param>
    /// <returns>注册结果 / Registration result.</returns>
    public Try Register(LinearMetaModel<Flt64> model) => model.Add(X);
}
