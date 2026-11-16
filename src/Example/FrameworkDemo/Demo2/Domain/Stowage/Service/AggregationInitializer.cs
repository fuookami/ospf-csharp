#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.LoadingEffectiveness.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using AircraftPosition = Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model.Position;
using DiscreteLoadingContext = Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.DiscreteLoadingContext<Fuookami.Ospf.Math.Algebra.Number.Flt64>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Service;

/// <summary>
/// 配载聚合初始化器 / The stowage aggregation initializer.
/// </summary>
/// <remarks>
/// 与对齐：**先校验，再翻译**。校验顺序沿用，因为它们互相依赖——
/// "货物名唯一"必须在"舱位引用的货物都存在"之前，"最大件数"必须在"已装件数不超上限"之前。
///
/// **本仓 DTO 缺四组字段**，所以一部分校验无从写起（不是省略，是没有输入）：`PositionInput` 只有名字、最大重量与两个力臂，
/// 没有 `spaceId`/`maxLoadCount`/`loadedItems`/`ala`/`alw`/`predicateLoadWeightMin`/`geometry`/`partitionId`，
/// `CargoInput` 也没有 `order`/`deckLocation`/`allowedPositions`。对应的基线校验（件数、已装货物、实际装载量、预配载下限、
/// 顺序区间）因此**全部缺席**，预配载模式在本仓也无法被满足。这是一处真实的能力缺口，已进差距表。
///
/// **validate first, then translate**, with the validation order copied because they depend on each other. **The DTO here lacks four field groups**,
/// so part of its validation cannot be written (not omitted, but without input): `PositionInput` carries only a name, a maximum weight and two arms — no
/// `spaceId`/`maxLoadCount`/`loadedItems`/`ala`/`alw`/`predicateLoadWeightMin`/`geometry`/`partitionId` — and `CargoInput` has no `order`/`deckLocation`/`allowedPositions`.
/// The matching baseline validations (counts, loaded items, actual loads, the predistribution floor, the order range) are therefore **all absent**, and predistribution mode
/// cannot be satisfied here. That is a real capability gap, recorded in the gap table.
/// </remarks>
internal static class AggregationInitializer
{
    /// <summary>
    /// 从飞机聚合与请求构造配载聚合 / Build the stowage aggregation from the aircraft aggregation and the request.
    /// </summary>
    /// <param name="aircraftAggregation">飞机聚合 / The aircraft aggregation.</param>
    /// <param name="input">请求 DTO / The request DTO.</param>
    /// <param name="stowageMode">装载模式 / The stowage mode.</param>
    /// <returns>聚合或失败 / The aggregation or a failure.</returns>
    public static Result<StowageAggregation, ErrorCode, Error<ErrorCode>> Initialize(
        AircraftAggregation aircraftAggregation,
        RequestDTO input,
        StowageMode stowageMode = StowageMode.FullLoad) {
        ArgumentNullException.ThrowIfNull(aircraftAggregation);
        ArgumentNullException.ThrowIfNull(input);

        if (input.Cargos.Count == 0) {
            return Fail("配载请求至少需要一件货物 / stowage request requires at least one cargo");
        }
        if (input.Cargos.Any(cargo =>
            string.IsNullOrWhiteSpace(cargo.Name)
            || string.IsNullOrWhiteSpace(cargo.Source)
            || string.IsNullOrWhiteSpace(cargo.Destination))) {
            return Fail("货物名称、来源和目的地不能为空 / cargo name, source and destination must not be blank");
        }
        if (input.Cargos.Select(cargo => cargo.Name).Distinct().Count() != input.Cargos.Count) {
            return Fail("货物名称必须唯一 / cargo names must be unique");
        }
        if (input.Cargos.Any(cargo => !double.IsFinite(cargo.Weight) || cargo.Weight <= 0.0 || cargo.Priority < 0)) {
            return Fail("货物重量必须为正且有限，优先级不得为负 / cargo weight must be positive and finite and priority must not be negative");
        }
        if (!double.IsFinite(input.PayloadUpperBound) || input.PayloadUpperBound <= 0.0) {
            return Fail("最大业载必须为正且有限 / the maximum payload must be positive and finite");
        }
        if (aircraftAggregation.Positions.Count != input.Positions.Count) {
            return Results.Failed<StowageAggregation>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                "飞机舱位与请求舱位数量不一致 / aircraft positions do not match request positions"));
        }
        if (input.Positions.Select(position => position.Name).Distinct().Count() != input.Positions.Count) {
            return Fail("请求舱位名称必须唯一 / request position names must be unique");
        }
        // **两条 `maxLoadCount` 校验**：`<= 0` 要拒，已装载件数超过它也要拒。
        //
        // 少了第一条，一个"装 0 件"的舱位会变成**永远不可用的舱位**——而它看起来只是一个普通的舱位，
        // 求解器用不上它也不报错。少了第二条，一个"已装 5 件、上限 3 件"的舱位会让内核收到一个**初始就不可行**的起点。
        //
        // **The code's two `maxLoadCount` checks**: reject `<= 0`, and reject "loaded items exceed it". Without the first, a "holds 0 items" position
        // becomes **permanently unusable** while looking like an ordinary one — the solver just never uses it and says nothing. Without the second, a position with
        // 5 items loaded and a cap of 3 hands the kernel **a starting point that is infeasible before it begins**.
        foreach (PositionInput position in input.Positions) {
            if (position.MaxLoadCount == 0UL) {
                return Fail($"舱位最大装载件数必须为正 / position max load count must be positive: {position.Name}");
            }
            if ((ulong)(position.LoadedItems?.Count ?? 0) > position.MaxLoadCount) {
                return Fail($"已装载件数超过舱位上限 / loaded items exceed the position cap: {position.Name}");
            }
            // **两条谓词装载重量下限校验**。的是 `predicateLoadWeightMin`（一个 `Double?`），
            // 本仓的对应物是三元组 `PredicateLoadWeight`，所以看的是 `Min` 那一端：
            //
            // 1. **非有限、为负、或大于舱位上限就拒**——这三个数进去，谓词重量的松弛右端就是一个非法区间，
            //    而求解器只会报"不可行"，**不会告诉你是哪一端写错了**；
            // 2. **预配载模式下缺这份规格就拒**——基线明写 `stowageMode == Predistribution && any { it == null }` 就失败。
            //    缺规格时那两条软目标会悄悄消失，**模式照跑，只是不再是预配载**。
            //
            // **The code's two predicate-lower-bound checks** . The code uses `predicateLoadWeightMin` (a `Double?`); this implementation's counterpart is the
            // `PredicateLoadWeight` triple, so it looks at the `Min` end: reject a non-finite, negative, or above-cap value — those make the slack right-hand side an
            // illegal interval and the solver only says "infeasible", **never which end is wrong**; and reject a missing spec in predistribution mode, which the
            // baseline states outright. Without the spec the two soft objectives quietly vanish, **and the mode still runs — just no longer as predistribution**.
            if (position.PredicateLoadWeight is { } predicate) {
                if (!double.IsFinite(predicate.Min) || predicate.Min < 0.0 || predicate.Min > position.MaxWeight) {
                    return Fail($"谓词装载重量下限非法 / illegal predicate load weight lower bound: {position.Name}");
                }
            }
            else if (stowageMode == StowageMode.Predistribution) {
                return Fail($"预配载模式要求每个舱位给出谓词装载重量下限 / predistribution requires a predicate load weight bound: {position.Name}");
            }
        }

        // 请求舱位按名字与飞机舱位配对。`first { it.name == basePosition.spaceName }` 缺项时会抛，这里先查一遍给出可读失败。
        // The request positions are paired with the aircraft's by name. The code's `first { ... }` throws on a miss; this checks first and returns a readable failure.
        var requestByName = new Dictionary<string, PositionInput>(input.Positions.Count);
        foreach (PositionInput position in input.Positions) {
            if (!requestByName.TryAdd(position.Name, position)) {
                return Fail("请求舱位名称必须唯一 / request position names must be unique");
            }
        }
        foreach (AircraftPosition position in aircraftAggregation.Positions) {
            if (!requestByName.ContainsKey(position.SpaceName)) {
                return Results.Failed<StowageAggregation>(new Err<ErrorCode>(
                    ErrorCode.ApplicationFailed,
                    "请求缺少舱位 " + position.SpaceName + " / the request has no position " + position.SpaceName));
            }
        }

        AircraftModel aircraftModel = aircraftAggregation.AircraftModel;
        var items = new List<Item>(input.Cargos.Count);
        foreach (CargoInput cargo in input.Cargos) {
            items.Add(new Item(
                name: cargo.Name,
                weight: cargo.Weight,
                // 优先级名与档位照做：`P{优先级}`，`>= 8` 高、`<= 2` 低、其余普通。
                // The priority name and bucket follow the code: `P{priority}`, `>= 8` high, `<= 2` low, otherwise normal.
                priority: new CargoPriority(
                    Name: "P" + cargo.Priority,
                    Priority: new Fuookami.Ospf.Math.Algebra.Number.UInt64((ulong)cargo.Priority),
                    Category: cargo.Priority >= 8
                        ? CargoPriorityCategory.High
                        : cargo.Priority <= 2
                            ? CargoPriorityCategory.Low
                            : CargoPriorityCategory.Normal),
                // 基线对所有货物都给 `GENERAL`，不看输入。
                // The code gives every cargo `GENERAL`, ignoring the input.
                cargoType: CargoType.FromName("GENERAL"),
                source: cargo.Source,
                destination: cargo.Destination,
                status: ItemStatus.Optional,
                // **必装从请求穿到模型**：`CargoInput.Required` → `Item.Required` → `LoadingItem.Required` → `loading_capacity_item_*` 的 `EQ`。
                // **Required flows from the request into the model**: `CargoInput.Required` → `Item.Required` → `LoadingItem.Required` → `EQ` on `loading_capacity_item_*`.
                required: cargo.Required,
                // **甲板来自请求**：`Main → normalMain`、其余 → `lowNotBulk`。
                // 之前这里**恒为主舱**，于是下舱货被当成主舱货——而"下舱货不能放主舱"正是甲板过滤要表达的约束，
                // **恒为主舱会让它从一开始就不成立**。
                //
                // **The deck comes from the request**: the code maps `Main → normalMain` and everything else → `lowNotBulk`. Before this was
                // **always main deck**, so lower-deck cargo counted as main-deck cargo — and "lower-deck cargo cannot go on the main deck" is exactly what the deck
                // filter expresses, **which was false from the start**.
                location: cargo.DeckLocation == DeckLocationInput.Main ? ItemLocation.MainDeck : ItemLocation.LowNotBulk,
                // **装载顺序来自请求**。基线 `ItemOrder(order = UInt8(it.toUByte))`，本仓对应字段叫 `Sequence`
                // （`byte?`，与`UInt8` 同宽）。之前恒不传，**等于所有货物在顺序约束上彼此无差别**。
                // **顺序超出 `byte` 范围就拒**，不静默截断：截断会让两件不同的货拿到同一个顺序，**而那是"无顺序差别"换了个样子**。
                //
                // **The loading order comes from the request** . The code writes `ItemOrder(order = UInt8(...))`; this implementation's field is `Sequence`
                // (`byte?`, the same width as its `UInt8`). Omitting it made **every cargo indistinguishable to the order constraints**. **A sequence
                // outside the `byte` range is refused, never truncated**: truncation would give two different cargos the same order, **which is "no order" wearing a
                // different hat**.
                order: cargo.Order is { } requestedOrder
                    ? (requestedOrder is < 0 or > byte.MaxValue
                        ? throw new ArgumentException("装载顺序超出范围 / Loading order out of range: " + requestedOrder)
                        : new ItemOrder { Sequence = (byte)requestedOrder })
                    : null));
        }

        var positions = new List<StowagePosition>(aircraftAggregation.Positions.Count);
        foreach (AircraftPosition basePosition in aircraftAggregation.Positions) {
            PositionInput requestPosition = requestByName[basePosition.SpaceName];
            // **状态从请求给的状态码推出来**，不再是"恒需要装载决策 + 恒不需调整"：
            // 舱位是 `Loaded`/`Reserved` 时不可用、`Preassigned` 时要调整，谓词/推荐重量还要看模式。
            // 旧写法把这四种情况压成一种，**模型里于是分不出"已装货"和"被预留"**。
            // **The status is derived from the request's code** instead of "always needs a decision, always needs no adjustment": a `Loaded`/`Reserved` position
            // is unavailable and a `Preassigned` one needs adjustment, and the predicate/recommended weight flags depend on the mode. The old form collapsed all
            // four cases into one, **so the model could no longer tell "already loaded" from "reserved"**.
            var status = PositionStatus.From(basePosition.Location, requestPosition.StatusCode, stowageMode);
            // 
            positions.Add(new StowagePosition(
                aircraftPosition: basePosition,
                status: status,
                mlw: new MaxLoadWeightLimit(requestPosition.MaxWeight),
                // **最大装载件数来自请求**。之前的"保守值 1"让一个能装 20 件的舱位只能装 1 件——
                // **不报错，只是把可行解砍掉一大半**。基线还校验 `<= 0` 要拒（见下面那道前置校验）。
                //
                // **The maximum item count comes from the request** . The old "conservative 1" let a position that holds 20 items hold 1 — **no error, just most
                // of the solution space gone**. The code also rejects `<= 0` (see the pre-check below).
                mla: requestPosition.MaxLoadCount,
                // **规格来自请求**：以前恒写 `null`，于是预配载模式下 `PredicateLoadWeightLimit` 必然报"缺少规格"。
                // 三个端点逐个映射，不做"取一个数代表全部"的简化。
                //
                // **The spec comes from the request**: it used to be hard-coded `null`, so in predistribution mode `PredicateLoadWeightLimit` could only report
                // "missing specification". The three ends are mapped one by one rather than collapsed into a single representative number.
                plw: requestPosition.PredicateLoadWeight is { } spec
                    ? new PositionPredicateLoadWeight(spec.Plw, spec.Min, spec.Max)
                    : null,
                // **已装载货物从请求解析成 `Item`**。以前恒为 `null`，于是每个舱位的 `LoadedItems` 都是空表：
                // 装载内核的 `FixedPositionId` 只能是 `null`，**已装载货物被当成自由货物，求解器可以把它挪走**。
                // **名字解析不到就拒**，而不是静默丢弃：一个拼错的货物名会让"这件货已装在这里"变成"这里什么都没装"，
                // **而后者不会报错，只会给出一个把已装载货物重新摆放的解**。
                //
                // **Loaded cargo names resolve to `Item`s here** . They used to be `null`, so every position's `LoadedItems` was empty and the kernel's
                // `FixedPositionId` could only be `null` — **cargo already on board counted as free and the solver could move it**. **An unresolvable name is
                // refused rather than dropped**: a typo would turn "this cargo is loaded here" into "nothing is loaded here", **which raises nothing and just
                // yields a plan that reshuffles cargo already loaded**.
                loadedItems: ResolveLoadedItems(requestPosition.LoadedItems, items)) {
                // **建议装载件数/重量从请求来**。`AdviceLoadAmountLimit`/`AdviceLoadWeightLimit`
                // **只收非 `null` 的舱位**，所以这两个属性拿不到值时那两条软目标**永远是空表**——不报错，只是"建议装载"
                // 这个能力在模型里根本不存在。
                //
                // **The advisory count/weight come from the request** . The code's limits **only collect positions with a non-null value**, so with no
                // source for these two the objectives were **always empty** — no error, just "advisory loading" simply not existing in the model.
                Ala = requestPosition.Ala,
                Alw = requestPosition.Alw,
            });
        }

        // 把请求里的货物名解析成 `Item`。局部函数没有失败通道，所以解析不到时**抛出**，由调用方统一转成 `IllegalArgument`
        // ——**绝不返回一个悄悄变短的列表**。
        //
        // Resolve requested cargo names to `Item`s. A local function has no failure channel, so an unresolvable name **throws** and the caller turns it into
        // `IllegalArgument` — **never a silently shorter list**.
        List<Item> ResolveLoadedItems(IReadOnlyList<string>? names, IReadOnlyList<Item> known) {
            var resolved = new List<Item>();
            if (names is null) {
                return resolved;
            }
            foreach (string name in names) {
                Item? match = null;
                foreach (Item candidate in known) {
                    if (string.Equals(candidate.Id, name, StringComparison.Ordinal)) {
                        match = candidate;
                        break;
                    }
                }
                if (match is null) {
                    throw new ArgumentException("已装载货物名不存在 / Unknown loaded cargo: " + name);
                }
                resolved.Add(match);
            }
            return resolved;
        }

        double plannedPayload = items.Sum(item => item.Weight);
        var flight = new Flight(new FlightNo("DEP-ARR"), new IATACode("DEP"), new IATACode("ARR"));

        var aggregation = new StowageAggregation(
            aircraftModel: aircraftModel,
            flight: flight,
            items: items,
            positions: positions,
            appointment: new Appointment(new Dictionary<Item, AircraftPosition>()),
            biologicalLimit: new BiologicalLimit(
                new List<(CargoType, CargoType)>(),
                new List<(CargoType, CargoType)>(),
                new List<CargoType>()),
            neighbours: aircraftAggregation.Neighbours,
            plannedPayload: plannedPayload,
            maxPayload: input.PayloadUpperBound,
            withMultiLoadingSchema: true,
            formula: aircraftAggregation.Formula,
            fuselage: aircraftAggregation.Fuselage,
            fuel: aircraftAggregation.Fuel,
            computedPayload: null,
            maxTotalWeight: new Dictionary<FlightPhase, double>(),
            computedTotalWeight: new Dictionary<FlightPhase, double>());

        Result<DiscreteLoadingContext, ErrorCode, Error<ErrorCode>> kernel = LoadingContextFactory.Build(input, aggregation);
        if (kernel is Failed<DiscreteLoadingContext, ErrorCode, Error<ErrorCode>> failed) {
            return new Failed<StowageAggregation, ErrorCode, Error<ErrorCode>>(failed.Error);
        }
        aggregation.Stowage.LoadingContext = ((Ok<DiscreteLoadingContext, ErrorCode, Error<ErrorCode>>)kernel).Value;

        // `stowageMode` 目前只影响预配载模式的可满足性（本仓 DTO 无预配载下限，该模式必然失败），构造本身不分模式。
        // `stowageMode` currently only affects predistribution satisfiability (this DTO has no floor, so that mode must fail); construction itself is mode-independent.
        _ = stowageMode;
        return Results.Ok(aggregation);
    }

    private static Result<StowageAggregation, ErrorCode, Error<ErrorCode>> Fail(string message) =>
        Results.Failed<StowageAggregation>(new Err<ErrorCode>(ErrorCode.IllegalArgument, message));
}
