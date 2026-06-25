#nullable enable

// Symbol polynomial quantity type aliases — C# does not support generic type aliases.
// In Kotlin: typealias QuantityLinearFlt64 = Quantity<LinearPolynomial<Flt64>>
// In C#, use the closed generic directly:
//   Quantity<LinearPolynomial<Flt64>>
//   Quantity<QuadraticPolynomial<Flt64>>
//   Quantity<CanonicalPolynomial<Flt64>>
//   Quantity<LinearPolynomial<FltX>>
//   Quantity<QuadraticPolynomial<FltX>>
//   Quantity<CanonicalPolynomial<FltX>>
//
// Per-file using aliases may be added at call sites for convenience:
//   using QuantityLinearFlt64 = Fuookami.Ospf.Quantities.Quantity.Quantity<Fuookami.Ospf.Math.Symbol.Polynomial.LinearPolynomial<Fuookami.Ospf.Math.Algebra.Number.Flt64>>;

namespace Fuookami.Ospf.Quantities.Symbol;

// No types defined here — use closed generics directly.
// See SymbolQuantity.cs comments for alias documentation.
