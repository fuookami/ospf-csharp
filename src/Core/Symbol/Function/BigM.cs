#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Core.Token;
namespace Fuookami.Ospf.Core.Symbol.Function
{
    using Try = Result<Success, ErrorCode, Error<ErrorCode>>;


/// <summary>
/// 线性多项式上下界记录 / Linear polynomial bounds record for Big-M method
/// </summary>
public sealed record LinearPolynomialBounds<V>(
    LinearPolynomial<V> LowerBound,
    LinearPolynomial<V> UpperBound
) where V : struct, IRing<V>;

/// <summary>
/// Big-M 约束编码函数 / Big-M constraint encoding function
/// </summary>
public sealed class BigMFunction : IMathFunctionSymbol<Flt64>
{
    public string Name { get; set; }
    public string? DisplayName { get; set; }
    public IReadOnlyList<IVariableItem> HelperVariables => Array.Empty<IVariableItem>();

    public BigMFunction(string name = "bigM", string? displayName = null)
    {
        Name = name;
        DisplayName = displayName;
    }

    public Flt64? Evaluate(IReadOnlyDictionary<ISymbol, Flt64> values) => null;

    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<Flt64> tokens) =>
        Results.Ok<Success>(Results.SuccessInstance);

    public Try RegisterConstraints(object model) =>
        Results.Ok<Success>(Results.SuccessInstance);
}
}
