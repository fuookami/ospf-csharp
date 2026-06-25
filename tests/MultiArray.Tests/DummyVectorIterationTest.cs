#nullable enable

using System;
using System.Linq;
using FluentAssertions;
using Xunit;

using Fuookami.Ospf.MultiArray;

namespace Fuookami.Ospf.MultiArray.Tests
{
    public class DummyVectorIterationTest
    {
        [Fact]
        public void DummyIndex_All_CoversEntireDimension()
        {
            var shape = Shape2.Invoke(3, 4);
            var all = DummyIndex.AllInstance;
            all.LenOf(shape, 0).Should().Be(3);
            all.LenOf(shape, 1).Should().Be(4);
        }

        [Fact]
        public void DummyIndex_Single_HasLengthOne()
        {
            var shape = Shape2.Invoke(3, 4);
            var idx = DummyIndex.From(2);
            idx.LenOf(shape, 0).Should().Be(1);
            idx.LenOf(shape, 1).Should().Be(1);
        }

        [Fact]
        public void DummyIndex_Range_CorrectLength()
        {
            var shape = Shape2.Invoke(3, 4);
            var range = DummyIndex.From(1..3);
            range.LenOf(shape, 1).Should().Be(2);
        }

        [Fact]
        public void DummyIndex_IndexArray_CorrectLength()
        {
            var shape = Shape2.Invoke(3, 4);
            var arr = DummyIndex.From(new[] { 0, 2 });
            arr.LenOf(shape, 0).Should().Be(2);
        }

        [Fact]
        public void DummyIndexIterator_Single_GetReturnsValue()
        {
            var iter = new DummyIndexIterator.Single(5);
            iter.Get(0).Should().Be(5);
            iter.Get(1).Should().BeNull();
            iter.Len().Should().Be(1);
        }

        [Fact]
        public void DummyIndexIterator_Continuous_IteratesCorrectly()
        {
            var iter = new DummyIndexIterator.Continuous(2, 5);
            iter.Len().Should().Be(3);
            iter.Get(0).Should().Be(2);
            iter.Get(1).Should().Be(3);
            iter.Get(2).Should().Be(4);
            iter.Get(3).Should().BeNull();
        }

        [Fact]
        public void DummyIndexIterator_Discrete_IteratesCorrectly()
        {
            var iter = new DummyIndexIterator.Discrete(new[] { 1, 3, 5 });
            iter.Len().Should().Be(3);
            iter.Get(0).Should().Be(1);
            iter.Get(1).Should().Be(3);
            iter.Get(2).Should().Be(5);
        }

        [Fact]
        public void Shape_DummyToIteratorVector_Works()
        {
            var shape = Shape2.Invoke(3, 4);
            var dummyVector = new DummyIndex[] { DummyIndex.AllInstance, DummyIndex.From(2) };
            var iterVector = shape.DummyToIteratorVector(dummyVector);

            iterVector.Count.Should().Be(2);
            iterVector[0].Len().Should().Be(3); // All of dim 0
            iterVector[1].Len().Should().Be(1); // Single index 2
        }

        [Fact]
        public void MapIndex_Creation()
        {
            var mapDummy = MapIndex.From(DummyIndex.AllInstance);
            mapDummy.Should().BeOfType<MapIndex.Dummy>();

            var mapDirect = MapIndex.CreateMap(1);
            mapDirect.Should().BeOfType<MapIndex.Map>();
            ((MapIndex.Map)mapDirect).Index.Should().Be(1);
        }
    }
}
