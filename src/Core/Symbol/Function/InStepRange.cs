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
/// 步长区间归属函数 / Step range membership function
/// </summary>
public sealed class InStepRangeFunction : IMathFunctionSymbol<Flt64>
{
    public string Name { get; set; }
    public string? DisplayName { get; set; }
    public IReadOnlyList<IVariableItem> HelperVariables => Array.Empty<IVariableItem>();

    public InStepRangeFunction(string name = "inStepRange", string? displayName = null)
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
