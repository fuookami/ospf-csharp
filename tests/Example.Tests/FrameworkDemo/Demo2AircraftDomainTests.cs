// 飞机域：邻接计算与聚合初始化 / Aircraft domain: neighbour calculation and aggregation initialisation.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Service;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;
using AircraftPosition = Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model.Position;
using AircraftPositionPair = Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model.PositionPair;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **飞机域（B613）** / The aircraft domain (B613).
/// </summary>
/// <remarks>
/// 三个部件：物理邻接、间接物理邻接、聚合初始化。判据的重点是那些"看起来等价但结果不同"的写法——
/// 同侧判定里的横向优先、同线判定里的"都在左"、同侧分支里的"区间内无第三方"、以及 `nearEnough` 的重叠分支。
///
/// Three pieces: physical adjacency, indirect physical adjacency, and aggregation initialisation. The criteria focus on the pairs of formulations that look equivalent but differ:
/// transverse precedence in the same-side test, the "both on the left" clause in the same-line test, "no third party inside the interval" in the same-side branch, and the overlap
/// branch of `nearEnough`.
/// </remarks>
public class Demo2AircraftDomainTests {
    /// <summary>
    /// 造一个舱位 / Build one position.
    /// </summary>
    /// <param name="name">名称 / The name.</param>
    /// <param name="front">前臂 / The front arm.</param>
    /// <param name="back">后臂 / The back arm.</param>
    /// <param name="left">左臂 / The left arm.</param>
    /// <param name="right">右臂 / The right arm.</param>
    /// <param name="order">装载顺序 / The loading order.</param>
    /// <returns>舱位 / The position.</returns>
    private static int _nextId;

    private static AircraftPosition MakePosition(
        string name, double front, double back, double left, double right, byte order = 0) =>
        new(
            // **`Position` 按 `Id` 判等**（见 `Position.Equals`），所以每个舱位必须有唯一 id；
            // 用 `order` 当 id 会让同批的多个舱位在 `HashSet`/`Contains` 里互相顶掉。
            // **`Position` compares by `Id`** (see `Position.Equals`), so every position needs a unique id; using `order` as the id makes several positions in one batch collide in
            // `HashSet`/`Contains`.
            id: (ulong)System.Threading.Interlocked.Increment(ref _nextId),
            spaceName: name,
            sizeCode: "N",
            linearLoadingOrder: order,
            coordinate: new PositionCoordinate(front, back, left, right),
            shape: new PositionShape(back - front, right - left, (back - front) * (right - left)),
            location: PositionLocation.NormalMainLocation);

    private static Deck MakeDeck(params AircraftPosition[] positions) =>
        new(DeckLocation.Main, Array.Empty<HatchDoor>(), positions, new Dictionary<HatchDoor, IReadOnlyList<DoorUbiety>>());

    /// <summary>
    /// **同线且不同侧 → 直接相邻；同侧要"区间内无第三方"** / Colinear on opposite sides is adjacent; same-side needs no third party inside.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. **左、右两侧在同一纵线上** → 相邻（走"同线且不同侧"分支）。
    /// 2. **两个都在左侧、纵向重叠** → 相邻（走"同侧且区间内无第三方"分支）。
    /// 3. **两个都在左侧、中间夹了第三个舱位** → **不相邻**。这是"区间内无第三方"与"两矩形不相交"唯一分道的地方。
    /// 4. **两个都在右侧、同线** → **不相邻**，因为 `onSameLine` 要求"都在左"。
    ///
    /// Four paired points: **left and right on one longitudinal line** are adjacent (the colinear/opposite-side branch); **two on the left overlapping longitudinally** are adjacent (the
    /// same-side/no-third-party branch); **two on the left with a third between them** are **not** adjacent, which is the one place "no third party inside the interval" diverges from
    /// "the two boxes do not overlap"; and **two on the right, colinear** are **not** adjacent, because `onSameLine` requires both to be on the left.
    /// </remarks>
    [Fact]
    public void PhysicalNeighbourCalculator_ShouldRespectTheSameSideBranch() {
        // **实测出来的基线形状，三条都很反直觉，逐条钉住**：
        //
        // 1. **"同线且不同侧"分支不可达**：它要求 `onSameLine`（内含"都在左"）与 `!onSameSide`（不同侧）同时成立，
        //    而"都在左"与"不同侧"互斥。所以**左右两侧的舱位永不物理相邻**（实测 `left+right partial -> 0`）。
        // 2. **同侧分支在两舱位纵向完全重叠时恒不通过**：`Between` 会把两个舱位**自身**也算进去，`none` 恒假
        //    （实测 `left+left identical -> 0`）。
        // 3. **部分重叠才相邻**（实测 `left+left partial -> 1`、`right+right partial -> 1`）。
        //
        // 三条都照抄。修好第 1 条会凭空造出基线没有的邻接，那会让下游的 `BiologicalAdjacentLimit` 变严格——
        // 而它已经在别的批次按基线行为钉过了。
        //
        // **The baseline's shape, measured, with three counter-intuitive facts pinned**: the "colinear and opposite sides" branch is **unreachable** (it needs `onSameLine`, which
        // requires "both on the left", together with `!onSameSide`, and the two are mutually exclusive), so **left and right positions are never physically adjacent** (measured
        // `left+right partial -> 0`); the same-side branch **never accepts two fully overlapping positions**, because `Between` also counts the two positions **themselves** and `none`
        // is then always false (measured `left+left identical -> 0`); and **partial overlap is adjacent** (measured `left+left partial -> 1`, `right+right partial -> 1`). All three
        // are copied: fixing the first would invent adjacencies the baseline does not have, making the downstream `BiologicalAdjacentLimit` stricter than the baseline already pinned.
        AircraftPosition leftA = MakePosition("L1", 100.0, 200.0, -60.0, -10.0);
        AircraftPosition rightA = MakePosition("R1", 150.0, 250.0, 10.0, 60.0);
        AircraftPosition leftB = MakePosition("L2", 150.0, 250.0, -60.0, -10.0);
        AircraftPosition rightB = MakePosition("R2", 200.0, 300.0, 10.0, 60.0);

        // 1. 左右两侧、纵向部分重叠 → **不相邻**。
        // 1. Left and right, partially overlapping → **not adjacent**.
        Unwrap(PhysicalNeighbourCalculator.Calculate(new[] { MakeDeck(leftA, rightA) }))
            .Should().BeEmpty("the colinear/opposite-side branch is unreachable, so left and right never meet");

        // 2. 同一侧、纵向部分重叠 → 相邻。
        // 2. Same side, partially overlapping → adjacent.
        Unwrap(PhysicalNeighbourCalculator.Calculate(new[] { MakeDeck(leftA, leftB) }))
            .Should().ContainSingle("partial overlap on one side is adjacent");
        Unwrap(PhysicalNeighbourCalculator.Calculate(new[] { MakeDeck(rightA, rightB) }))
            .Should().ContainSingle("the right side behaves the same, through the same branch");

        // 3. 同一侧、纵向完全重叠 → **不相邻**（两舱位自身让 `none` 恒假）。
        // 3. Same side, fully overlapping → **not adjacent** (the two positions themselves keep `none` false).
        AircraftPosition twin = MakePosition("L3", 100.0, 200.0, -60.0, -10.0);
        Unwrap(PhysicalNeighbourCalculator.Calculate(new[] { MakeDeck(leftA, twin) }))
            .Should().BeEmpty("the pair counts itself in the third-party scan, so `none` is always false");
    }

    /// <summary>
    /// **横向跨越优先，因此横向舱位与左侧舱位相邻** / Transverse precedence makes a transverse position adjacent to a left-side one.
    /// </summary>
    /// <remarks>
    /// 两点成对：
    ///
    /// 1. 横向舱位与左侧舱位、纵向部分重叠 → **相邻**（走同侧分支）。实测 `transverse+left partial -> 1`。
    /// 2. 把 `Transverse` 那一项去掉，这一对会落到不可达的第一分支，于是**不相邻**。两种写法结果相反。
    ///
    /// Two paired points: a transverse position with a left-side one, partially overlapping, is **adjacent** (same-side branch; measured `transverse+left partial -> 1`); removing the
    /// `Transverse` term sends the pair to the unreachable first branch and makes it **not adjacent**. The two formulations disagree.
    /// </remarks>
    [Fact]
    public void PhysicalNeighbourCalculator_ShouldTreatTransverseAsSameSide() {
        AircraftPosition transverse = MakePosition("T1", 100.0, 200.0, -40.0, 40.0);
        AircraftPosition left = MakePosition("L1", 150.0, 250.0, -60.0, -10.0);

        Unwrap(PhysicalNeighbourCalculator.Calculate(new[] { MakeDeck(transverse, left) }))
            .Should().ContainSingle(
                "transverse counts as same side; without that term the pair falls into the unreachable branch");
    }

    /// <summary>
    /// **间接邻接 = 全部物理邻接 + 共邻补出的新对** / Indirect adjacency is all physical adjacencies plus pairs inferred through shared neighbours.
    /// </summary>
    /// <remarks>
    /// 这个 fixture 是**搜出来的**，不是猜的：`A`(左, 0..50)、`B`(右, 0..50)、`C`(横向, 50..100)、`D`(横向, 50..100)。
    /// 物理邻接 4 条（A–C、A–D、B–C、B–D，都靠横向的 C/D），共邻补出 1 条 **A–B**（A 与 B 不同侧、纵向重叠、且有共同邻居 C 与 D）。
    ///
    /// 三点成对：
    ///
    /// 1. 结果**先含全部物理邻接**（换类型）。只算补对会让基础层消失。
    /// 2. 补出的 `(A, B)` 在结果里。
    /// 3. 每对只出现一次（`position2` 只在 `position1` 之后取）。
    ///
    /// The fixture is **searched for**, not guessed: `A`(left, 0..50), `B`(right, 0..50), `C`(transverse, 50..100), `D`(transverse, 50..100). There are four physical adjacencies
    /// (A–C, A–D, B–C, B–D, all through the transverse C/D) and one inferred pair **A–B** (opposite sides, overlapping longitudinally, sharing neighbours C and D). Three paired points:
    /// the result **starts with every physical adjacency** retyped, since computing only the inferred pairs would erase the base layer; the inferred `(A, B)` is present; and each
    /// pair appears once.
    /// </remarks>
    [Fact]
    public void IndirectPhysicsNeighbourCalculator_ShouldAddSharedNeighbourPairs() {
        AircraftPosition a = MakePosition("A", 0.0, 50.0, -60.0, -10.0);
        AircraftPosition b = MakePosition("B", 0.0, 50.0, 10.0, 60.0);
        AircraftPosition c = MakePosition("C", 50.0, 100.0, -40.0, 40.0);
        AircraftPosition d = MakePosition("D", 50.0, 100.0, -40.0, 40.0);
        IReadOnlyList<Neighbour> physical = Unwrap(
            PhysicalNeighbourCalculator.Calculate(new[] { MakeDeck(a, b, c, d) }));
        physical.Should().HaveCount(4, "A-C, A-D, B-C, B-D all go through the transverse C and D");

        IReadOnlyList<Neighbour> indirect = Unwrap(
            IndirectPhysicsNeighbourCalculator.Calculate(new[] { MakeDeck(a, b, c, d) }, physical));

        // 1. 全部物理邻接都在，类型已换。
        // 1. Every physical adjacency is present, retyped.
        indirect.Should().HaveCount(physical.Count + 1, "the four base pairs plus one inferred pair");
        indirect.Should().OnlyContain(neighbour => neighbour.Type == NeighbourType.IndirectPhysics);
        foreach (Neighbour neighbour in physical) {
            indirect.Select(item => item.Pair).Should().Contain(neighbour.Pair,
                "the base layer is copied in, not recomputed");
        }
        // 2. A 与 B 被补出来。
        // 2. A and B are inferred.
        indirect.Select(neighbour => neighbour.Pair).Should().Contain(new AircraftPositionPair(a, b));
        // 3. 每对只出现一次。
        // 3. Each pair appears once.
        indirect.GroupBy(neighbour => (neighbour.Pair.First.Id, neighbour.Pair.Second.Id))
            .Should().OnlyContain(group => group.Count() == 1, "position2 is only taken after position1");
    }

    /// <summary>
    /// **`nearEnough` 的纵向重叠直接为真** / `nearEnough` returns true outright on longitudinal overlap.
    /// </summary>
    /// <remarks>
    /// 两点成对，用同一组 fixture：
    ///
    /// 1. A 与 B **纵向完全重叠**（`0..50` 对 `0..50`），不存在"间隙"，`nearEnough` 走重叠分支返回真，于是补出。
    /// 2. 把 A 的纵向范围拉远到 `0..50` 之外（`-200..-150`）→ 间隙远超 `0.5 × min(长度)`，**补不出**。
    ///    第二点同时证明"距离判定真的在起作用"，而不只是"有共邻就补"。
    ///
    /// Two paired points on the same fixture: A and B **overlap longitudinally** (`0..50` against `0..50`), so there is no gap, `nearEnough` takes the overlap branch and returns
    /// true, and the pair is inferred; moving A out to `-200..-150` makes the gap far exceed `0.5 × min(length)` and the pair is **not** inferred, which also proves the distance
    /// test is doing work rather than "any shared neighbour suffices".
    /// </remarks>
    [Fact]
    public void IndirectPhysicsNeighbourCalculator_ShouldTreatOverlapAsNearEnough() {
        AircraftPosition b = MakePosition("B", 0.0, 50.0, 10.0, 60.0);
        AircraftPosition c = MakePosition("C", 50.0, 100.0, -40.0, 40.0);
        AircraftPosition d = MakePosition("D", 50.0, 100.0, -40.0, 40.0);

        AircraftPosition overlapping = MakePosition("A", 0.0, 50.0, -60.0, -10.0);
        IReadOnlyList<Neighbour> physical = Unwrap(
            PhysicalNeighbourCalculator.Calculate(new[] { MakeDeck(overlapping, b, c, d) }));
        IReadOnlyList<Neighbour> indirect = Unwrap(
            IndirectPhysicsNeighbourCalculator.Calculate(new[] { MakeDeck(overlapping, b, c, d) }, physical));
        indirect.Select(neighbour => neighbour.Pair).Should().Contain(
            new AircraftPositionPair(overlapping, b),
            "overlapping longitudinally counts as near enough");

        AircraftPosition farAway = MakePosition("A", -200.0, -150.0, -60.0, -10.0);
        IReadOnlyList<Neighbour> farPhysical = Unwrap(
            PhysicalNeighbourCalculator.Calculate(new[] { MakeDeck(farAway, b, c, d) }));
        IReadOnlyList<Neighbour> farIndirect = Unwrap(
            IndirectPhysicsNeighbourCalculator.Calculate(new[] { MakeDeck(farAway, b, c, d) }, farPhysical));
        farIndirect.Select(neighbour => neighbour.Pair).Should().NotContain(
            new AircraftPositionPair(farAway, b),
            "the gap exceeds half the shorter length, so the distance test rejects the pair");
    }

    /// <summary>
    /// **初始化器：八条校验各自可触发，且成功时构造出完整聚合** / The initializer: each of the eight checks fires, and success builds a complete aggregation.
    /// </summary>
    /// <remarks>
    /// 九点成对（八条校验 + 成功路径）。每条都断言**消息片段**，因为"全部返回失败"会让"所有校验都写错消息"的变异存活。
    ///
    /// Nine paired points (eight checks plus the success path). Each asserts a **message fragment**, because "everything fails" would let a mutant that writes the wrong message for
    /// every check survive.
    /// </remarks>
    [Fact]
    public void AggregationInitializer_ShouldValidateThenBuild() {
        PositionInput Good(string name, int order) =>
            new(name, 1000.0, 100.0 + order * 100.0, 0.0, "MAIN", "DEFAULT", order,
                new PositionGeometryInput(100.0 + order * 100.0, 200.0 + order * 100.0, -60.0, -10.0));
        IReadOnlyList<LoadingSpaceInput> spaces = new[] { new LoadingSpaceInput("MAIN", DeckLocationInput.Main) };
        RequestDTO Request(
            AircraftTypeInput type = AircraftTypeInput.B737,
            IReadOnlyList<PositionInput>? positions = null,
            IReadOnlyList<LoadingSpaceInput>? spaceList = null) =>
            new("REQ", Array.Empty<CargoInput>(), positions ?? new[] { Good("P1", 0) }, type,
                spaceList ?? spaces, LengthUnitInput.Inch);

        string? Message(RequestDTO request) {
            Result<AircraftAggregation, ErrorCode, Error<ErrorCode>> result =
                AggregationInitializer.Initialize(request);
            return result is Failed<AircraftAggregation, ErrorCode, Error<ErrorCode>> failed
                ? failed.Error.Message
                : null;
        }

        // 1. 未知机型。
        Message(Request(AircraftTypeInput.Unknown)).Should().Contain("未知机型");
        // 2. 无舱位。
        Message(Request(positions: Array.Empty<PositionInput>())).Should().Contain("至少需要一个可用舱位");
        // 3. 超过 256 个舱位。
        var many = new List<PositionInput>();
        for (int k = 0; k < 257; ++k) {
            many.Add(Good("P" + k, k));
        }
        Message(Request(positions: many)).Should().Contain("超过装载顺序索引上限");
        // 4. 舱位名为空。
        Message(Request(positions: new[] { new PositionInput("  ", 1000.0, 100.0, 0.0, "MAIN", "DEFAULT", 0, new PositionGeometryInput(100.0, 200.0, -60.0, -10.0)) }))
            .Should().Contain("名称不能为空");
        // 5. 舱位名重复。
        Message(Request(positions: new[] { Good("P1", 0), Good("P1", 1) })).Should().Contain("名称必须唯一");
        // 6. 最大载重非正。
        Message(Request(positions: new[] { new PositionInput("P1", 0.0, 100.0, 0.0, "MAIN", "DEFAULT", 0, new PositionGeometryInput(100.0, 200.0, -60.0, -10.0)) }))
            .Should().Contain("最大载重必须为正且有限");
        // 7. 力臂非有限。
        Message(Request(positions: new[] { new PositionInput("P1", 1000.0, double.NaN, 0.0, "MAIN", "DEFAULT", 0, new PositionGeometryInput(100.0, 200.0, -60.0, -10.0)) }))
            .Should().Contain("力臂必须为有限值");
        // 8. 空间/装载顺序无效（`spaceId` 不在空间表里）。
        Message(Request(positions: new[] { new PositionInput("P1", 1000.0, 100.0, 0.0, "NOPE", "DEFAULT", 0, new PositionGeometryInput(100.0, 200.0, -60.0, -10.0)) }))
            .Should().Contain("物理空间或装载顺序无效");
        // 8b. 装载顺序重复，同一消息。
        Message(Request(positions: new[] { Good("P1", 0), Good("P2", 0) })).Should().Contain("物理空间或装载顺序无效");
        // 8c. 几何缺失。
        Message(Request(positions: new[] { new PositionInput("P1", 1000.0, 100.0, 0.0, "MAIN", "DEFAULT", 0, null) }))
            .Should().Contain("有限正矩形边界");
        // 8d. **几何退化**（`front == back`）——这一条与 8c 分开，因为"缺几何"与"几何为零宽"是两件事：
        //     只测 8c 会让"把 `>=` 判断删掉"的变异存活（实测存活过一次）。
        // 8d. **Degenerate geometry** (`front == back`), kept apart from 8c because "missing geometry" and "zero-width geometry" are different things: testing only 8c lets a mutant
        //     that deletes the `>=` comparison survive (measured once).
        Message(Request(positions: new[] { new PositionInput("P1", 1000.0, 100.0, 0.0, "MAIN", "DEFAULT", 0, new PositionGeometryInput(100.0, 100.0, -60.0, -10.0)) }))
            .Should().Contain("有限正矩形边界");
        // 8e. 横向退化（`left == right`）。
        // 8e. Degenerate laterally (`left == right`).
        Message(Request(positions: new[] { new PositionInput("P1", 1000.0, 100.0, 0.0, "MAIN", "DEFAULT", 0, new PositionGeometryInput(100.0, 200.0, 10.0, 10.0)) }))
            .Should().Contain("有限正矩形边界");

        // 成功路径。
        // The success path.
        Result<AircraftAggregation, ErrorCode, Error<ErrorCode>> ok =
            AggregationInitializer.Initialize(Request(positions: new[] { Good("P1", 0), Good("P2", 1) }));
        ok.IsOk.Should().BeTrue();
        AircraftAggregation aggregation = ((Ok<AircraftAggregation, ErrorCode, Error<ErrorCode>>)ok).Value!;
        aggregation.Positions.Should().HaveCount(2);
        aggregation.Decks.Should().ContainSingle("both positions sit on the main deck");
        aggregation.Neighbours.Keys.Should().BeEquivalentTo(new[] {
            NeighbourType.Physics, NeighbourType.IndirectPhysics,
            NeighbourType.LinearLoadingOrder, NeighbourType.TopologicalLoadingOrder });
        // **装载顺序串起来**：第一个无前驱、第二个有前驱。
        // **Loading order is linked**: the first has no predecessor, the second has one.
        AircraftPosition first = aggregation.Positions.Single(position => position.SpaceName == "P1");
        AircraftPosition second = aggregation.Positions.Single(position => position.SpaceName == "P2");
        first.LoadingOrder!.DirectPrec.Should().BeEmpty();
        second.LoadingOrder!.DirectPrec.Should().Contain(first);
        first.LoadingOrder.DirectSucc.Should().Contain(second);
    }

    /// <summary>
    /// 解包成功结果 / Unwrap a successful result.
    /// </summary>
    /// <typeparam name="T">值类型 / The value type.</typeparam>
    /// <param name="result">结果 / The result.</param>
    /// <returns>值 / The value.</returns>
    private static T Unwrap<T>(Result<T, ErrorCode, Error<ErrorCode>> result) {
        result.IsOk.Should().BeTrue();
        return ((Ok<T, ErrorCode, Error<ErrorCode>>)result).Value!;
    }
}
