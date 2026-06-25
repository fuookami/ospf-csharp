#nullable enable

// Axis type aliases — re-export Math.Geometry axis types for quantity-layer consumers.
// In C# there is no generic typealias, so consumers reference Math.Geometry types directly.
// This file documents the mapping:
//   QuantityAxis2      = Math.Geometry.Axis2
//   QuantityAxis3      = Math.Geometry.Axis3
//   QuantityAxisPlane3 = Math.Geometry.AxisPlane3
//
// Additionally, non-generic aliases are provided as using directives for convenience.
// The following aliases are available via 'using' in consuming files:
//   using QuantityAxis2 = Fuookami.Ospf.Math.Geometry.Axis2;
//   using QuantityAxis3 = Fuookami.Ospf.Math.Geometry.Axis3;
//   using QuantityAxisPlane3 = Fuookami.Ospf.Math.Geometry.AxisPlane3;

namespace Fuookami.Ospf.Quantities.Geometry;

// No types defined here — axis types are used directly from Math.Geometry.
// See QuantityAxisAliases.cs comments for alias documentation.
