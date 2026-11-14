#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.MultiArray.Tests;

public class AccessOrderSemanticsTest {
    [Fact]
    public void IteratorReturnsIndependentSnapshots() {
        var iterator = new MultiIndexIterator(Shape2.Invoke(2, 3), AccessOrder.RowMajor);

        iterator.MoveNext().Should().BeTrue();
        int[] first = iterator.Current;
        iterator.MoveNext().Should().BeTrue();
        int[] second = iterator.Current;

        second.Should().Equal(0, 1);
        second[0] = 999;
        first.Should().Equal(0, 0);
        ReferenceEquals(first, second).Should().BeFalse();
    }

    [Fact]
    public void AccessOrderMatchesShapeIterationAcrossBuiltInShapes() {
        IShape[] baseShapes = {
            Shape1.Invoke(5),
            Shape2.Invoke(2, 3),
            Shape3.Invoke(2, 3, 4),
            Shape4.Invoke(2, 1, 3, 2),
            DynShape.Invoke(Array.Empty<int>()),
            DynShape.Invoke(new[] { 2, 0, 3 }),
            DynShape.Invoke(new[] { 2, 1, 3, 2, 1 }),
        };

        foreach (IShape baseShape in baseShapes) {
            foreach (StorageOrder storageOrder in Enum.GetValues<StorageOrder>()) {
                IShape shape = WithStorageOrder(baseShape, storageOrder);
                MultiArray<int, IShape> array = MultiArray<int, IShape>.Factory.NewBy(shape, (index, _) => index);
                foreach (AccessOrder accessOrder in Enum.GetValues<AccessOrder>()) {
                    var expected = new MultiIndexSequence(shape, accessOrder)
                        .Select(vector => array[vector])
                        .ToList();

                    array.IterWithOrder(accessOrder).Should().Equal(expected);
                    array.Flatten(accessOrder).Should().Equal(expected);
                }
            }
        }
    }

    [Fact]
    public void CustomShapeRetainsCoordinateIndexingFallback() {
        int calls = 0;
        var custom = new ReverseShape(Shape1.Invoke(4), () => calls++);
        MultiArray<int, ReverseShape> array =
            MultiArray<int, ReverseShape>.Factory.NewBy(custom, (index, _) => index);

        array.Flatten().Should().Equal(3, 2, 1, 0);
        calls.Should().Be(4);
    }

    [Fact]
    public void ModifiedOffsetsRetainCoordinateIndexingFallback() {
        var shape = Shape2.Invoke(2, 2);
        MultiArray<int, Shape2> array = MultiArray<int, Shape2>.Factory.NewBy(shape, (index, _) => index);
        shape.Offsets[0] = 1;

        int[] expected = new MultiIndexSequence(shape)
            .Select(vector => array[vector])
            .ToArray();

        expected.Should().Equal(0, 1, 1, 2);
        array.Flatten().Should().Equal(expected);
    }

    [Fact]
    public void DummyVectorIterationHonorsAccessOrderAndEmptyRanges() {
        var shape = Shape3.Invoke(2, 3, 2);
        IReadOnlyList<DummyIndex> all = new[] {
            DummyIndex.AllInstance,
            DummyIndex.AllInstance,
            DummyIndex.AllInstance,
        };

        all.IterateWithOrder(shape, AccessOrder.RowMajor)
            .Take(4)
            .Should()
            .BeEquivalentTo(new[] {
                new[] { 0, 0, 0 },
                new[] { 0, 0, 1 },
                new[] { 0, 1, 0 },
                new[] { 0, 1, 1 },
            }, options => options.WithStrictOrdering());

        all.IterateWithOrder(shape, AccessOrder.ColumnMajor)
            .Take(4)
            .Should()
            .BeEquivalentTo(new[] {
                new[] { 0, 0, 0 },
                new[] { 1, 0, 0 },
                new[] { 0, 1, 0 },
                new[] { 1, 1, 0 },
            }, options => options.WithStrictOrdering());

        IReadOnlyList<DummyIndex> empty = new[] {
            DummyIndex.From(new[] { 0, 2 }),
            DummyIndex.From(2..1),
        };
        empty.IterateWithOrder(Shape2.Invoke(3, 4)).Should().BeEmpty();
    }

    [Fact]
    public void ViewIterationHonorsRequestedOrder() {
        MultiArray<int, Shape2> array =
            MultiArray<int, Shape2>.Factory.NewBy(Shape2.Invoke(2, 3), (index, _) => index);
        MultiArrayView<int, Shape2> view = array[DummyIndex.AllInstance, DummyIndex.AllInstance];

        view.IterWithOrder(AccessOrder.RowMajor).Should().Equal(0, 1, 2, 3, 4, 5);
        view.IterWithOrder(AccessOrder.ColumnMajor).Should().Equal(0, 3, 1, 4, 2, 5);
    }

    private static IShape WithStorageOrder(IShape shape, StorageOrder order) => shape switch {
        Shape1 value => value.WithStorageOrder(order),
        Shape2 value => value.WithStorageOrder(order),
        Shape3 value => value.WithStorageOrder(order),
        Shape4 value => value.WithStorageOrder(order),
        DynShape value => value.WithStorageOrder(order),
        _ => throw new ArgumentOutOfRangeException(nameof(shape)),
    };

    private sealed class ReverseShape : IShape {
        private readonly IShape _inner;
        private readonly Action _onIndex;

        public ReverseShape(IShape inner, Action onIndex) {
            _inner = inner;
            _onIndex = onIndex;
        }

        public int Dimension => _inner.Dimension;
        public int Size => _inner.Size;
        public int[] Offsets => _inner.Offsets;
        public StorageOrder StorageOrder => _inner.StorageOrder;
        public int this[int index] => _inner[index];

        public Result<int, ErrorCode, Error<ErrorCode>> Index(int[] vector) {
            _onIndex();
            return _inner.Index(vector).Map(index => 3 - index);
        }

        public Result<int[], ErrorCode, Error<ErrorCode>> Vector(int index) => _inner.Vector(index);
    }
}
