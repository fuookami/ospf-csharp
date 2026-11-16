// 剩余三个基准的规模有效性测试 / Scale-validity tests for the remaining three benchmarks.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Benchmark;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Benchmark.Tests;

/// <summary>
/// `NetworkIndexBuild` / `ProjectionIndexBuild` / `PreparationHotPath` 的参数必须真的改变被测规模。
/// The parameters of `NetworkIndexBuild` / `ProjectionIndexBuild` / `PreparationHotPath` must really change the measured scale.
/// </summary>
/// <remarks>
/// 这三个基准**原本就有测试**，但那些测试问的是"构造对不对"（节点是不是四出边、行数是不是变量数的一半），
/// **没有一条问"规模参数有没有进到被测路径"**。教训是：夹具变了、被测路径没变，测出来的趋势就是假的。
/// 所以这里只加一个判据：**被测方法返回的计数必须随参数单调增长**。
///
/// These three benchmarks **already had tests**, but those asked "is the construction right" (does every node have four outgoing arcs, is the row count
/// half the variable count) — **not one asked whether the scale parameter reaches the measured path**. The lesson is that a changed fixture with an
/// unchanged path yields a fake trend. So this adds exactly one criterion: **the measured method's returned count must grow monotonically with the
/// parameter**.
/// </remarks>
public sealed class BenchmarkScaleValidityExtraTests {
    /// <summary>**节点数与弧数必须随 `Size` 增长** / Node and arc counts must grow with `Size`.</summary>
    [Fact]
    public void NetworkIndexBuild_ShouldGrowWithSize() {
        int small = MeasureNodes(256);
        int medium = MeasureNodes(1024);
        int large = MeasureNodes(4096);

        small.Should().Be(256);
        medium.Should().Be(1024);
        large.Should().Be(4096);
        small.Should().BeLessThan(medium);
        medium.Should().BeLessThan(large);

        static int MeasureNodes(int size) {
            var benchmark = new NetworkIndexBuildBenchmark { Size = size };
            benchmark.Setup();
            return benchmark.Nodes.Count;
        }
    }

    /// <summary>**弧数必须随 `Size` 线性增长（每节点四出边）** / Arc count must grow linearly with `Size` (four arcs per node).</summary>
    [Fact]
    public void NetworkIndexBuild_ShouldKeepFourArcsPerNodeAtEverySize() {
        foreach (int size in new[] { 256, 1024, 4096 }) {
            var benchmark = new NetworkIndexBuildBenchmark { Size = size };
            benchmark.Setup();
            benchmark.Arcs.Count.Should().Be(size * 4, "four arcs per node at size {0}", size);
        }
    }

    /// <summary>**投影的决策数必须随 `Size` 增长** / The projection's decision count must grow with `Size`.</summary>
    [Fact]
    public void ProjectionIndexBuild_ShouldGrowWithSize() {
        int small = MeasureDecisions(256);
        int medium = MeasureDecisions(1024);
        int large = MeasureDecisions(4096);

        small.Should().BeLessThan(medium);
        medium.Should().BeLessThan(large);

        static int MeasureDecisions(int size) {
            var benchmark = new ProjectionIndexBuildBenchmark { Size = size };
            benchmark.Setup();
            return benchmark.Create().Decisions.Count;
        }
    }

    /// <summary>**规模变化不得改变决策的块归属分布** / Changing the size must not change how decisions spread across blocks.</summary>
    [Fact]
    public void ProjectionIndexBuild_ShouldKeepTheBlockSpreadAtEverySize() {
        foreach (int size in new[] { 256, 1024, 4096 }) {
            var benchmark = new ProjectionIndexBuildBenchmark { Size = size };
            benchmark.Setup();
            ColumnProjection<Flt64> projection = benchmark.Create();
            projection.Decisions.Select(decision => decision.BlockId).Distinct()
                .Should().HaveCount(ProjectionIndexBuildBenchmark.BlockIds.Count, "every block must be used at size {0}", size);
            foreach (string block in ProjectionIndexBuildBenchmark.BlockIds) {
                projection.Decisions.Count(decision => decision.BlockId == block)
                    .Should().Be(size / ProjectionIndexBuildBenchmark.BlockIds.Count,
                        "blocks split evenly at size {0}", size);
            }
        }
    }

    /// <summary>**素数计数必须随 `Dataset` 增长** / The prime count must grow with `Dataset`.</summary>
    [Fact]
    public void PreparationHotPath_ShouldGrowMonotonicallyAcrossTiers() {
        int small = Measure("small");
        int medium = Measure("medium");
        int large = Measure("large");

        small.Should().BeLessThan(medium);
        medium.Should().BeLessThan(large);

        static int Measure(string dataset) {
            var benchmark = new PreparationHotPathBenchmark { Dataset = dataset };
            benchmark.Setup();
            return benchmark.ColdPrimeSequence();
        }
    }

    /// <summary>**常量数组的元素数必须随 `Dataset` 增长** / The constant array's element count must grow with `Dataset`.</summary>
    [Fact]
    public void PreparationHotPath_ShouldScaleTheArrayWithDataset() {
        int small = MeasureElements("small");
        int medium = MeasureElements("medium");
        int large = MeasureElements("large");

        small.Should().BeLessThan(medium);
        medium.Should().BeLessThan(large);

        static int MeasureElements(string dataset) {
            var benchmark = new PreparationHotPathBenchmark { Dataset = dataset };
            benchmark.Setup();
            return benchmark.ConstantArray().Shape.Size;
        }
    }
    /// <summary>**Benders 基准的规模必须真的进到被测路径** / The Benders benchmark's scale must reach the measured path.</summary>
    /// <remarks>
    /// 用多轮基准量到：`BendersSolve` 三档 **45.4 / 47.1 / 45.6 µs，分配完全相同（129.16 KB）**——
    /// 夹具**丢掉了 `Dataset`**，三档测的是同一条路径。这正是记过两次的那类错误，**第三次**。
    ///
    /// 判据不看耗时（耗时判据会抖），而是看**主问题变量数**：`Dataset` 变了它必须跟着变。
    /// 修复后 `small=4 / medium=6 / large=8`。
    ///
    /// measured with multi-round benchmarks: `BendersSolve` across three tiers was **45.4 / 47.1 / 45.6 µs with identical 129.16 KB allocations** — the fixture
    /// **dropped `Dataset`** and all three tiers measured one path. This is the class of mistake recorded twice already, now **the third time**. The
    /// criterion looks at **the master variable count**, not the timing (timings jitter): changing `Dataset` must change it. After the fix: `small=4 / medium=6 / large=8`.
    /// </remarks>
    [Fact]
    public void BendersSolve_ShouldScaleWithTheDataset() {
        int small = CountBindings("small");
        int medium = CountBindings("medium");
        int large = CountBindings("large");

        small.Should().Be(4);
        medium.Should().Be(6);
        large.Should().Be(8, "the tier count must reach the master model, otherwise all three tiers measure one path");

        static int CountBindings(string dataset) {
            var benchmark = new ConstraintProgrammingBenchmark { Dataset = dataset };
            benchmark.Setup();
            // **用被测方法自己的返回值**：`BendersSolve` 只返回结果的字符串，看不出规模——
            // 所以再跑一次真实求解并把主问题变量数读出来，两者走同一条 `Setup`。
            benchmark.BendersSolve().Should().NotBeNullOrEmpty();
            return benchmark.MasterVariableCount;
        }
    }

    /// <summary>**checkpoint 两个基准的规模必须真的进到被测路径** / The two checkpoint benchmarks must carry the scale.</summary>
    /// <remarks>
    /// `BendersSolve` 刚被抓到丢了 `Dataset`，**同一张夹具的邻居必须一起量**——这是 存在的理由。
    /// 两个基准都走 `_directModel.Snapshot`，而 `_directModel` 按 `count` 建，所以**编码长度应当随档位增长**。
    ///
    /// caught `BendersSolve` dropping `Dataset`; **the neighbours on the same fixture must be measured too**, which is why exists. Both benchmarks go through
    /// `_directModel.Snapshot`, which is built from `count`, so **the encoded length must grow with the tier**.
    /// </remarks>
    [Fact]
    public void CheckpointBenchmarks_ShouldScaleWithTheDataset() {
        int small = CaptureLength("small");
        int medium = CaptureLength("medium");
        int large = CaptureLength("large");

        small.Should().BeLessThan(medium, "a tier that does not change the encoded length measures the same path as the one below it");
        medium.Should().BeLessThan(large);

        static int CaptureLength(string dataset) {
            var benchmark = new ConstraintProgrammingBenchmark { Dataset = dataset };
            benchmark.Setup();
            string captured = benchmark.CheckpointCapture();
            captured.Should().NotBeNullOrEmpty();
            return captured.Length;
        }
    }

    /// <summary>**checkpoint 恢复也必须随档位增长** / The restore path must scale as well.</summary>
    [Fact]
    public void CheckpointRestore_ShouldScaleWithTheDataset() {
        string Decode(string dataset) {
            var benchmark = new ConstraintProgrammingBenchmark { Dataset = dataset };
            benchmark.Setup();
            return benchmark.CheckpointRestore();
        }

        // **恢复返回的是标识而不是长度**，所以这里量的是"三档都能恢复出同一个标识"——
        // 规模是否进到这条路径由快照长度那一条判据覆盖，**两条合起来才说明恢复不是空跑**。
        // **Restore returns an identifier, not a length**, so this measures "all three tiers restore to the same identifier"; whether the scale reaches this path is
        // covered by the snapshot-length criterion — **only both together show the restore is not a no-op**.
        string small = Decode("small");
        string medium = Decode("medium");
        string large = Decode("large");

        small.Should().NotBeNullOrEmpty();
        small.Should().Be(medium);
        medium.Should().Be(large);
    }

    /// <summary>**固定时长降阶随档位增长** / Fixed-duration lowering grows with the tier.</summary>
    /// <remarks>
    /// **三个降阶基准必须分开量。** 第一版把它们**求和**再比——求和让"只有一个随档位变"也能通过，
    /// 而另外两个**夹具写死了 3 个作业**、根本不看 `Dataset`。**一条能被一个真分支满足的判据等于没有判据。**
    ///
    /// **The three lowering benchmarks must be measured separately.** The first version **summed** them, which let "only one of them scales" pass — and the other two
    /// **hard-code 3 jobs** without reading `Dataset`. **A criterion one real branch can satisfy is no criterion.**
    /// </remarks>
    [Fact]
    public void FixedDurationLowering_ShouldScaleWithTheDataset() {
        Lowering("small", static b => b.FixedDurationMipLowering())
            .Should().BeLessThan(Lowering("medium", static b => b.FixedDurationMipLowering()));
        Lowering("medium", static b => b.FixedDurationMipLowering())
            .Should().BeLessThan(Lowering("large", static b => b.FixedDurationMipLowering()));
    }

    /// <summary>**可选时长降阶随档位增长** / Optional-duration lowering grows with the tier.</summary>
    /// <remarks>
    /// 这个夹具此前**写死 3 个 optional 作业**，所以三档返回同一个数——和 `BendersSolve` 丢 `Dataset` 是同一类问题。
    ///
    /// This fixture **hard-coded 3 optional jobs** (measured), so all three tiers returned the same number — the same class of problem as `BendersSolve`
    /// dropping `Dataset`.
    /// </remarks>
    [Fact]
    public void OptionalDurationLowering_ShouldScaleWithTheDataset() {
        Lowering("small", static b => b.OptionalDurationMipLowering())
            .Should().BeLessThan(Lowering("medium", static b => b.OptionalDurationMipLowering()));
        Lowering("medium", static b => b.OptionalDurationMipLowering())
            .Should().BeLessThan(Lowering("large", static b => b.OptionalDurationMipLowering()));
    }

    /// <summary>**可变时长降阶随档位增长** / Variable-duration lowering grows with the tier.</summary>
    [Fact]
    public void VariableDurationLowering_ShouldScaleWithTheDataset() {
        Lowering("small", static b => b.VariableDurationMipLowering())
            .Should().BeLessThan(Lowering("medium", static b => b.VariableDurationMipLowering()));
        Lowering("medium", static b => b.VariableDurationMipLowering())
            .Should().BeLessThan(Lowering("large", static b => b.VariableDurationMipLowering()));
    }

    /// <summary>**快照编码随档位增长** / Snapshot encoding grows with the tier.</summary>
    [Fact]
    public void SnapshotEncode_ShouldScaleWithTheDataset() {
        Encode("small").Should().BeLessThan(Encode("medium"));
        Encode("medium").Should().BeLessThan(Encode("large"));

        static int Encode(string dataset) {
            var benchmark = new ConstraintProgrammingBenchmark { Dataset = dataset };
            benchmark.Setup();
            return benchmark.SnapshotEncode();
        }
    }

    private static int Lowering(
        string dataset,
        Func<ConstraintProgrammingBenchmark, int> measure) {
        var benchmark = new ConstraintProgrammingBenchmark { Dataset = dataset };
        benchmark.Setup();
        return measure(benchmark);
    }
    /// <summary>**三份可求解排程夹具必须随档位变化** / The three solvable scheduling fixtures must follow the tier.</summary>
    /// <remarks>
    /// 多轮基准量到 `VariableDurationSchedulingSolve` 三档 **209.2 / 201.0 / 198.4 µs，分配完全相同（317.27 KB）**——
    /// 三份 `BuildSolvableSchedulingModel` 的作业数**全部写死**（4 / 3 / 3），`Dataset` 到不了被测路径。
    /// **这是同一类问题的第四次**（ BendersSolve、 邻居、 两个降阶夹具）。
    ///
    /// **判据不读结果字符串**：`SchedulingSolve` 返回的是 `ToString`，长度与作业数无关（实测三档都是 2910 字符）。
    /// 改读**求解器真正消费的那个模型**的变量数。
    ///
    /// Multi-round measurement found `VariableDurationSchedulingSolve` at **209.2 / 201.0 / 198.4 µs with identical 317.27 KB allocations** — all three
    /// `BuildSolvableSchedulingModel` calls **hard-code** their job count (4 / 3 / 3), so `Dataset` never reaches the measured path. **Fourth instance of the same
    /// class.** The criterion does **not** read the result string (its length is tier-independent — 2910 characters for all three); it reads the variable count of
    /// **the model the solver consumes**.
    /// </remarks>
    [Fact]
    public void SolvableSchedulingFixtures_ShouldScaleWithTheDataset() {
        Count("small").Should().BeLessThan(Count("medium"));
        Count("medium").Should().BeLessThan(Count("large"));

        static int Count(string dataset) {
            var benchmark = new ConstraintProgrammingBenchmark { Dataset = dataset };
            benchmark.Setup();
            return benchmark.SolvableSchedulingVariableCount;
        }
    }
    /// <summary>**未覆盖的基准方法逐个量完** / The previously uncovered benchmark methods, measured one by one.</summary>
    /// <remarks>
    /// 修完 `ConstraintProgrammingBenchmark` 之后，这里把另外四个基准类里**没被判据覆盖**的方法量一遍。
    /// 结果分两类：**能当规模信号**的，和**不能的**。**不能的那些必须把原因写下来**，否则下一个人还会拿它当信号。
    ///
    /// After fixed `ConstraintProgrammingBenchmark`, measures the methods the criteria did **not** cover across the other four benchmark
    /// classes. They fall into two groups: **usable as a scale signal** and **not**. **The unusable ones need their reason on record**, otherwise the next person
    /// reaches for them again.
    /// </remarks>
    [Fact]
    public void UncoveredBenchmarkMethods_ShouldBeMeasuredOneByOne() {
        // **能当规模信号的**：逐个断言严格增长，不做求和。
        // **Usable as a scale signal**: each asserted to grow strictly, never summed (the lesson).
        Tiers("core transpose", "small", "medium", "large", static dataset => {
            var benchmark = new CoreHotPathBenchmark { Dataset = dataset };
            benchmark.Setup();
            return benchmark.SparseMatrixTranspose();
        });
        Tiers("array blockGet", "small", "medium", "large", static dataset => {
            var benchmark = new MultiArrayHotPathBenchmark { Dataset = dataset };
            benchmark.Setup();
            return benchmark.BlockGetAndContains();
        });
        Tiers("array blockSet", "small", "medium", "large", static dataset => {
            var benchmark = new MultiArrayHotPathBenchmark { Dataset = dataset };
            benchmark.Setup();
            return benchmark.BlockSetAndRemove();
        });
        Tiers("symbol linPoly", "small", "medium", "large", static dataset => {
            var benchmark = new SymbolCombineBenchmark { Dataset = dataset };
            benchmark.Setup();
            return benchmark.CombineLinearPolynomialGeneric();
        });
        Tiers("symbol quadPoly", "small", "medium", "large", static dataset => {
            var benchmark = new SymbolCombineBenchmark { Dataset = dataset };
            benchmark.Setup();
            return benchmark.CombineQuadraticPolynomialGeneric();
        });
        Tiers("symbol mutLin", "small", "medium", "large", static dataset => {
            var benchmark = new SymbolCombineBenchmark { Dataset = dataset };
            benchmark.Setup();
            return benchmark.MutableLinearAccumulateAndCombine();
        });
        Tiers("symbol mutQuad", "small", "medium", "large", static dataset => {
            var benchmark = new SymbolCombineBenchmark { Dataset = dataset };
            benchmark.Setup();
            return benchmark.MutableQuadraticAccumulateAndCombine();
        });
        Tiers("dump coefficients", "small", "medium", "large", static dataset => {
            var benchmark = new CorePluginDumpBenchmark { Dataset = dataset };
            benchmark.Setup();
            return (int)benchmark.CollectObjectiveCoefficients();
        });
    }

    /// <summary>**三个不能当规模信号的返回值，原因逐条钉住** / The three values that cannot carry a scale signal, each with its reason pinned.</summary>
    /// <remarks>
    /// 这三条不是"修好它们"，而是**把"它们不能当信号"变成判据**。少了它们，下一个人会在 `dump sparse rows` 上看到 `NaN`、
    /// 在 `array fromList` 上看到"规模越大值越小"，然后花半天去找一个不存在的缺陷。
    ///
    /// These three do not "fix" anything: they turn **"this value cannot be a signal"** into a criterion. Without them the next person sees `NaN` from
    /// `dump sparse rows` or "bigger scale, smaller value" from `array fromList` and spends half a day hunting a defect that is not there.
    /// </remarks>
    [Fact]
    public void ValuesThatCannotCarryAScaleSignal_ShouldSayWhy() {
        // **`array fromList` 返回某个位置的元素值（`i % 97`）**，与规模无关：实测 88 → 15 → 6。
        // **`array fromList` returns the element value at one position (`i % 97`)**, unrelated to the scale: measured 88 → 15 → 6.
        int smallFromList = FromList("small");
        int largeFromList = FromList("large");
        largeFromList.Should().BeLessThan(smallFromList,
            "measured: this value shrinks as the tier grows, so it must never be used as a scale signal");

        // **`dump sparse rows` 返回 NaN 是既有行为**：它把 ±∞ 累加进 checksum（`walkSparseRowsAndBounds` 逐字如此）。
        // **`dump sparse rows` returns NaN, and that is the established behaviour**: it adds ±∞ into the checksum (`walkSparseRowsAndBounds` does exactly this).
        SparseRows("small").Should().Be(double.NaN,
            "measured: adding ±∞ makes the checksum NaN; it cannot carry a scale signal");

        // **`dump segments` 不单调**：分块大小随规模变大，块数反而变小（实测 26 → 21 → 82）。
        // **`dump segments` is not monotonic**: the segment size grows with the scale so the count can shrink (measured 26 → 21 → 82).
        Segments("small").Should().BeGreaterThan(Segments("medium"),
            "measured: a bigger scale yields a bigger segment size, hence fewer segments — not a scale signal either");

        static int FromList(string dataset) {
            var benchmark = new MultiArrayHotPathBenchmark { Dataset = dataset };
            benchmark.Setup();
            return benchmark.FromListRowMajor();
        }

        static double SparseRows(string dataset) {
            var benchmark = new CorePluginDumpBenchmark { Dataset = dataset };
            benchmark.Setup();
            return benchmark.WalkSparseRowsAndBounds();
        }

        static int Segments(string dataset) {
            var benchmark = new CorePluginDumpBenchmark { Dataset = dataset };
            benchmark.Setup();
            return benchmark.ComputeConstraintSegments();
        }
    }

    private static void Tiers(
        string name,
        string small,
        string medium,
        string large,
        Func<string, int> measure) {
        measure(small).Should().BeLessThan(measure(medium), name + " must grow from small to medium");
        measure(medium).Should().BeLessThan(measure(large), name + " must grow from medium to large");
    }
}
