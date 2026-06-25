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
/// 任意满足函数 / Any (at least one satisfied) function
/// </summary>
public sealed class AnyFunction : IMathFunctionSymbol<Flt64>
{
    public string Name { get; set; }
    public string? DisplayName { get; set; }
    public IReadOnlyList<IVariableItem> HelperVariables => Array.Empty<IVariableItem>();

    public AnyFunction(string name = "any", string? displayName = null)
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
/// 全部满足函数 / All (every item satisfied) function
/// </summary>
public sealed class AllFunction : IMathFunctionSymbol<Flt64>
{
    public string Name { get; set; }
    public string? DisplayName { get; set; }
    public IReadOnlyList<IVariableItem> HelperVariables => Array.Empty<IVariableItem>();

    public AllFunction(string name = "all", string? displayName = null)
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
/// 至少不等式函数 / At-least inequality function
/// </summary>
public sealed class AtLeastInequalityFunction : IMathFunctionSymbol<Flt64>
{
    public string Name { get; set; }
    public string? DisplayName { get; set; }
    public IReadOnlyList<IVariableItem> HelperVariables => Array.Empty<IVariableItem>();

    public AtLeastInequalityFunction(string name = "atLeastInequality", string? displayName = null)
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
/// 非全部满足函数 / Not-all function
/// </summary>
public sealed class NotAllFunction : IMathFunctionSymbol<Flt64>
{
    public string Name { get; set; }
    public string? DisplayName { get; set; }
    public IReadOnlyList<IVariableItem> HelperVariables => Array.Empty<IVariableItem>();

    public NotAllFunction(string name = "notAll", string? displayName = null)
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
/// 可计数满足函数 / Numerable satisfied function
/// </summary>
public sealed class NumerableFunction : IMathFunctionSymbol<Flt64>
{
    public string Name { get; set; }
    public string? DisplayName { get; set; }
    public IReadOnlyList<IVariableItem> HelperVariables => Array.Empty<IVariableItem>();

    public NumerableFunction(string name = "numerable", string? displayName = null)
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
