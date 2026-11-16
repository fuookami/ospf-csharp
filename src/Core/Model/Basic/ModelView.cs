#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Fuookami.Ospf.Utils.Concept;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Solver.Report;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Model.Basic;
/// <summary>
/// 变量松弛元数据 / Variable slack metadata
/// </summary>
/// <param name="LowerBound">下界变量 / Lower bound variable</param>
/// <param name="UpperBound">上界变量 / Upper bound variable</param>
/// <param name="Constraint">来源约束行 / Source constraint row</param>
public sealed record VariableSlack(
    ModelViewVariable? LowerBound = null,
    ModelViewVariable? UpperBound = null,
    ConstraintSlackOrigin? Constraint = null);

/// <summary>
/// 行松弛的来源行引用；弹性模型不保留原约束批次，因此 IIS 过滤需要它把松弛列回溯到原行。
/// Source-row reference for a row slack; the elastic model does not keep the original constraint batch, so IIS
/// filtering needs it to trace a slack column back to its origin row.
/// </summary>
/// <param name="RowIndex">原模型中的行索引 / Row index in the original model</param>
/// <param name="Relation">原行的关系符号 / Relation of the original row</param>
public sealed record ConstraintSlackOrigin(int RowIndex, ConstraintRelation Relation);

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
    /// <summary>来源变量（手工构造的模型可为空）/ Origin variable (optional in manually built models)</summary>
    public Variable.IVariableItem? Origin { get; }
    /// <summary>变量身份；为空时为未命名的模型内身份 / Variable identity; null is an anonymous model-local identity.</summary>
    public ModelElementIdentity? Identity { get; }
    /// <summary>变量 ID / Variable identifier.</summary>
    public VariableId? Id => Identity is null ? null : new VariableId(Identity.Id.Value);
    /// <summary>身份作用域 / Identity scope.</summary>
    public ModelElementScope IdentityScope => Identity?.Scope ?? ModelElementScope.ModelLocal;
    /// <summary>身份主来源 / Primary identity origin.</summary>
    public ModelElementOrigin? IdentityOrigin => Identity?.Origin;
    /// <summary>身份完整来源 / Complete identity provenance.</summary>
    public IReadOnlyList<ModelElementOrigin> IdentityProvenance => Identity?.Provenance ?? Array.Empty<ModelElementOrigin>();
    /// <summary>身份命名空间 / Identity namespace.</summary>
    public string? IdentityNamespace { get; }
    /// <summary>身份 schema 版本 / Identity schema version.</summary>
    public string? IdentitySchemaVersion { get; }

    /// <summary>
    /// 对偶约束来源：这个变量是第几行约束的对偶，以及那一行的关系 / The dual constraint origin.
    /// </summary>
    /// <remarks>
    /// 对齐基线 `ModelView.kt` 的 `dualOrigin: Constraint&lt;Flt64, *&gt;?`。
    /// **基线的类型是"那个约束对象本身"，C# 的类型是"行号 + 关系"。这不是简化，而是两侧模型表示的差异：
    /// Kotlin 的 `ModelView` 持有 `Constraint` 对象引用，C# 的 `ModelViewVariable` 只持有索引与关系，
    /// 因为 C# 的 `ModelView` 是按列/行组织的扁平结构。在这里引入一个约束对象引用会双向绑定视图与模型，
    /// 而视图的全部意义是"可复制的快照"。**
    /// **能表达的信息是<b>同</b>的**：**一个对偶变量对应哪一行、那一行是什么关系——
    /// 这两件事决定了它的符号与它能承载的对偶值范围。引用本身在视图之外没有用处，因为视图不修改模型。**
    /// **所以这是表示差异而不是缺口，但它必须被记下来：**
    /// **"C# 没有 `dualOrigin`"这句话在补上这个属性之前是真的，而现在不真了。**
    /// </remarks>
    public ConstraintSlackOrigin? DualOrigin { get; }

    public ModelViewVariable(
        int index,
        Flt64 lowerBound,
        Flt64 upperBound,
        Variable.IVariableTypeKind type,
        string name,
        Flt64? initialResult = null,
        VariableSlack? slack = null,
        Variable.IVariableItem? origin = null,
        ModelElementIdentity? identity = null,
        string? identityNamespace = null,
        string? identitySchemaVersion = null,
        ConstraintSlackOrigin? dualOrigin = null) {
        Index = index;
        LowerBound = lowerBound;
        UpperBound = upperBound;
        Type = type;
        Name = name;
        InitialResult = initialResult;
        Slack = slack;
        Origin = origin;
        Identity = identity;
        IdentityNamespace = identityNamespace;
        IdentitySchemaVersion = identitySchemaVersion;
        DualOrigin = dualOrigin;
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
        new(
            index: Index,
            lowerBound: LowerBound,
            upperBound: UpperBound,
            type: Type,
            name: Name,
            initialResult: InitialResult,
            slack: Slack,
            origin: Origin,
            identity: Identity,
            identityNamespace: IdentityNamespace,
            identitySchemaVersion: IdentitySchemaVersion,
            dualOrigin: DualOrigin
        );

    /// <summary>以新边界和类型复制变量，并保留身份 / Copy a variable with new bounds and type, preserving identity.</summary>
    /// <param name="lowerBound">新下界 / New lower bound.</param>
    /// <param name="upperBound">新上界 / New upper bound.</param>
    /// <param name="type">新变量类型 / New variable type.</param>
    /// <returns>复制的变量 / Copied variable.</returns>
    public ModelViewVariable WithDomain(Flt64 lowerBound, Flt64 upperBound, Variable.IVariableTypeKind type) => new(
        index: Index,
        lowerBound: lowerBound,
        upperBound: upperBound,
        type: type,
        name: Name,
        initialResult: InitialResult,
        slack: Slack,
        origin: Origin,
        identity: Identity,
        identityNamespace: IdentityNamespace,
        identitySchemaVersion: IdentitySchemaVersion,
        dualOrigin: DualOrigin
    );

    internal ModelViewVariable WithIdentity(ModelElementIdentity? identity, string? identityNamespace, string? schema) => new(
        index: Index,
        lowerBound: LowerBound,
        upperBound: UpperBound,
        type: Type,
        name: Name,
        initialResult: InitialResult,
        slack: Slack,
        origin: Origin,
        identity: identity,
        identityNamespace: identityNamespace,
        identitySchemaVersion: schema,
        dualOrigin: DualOrigin
    );

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
    internal bool IsDisposed { get; private set; }
    private readonly List<ConstraintRelation> _signs;
    private readonly List<Flt64> _rhs;
    private readonly List<string> _names;
    private readonly List<ConstraintSource> _sources;
    private readonly List<ModelElementIdentity> _identities;
    private readonly List<ConstraintId> _ids;

    /// <summary>约束数量 / Constraint count</summary>
    public int ConstraintCount { get; }

    /// <summary>左端表达式 / Left-hand side expressions</summary>
    public abstract IReadOnlyList<IReadOnlyList<ConCell>> Lhs { get; }

    /// <summary>约束关系符号的只读视图 / Read-only view of constraint signs</summary>
    public IReadOnlyList<ConstraintRelation> Signs { get; }
    /// <summary>右端值的只读视图 / Read-only view of right-hand side values</summary>
    public IReadOnlyList<Flt64> Rhs { get; }
    /// <summary>约束名称的只读视图 / Read-only view of constraint names</summary>
    public IReadOnlyList<string> Names { get; }
    /// <summary>约束来源的只读视图 / Read-only view of constraint sources</summary>
    public IReadOnlyList<ConstraintSource> Sources { get; }
    /// <summary>约束行身份；空列表表示匿名 model-local 行 / Row identities; an empty list means anonymous model-local rows.</summary>
    public IReadOnlyList<ModelElementIdentity> Identities { get; }
    /// <summary>约束行 ID / Constraint row identifiers.</summary>
    public IReadOnlyList<ConstraintId> Ids { get; }
    /// <summary>身份命名空间 / Identity namespace.</summary>
    public string? IdentityNamespace { get; }
    /// <summary>身份 schema 版本 / Identity schema version.</summary>
    public string? IdentitySchemaVersion { get; }

    /// <summary>读取行身份作用域 / Read a row identity scope.</summary>
    /// <param name="index">行索引 / Row index.</param>
    /// <returns>身份作用域，未指定时为 model-local / Identity scope, or model-local when absent.</returns>
    public ModelElementScope IdentityScopeAt(int index) => index >= 0 && index < _identities.Count
        ? _identities[index]?.Scope ?? ModelElementScope.ModelLocal
        : ModelElementScope.ModelLocal;

    /// <summary>读取行身份主来源 / Read a row's primary identity origin.</summary>
    /// <param name="index">行索引 / Row index.</param>
    /// <returns>主来源或 null / Primary origin or null.</returns>
    public ModelElementOrigin? IdentityOriginAt(int index) => index >= 0 && index < _identities.Count
        ? _identities[index]?.Origin : null;

    /// <summary>读取行身份完整来源 / Read a row's complete identity provenance.</summary>
    /// <param name="index">行索引 / Row index.</param>
    /// <returns>来源集合 / Provenance collection.</returns>
    public IReadOnlyList<ModelElementOrigin> IdentityProvenanceAt(int index) => index >= 0 && index < _identities.Count
        ? _identities[index]?.Provenance ?? Array.Empty<ModelElementOrigin>()
        : Array.Empty<ModelElementOrigin>();

    /// <summary>约束优先级（可选）/ Constraint priorities (optional)</summary>
    public virtual IReadOnlyList<int>? Priorities => null;

    /// <summary>约束数量（行数）/ Row count</summary>
    public int Size => _rhs.Count;
    /// <summary>索引范围 / Index range</summary>
    public Range Indices => 0.._rhs.Count;

    protected ModelConstraint(
        int constraintCount,
        IReadOnlyList<ConstraintRelation> signs,
        IReadOnlyList<Flt64> rhs,
        IReadOnlyList<string> names,
        IReadOnlyList<ConstraintSource> sources,
        IReadOnlyList<ModelElementIdentity>? identities = null,
        string? identityNamespace = null,
        string? identitySchemaVersion = null) {
        ConstraintCount = constraintCount;
        _signs = new List<ConstraintRelation>(signs);
        _rhs = new List<Flt64>(rhs);
        _names = new List<string>(names);
        _sources = new List<ConstraintSource>(sources);
        _identities = identities is null ? new List<ModelElementIdentity>() : new List<ModelElementIdentity>(identities);
        _ids = _identities.ConvertAll(identity => new ConstraintId(identity is null ? "" : identity.Id.Value));
        Signs = _signs.AsReadOnly();
        Rhs = _rhs.AsReadOnly();
        Names = _names.AsReadOnly();
        Sources = _sources.AsReadOnly();
        Identities = _identities.AsReadOnly();
        Ids = _ids.AsReadOnly();
        IdentityNamespace = identityNamespace;
        IdentitySchemaVersion = identitySchemaVersion;
    }

    /// <summary>校验行与元数据数量 / Validate the row and metadata counts.</summary>
    /// <returns>成功或形状错误 / Success or a shape error.</returns>
    public Try ValidateShape() {
        if (IsDisposed) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationStopped,
                "约束批次已关闭 / Constraint batch is closed"));
        }
        int rows = Lhs.Count;
        if (rows != ConstraintCount || Signs.Count != rows || Rhs.Count != rows
            || Names.Count != rows || Sources.Count != rows
            || (Identities.Count != 0 && Identities.Count != rows)
            || (Priorities is { } priorities && priorities.Count != rows)) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "约束行与元数据数量不一致 / Constraint row and metadata counts do not match"));
        }
        return Results.OkInstance;
    }

    public abstract ModelConstraint<ConCell> Copy();

    public void Dispose() {
        IsDisposed = true;
        _signs.Clear();
        _rhs.Clear();
        _names.Clear();
        _sources.Clear();
        _identities.Clear();
        _ids.Clear();
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
    /// <summary>目标单元格的浅复制只读列表 / Shallow-copied read-only list of objective cells</summary>
    public IReadOnlyList<C> Cells { get; }
    /// <summary>常数项 / Constant term</summary>
    public Flt64 Constant { get; }
    /// <summary>目标身份 / Objective identity.</summary>
    public ModelElementIdentity? Identity { get; }
    /// <summary>目标 ID / Objective identifier.</summary>
    public ObjectiveId? Id => Identity is null ? null : new ObjectiveId(Identity.Id.Value);
    /// <summary>身份作用域 / Identity scope.</summary>
    public ModelElementScope IdentityScope => Identity?.Scope ?? ModelElementScope.ModelLocal;
    /// <summary>身份主来源 / Primary identity origin.</summary>
    public ModelElementOrigin? IdentityOrigin => Identity?.Origin;
    /// <summary>身份完整来源 / Complete identity provenance.</summary>
    public IReadOnlyList<ModelElementOrigin> IdentityProvenance => Identity?.Provenance ?? Array.Empty<ModelElementOrigin>();
    /// <summary>身份命名空间 / Identity namespace.</summary>
    public string? IdentityNamespace { get; }
    /// <summary>身份 schema 版本 / Identity schema version.</summary>
    public string? IdentitySchemaVersion { get; }

    public Objective(
        ObjectCategory category,
        IReadOnlyList<C> cells,
        Flt64? constant = null,
        ModelElementIdentity? identity = null,
        string? identityNamespace = null,
        string? identitySchemaVersion = null) {
        Category = category;
        Cells = new List<C>(cells).AsReadOnly();
        Constant = constant ?? Flt64.Zero;
        Identity = identity;
        IdentityNamespace = identityNamespace;
        IdentitySchemaVersion = identitySchemaVersion;
    }

    public Objective<C> Copy() => new(
        category: Category,
        cells: Cells,
        constant: Constant,
        identity: Identity,
        identityNamespace: IdentityNamespace,
        identitySchemaVersion: IdentitySchemaVersion
    );
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
    bool ContainsContinuous {
        get {
            foreach (ModelViewVariable variable in Variables) {
                if (variable.Type.IsContinuousType) {
                    return true;
                }
            }
            return false;
        }
    }
    /// <summary>是否包含二值变量 / Whether contains binary variables</summary>
    bool ContainsBinary {
        get {
            foreach (ModelViewVariable variable in Variables) {
                if (variable.Type.IsBinaryType) {
                    return true;
                }
            }
            return false;
        }
    }
    /// <summary>是否包含整数变量 / Whether contains integer variables</summary>
    bool ContainsInteger {
        get {
            foreach (ModelViewVariable variable in Variables) {
                if (variable.Type.IsIntegerType) {
                    return true;
                }
            }
            return false;
        }
    }
    /// <summary>是否包含非二值整数变量 / Whether contains non-binary integer variables</summary>
    bool ContainsNotBinaryInteger {
        get {
            foreach (ModelViewVariable variable in Variables) {
                if (variable.Type.IsNotBinaryIntegerType) {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>使用当前目录和指定格式导出模型 / Export the model to the current directory in the requested format.</summary>
    /// <param name="format">文件格式 / File format.</param>
    /// <returns>导出结果 / Export result.</returns>
    Try Export(ModelFileFormat format) => ModelViewFileExporter.Export(this, format);

    /// <summary>使用指定文件或目录路径导出模型 / Export the model to a file or directory path.</summary>
    /// <param name="path">文件路径，或已存在且应写入模型名文件的目录 / File path, or an existing directory that should receive a file named after the model.</param>
    /// <param name="format">文件格式 / File format.</param>
    /// <returns>导出结果 / Export result.</returns>
    Try Export(string path, ModelFileFormat format) => ModelViewFileExporter.Export(this, path, format);

    /// <summary>导出 LP 格式 / Export LP format.</summary>
    /// <param name="writer">输出 writer / Output writer.</param>
    /// <returns>导出结果 / Export result.</returns>
    Try ExportLP(StreamWriter? writer);
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
