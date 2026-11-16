#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Flatten;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ret = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using SymbolFlatten = Fuookami.Ospf.Core.Symbol.Flatten;
using Token = Fuookami.Ospf.Core.Token;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Model.Mechanism;
// ===== IMechanismModel<V> =====

/// <summary>
/// 机制模型密封接口 / Sealed mechanism-model interface.
/// </summary>
public interface IMechanismModel<V> : IDisposable
    where V : struct, IRealNumber<V>, INumberField<V> {
    string Name { get; }
    IReadOnlyList<IConstraint<V, LinearCategory>> Constraints { get; }
    IObject ObjectFunction { get; }
    Fuookami.Ospf.Core.Token.IAbstractTokenTable<V> Tokens { get; }
}

// ===== IAbstractLinearMechanismModel<V> =====

/// <summary>线性机制模型抽象 / Abstract linear mechanism model</summary>
public interface IAbstractLinearMechanismModel<V> : IMechanismModel<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    Try AddConstraint(
        LinearInequality<V> relation,
        string? name = null,
        (IIntermediateSymbol Symbol, bool Direction)? from = null);

    Try AddConstraint(
        LinearInequality<V> relation,
        string? name = null,
        IIntermediateSymbol? from = null);

    /// <summary>开始约束写入事务 / Begin a constraint-write transaction.</summary>
    /// <remarks>
    /// 实现须在回滚时恢复写入影响的模型状态，包括 token；失败的提交仍须允许回滚。
    /// Implementations must restore model state affected by writes, including tokens; a failed commit must remain rollbackable.
    /// 同一模型的并发或重入修改不受支持 / Concurrent or reentrant mutation of the model is unsupported.
    /// 外部回调不得修改模型或 token；其副作用不由内置事务恢复。
    /// External callbacks must not mutate the model or tokens; built-in transactions do not restore their side effects.
    /// </remarks>
    /// <returns>事务或不支持错误 / A transaction or an unsupported error.</returns>
    Result<IConstraintWriteTransaction, ErrorCode, Error<ErrorCode>> BeginConstraintWrite() =>
        Results.Failed<IConstraintWriteTransaction>(new Err<ErrorCode>(
            ErrorCode.ApplicationError,
            "机制模型不支持约束写入事务。 / The mechanism model does not support constraint-write transactions."
        ));
}

/// <summary>约束写入事务 / Constraint-write transaction.</summary>
public interface IConstraintWriteTransaction {
    /// <summary>提交写入 / Commit the writes.</summary>
    /// <returns>提交结果 / Commit result.</returns>
    Try Commit();

    /// <summary>恢复开始事务时的状态 / Restore the state from the start of the transaction.</summary>
    /// <returns>回滚结果 / Rollback result.</returns>
    Try Rollback();
}

// ===== IAbstractQuadraticMechanismModel<V> =====

/// <summary>二次机制模型抽象 / Abstract quadratic mechanism model</summary>
public interface IAbstractQuadraticMechanismModel<V> : IAbstractLinearMechanismModel<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    new IReadOnlyList<IConstraint<V, QuadraticCategory>> Constraints { get; }

    Try AddConstraint(
        QuadraticInequalityOf<V> relation,
        string? name = null,
        (IIntermediateSymbol Symbol, bool Direction)? from = null);

    Try AddConstraint(
        QuadraticInequalityOf<V> relation,
        string? name = null,
        IIntermediateSymbol? from = null);
}

// ===== ISingleObjectMechanismModel<V> =====

/// <summary>单目标机制模型 / Single-objective mechanism model</summary>
public interface ISingleObjectMechanismModel<V> : IMechanismModel<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    new SingleObject ObjectFunction { get; }
}

// ===== BasicMechanismModel<V> =====

/// <summary>
/// 机制模型基础层 / Mechanism model base layer.
/// </summary>
public abstract class BasicMechanismModel<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly List<Func<bool>> _modelDependencies = new();
    private MechanismModelSourceSnapshot<V>? _sourceSnapshot;
    private MechanismModelSourceSnapshot<V>.VariableSnapshot[]? _preparedVariables;
    internal bool IsDisposed { get; private set; }
    internal object SolutionIdentity { get; } = new();
    public string Name { get; }
    public Fuookami.Ospf.Core.Token.IAbstractTokenTable<V> Tokens { get; }

    protected BasicMechanismModel(string name, Fuookami.Ospf.Core.Token.IAbstractTokenTable<V> tokens) {
        Name = name;
        Tokens = tokens;
    }

    /// <summary>变量数量 / Number of variables</summary>
    public int NumVariables => Tokens.TokensInSolver.Count;

    internal void TrackBoundDependencies(BoundDependencySnapshot<V> dependencies) =>
        _modelDependencies.Add(dependencies.IsCurrent);

    internal void TrackModelDependency(Func<bool> isCurrent) => _modelDependencies.Add(isCurrent);

    internal void CaptureSourceSnapshot(MechanismModelSourceSnapshot<V> snapshot) {
        _sourceSnapshot = snapshot;
        _preparedVariables = MechanismModelSourceSnapshot<V>.CaptureVariables(Tokens.TokensInSolver);
    }

    /// <summary>
    /// **把来源快照复制到副本上** / Carry the source snapshot over to a copy.
    /// </summary>
    /// <remarks>
    /// 这个成员存在的理由，是一件<b>会让整条接线必然失败</b>的事：
    ///
    /// **`_sourceSnapshot` 只在 `InvokeAsync` 里赋值，而 `DeferredFunctionMaterialization.Materialize`
    /// 用 `new LinearMechanismModel(&lt;V&gt;)` 构造副本——副本没有快照。**
    ///
    /// **于是物化出来的模型<b>不能转成三元模型</b>：`ValidateSourceSnapshot` 直接返回
    /// `来源模型快照缺失`。而接线接在转换处，所以真实路径上每一步都会失败。**
    ///
    /// **这个缺陷的形态值得记住：<b>物化本身是对的、转换本身也是对的、接线位置也是对的，
    /// 而串起来必然失败</b>——因为副本丢了源模型的一件状态，而"复制"这件事没有被检查过
    /// "应该复制哪些状态"。**
    ///
    /// **判据能抓到它，靠的不是"测了物化"或"测了转换"，而是<b>测了物化之后再转换</b>。**
    ///
    /// **This exists because of something that would make the whole wiring fail every time:
    /// `_sourceSnapshot` is assigned only inside `InvokeAsync`, while `DeferredFunctionMaterialization.Materialize` builds
    /// its copy with `new LinearMechanismModel(&lt;V&gt;)` — and the copy has no snapshot.**
    ///
    /// **So the materialised model <b>cannot become a triad model</b>: `ValidateSourceSnapshot` returns
    /// `Source model snapshot is missing`.** **Wiring at the conversion point would therefore fail on every real
    /// call.**
    ///
    /// **The shape of this defect is worth remembering: <b>materialisation is right, conversion is right, the wiring point
    /// is right, and the chain fails every time</b> — because the copy lost one piece of the source's state and "copying"
    /// was never checked for which state it should carry.**
    ///
    /// **What catches it is not "testing materialisation" or "testing conversion" but <b>materialising and then
    /// converting</b>.**
    /// </remarks>
    /// <param name="source">要复制快照的源模型 / The source model whose snapshot to carry over.</param>
    internal void InheritSourceSnapshotFrom(BasicMechanismModel<V> source) {
        ArgumentNullException.ThrowIfNull(source);
        if (source._sourceSnapshot is not null && source._preparedVariables is not null) {
            _sourceSnapshot = source._sourceSnapshot;
            _preparedVariables = source._preparedVariables;
        }
    }

    internal Try ValidateSourceSnapshot(bool checkVariables = true) {
        if (_sourceSnapshot is null || _preparedVariables is null) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "来源模型快照缺失 / Source model snapshot is missing"));
        }
        // 提交后父表包含辅助变量，因此使用准备后的变量快照 / After commit the parent includes helpers, so use the prepared-variable snapshot.
        Try source = _sourceSnapshot.Validate(checkVariables: false);
        return !source.IsOk || !checkVariables
            ? source
            : MechanismModelSourceSnapshot<V>.ValidateVariables(Tokens.TokensInSolver, _preparedVariables);
    }

    internal Try ValidateCommitSnapshot() {
        Try source = ValidateSourceSnapshot();
        return source.IsOk ? ValidateBoundDependencies() : source;
    }

    private protected void DisposeTokens() {
        if (!IsDisposed) {
            IsDisposed = true;
            Tokens.Dispose();
        }
    }

    private protected Result<IConstraintWriteTransaction, ErrorCode, Error<ErrorCode>> CreateConstraintWriteTransaction<T>(
        List<T> constraints) {
        if (Tokens is not Token.ConcurrentTokenTable<V> || Tokens.TokenList is not Token.TokenList<V>) {
            return Results.Failed<IConstraintWriteTransaction>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "机制模型令牌不是不可变结构快照。 / The mechanism model tokens are not a structurally immutable snapshot."
            ));
        }
        // shortcut: 外部回调副作用不回滚；实现回调隔离后移除 / External callback side effects are not restored; remove after callback isolation.
        return Results.Ok<IConstraintWriteTransaction>(new ConstraintWriteTransaction<T>(constraints, _modelDependencies));
    }

    private sealed class ConstraintWriteTransaction<T>(
        List<T> constraints,
        List<Func<bool>> dependencies) : IConstraintWriteTransaction {
        private readonly int _rowCount = constraints.Count;
        private readonly int _dependencyCount = dependencies.Count;
        private bool _finished;

        public Try Commit() {
            if (_finished) {
                return Invalid();
            }
            _finished = true;
            return Results.OkInstance;
        }

        public Try Rollback() {
            if (_finished || constraints.Count < _rowCount || dependencies.Count < _dependencyCount) {
                return Invalid();
            }
            constraints.RemoveRange(_rowCount, constraints.Count - _rowCount);
            dependencies.RemoveRange(_dependencyCount, dependencies.Count - _dependencyCount);
            _finished = true;
            return Results.OkInstance;
        }

        private static Try Invalid() => Results.Failed<Success>(new Err<ErrorCode>(
            ErrorCode.ApplicationError,
            "约束写入事务状态无效。 / Invalid constraint-write transaction state."
        ));
    }

    /// <summary>验证已登记的建模依赖仍然有效 / Validate registered modeling dependencies.</summary>
    /// <remarks>仅检查已登记的依赖；调用期间不得并发修改模型。 / Only registered dependencies are checked; concurrent model mutation is unsupported.</remarks>
    /// <returns>有效或须重建模型的错误 / Success or an error requiring a model rebuild.</returns>
    public Try ValidateBoundDependencies() => _modelDependencies.All(isCurrent => isCurrent())
        ? Results.Ok<Success>(Results.SuccessInstance)
        : Results.Failed<Success>(new Err<ErrorCode>(
            ErrorCode.IllegalArgument,
            "模型的建模依赖已变更，请重新构建。 / Modeling dependencies have changed; rebuild the model."
        ));
}

// ===== LinearMechanismModel<V> =====

/// <summary>
/// 线性机制模型实现 / Linear mechanism model implementation.
/// </summary>
public sealed class LinearMechanismModel<V> : BasicMechanismModel<V>,
    IAbstractLinearMechanismModel<V>, ISingleObjectMechanismModel<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    internal LinearMetaModel<V> Parent { get; }
    private readonly List<LinearConstraintImpl<V>> _constraints;

    IReadOnlyList<IConstraint<V, LinearCategory>> IMechanismModel<V>.Constraints => _constraints;
    IObject IMechanismModel<V>.ObjectFunction => ObjectFunction;
    public SingleObject ObjectFunction { get; }
    internal bool Concurrent => Parent.Configuration.Concurrent;
    internal IReadOnlyList<LinearConstraintImpl<V>> LinearConstraints => _constraints;
    public int NumConstraints => _constraints.Count;

    /// <summary>
    /// **函数符号展开策略** / The function-symbol expansion policy.
    /// </summary>
    /// <remarks>
    /// 对齐。**默认值是 `EAGER`，这里同样是 `Eager`。**
    ///
    /// **这个属性是三样缺失状态里唯一<b>不依赖求解路径</b>的一样：**
    /// **它只被读、不被写，而"该不该现在展开"这个判断可以在任何求解器都不在场时完成。**
    ///
    /// **所以它先来。** **把它先补上，`materializeMechanismFunctionFallbacks` 的第一个守卫
    /// （`policy == Eager` 时直接返回原模型）就能成为可测的行为，而不需要先动求解路径。**
    ///
    /// **默认值不是随手定的：** **`Eager` 是"什么都不延迟"，也就是<b>最保守</b>的那一档——
    /// 一个没配置过的模型不会突然走上延迟物化那条更复杂的路径。**
    ///
    /// **Its default is `EAGER`, and so is this one.**
    ///
    /// **Of the three missing pieces this is the only one that <b>does not depend on the solve path</b>: it is only read,
    /// never written, and "should this expand now" can be answered with no solver present.**
    ///
    /// **So it comes first.** **With it in place, the first guard of `materializeMechanismFunctionFallbacks` (return the
    /// model unchanged when the policy is `Eager`) becomes behaviour that can be measured without touching the solve
    /// path.**
    ///
    /// **The default is not arbitrary: `Eager` means "defer nothing", the <b>most conservative</b> setting — an
    /// unconfigured model will not silently take the more complicated materialisation path.**
    /// </remarks>
    public FunctionExpansionPolicy FunctionExpansionPolicy { get; }

    /// <summary>
    /// **延迟函数结构快照** / Snapshots of the deferred function structures.
    /// </summary>
    /// <remarks>
    /// 对齐。**只读 `List`，默认 `emptyList`，走构造函数。**
    ///
    /// **这是三样缺失状态里的第二样，而它是两样集合里较简单的一个：** **只增不删、只在构造时给定、之后只被读。**
    /// **`DeferredFunctionConstraintRegions` 依赖它（每条区域记录指回一个结构），所以顺序是先它。**
    ///
    /// **默认 `emptyList` 而不是"必须传"：** **绝大多数模型没有延迟结构。**
    /// **把"没有"和"有但空"分开会让每个调用点都要写一次 `Array.Empty`——
    /// 而那一次写法不携带任何信息。**
    ///
    /// **`null` 与空集合在这里<b>不等价</b>：** **`null` 表示"调用方没提供"，
    /// 空集合表示"提供了，是空的"。构造参数允许 `null` 是为了让旧调用点不必改，
    /// 而属性永远返回非 `null`——** **读的一方不该被迫区分这两件事。**
    ///
    /// **A read-only list, defaulting to empty, supplied through the
    /// constructor.**
    ///
    /// **This is the second of the three missing pieces and the simpler of the two collections: append-only, set once at
    /// construction, read afterwards.** **`DeferredFunctionConstraintRegions` depends on it (each region record points back
    /// at a structure), so it comes first.**
    ///
    /// **The default is empty rather than required: the overwhelming majority of models have no deferred structures, and
    /// separating "absent" from "present but empty" would make every call site write one `Array.Empty` that carries no
    /// information.**
    ///
    /// **`null` and an empty collection are <b>not</b> equivalent here: `null` means "the caller supplied nothing", an empty
    /// collection means "supplied and empty". The constructor accepts `null` so old call sites need not change, while the
    /// property always returns non-`null` — a reader should not be forced to tell those two apart.**
    /// </remarks>
    public IReadOnlyList<DeferredFunctionStructure> DeferredFunctionStructures { get; }

    private readonly List<DeferredFunctionConstraintRegion> _deferredFunctionConstraintRegions;

    /// <summary>
    /// **已经物化过的延迟函数约束区域** / The deferred-function constraint regions already materialised.
    /// </summary>
    /// <remarks>
    /// 对齐，这是三样缺失状态里的最后一样。
    ///
    /// **它存在是为了回答一个问题：** **"这个结构是不是已经物化过了？"**
    ///
    /// **而那正是第三个过滤条件。** **缺了它，同一个结构会被物化<b>两次</b>——
    /// 一次在区域里、一次在新的一批里——于是模型里出现两条描述同一个 `y` 的约束，
    /// 而求解器不会报错，只会给出一个"两条都满足"的解。**
    ///
    /// **区域记录用<b>行号</b>（`FirstConstraintIndex` + `ConstraintCount`）而不只是结构引用：
    /// 因为它同时要回答"这些约束在模型的哪一段"。** **只记结构能回答"做过了"，记不下"做在哪"。**
    ///
    /// **This is the last of the three missing pieces.**
    ///
    /// **It exists to answer one question: "has this structure already been materialised?" — which is exactly the third
    /// third filter condition.** **Without it one structure is materialised <b>twice</b>, once through its region
    /// and once in a later batch, leaving two constraint sets describing the same `y`; the solver does not complain and
    /// returns a solution satisfying both.**
    ///
    /// **The record carries <b>row numbers</b> (`FirstConstraintIndex` plus `ConstraintCount`) rather than only a structure
    /// reference, because it must also answer "which slice of the model are these rows" — a structure reference alone
    /// answers "it was done" but not "where".**
    /// </remarks>
    public IReadOnlyList<DeferredFunctionConstraintRegion> DeferredFunctionConstraintRegions =>
        _deferredFunctionConstraintRegions;

    /// <summary>
    /// **记下一段刚物化出来的约束** / Record one freshly materialised slice of constraints.
    /// </summary>
    /// <remarks>
    /// **只追加、不回删**：**区域记录描述的是"模型里已经有这一段"，而 `TruncateConstraintsTo` 会把行删掉——
    /// 两者一旦不一致，区域就会指向一段不存在的行。**
    ///
    /// **所以这个方法的调用点必须与截断保持在同一层事务里：
    /// 回滚行数的同时把区域记录也回滚掉。** **截断原语只截行、不截区域，
    /// 而现在的调用方（`LinearMechanismFallbackTarget`）不会在区域记录非空之后回滚——
    /// 这一点写在这里，因为它是一个真实的限制。**
    ///
    /// **Append-only: a region record says "these rows exist in the model", while `TruncateConstraintsTo` removes rows. If
    /// the two disagree the region points at rows that no longer exist.**
    ///
    /// **So the caller must keep this in the same transaction as truncation: roll back the region records together with the
    /// row count.** **The truncation primitive truncates rows only, and the present caller never rolls back
    /// after the region list is non-empty — stated here because it is a real limitation.**
    /// </remarks>
    /// <param name="region">要记下的区域 / The region to record.</param>
    internal void RecordDeferredFunctionConstraintRegion(DeferredFunctionConstraintRegion region) {
        ArgumentNullException.ThrowIfNull(region);
        _deferredFunctionConstraintRegions.Add(region);
    }

    /// <summary>
    /// **把约束行截断到指定行数** / Truncate the constraint rows to the given count.
    /// </summary>
    /// <remarks>
    /// 延迟函数 fallback 的事务回滚原语。
    ///
    /// **为什么回滚需要模型自己的一个方法，而不是让适配器直接改 `_constraints`：**
    /// **"截断到一个行数"是一件只有模型知道怎么做对的事** ——
    /// **同一个模型上还有 `_dependencies`、符号表、以及延迟函数区域记录，它们与行数必须一致。**
    /// **让外部代码去 `RemoveRange`，等于把"行数与那几样东西的一致性"这个不变量交给外部维护。**
    ///
    /// **这里只截断 `_constraints`，因为目前的调用方（`LinearMechanismFallbackTarget`）只在
    /// fallback 物化期间追加约束、不追加依赖——但这一点写在这里，而不是隐含。**
    ///
    /// **The rollback primitive for the deferred-function fallback.**
    ///
    /// **Why rollback needs a method on the model rather than the adapter reaching into `_constraints`: "truncate to a row
    /// count" is a thing only the model can do correctly — the same model carries `_dependencies`, the symbol table and the
    /// deferred-function region records, and they must stay consistent with the row count. Letting outside code call
    /// `RemoveRange` hands that invariant to outsiders.**
    ///
    /// **Only `_constraints` is truncated, because the current caller appends constraints without dependencies during
    /// fallback materialisation — but that is stated here rather than implied.**
    /// </remarks>
    /// <param name="constraintCount">目标行数 / The target row count.</param>
    internal void TruncateConstraintsTo(int constraintCount) {
        if (constraintCount < 0 || constraintCount > _constraints.Count) {
            throw new ArgumentOutOfRangeException(nameof(constraintCount));
        }
        _constraints.RemoveRange(constraintCount, _constraints.Count - constraintCount);
    }

    internal LinearMechanismModel(
        LinearMetaModel<V> parent,
        string name,
        IReadOnlyList<LinearConstraintImpl<V>> constraints,
        SingleObject objectFunction,
        Fuookami.Ospf.Core.Token.IAbstractTokenTable<V> tokens,
        FunctionExpansionPolicy functionExpansionPolicy = FunctionExpansionPolicy.Eager,
        IReadOnlyList<DeferredFunctionStructure>? deferredFunctionStructures = null,
        IReadOnlyList<DeferredFunctionConstraintRegion>? deferredFunctionConstraintRegions = null)
        : base(name, tokens) {
        Parent = parent;
        _constraints = constraints.ToList();
        ObjectFunction = objectFunction;
        FunctionExpansionPolicy = functionExpansionPolicy;
        DeferredFunctionStructures = deferredFunctionStructures ?? Array.Empty<DeferredFunctionStructure>();
        _deferredFunctionConstraintRegions =
            deferredFunctionConstraintRegions?.ToList()
            ?? new List<DeferredFunctionConstraintRegion>();
    }

    /// <summary>
    /// 构建线性机制模型并准备符号值 / Build a linear mechanism model and prepare symbol values.
    /// </summary>
    /// <param name="metaModel">源元模型 / Source meta-model.</param>
    /// <param name="concurrent">保留参数，当前未使用 / Reserved parameter, currently ignored.</param>
    /// <param name="blocking">保留参数，当前未使用 / Reserved parameter, currently ignored.</param>
    /// <param name="fixedVariables">用于符号准备的固定值，不执行列消元或值域校验 / Fixed values for symbol preparation, without column elimination or domain validation.</param>
    /// <param name="registrationStatusCallBack">可选注册进度回调 / Optional registration progress callback.</param>
    /// <param name="dumpingStatusCallBack">保留参数，当前未使用 / Reserved parameter, currently ignored.</param>
    /// <param name="functionExpansionPolicy">
    /// 函数符号展开策略，缺省 `Eager` / The function-symbol expansion policy, `Eager` by default.
    /// </param>
    /// <param name="deferredFunctionStructures">
    /// 延迟函数结构快照，缺省为空 / The deferred function structure snapshots, empty by default.
    /// </param>
    /// <returns>成功模型或失败结果；调用方须释放成功模型 / A model or failure; the caller must dispose a successful model.</returns>
    public static Task<Result<LinearMechanismModel<V>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        LinearMetaModel<V> metaModel,
        bool? concurrent = null,
        bool? blocking = null,
        IReadOnlyDictionary<IVariableItem, V>? fixedVariables = null,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        MechanismModelDumpingStatusCallBack? dumpingStatusCallBack = null,
        FunctionExpansionPolicy functionExpansionPolicy = FunctionExpansionPolicy.Eager,
        IReadOnlyList<DeferredFunctionStructure>? deferredFunctionStructures = null) => InvokeBeforeCommitAsync(
            metaModel: metaModel,
            fixedVariables: fixedVariables,
            registrationStatusCallBack: registrationStatusCallBack,
            beforeCommit: null,
            functionExpansionPolicy: functionExpansionPolicy,
            deferredFunctionStructures: deferredFunctionStructures
        );

    /// <summary>
    /// **构建机制模型；决定延迟展开策略与延迟结构快照** /
    /// Build a mechanism model, deciding the deferred-expansion policy and the deferred-structure snapshots.
    /// </summary>
    /// <remarks>
    /// 这两个参数属于这条唯一的生产构建路径。**没有它们，延迟物化链<b>没有任何生产模型能走上</b>：
    /// 构造点不传策略也不传结构，于是每个模型都是 `Eager`、都没有待展开的东西。**
    ///
    /// **默认值仍是 `Eager` 与 `null`，这一点是有意的：** **调用方不显式要延迟展开，就不该得到它。
    /// 缺口补上不等于行为改变——改变的只是"想用的人现在能用了"。**
    ///
    /// **而这正是缺口判据的价值：** **它逼着补的人回来把"还没有"改成"现在有了"，
    /// 于是文档与代码不会分家。**
    ///
    /// **These two parameters belong to the single production build path. Without them the
    /// deferred-materialisation work <b>could not be reached by any production model</b>.** **The defaults stay `Eager`
    /// and `null` on purpose: a caller that does not ask for deferred expansion must not get it.**
    ///
    /// **And that is the value of a gap criterion: it forces the closer to come back and rewrite "not yet" into "now", so
 /// the documents and the implementation cannot drift apart.**
    /// </remarks>
    internal static async Task<Result<LinearMechanismModel<V>, ErrorCode, Error<ErrorCode>>> InvokeBeforeCommitAsync(
        LinearMetaModel<V> metaModel,
        IReadOnlyDictionary<IVariableItem, V>? fixedVariables,
        RegistrationStatusCallBack? registrationStatusCallBack,
        Func<LinearMechanismModel<V>, Try>? beforeCommit,
        FunctionExpansionPolicy functionExpansionPolicy = FunctionExpansionPolicy.Eager,
        IReadOnlyList<DeferredFunctionStructure>? deferredFunctionStructures = null) {
        if (metaModel.IsClosed) {
            return Results.Failed<LinearMechanismModel<V>>(new Err<ErrorCode>(
                ErrorCode.ApplicationStopped,
                "模型已关闭，无法构建机制模型 / Model is closed; cannot build a mechanism model"));
        }
        MechanismModelSourceSnapshot<V> sourceSnapshot;
        try {
            sourceSnapshot = new MechanismModelSourceSnapshot<V>(metaModel);
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            return MechanismModelTokenPreparation<V>.BuildFailure<LinearMechanismModel<V>>(error);
        }
        Result<MechanismModelTokenPreparation<V>, ErrorCode, Error<ErrorCode>> preparationResult =
            MechanismModelTokenPreparation<V>.Create(
                parent: metaModel,
                fixedVariables: fixedVariables,
                callBack: registrationStatusCallBack
            );
        if (preparationResult is Failed<MechanismModelTokenPreparation<V>, ErrorCode, Error<ErrorCode>> failedPreparation) {
            return Results.Failed<LinearMechanismModel<V>>(failedPreparation.Error);
        }
        if (preparationResult is Fatal<MechanismModelTokenPreparation<V>, ErrorCode, Error<ErrorCode>> fatalPreparation) {
            return new Fatal<LinearMechanismModel<V>, ErrorCode, Error<ErrorCode>>(fatalPreparation.Errors);
        }
        using MechanismModelTokenPreparation<V> preparation = preparationResult.Value;
        Fuookami.Ospf.Core.Token.IAbstractTokenTable<V> tokens = preparation.Tokens;
        try {
            Try sourceCheck = sourceSnapshot.Validate();
            if (!sourceCheck.IsOk) {
                return MechanismModelSourceSnapshot<V>.Propagate<LinearMechanismModel<V>>(sourceCheck);
            }
            Try dependencies = metaModel.ValidateDynamicRows(tokens);
            if (dependencies is Failed<Success, ErrorCode, Error<ErrorCode>> invalidDependency) {
                return Results.Failed<LinearMechanismModel<V>>(invalidDependency.Error);
            }
            if (dependencies is Fatal<Success, ErrorCode, Error<ErrorCode>> fatalDependency) {
                return new Fatal<LinearMechanismModel<V>, ErrorCode, Error<ErrorCode>>(fatalDependency.Errors);
            }

            // Build constraints
            var constraints = new List<LinearConstraintImpl<V>>();
            foreach (LinearInequalityConstraint<V> mc in metaModel.RelationConstraints) {
                Result<LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> flattenResult = mc.Inequality.ToLinearFlattenData(tokens);
                if (flattenResult is Failed<SymbolFlatten.LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> f) {
                    return Results.Failed<LinearMechanismModel<V>>(f.Error);
                }
                if (flattenResult is Fatal<SymbolFlatten.LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> fatalFlatten) {
                    return new Fatal<LinearMechanismModel<V>, ErrorCode, Error<ErrorCode>>(fatalFlatten.Errors);
                }

                LinearFlattenData<V> flattenData = ((Ok<SymbolFlatten.LinearFlattenData<V>, ErrorCode, Error<ErrorCode>>)flattenResult).Value;
                var relation = new LinearRelationImpl<V>(
                    FlattenData: flattenData,
                    Sign: mc.Sign,
                    Name: mc.Name,
                    DisplayName: mc.DisplayName
                );
                Result<LinearConstraintImpl<V>, ErrorCode, Error<ErrorCode>> constraintResult = LinearConstraintImpl<V>.Create(
                    relation: relation,
                    tokens: tokens,
                    converter: metaModel.Converter,
                    lazy: mc.Lazy,
                    name: mc.Name,
                    origin: mc
                );
                if (constraintResult is Failed<LinearConstraintImpl<V>, ErrorCode, Error<ErrorCode>> cf) {
                    return Results.Failed<LinearMechanismModel<V>>(cf.Error);
                }
                if (constraintResult is Fatal<LinearConstraintImpl<V>, ErrorCode, Error<ErrorCode>> fatalConstraint) {
                    return new Fatal<LinearMechanismModel<V>, ErrorCode, Error<ErrorCode>>(fatalConstraint.Errors);
                }

                constraints.Add(((Ok<LinearConstraintImpl<V>, ErrorCode, Error<ErrorCode>>)constraintResult).Value);
            }

            // Build objective sub-objects
            var subObjects = new List<object>();
            foreach (MetaSubObject<V> source in metaModel.MetaSubObjects) {
                var flattenData = new SymbolFlatten.LinearFlattenData<V>(
                    source.Polynomial.Monomials,
                    source.Polynomial.Constant);
                Result<LinearSubObject<V>, ErrorCode, Error<ErrorCode>> subObj = LinearSubObject<V>.Create(
                    flattenData: flattenData,
                    tokens: tokens,
                    converter: metaModel.Converter,
                    category: source.Category,
                    name: source.Name,
                    displayName: source.DisplayName
                );
                if (subObj is Failed<LinearSubObject<V>, ErrorCode, Error<ErrorCode>> objectiveFailure) {
                    return Results.Failed<LinearMechanismModel<V>>(objectiveFailure.Error);
                }
                if (subObj is Fatal<LinearSubObject<V>, ErrorCode, Error<ErrorCode>> fatalObjective) {
                    return new Fatal<LinearMechanismModel<V>, ErrorCode, Error<ErrorCode>>(fatalObjective.Errors);
                }
                subObjects.Add(subObj.Value with { Origin = source });
            }

            var objectFunction = new SingleObject(metaModel.ObjectCategory, subObjects);

            // **延迟策略下，从函数符号自己派生结构快照，并让那些符号跳过急切注册。**
            //
            // **这一段存在的理由，是那个"投影有了、没人调用"的缺口。
            // 而直接接上会得到两套行——`AbsFunction` 注册时会同时生成自己的急切约束。**
            //
            // **所以派生与跳过必须一起做：** **派生了哪些，就必须跳过哪些。**
            // **两者用同一个集合，是为了让"派生"与"跳过"不可能对不上。**
            //
            // **只在调用方没有显式传结构时自动派生：** **显式传入永远赢——
            // 调用方比构建路径更知道这个模型要什么。**
            //
            // **只处理 `Abs`：** **七种结构里只有 `AbsFunction` 的投影已经写出来。
            // 其余六种会走急切注册，行为与今天完全一样。**
            //
            // **Under a deferred policy, derive structure snapshots from the function symbols themselves and let those
            // symbols skip eager registration.**
            //
            // **This exists because of the gap where the projection exists and nobody calls it — while wiring it
            // alone would produce two row sets, since `AbsFunction` also registers its own eager constraints.**
            //
            // **So deriving and skipping must happen together, and they share one set so the two cannot disagree.**
            //
            // **Auto-derivation only when the caller passed nothing: an explicit argument always wins, because the caller
            // knows this model better than the build path does.**
            //
            // **Only `Abs` for now: of the seven structures only `AbsFunction` has a projection; the other six
            // register eagerly, exactly as they do today.**
            IReadOnlyList<DeferredFunctionStructure> effectiveStructures =
                deferredFunctionStructures ?? Array.Empty<DeferredFunctionStructure>();
            var deferredSymbols = new HashSet<IIntermediateSymbol>();
            if (deferredFunctionStructures is null
                && functionExpansionPolicy != FunctionExpansionPolicy.Eager
                && typeof(V) == typeof(Flt64)) {
                var derived = new List<DeferredFunctionStructure>(effectiveStructures);
                foreach (IIntermediateSymbol symbol in tokens.Symbols) {
                    // **模型里的符号表装的是<b>适配器</b>，不是裸函数符号**：
                    // 所以要先解包 `LinearFunctionSymbolAdapter.Function`。
                    // **The model's symbol table carries <b>adapters</b>, not raw function symbols**, so unwrap
                    // `LinearFunctionSymbolAdapter.Function` first.
                    //
                    // **而跳过用的仍然是<b>表里那个</b>符号**（适配器），因为 `RegisterFunctionConstraints`
                    // 遍历的正是这张表。
                    // **The skip set still holds the symbol <b>from the table</b> (the adapter), because that is what
                    // `RegisterFunctionConstraints` iterates.**
                    if (symbol is LinearFunctionSymbolAdapter<Flt64> adapter
                        && TryDerive(adapter.Function, out DeferredFunctionStructure? structure)) {
                        // **`metaModel.Converter` 是 `IFlt64ValueConverter<V>`，不是 `IIntoValue<Flt64>`，
                        // 两者不能互转。而 `V = Flt64` 时它是<b>按构造恒等</b>的**
                        // （`IdentityFlt64Converter.IntoValue` 就是原样返回），
                        // **所以这里用 `IIntoValue<Flt64>.Identity` 不是近似，是同一个函数。**
                        // **`metaModel.Converter` is `IFlt64ValueConverter<V>`, not `IIntoValue<Flt64>`, and the two do not
                        // cast. With `V = Flt64` it is <b>identity by construction</b> (`IdentityFlt64Converter.IntoValue`
                        // returns its argument), so `IIntoValue<Flt64>.Identity` here is the same function, not an
                        // approximation.**
                        derived.Add(structure!);
                        deferredSymbols.Add(symbol);
                    }
                }
                effectiveStructures = derived;
            }

            var model = new LinearMechanismModel<V>(
                parent: metaModel,
                name: metaModel.Name,
                constraints: constraints,
                objectFunction: objectFunction,
                tokens: tokens,
                functionExpansionPolicy: functionExpansionPolicy,
                deferredFunctionStructures: effectiveStructures
            );
            Try registration = RegisterFunctionConstraints(model, tokens.Symbols, deferredSymbols);
            if (registration is Failed<Success, ErrorCode, Error<ErrorCode>> failed) {
                return Results.Failed<LinearMechanismModel<V>>(failed.Error);
            }
            if (registration is Fatal<Success, ErrorCode, Error<ErrorCode>> fatal) {
                return new Fatal<LinearMechanismModel<V>, ErrorCode, Error<ErrorCode>>(fatal.Errors);
            }
            dependencies = metaModel.ValidateDynamicRows(tokens);
            if (dependencies is Failed<Success, ErrorCode, Error<ErrorCode>> changedDependency) {
                return Results.Failed<LinearMechanismModel<V>>(changedDependency.Error);
            }
            if (dependencies is Fatal<Success, ErrorCode, Error<ErrorCode>> fatalChangedDependency) {
                return new Fatal<LinearMechanismModel<V>, ErrorCode, Error<ErrorCode>>(fatalChangedDependency.Errors);
            }
            Try bounds = model.ValidateBoundDependencies();
            if (bounds is Failed<Success, ErrorCode, Error<ErrorCode>> changedBounds) {
                return Results.Failed<LinearMechanismModel<V>>(changedBounds.Error);
            }
            sourceCheck = sourceSnapshot.Validate();
            if (!sourceCheck.IsOk) {
                return MechanismModelSourceSnapshot<V>.Propagate<LinearMechanismModel<V>>(sourceCheck);
            }
            model.CaptureSourceSnapshot(sourceSnapshot);
            sourceCheck = model.ValidateSourceSnapshot();
            if (!sourceCheck.IsOk) {
                return MechanismModelSourceSnapshot<V>.Propagate<LinearMechanismModel<V>>(sourceCheck);
            }
            Try commit = preparation.Commit(
                beforeCommit: beforeCommit is null ? null : () => beforeCommit(model),
                validateSource: model.ValidateCommitSnapshot
            );
            if (commit is Failed<Success, ErrorCode, Error<ErrorCode>> failedCommit) {
                return Results.Failed<LinearMechanismModel<V>>(failedCommit.Error);
            }
            if (commit is Fatal<Success, ErrorCode, Error<ErrorCode>> fatalCommit) {
                return new Fatal<LinearMechanismModel<V>, ErrorCode, Error<ErrorCode>>(fatalCommit.Errors);
            }
            preparation.TransferOwnership();
            return Results.Ok(model);
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            return MechanismModelTokenPreparation<V>.BuildFailure<LinearMechanismModel<V>>(error);
        }
    }

    public Try AddConstraint(
        LinearInequality<V> relation,
        string? name = null,
        (IIntermediateSymbol Symbol, bool Direction)? from = null) {
        Result<LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> flattenResult = relation.ToLinearFlattenData(Tokens);
        if (flattenResult is Failed<SymbolFlatten.LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> f) {
            return Results.Failed<Success>(f.Error);
        }
        if (flattenResult is Fatal<SymbolFlatten.LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> fatalFlatten) {
            return new Fatal<Success, ErrorCode, Error<ErrorCode>>(fatalFlatten.Errors);
        }

        LinearFlattenData<V> flattenData = ((Ok<SymbolFlatten.LinearFlattenData<V>, ErrorCode, Error<ErrorCode>>)flattenResult).Value;
        string rowName = name ?? relation.Name;
        var rel = new LinearRelationImpl<V>(
            FlattenData: flattenData,
            Sign: relation.Comparison,
            Name: rowName,
            DisplayName: relation.DisplayName
        );
        Result<LinearConstraintImpl<V>, ErrorCode, Error<ErrorCode>> result = LinearConstraintImpl<V>.Create(
            relation: rel,
            tokens: Tokens,
            converter: Parent.Converter,
            name: rowName,
            from: from
        );
        if (result is Failed<LinearConstraintImpl<V>, ErrorCode, Error<ErrorCode>> cf) {
            return Results.Failed<Success>(cf.Error);
        }
        if (result is Fatal<LinearConstraintImpl<V>, ErrorCode, Error<ErrorCode>> fatal) {
            return new Fatal<Success, ErrorCode, Error<ErrorCode>>(fatal.Errors);
        }

        _constraints.Add(((Ok<LinearConstraintImpl<V>, ErrorCode, Error<ErrorCode>>)result).Value);
        return Results.Ok(Results.SuccessInstance);
    }

    public Try AddConstraint(
        LinearInequality<V> relation,
        string? name = null,
        IIntermediateSymbol? from = null) => AddConstraint(relation, name, from is not null ? (from, false) : null);

    /// <inheritdoc/>
    public Result<IConstraintWriteTransaction, ErrorCode, Error<ErrorCode>> BeginConstraintWrite() =>
        CreateConstraintWriteTransaction(_constraints);

    public void Dispose() => DisposeTokens();
    public override string ToString() => Name;

    /// <summary>
    /// **把一个函数符号投影成延迟结构；没有对应投影的返回 `false`** /
    /// Project a function symbol into a deferred structure; `false` when there is no projection for it.
    /// </summary>
    /// <remarks>
    /// **这个分派集中在一处。** **构建路径不再各写一遍
    /// `adapter.Function is AbsFunction(&lt;Flt64&gt;) abs`，加第二种就要再写一个 `if`。**
    ///
    /// **现在它是一个 `switch`，而<b>加一种结构就是加一个分支</b>——并且返回 `false` 的那些
    /// 会继续走急切注册，所以"没投影"与"投影了但跳过"是两条明确分开的路径。**
    ///
    /// **这也是这条线剩下的缺口清单本身：** **下面六个分支就是六种已投影的结构；
    /// 不在列表里的（`BivariateLinearPiecewise`、`BinaryLogic`、`Indicator`、`Masking`）
    /// 仍然只走急切注册。**
    ///
    /// **The dispatch lives in one place.** **The build path no longer repeats
    /// `adapter.Function is AbsFunction(&lt;Flt64&gt;) abs`, and a second kind meant another `if`.**
    ///
    /// **Now it is a `switch`, so <b>adding a kind is adding a branch</b> — and the `false` cases keep going through eager
    /// registration, so "no projection" and "projected and skipped" are two clearly separate paths.**
    ///
    /// **It is also the remaining gap list, and each absent kind is absent for a recorded reason — none of them is
    /// "not written yet":**
    ///
    /// | 没有分支的种类 | 断在哪一侧 | 具体原因 |
    /// | --- | --- | --- |
    /// | `XorFunction` | 符号 | 没存二元输入（`And`／`Or`／`Not` 都存了） |
    /// | `MaskingFunction` | 符号 | 没存 `ConditionBounds`，而 `MaskingStructure` 要一个 `ConditionalValue` |
    /// | `UnivariateLinearPiecewiseFunction` | 捕获 | 边界<b>捕获了</b>（`_inputDependencies`），但 `BoundDependencySnapshot` 是 `internal` 且只提供 `IsCurrent`——只能核对，读不回来 |
    /// | `IndicatorStructure` | 源 | <b>根本没有对应的函数符号</b>，只有几个行生成器辅助类 |
    ///
    /// **前两种是"符号没保留物化需要的东西"，后两种是"前提捕获了却拿不到"与"没有源"。**
    /// **四者都不是写一个投影就能解决的，所以它们不在这里的分支里，也不该在这里。**
    ///
    /// **It is also the remaining gap list, and each absent kind is absent for a recorded reason — none of them is "not
    /// written yet": two are "the symbol does not keep what materialisation needs", one captured the precondition but
    /// cannot hand it back, and one has no symbol to project at all.**
    ///
    /// **None is solvable by writing a projection, so none belongs in this `switch`.**
    /// </remarks>
    /// <param name="function">函数符号 / The function symbol.</param>
    /// <param name="structure">投影结果 / The projected structure.</param>
    /// <returns>是否有对应投影 / Whether a projection exists for it.</returns>
    private static bool TryDerive(
        IMathFunctionSymbol<Flt64> function,
        out DeferredFunctionStructure? structure) {
        switch (function) {
            case AbsFunction<Flt64> abs:
                structure = abs.ToDeferredStructure(IIntoValue<Flt64>.Identity);
                return true;
            case ExtremumFunctionBase<Flt64> extremum:
                structure = extremum.ToDeferredStructure(IIntoValue<Flt64>.Identity);
                return true;
            case SemiFunction semi:
                structure = semi.ToDeferredStructure(IIntoValue<Flt64>.Identity);
                return true;
            // **三种逻辑符号：投影可能返回 `null`（输入不是二元变量时），那时应当<b>不派生</b>并继续走急切注册。**
            // **Three logic symbols: the projection returns `null` when the inputs are not binary, and then nothing is
            // derived and eager registration continues.**
            case AndFunction andFunction:
                structure = andFunction.ToDeferredStructure(IIntoValue<Flt64>.Identity);
                return structure is not null;
            case OrFunction orFunction:
                structure = orFunction.ToDeferredStructure(IIntoValue<Flt64>.Identity);
                return structure is not null;
            case NotFunction notFunction:
                structure = notFunction.ToDeferredStructure(IIntoValue<Flt64>.Identity);
                return structure is not null;
            default:
                structure = null;
                return false;
        }
    }

    /// <summary>
    /// **注册函数符号的急切约束，可跳过交给延迟物化的那些** /
    /// Register the function symbols' eager constraints, skipping those handed to deferred materialisation.
    /// </summary>
    /// <remarks>
    /// **`deferredSymbols` 这个参数的作用。**
    ///
    /// **没有它，接上投影就会得到两套行：** **`AbsFunction` 既注册急切约束、又被投影成结构，
    /// 于是同一个 `abs` 被描述两次——而求解仍然成功，只是模型多了一段。**
    ///
    /// **这个参数默认是空集合，所以所有既有调用点的行为一个字都没变。**
    ///
    /// **What the `deferredSymbols` parameter is for.**
    ///
    /// **Without it, wiring the projection yields two row sets: `AbsFunction` both registers eagerly and is projected into a
    /// structure, so one `abs` is described twice — and the solve still succeeds, merely with an extra slice.**
    ///
    /// **It defaults to empty, so every existing call site behaves exactly as before.**
    /// </remarks>
    private static Try RegisterFunctionConstraints(
        LinearMechanismModel<V> model,
        IEnumerable<IIntermediateSymbol> symbols,
        IReadOnlySet<IIntermediateSymbol>? deferredSymbols = null) {
        foreach (IIntermediateSymbol symbol in symbols) {
            if (deferredSymbols is not null && deferredSymbols.Contains(symbol)) {
                continue;
            }
            Try result = symbol is IMathFunctionSymbolBase<V> function
                ? function.RegisterConstraints(model)
                : Results.Ok<Success>(Results.SuccessInstance);
            if (result.IsFailed) {
                return result;
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}

// ===== QuadraticMechanismModel<V> =====

/// <summary>
/// 二次机制模型实现 / Quadratic mechanism model implementation.
/// </summary>
public sealed class QuadraticMechanismModel<V> : BasicMechanismModel<V>,
    IAbstractQuadraticMechanismModel<V>, ISingleObjectMechanismModel<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    internal QuadraticMetaModel<V> Parent { get; }
    private readonly List<QuadraticConstraintImpl<V>> _quadraticConstraints;
    private readonly List<LinearConstraintImpl<V>> _linearConstraints;

    IReadOnlyList<IConstraint<V, LinearCategory>> IMechanismModel<V>.Constraints => _linearConstraints;
    IReadOnlyList<IConstraint<V, QuadraticCategory>> IAbstractQuadraticMechanismModel<V>.Constraints => _quadraticConstraints;
    IObject IMechanismModel<V>.ObjectFunction => ObjectFunction;
    public SingleObject ObjectFunction { get; }
    internal IReadOnlyList<QuadraticConstraintImpl<V>> QuadraticConstraints => _quadraticConstraints;

    internal QuadraticMechanismModel(
        QuadraticMetaModel<V> parent,
        string name,
        IReadOnlyList<QuadraticConstraintImpl<V>> constraints,
        SingleObject objectFunction,
        Fuookami.Ospf.Core.Token.IAbstractTokenTable<V> tokens)
        : base(name, tokens) {
        Parent = parent;
        _quadraticConstraints = constraints.ToList();
        _linearConstraints = new List<LinearConstraintImpl<V>>();
        ObjectFunction = objectFunction;
    }

    /// <summary>
    /// 构建二次机制模型并准备符号值 / Build a quadratic mechanism model and prepare symbol values.
    /// </summary>
    /// <param name="metaModel">源元模型 / Source meta-model.</param>
    /// <param name="concurrent">保留参数，当前未使用 / Reserved parameter, currently ignored.</param>
    /// <param name="blocking">保留参数，当前未使用 / Reserved parameter, currently ignored.</param>
    /// <param name="fixedVariables">用于符号准备的固定值，不执行列消元或值域校验 / Fixed values for symbol preparation, without column elimination or domain validation.</param>
    /// <param name="registrationStatusCallBack">可选注册进度回调 / Optional registration progress callback.</param>
    /// <param name="dumpingStatusCallBack">保留参数，当前未使用 / Reserved parameter, currently ignored.</param>
    /// <returns>成功模型或失败结果；调用方须释放成功模型 / A model or failure; the caller must dispose a successful model.</returns>
    public static Task<Result<QuadraticMechanismModel<V>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        QuadraticMetaModel<V> metaModel,
        bool? concurrent = null,
        bool? blocking = null,
        IReadOnlyDictionary<IVariableItem, V>? fixedVariables = null,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        MechanismModelDumpingStatusCallBack? dumpingStatusCallBack = null) => InvokeBeforeCommitAsync(
            metaModel: metaModel,
            fixedVariables: fixedVariables,
            registrationStatusCallBack: registrationStatusCallBack,
            beforeCommit: null
        );

    internal static async Task<Result<QuadraticMechanismModel<V>, ErrorCode, Error<ErrorCode>>> InvokeBeforeCommitAsync(
        QuadraticMetaModel<V> metaModel,
        IReadOnlyDictionary<IVariableItem, V>? fixedVariables,
        RegistrationStatusCallBack? registrationStatusCallBack,
        Func<QuadraticMechanismModel<V>, Try>? beforeCommit) {
        if (metaModel.IsClosed) {
            return Results.Failed<QuadraticMechanismModel<V>>(new Err<ErrorCode>(
                ErrorCode.ApplicationStopped,
                "模型已关闭，无法构建机制模型 / Model is closed; cannot build a mechanism model"));
        }
        MechanismModelSourceSnapshot<V> sourceSnapshot;
        try {
            sourceSnapshot = new MechanismModelSourceSnapshot<V>(metaModel);
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            return MechanismModelTokenPreparation<V>.BuildFailure<QuadraticMechanismModel<V>>(error);
        }
        Result<MechanismModelTokenPreparation<V>, ErrorCode, Error<ErrorCode>> preparationResult =
            MechanismModelTokenPreparation<V>.Create(
                parent: metaModel,
                fixedVariables: fixedVariables,
                callBack: registrationStatusCallBack
            );
        if (preparationResult is Failed<MechanismModelTokenPreparation<V>, ErrorCode, Error<ErrorCode>> failedPreparation) {
            return Results.Failed<QuadraticMechanismModel<V>>(failedPreparation.Error);
        }
        if (preparationResult is Fatal<MechanismModelTokenPreparation<V>, ErrorCode, Error<ErrorCode>> fatalPreparation) {
            return new Fatal<QuadraticMechanismModel<V>, ErrorCode, Error<ErrorCode>>(fatalPreparation.Errors);
        }
        using MechanismModelTokenPreparation<V> preparation = preparationResult.Value;
        Fuookami.Ospf.Core.Token.IAbstractTokenTable<V> tokens = preparation.Tokens;
        try {
            Try sourceCheck = sourceSnapshot.Validate();
            if (!sourceCheck.IsOk) {
                return MechanismModelSourceSnapshot<V>.Propagate<QuadraticMechanismModel<V>>(sourceCheck);
            }
            var constraints = new List<QuadraticConstraintImpl<V>>();
            foreach (QuadraticInequalityConstraint<V> mc in metaModel.RelationConstraints) {
                Result<SymbolFlatten.QuadraticFlattenData<V>, ErrorCode, Error<ErrorCode>> flattened =
                    mc.Inequality.ToQuadraticFlattenData(tokens);
                if (flattened is Failed<SymbolFlatten.QuadraticFlattenData<V>, ErrorCode, Error<ErrorCode>> failedFlatten) {
                    return Results.Failed<QuadraticMechanismModel<V>>(failedFlatten.Error);
                }
                if (flattened is Fatal<SymbolFlatten.QuadraticFlattenData<V>, ErrorCode, Error<ErrorCode>> fatalFlatten) {
                    return new Fatal<QuadraticMechanismModel<V>, ErrorCode, Error<ErrorCode>>(fatalFlatten.Errors);
                }
                SymbolFlatten.QuadraticFlattenData<V> flattenData = flattened.Value;
                var relation = new QuadraticRelationImpl<V>(
                    FlattenData: flattenData,
                    Sign: mc.Sign,
                    Name: mc.Name,
                    DisplayName: mc.DisplayName
                );
                Result<QuadraticConstraintImpl<V>, ErrorCode, Error<ErrorCode>> result = QuadraticConstraintImpl<V>.Create(
                    relation: relation,
                    tokens: tokens,
                    converter: metaModel.Converter,
                    lazy: mc.Lazy,
                    name: mc.Name,
                    origin: mc
                );
                if (result is Failed<QuadraticConstraintImpl<V>, ErrorCode, Error<ErrorCode>> cf) {
                    return Results.Failed<QuadraticMechanismModel<V>>(cf.Error);
                }
                if (result is Fatal<QuadraticConstraintImpl<V>, ErrorCode, Error<ErrorCode>> fatalConstraint) {
                    return new Fatal<QuadraticMechanismModel<V>, ErrorCode, Error<ErrorCode>>(fatalConstraint.Errors);
                }

                constraints.Add(((Ok<QuadraticConstraintImpl<V>, ErrorCode, Error<ErrorCode>>)result).Value);
            }

            var subObjects = new List<object>();
            foreach (QuadraticFlattenSubObject<V> source in metaModel.FlattenSubObjects) {
                Result<QuadraticSubObject<V>, ErrorCode, Error<ErrorCode>> subObj = QuadraticSubObject<V>.Create(
                    flattenData: source.FlattenData,
                    tokens: tokens,
                    converter: metaModel.Converter,
                    category: source.Category,
                    name: source.Name,
                    displayName: source.DisplayName
                );
                if (subObj is Failed<QuadraticSubObject<V>, ErrorCode, Error<ErrorCode>> objectiveFailure) {
                    return Results.Failed<QuadraticMechanismModel<V>>(objectiveFailure.Error);
                }
                if (subObj is Fatal<QuadraticSubObject<V>, ErrorCode, Error<ErrorCode>> fatalObjective) {
                    return new Fatal<QuadraticMechanismModel<V>, ErrorCode, Error<ErrorCode>>(fatalObjective.Errors);
                }
                subObjects.Add(subObj.Value with { Origin = source });
            }

            var objectFunction = new SingleObject(metaModel.ObjectCategory, subObjects);
            var model = new QuadraticMechanismModel<V>(
                parent: metaModel,
                name: metaModel.Name,
                constraints: constraints,
                objectFunction: objectFunction,
                tokens: tokens
            );
            Try registration = RegisterFunctionConstraints(model, tokens.Symbols);
            if (registration is Failed<Success, ErrorCode, Error<ErrorCode>> failed) {
                return Results.Failed<QuadraticMechanismModel<V>>(failed.Error);
            }
            if (registration is Fatal<Success, ErrorCode, Error<ErrorCode>> fatal) {
                return new Fatal<QuadraticMechanismModel<V>, ErrorCode, Error<ErrorCode>>(fatal.Errors);
            }
            Try bounds = model.ValidateBoundDependencies();
            if (bounds is Failed<Success, ErrorCode, Error<ErrorCode>> changedBounds) {
                return Results.Failed<QuadraticMechanismModel<V>>(changedBounds.Error);
            }
            sourceCheck = sourceSnapshot.Validate();
            if (!sourceCheck.IsOk) {
                return MechanismModelSourceSnapshot<V>.Propagate<QuadraticMechanismModel<V>>(sourceCheck);
            }
            model.CaptureSourceSnapshot(sourceSnapshot);
            sourceCheck = model.ValidateSourceSnapshot();
            if (!sourceCheck.IsOk) {
                return MechanismModelSourceSnapshot<V>.Propagate<QuadraticMechanismModel<V>>(sourceCheck);
            }
            Try commit = preparation.Commit(
                beforeCommit: beforeCommit is null ? null : () => beforeCommit(model),
                validateSource: model.ValidateCommitSnapshot
            );
            if (commit is Failed<Success, ErrorCode, Error<ErrorCode>> failedCommit) {
                return Results.Failed<QuadraticMechanismModel<V>>(failedCommit.Error);
            }
            if (commit is Fatal<Success, ErrorCode, Error<ErrorCode>> fatalCommit) {
                return new Fatal<QuadraticMechanismModel<V>, ErrorCode, Error<ErrorCode>>(fatalCommit.Errors);
            }
            preparation.TransferOwnership();
            return Results.Ok(model);
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            return MechanismModelTokenPreparation<V>.BuildFailure<QuadraticMechanismModel<V>>(error);
        }
    }

    public Try AddConstraint(
        LinearInequality<V> relation,
        string? name = null,
        (IIntermediateSymbol Symbol, bool Direction)? from = null) {
        // Promote to quadratic
        Result<LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> flattenResult = relation.ToLinearFlattenData(Tokens);
        if (flattenResult is Failed<SymbolFlatten.LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> f) {
            return Results.Failed<Success>(f.Error);
        }
        if (flattenResult is Fatal<SymbolFlatten.LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> fatalFlatten) {
            return new Fatal<Success, ErrorCode, Error<ErrorCode>>(fatalFlatten.Errors);
        }

        LinearFlattenData<V> flattenData = ((Ok<SymbolFlatten.LinearFlattenData<V>, ErrorCode, Error<ErrorCode>>)flattenResult).Value;
        var qMonomials = flattenData.Monomials.Select(m =>
            new QuadraticMonomial<V>(m.Coefficient, m.Symbol)).ToList();
        var qFlattenData = new SymbolFlatten.QuadraticFlattenData<V>(qMonomials, flattenData.Constant);
        string rowName = name ?? relation.Name;
        var rel = new QuadraticRelationImpl<V>(
            FlattenData: qFlattenData,
            Sign: relation.Comparison,
            Name: rowName,
            DisplayName: relation.DisplayName
        );
        Result<QuadraticConstraintImpl<V>, ErrorCode, Error<ErrorCode>> result = QuadraticConstraintImpl<V>.Create(
            relation: rel,
            tokens: Tokens,
            converter: Parent.Converter,
            name: rowName,
            from: from
        );
        if (result is Failed<QuadraticConstraintImpl<V>, ErrorCode, Error<ErrorCode>> cf) {
            return Results.Failed<Success>(cf.Error);
        }
        if (result is Fatal<QuadraticConstraintImpl<V>, ErrorCode, Error<ErrorCode>> fatal) {
            return new Fatal<Success, ErrorCode, Error<ErrorCode>>(fatal.Errors);
        }

        _quadraticConstraints.Add(((Ok<QuadraticConstraintImpl<V>, ErrorCode, Error<ErrorCode>>)result).Value);
        return Results.Ok(Results.SuccessInstance);
    }

    public Try AddConstraint(
        LinearInequality<V> relation,
        string? name = null,
        IIntermediateSymbol? from = null) => AddConstraint(relation, name, from is not null ? (from, false) : null);

    /// <inheritdoc/>
    public Result<IConstraintWriteTransaction, ErrorCode, Error<ErrorCode>> BeginConstraintWrite() =>
        CreateConstraintWriteTransaction(_quadraticConstraints);

    public Try AddConstraint(
        QuadraticInequalityOf<V> relation,
        string? name = null,
        (IIntermediateSymbol Symbol, bool Direction)? from = null) {
        Result<SymbolFlatten.QuadraticFlattenData<V>, ErrorCode, Error<ErrorCode>> flattened =
            relation.ToQuadraticFlattenData(Tokens);
        if (flattened is Failed<SymbolFlatten.QuadraticFlattenData<V>, ErrorCode, Error<ErrorCode>> failedFlatten) {
            return Results.Failed<Success>(failedFlatten.Error);
        }
        if (flattened is Fatal<SymbolFlatten.QuadraticFlattenData<V>, ErrorCode, Error<ErrorCode>> fatalFlatten) {
            return new Fatal<Success, ErrorCode, Error<ErrorCode>>(fatalFlatten.Errors);
        }
        SymbolFlatten.QuadraticFlattenData<V> flattenData = flattened.Value;
        string rowName = name ?? relation.Name;
        var rel = new QuadraticRelationImpl<V>(
            FlattenData: flattenData,
            Sign: relation.Comparison,
            Name: rowName,
            DisplayName: relation.DisplayName
        );
        Result<QuadraticConstraintImpl<V>, ErrorCode, Error<ErrorCode>> result = QuadraticConstraintImpl<V>.Create(
            relation: rel,
            tokens: Tokens,
            converter: Parent.Converter,
            name: rowName,
            from: from
        );
        if (result is Failed<QuadraticConstraintImpl<V>, ErrorCode, Error<ErrorCode>> cf) {
            return Results.Failed<Success>(cf.Error);
        }
        if (result is Fatal<QuadraticConstraintImpl<V>, ErrorCode, Error<ErrorCode>> fatal) {
            return new Fatal<Success, ErrorCode, Error<ErrorCode>>(fatal.Errors);
        }

        _quadraticConstraints.Add(((Ok<QuadraticConstraintImpl<V>, ErrorCode, Error<ErrorCode>>)result).Value);
        return Results.Ok(Results.SuccessInstance);
    }

    public Try AddConstraint(
        QuadraticInequalityOf<V> relation,
        string? name = null,
        IIntermediateSymbol? from = null) => AddConstraint(relation, name, from is not null ? (from, false) : null);

    private static Try RegisterFunctionConstraints(
        QuadraticMechanismModel<V> model,
        IEnumerable<IIntermediateSymbol> symbols) {
        foreach (IIntermediateSymbol symbol in symbols) {
            Try result = symbol switch {
                IQuadraticMathFunctionSymbolBase<V> quadratic =>
                    quadratic.RegisterConstraints(model),
                IMathFunctionSymbolBase<V> linear =>
                    linear.RegisterConstraints(model),
                _ => Results.Ok<Success>(Results.SuccessInstance),
            };
            if (result.IsFailed) {
                return result;
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    public void Dispose() => DisposeTokens();
    public override string ToString() => Name;
}
