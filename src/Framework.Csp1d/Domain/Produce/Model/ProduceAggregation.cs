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
        for (int i = 0; i < cuttingPlans.Count; i++) {
            var key = CuttingPlanCanonicalKey.From(cuttingPlans[i]);
            _planKeyIndex[key] = i;
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

        MaterialUsage = Array.Empty<MaterialUsage<V>>();
        MachineUsage = Array.Empty<MachineCapacityUsage<V>>();
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
            if (!_planKeyIndex.ContainsKey(key)) {
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
