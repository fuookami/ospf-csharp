// 参数演化源与初始化算子测试 / Tests for parameter sources and initializers.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 参数演化源 / Parameter evolution sources.
/// </summary>
public sealed class ParameterSourceTests {
    /// <summary>恒定参数不随迭代变化 / A constant parameter never changes.</summary>
    [Fact]
    public void Constant_ShouldNotChange() {
        var iteration = new Iteration();
        iteration.Next(false);
        iteration.Next(false);

        new ConstantSource(new Flt64(3.0)).Value(iteration).Should().Be(new Flt64(3.0));
    }

    /// <summary>**线性演化在起点与终点取端值** / A linear source hits both endpoints.</summary>
    [Fact]
    public void Linear_ShouldReachBothEndpoints() {
        var iteration = new Iteration();
        var source = new LinearSource(new Flt64(0.0), new Flt64(10.0), 2);

        source.Value(iteration).Should().Be(new Flt64(0.0));
        iteration.Next(true);
        source.Value(iteration).Should().Be(new Flt64(5.0));
        iteration.Next(true);
        source.Value(iteration).Should().Be(new Flt64(10.0));
    }

    /// <summary>**上限为 0 时进度取 1** / A zero limit yields progress 1.</summary>
    /// <remarks>
    /// 取 0 会让"未设上限"被解释成"还没开始"，线性退火永远停在起点，而搜索看起来已经在跑。
    /// Yielding 0 would interpret "no limit" as "not started", pinning linear annealing at its starting point.
    /// </remarks>
    [Fact]
    public void Progress_ShouldBeOneForZeroLimit() =>
        ParameterSources.Progress(new Iteration(), 0).Should().Be(Flt64.One);

    /// <summary>进度被夹到 [0,1] / Progress is clamped into [0,1].</summary>
    [Fact]
    public void Progress_ShouldClamp() {
        var iteration = new Iteration();
        for (int index = 0; index < 5; index++) {
            iteration.Next(true);
        }

        ParameterSources.Progress(iteration, 2).Should().Be(Flt64.One);
    }

    /// <summary>几何演化按比值逐次相乘 / A geometric source multiplies by the ratio each step.</summary>
    [Fact]
    public void Geometric_ShouldDecay() {
        var iteration = new Iteration();
        var source = new GeometricSource(new Flt64(100.0), new Flt64(0.5));

        source.Value(iteration).Should().Be(new Flt64(100.0));
        iteration.Next(true);
        source.Value(iteration).Should().Be(new Flt64(50.0));
        iteration.Next(true);
        source.Value(iteration).Should().Be(new Flt64(25.0));
    }

    /// <summary>指数衰减按 e^{-rate·t} / An exponential source decays as e^{-rate*t}.</summary>
    [Fact]
    public void Exponential_ShouldDecay() {
        var iteration = new Iteration();
        var source = new ExponentialSource(new Flt64(1.0), new Flt64(1.0));

        source.Value(iteration).Should().Be(new Flt64(1.0));
        iteration.Next(true);
        source.Value(iteration).ToDouble().Should().BeApproximately(global::System.Math.Exp(-1.0), 1e-12);
    }
}

/// <summary>
/// 初始化算子 / Initializers.
/// </summary>
public sealed class InitializerTests {
    /// <summary>**固定初始化解长度恒为请求数量且请求 0 仍给 1 份** / Fixed initialization honours the amount and yields one entry
    /// even for a zero request.</summary>
    [Fact]
    public void Fixed_ShouldHonourAmount() {
        var initializer = new FixedInitializer(new[] { 1.0, 2.0 });
        var model = new StubModel(value => value[0]);

        initializer.Generate<Flt64, object?, Flt64>(new Iteration(), model, 3, IIntoValue<Flt64>.Identity)
            .Should().HaveCount(3);
        initializer.Generate<Flt64, object?, Flt64>(new Iteration(), model, 0, IIntoValue<Flt64>.Identity)
            .Should().HaveCount(1, "空列表会让种子路径拿到'没有种群' / an empty list starves the seeding path");
    }

    /// <summary>固定初始化把值写入每个解 / Fixed initialization writes the values into every solution.</summary>
    [Fact]
    public void Fixed_ShouldWriteValues() {
        var initializer = new FixedInitializer(new[] { 7.0 });
        var model = new StubModel(value => value[0]);

        IReadOnlyList<Solution<Flt64>> solutions = initializer.Generate<Flt64, object?, Flt64>(
            new Iteration(), model, 1, IIntoValue<Flt64>.Identity);

        solutions[0].Values[0].Should().Be(new Flt64(7.0));
    }

    /// <summary>空固定向量返回空列表 / An empty fixed vector yields an empty list.</summary>
    [Fact]
    public void Fixed_ShouldReturnEmptyForEmptyValues() =>
        new FixedInitializer(Array.Empty<double>())
            .Generate<Flt64, object?, Flt64>(new Iteration(), new StubModel(value => value[0]), 2,
                IIntoValue<Flt64>.Identity)
            .Should().BeEmpty();

    /// <summary>**无界模型下均匀初始化回退到模型初始解** / Uniform initialization falls back to the model without bounds.</summary>
    /// <remarks>
    /// 对无界变量瞎猜区间，会让种群从一开始就落在与问题量级无关的范围里，之后的适应度与收敛曲线全部失真。
    /// Guessing an interval for unbounded variables starts the population in a range unrelated to the problem's scale.
    /// </remarks>
    [Fact]
    public void RandomUniform_ShouldFallBackWithoutBounds() {
        var random = new SearchRandom(1);
        var initializer = new RandomUniformInitializer(() => new Flt64(random.NextDouble()));
        var model = new StubModel(value => value[0]);

        // StubModel 的令牌表为空，模型自带初始解也为空——回退的结果因此是空列表，而不是凭空造出解。
        // The StubModel token table is empty and so is its own initial-solution generator, so the fallback yields nothing rather than
        // inventing solutions.
        initializer.Generate<Flt64, object?, Flt64>(new Iteration(), model, 3, IIntoValue<Flt64>.Identity)
            .Should().BeEmpty("无界时必须回退而不是猜区间 / without bounds it must fall back, not guess");
    }

    /// <summary>**反对立初始化在不崩溃的前提下补充镜像解** / Opposition initialization adds mirrored solutions without crashing.</summary>
    /// <remarks>
    /// 基线的这一实现用 `token.lowerBound!!` 非空断言，模型里有一个无界变量就会抛异常。本移植改为无界变量保持原值。
    /// The baseline dereferences `token.lowerBound!!`, throwing on any unbounded variable. This port keeps the original value instead.
    /// </remarks>
    [Fact]
    public void Opposition_ShouldNotCrashWithoutBounds() {
        var initializer = new OppositionBasedInitializer(new FixedInitializer(new[] { 1.0, 2.0 }));
        var model = new StubModel(value => value[0]);

        IReadOnlyList<Solution<Flt64>> solutions = initializer.Generate<Flt64, object?, Flt64>(
            new Iteration(), model, 2, IIntoValue<Flt64>.Identity);

        solutions.Should().HaveCount(4, "镜像解让数量翻倍 / mirroring doubles the count");
        solutions[2].Values.Should().Equal(new[] { new Flt64(1.0), new Flt64(2.0) },
            "无界变量保持原值 / unbounded variables keep their original value");
    }

    /// <summary>模型初始化透传模型自带初始解 / Model initialization passes the model's own solutions through.</summary>
    [Fact]
    public void Model_ShouldReturnEmptyWhenModelHasNone() =>
        ModelInitializer.Instance.Generate<Flt64, object?, Flt64>(
            new Iteration(), new StubModel(value => value[0]), 2, IIntoValue<Flt64>.Identity)
            .Should().BeEmpty();
}
