#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Core.Token;
namespace Fuookami.Ospf.Core.Symbol.Function
{
    using Try = Result<Success, ErrorCode, Error<ErrorCode>>;


/// <summary>
/// 最小中的最大值函数 / Min-max function (max of mins)
/// </summary>
public sealed class MinMaxFunction : IMathFunctionSymbol<Flt64>
{
    public string Name { get; set; }
    public string? DisplayName { get; set; }
    public IReadOnlyList<IVariableItem> HelperVariables => Array.Empty<IVariableItem>();

    public MinMaxFunction(string name = "minMax", string? displayName = null)
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

/// <summary>
/// 最大中的最小值函数 / Max-min function (min of maxes)
/// </summary>
public sealed class MaxMinFunction : IMathFunctionSymbol<Flt64>
{
    public string Name { get; set; }
    public string? DisplayName { get; set; }
    public IReadOnlyList<IVariableItem> HelperVariables => Array.Empty<IVariableItem>();

    public MaxMinFunction(string name = "maxMin", string? displayName = null)
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
