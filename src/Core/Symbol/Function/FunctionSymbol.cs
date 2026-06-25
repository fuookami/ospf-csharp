#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Symbol.Function;
// Use these type aliases matching the project conventions:
// using Try = Result<Success, ErrorCode, Error<ErrorCode>>;

/// <summary>函数符号注册生命周期基类 / V-generic base for function symbol registration lifecycle</summary>
public interface IMathFunctionSymbolBase<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    Try RegisterAuxiliaryTokens(IAddableTokenCollection<V> tokens);
    Try RegisterConstraints(object model); // placeholder: AbstractLinearMechanismModel<V>
}

/// <summary>暴露结果多项式的可选接口</summary>
public interface IHasResultPolynomial<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    LinearPolynomial<V> ResultPolynomial { get; }
}

/// <summary>数学函数符号基础接口</summary>
public interface IMathFunctionSymbol<V> : IMathFunctionSymbolBase<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    string Name { get; set; }
    string? DisplayName { get; set; }
    IReadOnlyList<IVariableItem> HelperVariables { get; }
    V? Evaluate(IReadOnlyDictionary<ISymbol, V> values);
}

/// <summary>内部二次函数符号注册接口</summary>
internal interface IQuadraticMathFunctionSymbolBase<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    Try RegisterAuxiliaryTokens(IAddableTokenCollection<V> tokens);
    Try RegisterConstraints(object model);
}
