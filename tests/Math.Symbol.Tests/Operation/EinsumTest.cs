#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.MultiArray.Einsum;
using Fuookami.Ospf.MultiArray;
using Xunit;

namespace Fuookami.Ospf.Math.Symbol.Operation.Tests
{
    public class EinsumTest
    {
        [Fact]
        public void IndexLabel_Defaults()
        {
            var labels = IndexLabelExtensions.Defaults(3);
            labels.Should().HaveCount(3);
            labels[0].Should().Be(IndexLabel.I);
            labels[1].Should().Be(IndexLabel.J);
            labels[2].Should().Be(IndexLabel.K);
        }

        [Fact]
        public void IndexLabel_Names()
        {
            var list = new IndexList(new[] { IndexLabel.I, IndexLabel.J, IndexLabel.K });
            list.Names.Should().Be("ijk");
            list.Length.Should().Be(3);
        }

        [Fact]
        public void TensorExpr_NewSafe_DimensionMismatch()
        {
            var shape = Shape2.Invoke(2, 3);
            var arr = MultiArray<int, Shape2>.Factory.NewBy(shape, (_, _) => 0);
            var indices = new IndexList(new[] { IndexLabel.I }); // 1 != 2

            var result = TensorExpr<int, Shape2>.NewSafe(arr, indices);
            result.IsFailed.Should().BeTrue();
        }

        [Fact]
        public void TensorExpr_WithDefaultIndices()
        {
            var shape = Shape2.Invoke(2, 3);
            var arr = MultiArray<int, Shape2>.Factory.NewBy(shape, (_, _) => 0);

            var result = TensorExpr<int, Shape2>.WithDefaultIndices(arr);
            result.IsOk.Should().BeTrue();
            result.Value.IndexNames.Should().Be("ij");
        }

        [Fact]
        public void Einstein_Matmul()
        {
            // [[1,2],[3,4]] * [[5,6],[7,8]]
            var shapeA = Shape2.Invoke(2, 2);
            var a = MultiArray<Flt64, Shape2>.Factory.NewBy(shapeA, (i, _) =>
                new Flt64(new[] { 1.0, 2.0, 3.0, 4.0 }[i]));

            var shapeB = Shape2.Invoke(2, 2);
            var b = MultiArray<Flt64, Shape2>.Factory.NewBy(shapeB, (i, _) =>
                new Flt64(new[] { 5.0, 6.0, 7.0, 8.0 }[i]));

            var ctx = EinsumParser.Einstein(a, b, Flt64.Zero);
            var result = ctx.Matmul();
            result.IsOk.Should().BeTrue();

            var r = result.Value;
            // [[1*5+2*7, 1*6+2*8], [3*5+4*7, 3*6+4*8]] = [[19,22],[43,50]]
            r[new[] { 0, 0 }].Should().Be(new Flt64(19.0));
            r[new[] { 0, 1 }].Should().Be(new Flt64(22.0));
            r[new[] { 1, 0 }].Should().Be(new Flt64(43.0));
            r[new[] { 1, 1 }].Should().Be(new Flt64(50.0));
        }

        [Fact]
        public void Einstein_Dot()
        {
            var shape = Shape1.Invoke(3);
            var a = MultiArray<Flt64, Shape1>.Factory.NewBy(shape, (i, _) =>
                new Flt64(new[] { 1.0, 2.0, 3.0 }[i]));
            var b = MultiArray<Flt64, Shape1>.Factory.NewBy(shape, (i, _) =>
                new Flt64(new[] { 4.0, 5.0, 6.0 }[i]));

            var ctx = EinsumParser.Einstein(a, b, Flt64.Zero);
            var result = ctx.Dot();
            result.IsOk.Should().BeTrue();
            result.Value.Should().Be(new Flt64(32.0)); // 1*4+2*5+3*6
        }

        [Fact]
        public void Einstein_Trace()
        {
            var shape = Shape2.Invoke(3, 3);
            var a = MultiArray<Flt64, Shape2>.Factory.NewBy(shape, (i, v) =>
            {
                // Diagonal: 1, 5, 9
                return v[0] == v[1] ? new Flt64(1.0 + 4.0 * v[0]) : Flt64.Zero;
            });

            var ctx = EinsumParser.Einstein(a, Flt64.Zero);
            var result = ctx.Trace();
            result.IsOk.Should().BeTrue();
            result.Value.Should().Be(new Flt64(15.0)); // 1+5+9
        }

        [Fact]
        public void Einstein_Transpose()
        {
            var shape = Shape2.Invoke(2, 3);
            var a = MultiArray<Flt64, Shape2>.Factory.NewBy(shape, (i, v) =>
                new Flt64(v[0] * 3.0 + v[1] + 1.0));

            var ctx = EinsumParser.Einstein(a, Flt64.Zero);
            var result = ctx.Transpose();
            result.IsOk.Should().BeTrue();

            var r = result.Value;
            r[new[] { 0, 0 }].Should().Be(a[new[] { 0, 0 }]);
            r[new[] { 1, 0 }].Should().Be(a[new[] { 0, 1 }]);
            r[new[] { 2, 0 }].Should().Be(a[new[] { 0, 2 }]);
        }

        [Fact]
        public void EinsumParser_TwoOperand_Matmul()
        {
            var shape = Shape2.Invoke(2, 2);
            var a = MultiArray<Flt64, Shape2>.Factory.NewBy(shape, (i, _) =>
                new Flt64(new[] { 1.0, 2.0, 3.0, 4.0 }[i]));
            var b = MultiArray<Flt64, Shape2>.Factory.NewBy(shape, (i, _) =>
                new Flt64(new[] { 5.0, 6.0, 7.0, 8.0 }[i]));

            var result = EinsumParser.Einsum(a, b, "ij,jk->ik", Flt64.Zero);
            result.IsOk.Should().BeTrue();
        }

        [Fact]
        public void EinsumParser_SingleOperand_Trace()
        {
            var shape = Shape2.Invoke(2, 2);
            var a = MultiArray<Flt64, Shape2>.Factory.NewBy(shape, (i, v) =>
                v[0] == v[1] ? new Flt64(5.0) : Flt64.Zero);

            var result = EinsumParser.Einsum(a, "ii->", Flt64.Zero);
            result.IsOk.Should().BeTrue();
        }
    }
}
