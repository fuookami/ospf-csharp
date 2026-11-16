#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.Bpp2d.Domain.Item.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.Model;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.Service.Limits;

/// <summary>
/// 面积最小化目标 / The area-minimisation objective.
/// </summary>
/// <remarks>
/// 把非精确分配的**面积符号**按系数放进目标函数。
///
/// 三处是本仓特有的形状约束：
///
/// 1. **`LinearMetaModel` 没有 `Minimize` 方法**（`src/Example/FrameworkDemo/Demo2/.../SoftSecurityLimits.cs:100` 有同样记录），
///    目标走 `AddObject(ObjectCategory.Minimum, …)`。
/// 2. **面积是符号，不是数值**：`assignment.area.toLinearPolynomial` 逐项加系数，本仓的 `AreaSymbol` 是个
///    `LinearExpressionSymbol`，直接当单项符号放进目标——**展开它的多项式再重新加一遍系数会丢掉常数项**。
/// 3. **系数为一时仍然走乘法**，好让系数为 1e-6 这类平局权重的调用方走同一条路径。
///
/// Puts the imprecise assignment's **area symbol** into the objective with a coefficient. Three shape constraints of this
/// repository: **`LinearMetaModel` has no `Minimize`** (recorded at `src/Example/FrameworkDemo/Demo2/.../SoftSecurityLimits.cs:100`), so objectives go through `AddObject(ObjectCategory.Minimum,
/// …)`; **area is a symbol rather than a number**, and the implementation scales each term of `assignment.area.toLinearPolynomial` while this repository's `AreaSymbol` is a
/// `LinearExpressionSymbol` placed straight into the objective, since expanding it and rescaling the terms would drop the constant; and **the coefficient multiplies even when it is one**, so
/// callers using a tie-break weight such as 1e-6 take the same path.
/// </remarks>
public sealed class AreaMinimization
{
    private readonly ImpreciseAssignment _assignment;
    private readonly FltX _coefficient;

    /// <summary>
    /// 构造面积最小化 / Construct the area minimisation.
    /// </summary>
    /// <param name="assignment">非精确层分配 / The imprecise layer assignment.</param>
    /// <param name="coefficient">面积系数 / The area coefficient.</param>
    /// <param name="name">目标名 / The objective name.</param>
    public AreaMinimization(
        ImpreciseAssignment assignment,
        FltX? coefficient = null,
        string name = "area_minimization")
    {
        ArgumentNullException.ThrowIfNull(assignment);
        _assignment = assignment;
        _coefficient = coefficient ?? new FltX(1.0);
        Name = name;
    }

    /// <summary>目标名 / The objective name</summary>
    public string Name { get; }

    /// <summary>面积系数 / The area coefficient</summary>
    public FltX Coefficient => _coefficient;

    /// <summary>
    /// 把目标加到模型 / Add the objective to the model.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>执行结果 / Execution result.</returns>
    /// <remarks>
    /// **面积符号未注册时直接失败**，不悄悄加一个空目标：没有面积项的目标等于"什么都不最小化"，而调用方会以为
    /// 面积已被优化。这是反复出现的那类"返回 ok 但什么都没做"。
    ///
    /// **An unregistered area symbol fails outright** rather than silently adding an empty objective: an objective with no area term minimises nothing while the caller believes area is
    /// optimised, which is the "returns ok but does nothing" class this session keeps meeting.
    /// </remarks>
    public Try Invoke(LinearMetaModel<Flt64> model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (_assignment.AreaSymbol is null)
        {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                "面积符号尚未注册，无法建立面积目标 / the area symbol is not registered, so the area objective cannot be built"));
        }

        // 单个单项式，不走 `SafeLinearPolynomial`（那个工具是 FltX 专用，且这里的单项数恒为 1）。
        // A single monomial, so the FltX-only `SafeLinearPolynomial` helper is not used; the count is always one here.
        var polynomial = new LinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(_coefficient.ToFlt64(), _assignment.AreaSymbol) },
            Flt64.Zero);
        return model.AddObject(ObjectCategory.Minimum, polynomial, Name, displayName: null);
    }
}
