#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// 生物混沌模型 / Biology Chaotic Model.
/// 三维离散映射。/ 3D discrete map.
/// </summary>
public sealed record BiologyChaoticModel<V>(V A, V B, V C, V R)
    : IExtractor<Point<Dim3, V>, Point<Dim3, V>>
    where V : struct, IFloatingNumber<V> {
    public Point<Dim3, V> Invoke(Point<Dim3, V> p) {
        V x = p[0]; V y = p[1]; V z = p[2];
        V one = x.Constants.One;
        V xNew = R.Times(x).Times(one.Minus(A.Times(x)).Minus(B.Times(y)).Minus(C.Times(z)));
        return new Point<Dim3, V>(new V[] {
            xNew,
            x,
            y
        }, Dim3.Instance);
    }

    public static BiologyChaoticModel<Flt64> Create(
        Flt64? a = null, Flt64? b = null, Flt64? c = null, Flt64? r = null)
        => new(a ?? new Flt64(0.5), b ?? new Flt64(0.5), c ?? new Flt64(0.5), r ?? new Flt64(0.5));
}

/// <summary>
/// 生物混沌模型生成器 / Biology Chaotic Model Generator.
/// </summary>
public sealed class BiologyChaoticModelGenerator : IGenerator<Point<Dim3, Flt64>> {
    public BiologyChaoticModel<Flt64> Model { get; }
    public Point<Dim3, Flt64> X { get; private set; }

    public BiologyChaoticModelGenerator(BiologyChaoticModel<Flt64>? model = null, Point<Dim3, Flt64>? x = null) {
        Model = model ?? BiologyChaoticModel<Flt64>.Create();
        X = x ?? PointFactory.point3(new Flt64(0.1), new Flt64(0.1), new Flt64(0.1));
    }

    public static BiologyChaoticModelGenerator Create(
        Flt64 a, Flt64 b, Flt64 c, Flt64 r, Point<Dim3, Flt64>? x = null)
        => new(BiologyChaoticModel<Flt64>.Create(a, b, c, r), x);

    public Point<Dim3, Flt64> Invoke() {
        Point<Dim3, Flt64> cur = X;
        X = Model.Invoke(X);
        return cur;
    }
}
