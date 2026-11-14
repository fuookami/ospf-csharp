#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Framework.Csp1d.Application.Model;
using Fuookami.Ospf.Framework.Csp1d.Application.Service;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

using PoolResult = System.ValueTuple<
    Fuookami.Ospf.Core.Solver.Output.FeasibleSolverOutput<Fuookami.Ospf.Math.Algebra.Number.Flt64>,
    System.Collections.Generic.List<System.Collections.Generic.List<Fuookami.Ospf.Math.Algebra.Number.Flt64>>>;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// 加进来的列必须**真的进入下一轮 LP 模型**，而不只是进了方案池。
/// An added column must **really enter the next LP model**, not merely the plan pool.
/// </summary>
/// <remarks>
/// **B337 记录的缺口**：`AddColumns` 成功即视为生效，但**新列是否进入下一轮 LP 的变量集**没有被验证。
/// "池里多了一条"与"LP 多了一个变量"是两件事——**后者才是列生成的意义**。
///
/// 判据靠**在求解器入口采样**：`IColumnGenerationSolver` 的 LP 入口收到的是 `LinearMetaModel`，
/// 它的 `Tokens` 集合就是模型的变量/约束记号表。**每轮采样它的数量**，
/// 若加列真的注册进了模型，**第二轮采样值必须更大**。
///
/// **这比"方案池变大了"强**：池是 `ProduceAggregation` 自己的账本，
/// 加列后忘了 `model.Add(variable)` 也照样会让池变大，而 LP 根本没多一个变量。
///
/// **The gap recorded in B337**: `AddColumns` returning successfully was treated as effective, but **whether the new column enters the next LP's variable set** was never
/// verified. "The pool grew by one" and "the LP grew by one variable" are different things — **the latter is what column generation is for**.
///
/// The criterion **samples at the solver entry**: the `IColumnGenerationSolver` LP entry receives a `LinearMetaModel` whose `Tokens` collection is the model's
/// variable/constraint token table. **Sampling its size each round**, if the added column really registered into the model, **the second sample must be larger**.
///
/// **This is stronger than "the pool grew"**: the pool is `ProduceAggregation`'s own bookkeeping — forgetting `model.Add(variable)` still grows the pool while the LP
/// gains no variable.
/// </remarks>
public class Csp1dColumnEnteringLpTests {
    private static Quantity<Flt64> Millimetres(double value) =>
        new(new Flt64(value), LengthUnits.Millimeter);

    private static Material<Flt64> MakeMaterial() =>
        new(
            id: "M1",
            name: "material",
            widthRange: new WidthRange<Flt64>(
                Width: new QuantityRange<Flt64>(
                    LowerBound: Millimetres(100.0),
                    UpperBound: Millimetres(200.0)),
                Step: Millimetres(1.0)));

    private static Csp1dProblem<Flt64> MakeProblem() {
        Material<Flt64> material = MakeMaterial();
        var product = new Product<Flt64>(
            id: "A",
            name: "product-a",
            width: new List<Quantity<Flt64>> { Millimetres(50.0) });
        var builder = new Csp1dProblemBuilder<Flt64>();
        builder.AddProduct(product);
        builder.AddMaterial(material);
        builder.AddDemand(new ProductDemand<Flt64>(product, Millimetres(50.0)));
        return builder.Build();
    }

    /// <summary>第一轮给一条新列、之后不给的定价替身 / A pricing double yielding one new column, then none.</summary>
    private sealed class OneShotPricingGenerator : ICsp1dPricingGenerator<CuttingPlan<Flt64>, Csp1dPricingInput<Flt64>> {
        private readonly Material<Flt64> _material;

        public OneShotPricingGenerator(Material<Flt64> material) => _material = material;

        public int Calls { get; private set; }

        public IReadOnlyList<CuttingPlan<Flt64>> Generate(Csp1dPricingInput<Flt64> input) {
            ++Calls;
            if (Calls > 1) {
                return new List<CuttingPlan<Flt64>>();
            }
            var product = new Product<Flt64>(
                id: "A",
                name: "product-a",
                width: new List<Quantity<Flt64>> { Millimetres(50.0) });
            return new List<CuttingPlan<Flt64>> {
                new(
                    id: "priced-1",
                    material: _material,
                    slices: new List<CuttingPlanSlice<Flt64>> { new(product, Millimetres(50.0)) }),
            };
        }
    }

    /// <summary>在 LP 入口采样模型记号数量的求解器包装 / A solver wrapper sampling the model's token count at the LP entry.</summary>
    private sealed class SamplingSolver : IColumnGenerationSolver {
        private readonly IColumnGenerationSolver _inner;

        public SamplingSolver(IColumnGenerationSolver inner) => _inner = inner;

        public List<int> LpTokenCounts { get; } = new();

        public string Name => _inner.Name;

        public Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
            string name,
            LinearMetaModel<Flt64> metaModel,
            bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null) =>
            _inner.SolveMILPAsync(name, metaModel, toLogModel, registrationStatusCallBack, solvingStatusCallBack);

        public Task<Result<PoolResult, ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
            string name,
            LinearMetaModel<Flt64> metaModel,
            ulong amount,
            bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null) =>
            _inner.SolveMILPAsync(name, metaModel, amount, toLogModel, registrationStatusCallBack, solvingStatusCallBack);

        public Task<Result<FeasibleSolverOutput<V>, ErrorCode, Error<ErrorCode>>> SolveMILPAsAsync<V>(
            string name,
            LinearMetaModel<Flt64> metaModel,
            IIntoValue<V> converter,
            bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null)
            where V : struct, IRealNumber<V>, INumberField<V> =>
            _inner.SolveMILPAsAsync(name, metaModel, converter, toLogModel, registrationStatusCallBack, solvingStatusCallBack);

        public Task<Result<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>>> SolveLPAsync(
            string name,
            LinearMetaModel<Flt64> metaModel,
            bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null) {
            LpTokenCounts.Add(metaModel.Tokens.Tokens.Count);
            return _inner.SolveLPAsync(name, metaModel, toLogModel, registrationStatusCallBack, solvingStatusCallBack);
        }

        public Task<Result<IColumnGenerationSolver.LpResultOf<V>, ErrorCode, Error<ErrorCode>>> SolveLPAsAsync<V>(
            string name,
            LinearMetaModel<Flt64> metaModel,
            IIntoValue<V> converter,
            bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null)
            where V : struct, IRealNumber<V>, INumberField<V> =>
            _inner.SolveLPAsAsync(name, metaModel, converter, toLogModel, registrationStatusCallBack, solvingStatusCallBack);
    }

    /// <summary>**新列必须真的进入下一轮 LP 模型** / The new column must really enter the next LP model.</summary>
    [Fact]
    public async Task SolveWithTraceAsync_ShouldRegisterTheAddedColumnIntoTheNextLpModel() {
        Material<Flt64> material = MakeMaterial();
        var sampling = new SamplingSolver(new MockColumnGenerationSolver());
        var generation = new Csp1dColumnGeneration<Flt64>(
            sampling,
            pricingGenerator: new OneShotPricingGenerator(material));

        await generation.SolveWithTraceAsync(MakeProblem());

        sampling.LpTokenCounts.Should().HaveCountGreaterThanOrEqualTo(2,
            "the loop must solve the LP again after adding the column");
        sampling.LpTokenCounts[1].Should().BeGreaterThan(
            sampling.LpTokenCounts[0],
            "the added column must register a variable into the model, growing its token table");
    }

    /// <summary>**没有加列时模型记号数不得增长** / Without added columns the model's token count must not grow.</summary>
    /// <remarks>
    /// **反向判据**：若模型增长来自别的东西（例如每次求解都重复注册变量），
    /// 上一条会在"没加列"的场景下也通过。这条用**默认定价**（产不出新列）跑，
    /// 要求两轮的记号数**相等**。
    ///
    /// **The reverse criterion**: if growth came from something else (say variables re-registered each solve), the previous criterion would also pass when **no column was
    /// added**. This runs with **default pricing** (which yields nothing) and requires the two token counts to be **equal**.
    /// </remarks>
    [Fact]
    public async Task SolveWithTraceAsync_ShouldNotGrowTheModelWhenNoColumnIsAdded() {
        var sampling = new SamplingSolver(new MockColumnGenerationSolver());
        var generation = new Csp1dColumnGeneration<Flt64>(sampling);

        await generation.SolveWithTraceAsync(MakeProblem());

        sampling.LpTokenCounts.Should().NotBeEmpty("the LP must be solved at least once");
        sampling.LpTokenCounts.Should().OnlyContain(
            count => count == sampling.LpTokenCounts[0],
            "without added columns the model's token table must not change");
    }
}
