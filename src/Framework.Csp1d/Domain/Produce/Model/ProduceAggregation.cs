#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.Csp1d.Application.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce.Model;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.MultiArray;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.Produce;
/// <summary>
/// CSP1D 产出聚合根 / CSP1D produce aggregation root.
///
/// 管理切割方案选择变量 x[j]、需求/物料/设备中间符号，
/// 以及列生成迭代中的新增列操作。
///
/// Manages cutting plan selection variables x[j], demand/material/machine
/// intermediate symbols, and column addition during column generation iteration.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public sealed class ProduceAggregation<V> : ICsp1dAggregation<V>
    where V : struct {
    private readonly UIntVariable1 _planVars;
    private readonly List<CuttingPlan<V>> _allPlans;
    private readonly Dictionary<CuttingPlanCanonicalKey, int> _planKeyIndex;
    private readonly HashSet<string> _planIds;
    private readonly List<List<IVariableItem>> _batchGroups = new();
    private readonly List<IVariableItem> _additionalVars = new();

    /// <summary>
    /// 切割方案列表 / Cutting plan list.
    /// </summary>
    public IReadOnlyList<CuttingPlan<V>> CuttingPlans { get; }

    /// <summary>
    /// 需求列表 / Demand list.
    /// </summary>
    public IReadOnlyList<ProductDemand<V>> Demands { get; }

    /// <summary>
    /// 物料列表 / Material list.
    /// </summary>
    public IReadOnlyList<Material<V>> Materials { get; }

    /// <summary>
    /// 设备列表 / Machine list.
    /// </summary>
    public IReadOnlyList<Machine<V>> Machines { get; }

    /// <summary>
    /// 方案数量 / Plan count.
    /// </summary>
    public int PlanCount => CuttingPlans.Count;

    /// <summary>
    /// 需求贡献中间符号，按需求索引 / Demand contribution intermediate symbols, indexed by demand.
    /// </summary>
    public IReadOnlyList<ILinearIntermediateSymbol<Flt64>> DemandQuantity { get; }

    /// <summary>
    /// 物料用量中间符号，按物料索引 / Material usage intermediate symbols, indexed by material.
    /// </summary>
    public IReadOnlyList<ILinearIntermediateSymbol<Flt64>> MaterialQuantity { get; }

    /// <summary>
    /// 设备批次数中间符号，按设备索引 / Machine batch count intermediate symbols, indexed by machine.
    /// </summary>
    public IReadOnlyList<ILinearIntermediateSymbol<Flt64>> MachineBatchQuantity { get; }

    /// <summary>
    /// 设备产能中间符号，按设备索引 / Machine capacity intermediate symbols, indexed by machine.
    /// </summary>
    public IReadOnlyList<ILinearIntermediateSymbol<Flt64>> MachineCapacityQuantity { get; }

    /// <summary>
    /// 物料使用统计 / Material usage statistics.
    /// </summary>
    public IReadOnlyList<MaterialUsage<V>> MaterialUsage { get; }

    /// <summary>
    /// 设备产能使用统计 / Machine capacity usage statistics.
    /// </summary>
    public IReadOnlyList<MachineCapacityUsage<V>> MachineUsage { get; }

    /// <summary>
    /// 构造产出聚合 / Construct produce aggregation.
    /// </summary>
    /// <param name="cuttingPlans">切割方案列表 / Cutting plan list.</param>
    /// <param name="demands">需求列表 / Demand list.</param>
    /// <param name="materials">物料列表 / Material list.</param>
    /// <param name="machines">设备列表 / Machine list.</param>
    public ProduceAggregation(
        IReadOnlyList<CuttingPlan<V>> cuttingPlans,
        IReadOnlyList<ProductDemand<V>> demands,
        IReadOnlyList<Material<V>> materials,
        IReadOnlyList<Machine<V>> machines) {
        _allPlans = new List<CuttingPlan<V>>(cuttingPlans);
        CuttingPlans = _allPlans;
        Demands = demands;
        Materials = materials;
        Machines = machines;

        // 创建方案选择变量 x[j]
        // Create plan selection variables x[j]
        _planVars = new UIntVariable1("x", Fuookami.Ospf.MultiArray.Shape1.Invoke(cuttingPlans.Count));

        // 构建 canonical key 索引 / Build canonical key index
        _planKeyIndex = new Dictionary<CuttingPlanCanonicalKey, int>();
        // **基线按 `id` 与 `canonicalKey` **双键**去重**（：
        // `candidate.id !in registeredIds && candidate.canonicalKey !in registeredKeys`）。
        // C# 原本**只有键索引**，于是"同 id 不同内容"的两条方案会被同时接受。
        //
        // **The code dedupes by **two keys**, id and canonicalKey**. C# originally had **only the key index**, so two plans sharing an id but
        // differing in content would both be accepted.
        _planIds = new HashSet<string>();
        for (int i = 0; i < cuttingPlans.Count; i++) {
            var key = CuttingPlanCanonicalKey.From(cuttingPlans[i]);
            _planKeyIndex[key] = i;
            _planIds.Add(cuttingPlans[i].Id);
        }

        // 创建需求贡献中间符号
        // Create demand contribution intermediate symbols
        var demandSymbols = new LinearExpressionSymbol[demands.Count];
        for (int i = 0; i < demands.Count; i++) {
            demandSymbols[i] = new LinearExpressionSymbol(
                new MutableLinearPolynomial<Flt64>(new List<LinearMonomial<Flt64>>(), Flt64.Zero),
                name: $"demandQuantity_{i}");
        }
        DemandQuantity = demandSymbols;

        // 创建物料用量中间符号
        // Create material usage intermediate symbols
        var materialSymbols = new LinearExpressionSymbol[materials.Count];
        for (int i = 0; i < materials.Count; i++) {
            materialSymbols[i] = new LinearExpressionSymbol(
                new MutableLinearPolynomial<Flt64>(new List<LinearMonomial<Flt64>>(), Flt64.Zero),
                name: $"materialQuantity_{i}");
        }
        MaterialQuantity = materialSymbols;

        // 创建设备批次数中间符号
        // Create machine batch count intermediate symbols
        var machineBatchSymbols = new LinearExpressionSymbol[machines.Count];
        for (int i = 0; i < machines.Count; i++) {
            machineBatchSymbols[i] = new LinearExpressionSymbol(
                new MutableLinearPolynomial<Flt64>(new List<LinearMonomial<Flt64>>(), Flt64.Zero),
                name: $"machineBatchQuantity_{i}");
        }
        MachineBatchQuantity = machineBatchSymbols;

        // 创建设备产能中间符号
        // Create machine capacity intermediate symbols
        var machineCapacitySymbols = new LinearExpressionSymbol[machines.Count];
        for (int i = 0; i < machines.Count; i++) {
            machineCapacitySymbols[i] = new LinearExpressionSymbol(
                new MutableLinearPolynomial<Flt64>(new List<LinearMonomial<Flt64>>(), Flt64.Zero),
                name: $"machineCapacityQuantity_{i}");
        }
        MachineCapacityQuantity = machineCapacitySymbols;

        // **初始方案也必须填充中间符号。**
        //
        // `register` 里先建 `PlanBatchTerms`，再用它填 `demandQuantity` /
        // `materialQuantity` / `machineBatchQuantity` / `machineCapacityQuantity`；
        // C# 原本**只建空符号**，于是第一轮 LP 的需求/物料约束**左边恒为零**——
        // 求解器会认为"什么都不做就已满足全部需求"。
        //
        // **Initial plans must populate the intermediate symbols too.** The code builds `PlanBatchTerms` in `register` and fills `demandQuantity` /
        // `materialQuantity` / `machineBatchQuantity` / `machineCapacityQuantity` from it; C# originally **only created empty symbols**, so the first LP's demand and
        // material constraints had **an identically zero left side** — the solver would believe "doing nothing already satisfies every demand".
        PopulateInitialSymbols(cuttingPlans, demands, materials, machines);

        MaterialUsage = Array.Empty<MaterialUsage<V>>();
        MachineUsage = Array.Empty<MachineCapacityUsage<V>>();
    }

    /// <summary>
    /// 用初始方案填充需求/物料/设备中间符号 / Populate the demand, material and machine symbols from the initial plans.
    /// </summary>
    /// <remarks>
    /// 与基线 `PlanBatchTerms` 的 `init` 逐条对应：
    ///
    /// - 每条方案对**它自己的物料**贡献一个系数为 `1` 的单项式；
    /// - 每条方案对**它自己的设备**贡献一个系数为 `1` 的批次单项式；
    /// - 需求贡献**只计入 `demands` 里出现过的（产品 id, 单位）组合**，且**同一方案内同一组合只计一次**；
    /// - 设备产能**只在单位一致且消耗量严格大于零时**计入。
    ///
    /// 后面两条不是优化，是**基线写明的过滤条件**：不滤的话，一条以厘米计的贡献会被
    /// 当成毫米计入（系数差 10 倍），而零消耗的单项式会让产能约束失去意义。
    ///
    /// `PlanBatchTerms.init` clause by clause: one coefficient-1 monomial per plan for **its own material**; one batch monomial for **its own machine**;
    /// demand contributions **only for (product id, unit) pairs present in `demands`, and only once per pair within a plan**; machine capacity **only when the unit matches
    /// and the consumption is strictly positive**. The last two are not optimisations but **explicit baseline filters**: without them a centimetre contribution would count as
    /// millimetres (a factor of 10) and zero-consumption monomials would make the capacity constraint meaningless.
    /// </remarks>
    private void PopulateInitialSymbols(
        IReadOnlyList<CuttingPlan<V>> cuttingPlans,
        IReadOnlyList<ProductDemand<V>> demands,
        IReadOnlyList<Material<V>> materials,
        IReadOnlyList<Machine<V>> machines) {
        var wantedDemands = new HashSet<(string ProductId, object Unit)>();
        for (int i = 0; i < demands.Count; i++) {
            wantedDemands.Add((demands[i].Product.Id, demands[i].Quantity.Unit));
        }

        for (int planIndex = 0; planIndex < cuttingPlans.Count; planIndex++) {
            CuttingPlan<V> plan = cuttingPlans[planIndex];
            IVariableItem variable = _planVars[planIndex];

            for (int matIndex = 0; matIndex < materials.Count; matIndex++) {
                if (plan.Material.Id == materials[matIndex].Id) {
                    ((LinearExpressionSymbol)MaterialQuantity[matIndex]).AsMutable()
                        .AddMonomial(new LinearMonomial<Flt64>(Flt64.One, variable));
                }
            }

            for (int machineIndex = 0; machineIndex < machines.Count; machineIndex++) {
                if (plan.MachineId == machines[machineIndex].Id) {
                    ((LinearExpressionSymbol)MachineBatchQuantity[machineIndex]).AsMutable()
                        .AddMonomial(new LinearMonomial<Flt64>(Flt64.One, variable));
                    if (plan.CapacityConsumption is not null) {
                        Quantity<V> consumption = plan.CapacityConsumption;
                        Quantity<V>? capacity = machines[machineIndex].Capacity;
                        if (capacity is not null
                            && consumption.Unit == capacity.Unit
                            && consumption.Value.ToFlt64() > Flt64.Zero) {
                            ((LinearExpressionSymbol)MachineCapacityQuantity[machineIndex]).AsMutable()
                                .AddMonomial(new LinearMonomial<Flt64>(consumption.Value.ToFlt64(), variable));
                        }
                    }
                }
            }

            var seen = new HashSet<(string ProductId, object Unit)>();
            foreach (CuttingPlanDemandContribution<V> contribution in plan.DemandContributions) {
                (string, object) key = (contribution.Product.Id, contribution.Quantity.Unit);
                if (wantedDemands.Contains(key) && seen.Add(key)) {
                    for (int demandIndex = 0; demandIndex < demands.Count; demandIndex++) {
                        if (demands[demandIndex].Product.Id == contribution.Product.Id
                            && demands[demandIndex].Quantity.Unit == contribution.Quantity.Unit) {
                            ((LinearExpressionSymbol)DemandQuantity[demandIndex]).AsMutable()
                                .AddMonomial(new LinearMonomial<Flt64>(
                                    contribution.Quantity.Value.ToFlt64(),
                                    variable));
                            break;
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// 按索引获取方案选择变量 / Get plan selection variable by index.
    /// </summary>
    /// <param name="index">方案索引 / Plan index.</param>
    /// <returns>方案选择变量 / Plan selection variable.</returns>
    public IVariableItem this[int index] => _planVars[index];

    /// <summary>
    /// 注册到元模型 / Register to meta model.
    ///
    /// 注册方案选择变量和中间符号到元模型。
    /// Register plan selection variables and intermediate symbols to the meta model.
    /// </summary>
    /// <param name="model">元模型 / Meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Register(LinearMetaModel<Flt64> model) {
        // 注册方案选择变量
        // Register plan selection variables
        Try result = model.Add(_planVars);
        if (result.IsFailed) {
            return result;
        }

        // 注册中间符号
        // Register intermediate symbols
        foreach (ILinearIntermediateSymbol<Flt64> symbol in DemandQuantity) {
            result = model.Add(symbol);
            if (result.IsFailed) {
                return result;
            }
        }
        foreach (ILinearIntermediateSymbol<Flt64> symbol in MaterialQuantity) {
            result = model.Add(symbol);
            if (result.IsFailed) {
                return result;
            }
        }
        foreach (ILinearIntermediateSymbol<Flt64> symbol in MachineBatchQuantity) {
            result = model.Add(symbol);
            if (result.IsFailed) {
                return result;
            }
        }
        foreach (ILinearIntermediateSymbol<Flt64> symbol in MachineCapacityQuantity) {
            result = model.Add(symbol);
            if (result.IsFailed) {
                return result;
            }
        }

        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 最近一次 AddColumns 添加的变量列表 / Variables added by the latest AddColumns call.
    ///
    /// 用于 Csp1dProduceContext 追加目标项。null 表示尚未执行过 AddColumns。
    /// Used by Csp1dProduceContext to append objective terms. null if AddColumns has not been called.
    /// </summary>
    public IReadOnlyList<IVariableItem>? LatestBatch =>
        _batchGroups.Count > 0 ? _batchGroups[^1] : null;

    /// <summary>
    /// 增量添加新切割方案列 / Incrementally add new cutting plan columns.
    ///
    /// 对新方案去重后，创建新变量、注册到模型并更新中间符号表达式。
    /// Deduplicates new plans, creates new variables, registers them to the model,
    /// and updates intermediate symbol expressions.
    /// </summary>
    /// <param name="iteration">当前迭代号 / Current iteration number.</param>
    /// <param name="newPlans">新切割方案列表 / New cutting plan list.</param>
    /// <param name="model">元模型 / Meta model.</param>
    /// <returns>去重后实际添加的方案列表 / Deduplicated plans actually added.</returns>
    public Result<IReadOnlyList<CuttingPlan<V>>, ErrorCode, Error<ErrorCode>> AddColumns(
        UInt64 iteration,
        IReadOnlyList<CuttingPlan<V>> newPlans,
        IAbstractLinearMetaModel<Flt64> model) {
        // 去重：过滤已存在的方案 / Dedup: filter out existing plans
        var uniquePlans = new List<CuttingPlan<V>>();
        foreach (CuttingPlan<V> plan in newPlans) {
            var key = CuttingPlanCanonicalKey.From(plan);
            // **两个条件都要查**：`id 不在 AND 键不在`。
            // **Both conditions must be checked**: the code is `id not in AND key not in`.
            if (!_planIds.Contains(plan.Id) && !_planKeyIndex.ContainsKey(key)) {
                uniquePlans.Add(plan);
            }
        }
        if (uniquePlans.Count == 0) {
            return Results.Ok<IReadOnlyList<CuttingPlan<V>>>(Array.Empty<CuttingPlan<V>>());
        }

        // 为每个新方案创建变量并注册 / Create and register a variable for each new plan
        var newVars = new List<IVariableItem>();
        foreach (CuttingPlan<V> plan in uniquePlans) {
            string varName = $"x_{PlanCount}";
            var newVar = new URealVar(varName);
            _additionalVars.Add(newVar);
            newVars.Add(newVar);

            var key = CuttingPlanCanonicalKey.From(plan);
            _planKeyIndex[key] = _allPlans.Count;
            _planIds.Add(plan.Id);
            _allPlans.Add(plan);
        }

        // 注册新变量到模型 / Register new variables to model
        foreach (IVariableItem v in newVars) {
            Try result = model.Add(v);
            if (result is Failed<Success, ErrorCode, Error<ErrorCode>> failed) {
                return new Failed<IReadOnlyList<CuttingPlan<V>>, ErrorCode, Error<ErrorCode>>(failed.Error);
            }
            if (result is Fatal<Success, ErrorCode, Error<ErrorCode>> fatal) {
                return new Fatal<IReadOnlyList<CuttingPlan<V>>, ErrorCode, Error<ErrorCode>>(fatal.Errors);
            }
        }

        // 更新中间符号表达式 / Update intermediate symbol expressions
        for (int i = 0; i < uniquePlans.Count; i++) {
            CuttingPlan<V> plan = uniquePlans[i];
            IVariableItem variable = newVars[i];

            // 更新需求贡献中间符号 / Update demand contribution intermediate symbols
            foreach (CuttingPlanDemandContribution<V> contribution in plan.DemandContributions) {
                for (int demandIndex = 0; demandIndex < Demands.Count; demandIndex++) {
                    ProductDemand<V> demand = Demands[demandIndex];
                    if (contribution.Product.Id == demand.Product.Id
                        && contribution.Quantity.Unit == demand.Quantity.Unit) {
                        Flt64 coeff = ValueToFlt64(contribution.Quantity.Value);
                        ((LinearExpressionSymbol)DemandQuantity[demandIndex]).AsMutable()
                            .AddMonomial(new LinearMonomial<Flt64>(coeff, variable));
                    }
                }
            }

            // 更新物料用量中间符号 / Update material usage intermediate symbols
            for (int matIndex = 0; matIndex < Materials.Count; matIndex++) {
                if (plan.Material.Id == Materials[matIndex].Id) {
                    ((LinearExpressionSymbol)MaterialQuantity[matIndex]).AsMutable()
                        .AddMonomial(new LinearMonomial<Flt64>(Flt64.One, variable));
                }
            }

            // 更新设备批次数中间符号 / Update machine batch count intermediate symbols
            for (int machineIndex = 0; machineIndex < Machines.Count; machineIndex++) {
                if (plan.MachineId == Machines[machineIndex].Id) {
                    ((LinearExpressionSymbol)MachineBatchQuantity[machineIndex]).AsMutable()
                        .AddMonomial(new LinearMonomial<Flt64>(Flt64.One, variable));
                }
            }

            // 更新设备产能中间符号 / Update machine capacity intermediate symbols
            if (plan.CapacityConsumption is not null) {
                for (int machineIndex = 0; machineIndex < Machines.Count; machineIndex++) {
                    if (plan.MachineId == Machines[machineIndex].Id) {
                        Flt64 capacityCoeff = ValueToFlt64(plan.CapacityConsumption.Value);
                        ((LinearExpressionSymbol)MachineCapacityQuantity[machineIndex]).AsMutable()
                            .AddMonomial(new LinearMonomial<Flt64>(capacityCoeff, variable));
                    }
                }
            }
        }

        _batchGroups.Add(newVars);
        return Results.Ok<IReadOnlyList<CuttingPlan<V>>>(uniquePlans);
    }

    /// <summary>
    /// 将 V 类型值转换为 Flt64 / Convert V type value to Flt64.
    /// </summary>
    private static Flt64 ValueToFlt64(V value) {
        if (value is Flt64 f) {
            return f;
        }

        if (value is FltX fx) {
            return fx.ToFlt64();
        }

        return new Flt64(((dynamic)value).ToDouble());
    }
}
