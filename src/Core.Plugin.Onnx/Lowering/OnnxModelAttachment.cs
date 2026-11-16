#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Plugin.Onnx.Model;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Symbol.Flatten;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Lowering;

/// <summary>
/// **把一个 ONNX 仿射结果挂进真实的机制模型** / Attach an ONNX affine result to a real mechanism model.
/// </summary>
/// <remarks>
/// 同步 Kotlin `dee897d2` 的 `OnnxFunctionalModel` 里"把降阶结果加进模型"那一步。**这是"插件能往真实模型里加东西"的实证：**
/// **变量被注册、界以约束形式加上、目标被设上，然后整条链由求解器解出来。**
///
/// **为什么按这个次序加：** **变量必须先进令牌表，否则约束引用不到它们；** **目标最后加，因为它是整个模型的落点。**
/// **次序错了不会报错——** **会得到"模型里有一半东西"的另一种模型。**
///
/// Syncs the "add the lowered result into the model" step. **This is the evidence that the plugin can put something into a real model: variables get registered,
/// bounds arrive as constraints, the objective is set, and the solver resolves the whole chain.**
///
/// **Why this order: variables must enter the token table first or constraints cannot reference them, and the objective goes last because it is where the model
/// lands. Getting the order wrong throws nothing — it yields another model, one holding half of what was meant.**
/// </remarks>
public static class OnnxModelAttachment
{
    /// <summary>
    /// 挂接结果 / The attachment result.
    /// </summary>
    /// <param name="Variables">输入名到变量的映射 / The mapping from input names to variables.</param>
    /// <param name="Failure">失败原因 / The failure reason.</param>
    public sealed record Attached(IReadOnlyDictionary<string, RealVar>? Variables, string? Failure);

    /// <summary>
    /// 挂进一个线性元模型 / Attach into a linear meta model.
    /// </summary>
    /// <param name="model">元模型 / The meta model.</param>
    /// <param name="polynomial">目标多项式 / The objective polynomial.</param>
    /// <param name="bounds">输入区间 / The input ranges.</param>
    /// <param name="category">优化方向 / The optimisation direction.</param>
    /// <param name="name">目标名 / The objective name.</param>
    /// <returns>挂接结果 / The result.</returns>
    public static Attached Attach(
        LinearMetaModel<Flt64> model,
        LinearPolynomial<Flt64> polynomial,
        IReadOnlyDictionary<string, OnnxBounds> bounds,
        ObjectCategory category = ObjectCategory.Minimum,
        string name = "onnx-affine") {
        var variables = new Dictionary<string, RealVar>(StringComparer.Ordinal);
        foreach (LinearMonomial<Flt64> monomial in polynomial.Monomials) {
            string key = monomial.Symbol.Name;
            if (variables.ContainsKey(key)) {
                continue;
            }
            if (!bounds.TryGetValue(key, out OnnxBounds? range)) {
                return new Attached(null, "变量 '" + key + "' 缺少区间 / the variable has no range");
            }
            if (!range.Validate(out string? boundFailure)) {
                return new Attached(null, "变量 '" + key + "' 的" + boundFailure);
            }
            // 必须复用多项式里那个实例：新建一个同名变量会注册一个"另一个对象"，而目标引用的是原来那个，
            // 于是机制模型报"变量未注册"——名字一样，身份不同。
            //
            // The instance from the polynomial has to be reused: a fresh same-named variable registers another object while the objective references the original, and the
            // mechanism model then reports "variable not registered" — same name, different identity.
            variables[key] = monomial.Symbol as RealVar ?? new RealVar(key);
        }

        // ① 变量先进令牌表：约束引用的是符号，符号不在表里时约束无处落脚。
        // ① Variables enter the token table first: constraints reference symbols, and a symbol outside the table leaves them nowhere to land.
        foreach (RealVar variable in variables.Values) {
            if (!model.Add(variable).IsOk) {
                return new Attached(null, "变量 '" + variable.Name + "' 无法注册 / the variable could not be registered");
            }
        }

        // ② 界以约束形式加上：C# 侧没有逐变量的界设置入口。
        // ② Bounds arrive as constraints: there is no per-variable bound setter on the C# side.
        OnnxBoundsConstraints.Generated generated = OnnxBoundsConstraints.ForAll(
            AsSymbols(variables),
            bounds,
            out IReadOnlyList<string> missing);
        if (generated.Failure is not null) {
            return new Attached(null, generated.Failure);
        }
        if (missing.Count != 0) {
            return new Attached(null, "缺少输入区间：" + string.Join(", ", missing) + " / missing input bounds");
        }
        foreach (LinearInequality<Flt64> constraint in generated.Constraints) {
            if (!model.AddConstraint(relation: constraint, group: null).IsOk) {
                return new Attached(null, "界约束 '" + constraint.Name + "' 无法加入 / the bound constraint could not be added");
            }
        }

        // ③ 目标最后加：它是整个模型的落点。
        // ③ The objective goes last: it is where the model lands.
        if (!model.AddObject(category: category, flattenData: new LinearFlattenData<Flt64>(polynomial.Monomials, polynomial.Constant), name: name, displayName: null).IsOk) {
            return new Attached(null, "目标无法设置 / the objective could not be set");
        }

        return new Attached(variables, null);
    }

    private static IReadOnlyDictionary<string, ISymbol> AsSymbols(IReadOnlyDictionary<string, RealVar> variables) {
        var symbols = new Dictionary<string, ISymbol>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, RealVar> entry in variables) {
            symbols[entry.Key] = entry.Value;
        }
        return symbols;
    }
}