#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Concept;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.IO;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Model.Basic;
/// <summary>
/// 变量松弛元数据 / Variable slack metadata
/// </summary>
/// <param name="LowerBound">下界变量 / Lower bound variable</param>
/// <param name="UpperBound">上界变量 / Upper bound variable</param>
public sealed record VariableSlack(
    ModelViewVariable? LowerBound = null,
    ModelViewVariable? UpperBound = null);

/// <summary>
/// 模型视图中的变量（包装变量项+边界）/ Variable in model view (wraps variable item + bounds)
/// </summary>
public sealed class ModelViewVariable : ICopyable<ModelViewVariable> {
    private static readonly INumericConstants<Flt64> _flt64Constants = NumericConstantsRegistry.For<Flt64>();
    /// <summary>变量索引 / Variable index</summary>
    public int Index { get; }
    /// <summary>下界 / Lower bound</summary>
    public Flt64 LowerBound { get; }
    /// <summary>上界 / Upper bound</summary>
    public Flt64 UpperBound { get; }
    /// <summary>变量类型 / Variable type</summary>
    public Variable.IVariableTypeKind Type { get; }
    /// <summary>名称 / Name</summary>
    public string Name { get; }
    /// <summary>初始结果 / Initial result</summary>
    public Flt64? InitialResult { get; }
    /// <summary>松弛信息 / Slack info</summary>
    public VariableSlack? Slack { get; }

    public ModelViewVariable(
        int index,
        Flt64 lowerBound,
        Flt64 upperBound,
        Variable.IVariableTypeKind type,
        string name,
        Flt64? initialResult = null,
        VariableSlack? slack = null) {
        Index = index;
        LowerBound = lowerBound;
        UpperBound = upperBound;
        Type = type;
        Name = name;
        InitialResult = initialResult;
        Slack = slack;
    }

    /// <summary>是否自由（上下界均无穷）/ Whether free (both bounds infinite)</summary>
    public bool Free => NegativeFree && PositiveFree;
    /// <summary>是否已归一化 / Whether normalized</summary>
    public bool Normalized => NegativeNormalized || PositiveNormalized;
    /// <summary>负方向已归一化 / Negative normalized</summary>
    public bool NegativeNormalized => NegativeFree && UpperBound == Flt64.Zero;
    /// <summary>负方向自由 / Negative free</summary>
    public bool NegativeFree =>
        LowerBound == _flt64Constants.NegativeInfinity!.Value || LowerBound <= -Flt64.One / _flt64Constants.DecimalPrecision!.Value;
    /// <summary>正方向已归一化 / Positive normalized</summary>
    public bool PositiveNormalized => PositiveFree && LowerBound == Flt64.Zero;
    /// <summary>正方向自由 / Positive free</summary>
    public bool PositiveFree =>
        UpperBound == _flt64Constants.PositiveInfinity!.Value || UpperBound >= Flt64.One / _flt64Constants.DecimalPrecision!.Value;

    public ModelViewVariable Copy() =>
        new(Index, LowerBound, UpperBound, Type, Name, InitialResult, Slack);

    public override string ToString() => Name;
}

/// <summary>
/// 模型单元格基础接口 / Model cell base interface
/// </summary>
public interface IModelCell<TSelf> where TSelf : IModelCell<TSelf> {
    /// <summary>系数 / Coefficient</summary>
    Flt64 Coefficient { get; }
    /// <summary>取反 / Unary negation</summary>
    TSelf Negate();
}

/// <summary>
/// 约束单元格接口 / Constraint cell interface
/// </summary>
public interface IConstraintCell<TSelf> : IModelCell<TSelf> where TSelf : IConstraintCell<TSelf> {
    /// <summary>行索引 / Row index</summary>
    int RowIndex { get; }
}

/// <summary>
/// 约束来源 / Constraint source
/// </summary>
public enum ConstraintSource {
    Origin,
    LowerBound,
    UpperBound,
    Dual,
    FarkasDual,
    Feasibility,
    Elastic,
    ElasticLowerBound,
    ElasticUpperBound,
    ElasticSlackBinary,
    ElasticSlackMinmax,
}

/// <summary>
/// 模型约束抽象基类 / Abstract model constraint base
/// </summary>
public abstract class ModelConstraint<ConCell> : ICopyable<ModelConstraint<ConCell>>, IDisposable
    where ConCell : IConstraintCell<ConCell>, ICopyable<ConCell> {
    private readonly List<ConstraintRelation> _signs;
    private readonly List<Flt64> _rhs;
    private readonly List<string> _names;
    private readonly List<ConstraintSource> _sources;

    /// <summary>约束数量 / Constraint count</summary>
    public int ConstraintCount { get; }

    /// <summary>左端表达式 / Left-hand side expressions</summary>
    public abstract IReadOnlyList<IReadOnlyList<ConCell>> Lhs { get; }

    /// <summary>约束关系符号 / Constraint signs</summary>
    public IReadOnlyList<ConstraintRelation> Signs => _signs;
    /// <summary>右端值 / Right-hand side values</summary>
    public IReadOnlyList<Flt64> Rhs => _rhs;
    /// <summary>约束名称 / Constraint names</summary>
    public IReadOnlyList<string> Names => _names;
    /// <summary>约束来源 / Constraint sources</summary>
    public IReadOnlyList<ConstraintSource> Sources => _sources;

    /// <summary>约束数量（行数）/ Row count</summary>
    public int Size => _rhs.Count;
    /// <summary>索引范围 / Index range</summary>
    public Range Indices => 0.._rhs.Count;

    protected ModelConstraint(
        int constraintCount,
        IReadOnlyList<ConstraintRelation> signs,
        IReadOnlyList<Flt64> rhs,
        IReadOnlyList<string> names,
        IReadOnlyList<ConstraintSource> sources) {
        ConstraintCount = constraintCount;
        _signs = new List<ConstraintRelation>(signs);
        _rhs = new List<Flt64>(rhs);
        _names = new List<string>(names);
        _sources = new List<ConstraintSource>(sources);
    }

    public abstract ModelConstraint<ConCell> Copy();

    public void Dispose() {
        _signs.Clear();
        _rhs.Clear();
        _names.Clear();
        _sources.Clear();
    }
}

/// <summary>
/// 目标函数 / Objective function
/// </summary>
/// <typeparam name="C">目标单元格类型 / Objective cell type</typeparam>
public sealed class Objective<C> : ICopyable<Objective<C>>
    where C : ICopyable<C> {
    /// <summary>优化方向 / Optimization category</summary>
    public ObjectCategory Category { get; }
    /// <summary>目标单元格 / Objective cells</summary>
    public IReadOnlyList<C> Cells { get; }
    /// <summary>常数项 / Constant term</summary>
    public Flt64 Constant { get; }

    public Objective(ObjectCategory category, IReadOnlyList<C> cells, Flt64? constant = null) {
        Category = category;
        Cells = cells;
        Constant = constant ?? Flt64.Zero;
    }

    public Objective<C> Copy() => new(Category, new List<C>(Cells), Constant);
}

/// <summary>
/// 基本模型视图接口（变量/约束/名称/导出）
/// Basic model view interface (variables/constraints/name/export)
/// </summary>
public interface IBasicModelView<ConCell> : IDisposable
    where ConCell : IConstraintCell<ConCell>, ICopyable<ConCell> {
    /// <summary>变量列表 / Variable list</summary>
    IReadOnlyList<ModelViewVariable> Variables { get; }
    /// <summary>约束 / Constraints</summary>
    ModelConstraint<ConCell> Constraints { get; }
    /// <summary>模型名称 / Model name</summary>
    string Name { get; }

    /// <summary>是否包含连续变量 / Whether contains continuous variables</summary>
    bool ContainsContinuous => false;
    /// <summary>是否包含二值变量 / Whether contains binary variables</summary>
    bool ContainsBinary => false;
    /// <summary>是否包含整数变量 / Whether contains integer variables</summary>
    bool ContainsInteger => false;
    /// <summary>是否包含非二值整数变量 / Whether contains non-binary integer variables</summary>
    bool ContainsNotBinaryInteger => false;

    /// <summary>导出 LP 格式 / Export LP format</summary>
    Try ExportLP(StreamWriter writer);
}

/// <summary>
/// 完整模型视图接口（含目标函数）
/// Full model view interface (with objective)
/// </summary>
public interface IModelView<ConCell, ObjCell> : IBasicModelView<ConCell>
    where ConCell : IConstraintCell<ConCell>, ICopyable<ConCell>
    where ObjCell : IModelCell<ObjCell>, ICopyable<ObjCell> {
    /// <summary>目标函数 / Objective function</summary>
    Objective<ObjCell> Objective { get; }
}
