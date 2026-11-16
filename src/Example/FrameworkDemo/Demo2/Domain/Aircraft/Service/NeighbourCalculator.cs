#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Service;

/// <summary>
/// 物理邻接计算器 / The physical-neighbour calculator.
/// </summary>
/// <remarks>
/// 与的 `PhysicalNeighbourCalculator` 对齐：同一甲板内**两两组合**，按两条规则判定：
///
/// | 情形 | 判定 |
/// | --- | --- |
/// | 同一纵线且**不同侧** | 直接相邻 |
/// | 同侧（含横向跨越） | 两个舱位的前后臂区间内**没有第三个舱位**才相邻 |
///
/// 四处必须沿用：
///
/// 1. **`onSameSide` 的"或"顺序**：横向跨越（`Transverse`）优先，任一舱位横向就视为"同侧"。写成"都在左或都在右"会漏掉横向跨越的舱位。
/// 2. **`onSameLine` 要求"都在左"**。少了它会把右侧的两舱位也判成同线。
/// 3. **同侧分支看的是"区间内没有别的舱位"**，不是"两舱位不相交"。用后者会让隔着第三个舱位的两个舱位也算相邻。
/// 4. **`i` 小于 `j` 的组合**，不是全排列——全排列会让每对出现两次。
///
/// Aligned with its `PhysicalNeighbourCalculator`: pairwise **combinations** within one deck, decided by the two rules tabulated above. Four things are carried over: **the
/// `onSameSide` disjunction order** — a transverse coordinate wins, so either position being transverse counts as "same side"; **`onSameLine` requires "both on the left"**, without
/// which two right-side positions read as colinear; **the same-side branch asks whether a third position sits inside the front/back interval**, not whether the two boxes overlap,
/// since the latter makes positions separated by a third read as adjacent; and **`i`-below-`j` combinations**, not permutations.
/// </remarks>
internal static class PhysicalNeighbourCalculator
{
    /// <summary>
    /// 计算物理邻接 / Calculate the physical adjacencies.
    /// </summary>
    /// <param name="decks">甲板 / The decks.</param>
    /// <returns>邻接列表 / The neighbour list.</returns>
    public static Result<IReadOnlyList<Neighbour>, ErrorCode, Error<ErrorCode>> Calculate(IReadOnlyList<Deck> decks)
    {
        ArgumentNullException.ThrowIfNull(decks);

        var neighbours = new List<Neighbour>();
        foreach (Deck deck in decks)
        {
            if (deck.Positions.Count <= 1)
            {
                continue;
            }

            for (int i = 0; i < deck.Positions.Count; ++i)
            {
                Position position1 = deck.Positions[i];
                for (int j = i + 1; j < deck.Positions.Count; ++j)
                {
                    Position position2 = deck.Positions[j];

                    if (OnSameLine(position1, position2) && !OnSameSide(position1, position2))
                    {
                        neighbours.Add(new Neighbour(NeighbourType.Physics, new PositionPair(position1, position2)));
                    }
                    else if (OnSameSide(position1, position2))
                    {
                        double frontArm = System.Math.Min(position1.Coordinate.BackArm, position2.Coordinate.BackArm);
                        double backArm = System.Math.Max(position1.Coordinate.FrontArm, position2.Coordinate.FrontArm);
                        bool blocked = false;
                        foreach (Position other in deck.Positions)
                        {
                            if (other.Coordinate.Between(frontArm, backArm))
                            {
                                blocked = true;
                                break;
                            }
                        }
                        if (!blocked)
                        {
                            neighbours.Add(new Neighbour(NeighbourType.Physics, new PositionPair(position1, position2)));
                        }
                    }
                }
            }
        }
        return Results.Ok<IReadOnlyList<Neighbour>>(neighbours);
    }

    /// <summary>
    /// 是否同侧 / Whether both are on the same side.
    /// </summary>
    /// <param name="position1">第一舱位 / The first position.</param>
    /// <param name="position2">第二舱位 / The second position.</param>
    /// <returns>是否同侧 / Whether on the same side.</returns>
    private static bool OnSameSide(Position position1, Position position2) =>
        position1.Coordinate.Transverse
        || position2.Coordinate.Transverse
        || (position1.Coordinate.OnLeft && position2.Coordinate.OnLeft)
        || (position1.Coordinate.OnRight && position2.Coordinate.OnRight);

    /// <summary>
    /// 是否同一纵线 / Whether both lie on one longitudinal line.
    /// </summary>
    /// <param name="position1">第一舱位 / The first position.</param>
    /// <param name="position2">第二舱位 / The second position.</param>
    /// <returns>是否同线 / Whether colinear.</returns>
    private static bool OnSameLine(Position position1, Position position2) =>
        (position1.Coordinate.On(position2.Coordinate.LongitudinalArm)
            || position2.Coordinate.On(position1.Coordinate.LongitudinalArm))
        && position1.Coordinate.OnLeft
        && position2.Coordinate.OnLeft;
}

/// <summary>
/// 间接物理邻接计算器 / The indirect-physics neighbour calculator.
/// </summary>
/// <remarks>
/// 与基线 `IndirectPhysicsNeighbourCalculator` 对齐。结果 = **全部物理邻接**（类型换成 `IndirectPhysics`）+ **"共邻"补出的新对**。
///
/// 补对的条件（四个同时成立）：
///
/// | 条件 | 含义 |
/// | --- | --- |
/// | `!onSameSide` | 两舱位不同侧 |
/// | `nearEnough` | 纵向距离不超过 `0.5 × min(两舱位长度)` |
/// | 两者**本来不是**物理邻接 | `position2` 不在 `position1` 的共邻表里 |
/// | 存在两个不同的共邻 | 两个共邻的邻接表交集非空（`p` 与 `q` 取组合） |
///
/// 四处必须沿用：
///
/// 1. **物理邻接先整批复制进来**。只算"补对"会让间接邻接少了基础那一层，而 `BiologicalAdjacentLimit` 依赖它。
/// 2. **`nearEnough` 的三支**：第二舱位在前 → 看前向间隙；第一舱位在前 → 看后向间隙；重叠 → **直接为真**。
/// 3. **`tidy` 是双向的**：`a→b` 与 `b→a` 都记。单向会让"共邻"只在一个方向成立。
/// 4. **`position2` 只在 `position1` 之后取**，避免同一对出现两次。
///
/// The result is **all physical adjacencies** retyped as `IndirectPhysics` **plus new pairs** inferred through shared neighbours, with four conditions all
/// required, tabulated above. Four things are carried over: **the physical adjacencies are copied in wholesale** — computing only the inferred pairs would drop the base layer that
/// `BiologicalAdjacentLimit` relies on; **`nearEnough`'s three branches** (second ahead → forward gap; first ahead → backward gap; overlap → **true**); **`tidy` is bidirectional**,
/// and one direction alone makes "shared neighbour" hold only one way; and **`position2` is taken only after `position1`**, so no pair appears twice.
/// </remarks>
internal static class IndirectPhysicsNeighbourCalculator
{
    // `nearEnoughLongitudinalRatio = 0.5`。
    // The code's `nearEnoughLongitudinalRatio = 0.5`.
    private const double NearEnoughLongitudinalRatio = 0.5;

    /// <summary>
    /// 计算间接物理邻接 / Calculate the indirect-physics adjacencies.
    /// </summary>
    /// <param name="decks">甲板 / The decks.</param>
    /// <param name="physicalNeighbours">物理邻接 / The physical neighbours.</param>
    /// <returns>邻接列表 / The neighbour list.</returns>
    public static Result<IReadOnlyList<Neighbour>, ErrorCode, Error<ErrorCode>> Calculate(
        IReadOnlyList<Deck> decks,
        IReadOnlyList<Neighbour> physicalNeighbours)
    {
        ArgumentNullException.ThrowIfNull(decks);
        ArgumentNullException.ThrowIfNull(physicalNeighbours);

        var neighbours = new List<Neighbour>();
        // **先整批复制物理邻接**：间接邻接包含基础那一层。
        // **Copy the physical adjacencies in first**: the indirect set includes the base layer.
        foreach (Neighbour neighbour in physicalNeighbours)
        {
            neighbours.Add(new Neighbour(NeighbourType.IndirectPhysics, neighbour.Pair));
        }

        IReadOnlyDictionary<Position, List<Position>> map = Tidy(physicalNeighbours);
        foreach (Deck deck in decks)
        {
            for (int i = 0; i < deck.Positions.Count; ++i)
            {
                Position position1 = deck.Positions[i];
                List<Position> shared = map.TryGetValue(position1, out List<Position>? found)
                    ? found
                    : new List<Position>();

                for (int p = 0; p < shared.Count; ++p)
                {
                    List<Position> neighbours1 = map.TryGetValue(shared[p], out List<Position>? one)
                        ? one
                        : new List<Position>();
                    for (int q = p + 1; q < shared.Count; ++q)
                    {
                        List<Position> neighbours2 = map.TryGetValue(shared[q], out List<Position>? two)
                            ? two
                            : new List<Position>();
                        for (int k = i + 1; k < deck.Positions.Count; ++k)
                        {
                            Position position2 = deck.Positions[k];
                            if (!neighbours1.Contains(position2) || !neighbours2.Contains(position2))
                            {
                                continue;
                            }
                            if (!OnSameSide(position1, position2)
                                && NearEnough(position1, position2)
                                && !shared.Contains(position2))
                            {
                                neighbours.Add(new Neighbour(NeighbourType.IndirectPhysics, new PositionPair(position1, position2)));
                            }
                        }
                    }
                }
            }
        }
        return Results.Ok<IReadOnlyList<Neighbour>>(neighbours);
    }

    /// <summary>
    /// 把物理邻接整理成**双向**映射 / Organise the physical adjacencies into a **bidirectional** map.
    /// </summary>
    /// <param name="physicalNeighbours">物理邻接 / The physical neighbours.</param>
    /// <returns>位置到相邻位置的映射 / The map from a position to its neighbours.</returns>
    private static IReadOnlyDictionary<Position, List<Position>> Tidy(IReadOnlyList<Neighbour> physicalNeighbours)
    {
        var map = new Dictionary<Position, List<Position>>();
        foreach (Neighbour neighbour in physicalNeighbours)
        {
            if (!map.TryGetValue(neighbour.Pair.First, out List<Position>? first))
            {
                first = new List<Position>();
                map[neighbour.Pair.First] = first;
            }
            first.Add(neighbour.Pair.Second);
            if (!map.TryGetValue(neighbour.Pair.Second, out List<Position>? second))
            {
                second = new List<Position>();
                map[neighbour.Pair.Second] = second;
            }
            second.Add(neighbour.Pair.First);
        }
        return map;
    }

    /// <summary>
    /// 是否同侧 / Whether both are on the same side.
    /// </summary>
    /// <param name="position1">第一舱位 / The first position.</param>
    /// <param name="position2">第二舱位 / The second position.</param>
    /// <returns>是否同侧 / Whether on the same side.</returns>
    private static bool OnSameSide(Position position1, Position position2) =>
        position1.Coordinate.Transverse
        || position2.Coordinate.Transverse
        || (position1.Coordinate.OnLeft && position2.Coordinate.OnLeft)
        || (position1.Coordinate.OnRight && position2.Coordinate.OnRight);

    /// <summary>
    /// 纵向是否足够接近 / Whether close enough longitudinally.
    /// </summary>
    /// <param name="position1">第一舱位 / The first position.</param>
    /// <param name="position2">第二舱位 / The second position.</param>
    /// <returns>是否足够接近 / Whether near enough.</returns>
    private static bool NearEnough(Position position1, Position position2)
    {
        double distance = NearEnoughLongitudinalRatio * System.Math.Min(
            position1.Shape.Length, position2.Shape.Length);
        if (position2.Coordinate.BackArm <= position1.Coordinate.FrontArm)
        {
            return position1.Coordinate.FrontArm - position2.Coordinate.BackArm <= distance;
        }
        if (position1.Coordinate.BackArm <= position2.Coordinate.FrontArm)
        {
            return position2.Coordinate.FrontArm - position1.Coordinate.BackArm <= distance;
        }
        // **重叠时直接为真**：两个舱位在纵向相交，不存在"距离"。
        // **Overlap is true outright**: the two boxes intersect longitudinally, so there is no distance.
        return true;
    }
}
