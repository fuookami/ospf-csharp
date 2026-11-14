#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

internal sealed class UnknownFunctionStub : IMathFunctionSymbol<Flt64> {
    public string Name { get; set; } = "unknown";
    public string? DisplayName { get; set; }
    public IReadOnlyList<IVariableItem> HelperVariables => Array.Empty<IVariableItem>();
    public Flt64? Evaluate(IReadOnlyDictionary<ISymbol, Flt64> values) => null;
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<Flt64> tokens) => Results.OkInstance;
    public Try RegisterConstraints(IAbstractLinearMechanismModel<Flt64> model) => Results.OkInstance;
}
