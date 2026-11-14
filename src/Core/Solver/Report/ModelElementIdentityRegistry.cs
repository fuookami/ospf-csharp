#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Solver.Report;

/// <summary>模型元素身份绑定 / Model-element identity binding.</summary>
/// <remarks>直接构造或 with 赋值要求来源集合可读取，读取异常会传播；不可信输入应通过注册表校验。 / Direct construction or with assignment requires readable provenance and propagates read errors; validate untrusted input through the registry.</remarks>
public sealed record ModelElementIdentity(
    ModelElementKind Kind,
    ModelElementId Id,
    ModelElementScope Scope,
    ModelElementOrigin? Origin = null,
    IReadOnlyList<ModelElementOrigin>? Provenance = null) {
    private readonly IReadOnlyList<ModelElementOrigin> _provenance = Array.AsReadOnly(
        (Provenance ?? (Origin is null ? Array.Empty<ModelElementOrigin>() : new[] { Origin })).ToArray());

    /// <summary>完整来源集合 / Complete provenance.</summary>
    public IReadOnlyList<ModelElementOrigin> Provenance {
        get => _provenance;
        init => _provenance = Array.AsReadOnly((value ?? Array.Empty<ModelElementOrigin>()).ToArray());
    }

    /// <summary>按字段和来源顺序比较身份 / Compare identity fields and ordered provenance.</summary>
    /// <param name="other">待比较身份 / Identity to compare.</param>
    /// <returns>是否相等 / Whether the identities are equal.</returns>
    public bool Equals(ModelElementIdentity? other) => other is not null
        && Kind == other.Kind && Id == other.Id && Scope == other.Scope
        && object.Equals(Origin, other.Origin) && Provenance.SequenceEqual(other.Provenance);

    /// <inheritdoc/>
    public override int GetHashCode() {
        var hash = new HashCode();
        hash.Add(Kind);
        hash.Add(Id);
        hash.Add(Scope);
        hash.Add(Origin);
        foreach (ModelElementOrigin origin in Provenance) {
            hash.Add(origin);
        }
        return hash.ToHashCode();
    }
}

/// <summary>稳定模型元素身份注册表 / Stable model-element identity registry.</summary>
/// <remarks>身份解析期间重入新增绑定会失败；已有相同绑定仍可重用。 / Reentrant new bindings fail during identity resolution; identical existing bindings remain reusable.</remarks>
public sealed class ModelElementIdentityRegistry {
    private static readonly string[] ReservedPrefixes = { "model-local-", "artifact:" };
    private sealed record WriteState(
        KeyValuePair<object, ModelElementIdentity>[] Bindings,
        KeyValuePair<object, KeyValuePair<ModelElementKind, ModelElementIdentity>[]>[] FallbackBindings,
        KeyValuePair<ModelElementId, (ModelElementKind Kind, object Element)>[] Ids,
        KeyValuePair<(ModelElementKind Kind, string Base), int>[] FallbackCounters,
        object Revision,
        int ResolutionDepth);

    private readonly object _gate = new();
    private readonly Dictionary<object, ModelElementIdentity> _bindings =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, Dictionary<ModelElementKind, ModelElementIdentity>> _fallbackBindings =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ModelElementId, (ModelElementKind Kind, object Element)> _ids = new();
    private readonly Dictionary<(ModelElementKind Kind, string Base), int> _fallbackCounters = new();
    private object _revision = new();
    private int _resolutionDepth;

    internal object Revision {
        get {
            lock (_gate) {
                return _revision;
            }
        }
    }

    /// <summary>创建身份注册表 / Create an identity registry.</summary>
    /// <param name="namespace">序列化 artifact 的身份命名空间 / Identity namespace for serialized artifacts.</param>
    /// <param name="schemaVersion">身份注册表 schema 版本 / Identity registry schema version.</param>
    public ModelElementIdentityRegistry(
        string @namespace = "ospf",
        string schemaVersion = "1.0") {
        Namespace = @namespace;
        SchemaVersion = schemaVersion;
    }

    internal object CaptureState() {
        lock (_gate) {
            return new WriteState(
                Bindings: _bindings.ToArray(),
                FallbackBindings: _fallbackBindings
                    .Select(pair => new KeyValuePair<
                        object,
                        KeyValuePair<ModelElementKind, ModelElementIdentity>[]>(
                        pair.Key,
                        pair.Value.ToArray()))
                    .ToArray(),
                Ids: _ids.ToArray(),
                FallbackCounters: _fallbackCounters.ToArray(),
                Revision: _revision,
                ResolutionDepth: _resolutionDepth);
        }
    }

    internal Try RestoreState(object state) {
        if (state is not WriteState snapshot) {
            return Failure("模型身份事务快照无效 / Invalid model-identity transaction snapshot");
        }
        lock (_gate) {
            try {
                _bindings.Clear();
                foreach ((object element, ModelElementIdentity identity) in snapshot.Bindings) {
                    _bindings.Add(element, identity);
                }
                _fallbackBindings.Clear();
                foreach (KeyValuePair<
                             object,
                             KeyValuePair<ModelElementKind, ModelElementIdentity>[]> pair in snapshot.FallbackBindings) {
                    _fallbackBindings.Add(
                        pair.Key,
                        pair.Value.ToDictionary(entry => entry.Key, entry => entry.Value));
                }
                _ids.Clear();
                foreach (KeyValuePair<ModelElementId, (ModelElementKind Kind, object Element)> pair in snapshot.Ids) {
                    _ids.Add(pair.Key, pair.Value);
                }
                _fallbackCounters.Clear();
                foreach (KeyValuePair<(ModelElementKind Kind, string Base), int> pair in snapshot.FallbackCounters) {
                    _fallbackCounters.Add(pair.Key, pair.Value);
                }
                _revision = snapshot.Revision;
                _resolutionDepth = snapshot.ResolutionDepth;
                return Results.Ok(Results.SuccessInstance);
            }
            catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
                return Failure(
                    $"恢复模型身份事务失败：{error.Message} / Cannot restore model-identity transaction: {error.Message}");
            }
        }
    }

    /// <summary>身份命名空间 / Identity namespace.</summary>
    public string Namespace { get; }

    /// <summary>身份注册表 schema 版本 / Identity registry schema version.</summary>
    public string SchemaVersion { get; }

    /// <summary>注册变量身份 / Register a variable identity.</summary>
    /// <param name="element">变量对象；null 返回失败 / Variable object; null returns a failure.</param>
    /// <param name="id">稳定变量标识 / Stable variable identifier.</param>
    /// <param name="origin">主来源 / Primary origin.</param>
    /// <param name="provenance">完整来源 / Complete provenance.</param>
    /// <returns>注册结果 / Registration result.</returns>
    public Try RegisterVariable(
        object? element,
        VariableId id,
        ModelElementOrigin? origin = null,
        IReadOnlyList<ModelElementOrigin>? provenance = null) =>
        Register(
            element: element,
            kind: ModelElementKind.Variable,
            id: new ModelElementId(id.Value),
            origin: origin,
            provenance: provenance);

    /// <summary>注册约束身份 / Register a constraint identity.</summary>
    /// <param name="element">约束对象；null 返回失败 / Constraint object; null returns a failure.</param>
    /// <param name="id">稳定约束标识 / Stable constraint identifier.</param>
    /// <param name="origin">主来源 / Primary origin.</param>
    /// <param name="provenance">完整来源 / Complete provenance.</param>
    /// <returns>注册结果 / Registration result.</returns>
    public Try RegisterConstraint(
        object? element,
        ConstraintId id,
        ModelElementOrigin? origin = null,
        IReadOnlyList<ModelElementOrigin>? provenance = null) =>
        Register(
            element: element,
            kind: ModelElementKind.Constraint,
            id: new ModelElementId(id.Value),
            origin: origin,
            provenance: provenance);

    /// <summary>注册目标身份 / Register an objective identity.</summary>
    /// <param name="element">目标对象；null 返回失败 / Objective object; null returns a failure.</param>
    /// <param name="id">稳定目标标识 / Stable objective identifier.</param>
    /// <param name="origin">主来源 / Primary origin.</param>
    /// <param name="provenance">完整来源 / Complete provenance.</param>
    /// <returns>注册结果 / Registration result.</returns>
    public Try RegisterObjective(
        object? element,
        ObjectiveId id,
        ModelElementOrigin? origin = null,
        IReadOnlyList<ModelElementOrigin>? provenance = null) =>
        Register(
            element: element,
            kind: ModelElementKind.Objective,
            id: new ModelElementId(id.Value),
            origin: origin,
            provenance: provenance);

    /// <summary>解析变量标识 / Resolve a variable identifier.</summary>
    /// <param name="element">变量对象 / Variable object.</param>
    /// <param name="fallbackIndex">模型内回退索引 / Model-local fallback index.</param>
    /// <returns>变量标识 / Variable identifier.</returns>
    public VariableId VariableId(object element, int fallbackIndex) {
        ArgumentNullException.ThrowIfNull(element);
        lock (_gate) {
            return new VariableId(
                Resolve(
                    element: element,
                    kind: ModelElementKind.Variable,
                    fallback: $"model-local-variable:{fallbackIndex}").Value);
        }
    }

    /// <summary>解析约束标识 / Resolve a constraint identifier.</summary>
    /// <param name="element">约束对象 / Constraint object.</param>
    /// <param name="fallbackIndex">模型内回退索引 / Model-local fallback index.</param>
    /// <returns>约束标识 / Constraint identifier.</returns>
    public ConstraintId ConstraintId(object element, int fallbackIndex) {
        ArgumentNullException.ThrowIfNull(element);
        lock (_gate) {
            return new ConstraintId(
                Resolve(
                    element: element,
                    kind: ModelElementKind.Constraint,
                    fallback: $"model-local-constraint:{fallbackIndex}").Value);
        }
    }

    /// <summary>解析目标标识 / Resolve an objective identifier.</summary>
    /// <param name="element">目标对象 / Objective object.</param>
    /// <param name="fallbackIndex">模型内回退索引 / Model-local fallback index.</param>
    /// <returns>目标标识 / Objective identifier.</returns>
    public ObjectiveId ObjectiveId(object element, int fallbackIndex = 0) {
        ArgumentNullException.ThrowIfNull(element);
        lock (_gate) {
            return new ObjectiveId(
                Resolve(
                    element: element,
                    kind: ModelElementKind.Objective,
                    fallback: $"model-local-objective:{fallbackIndex}").Value);
        }
    }

    /// <summary>返回元素已注册的稳定身份 / Return the registered stable identity.</summary>
    /// <param name="element">模型元素 / Model element.</param>
    /// <returns>稳定身份；未显式注册时为 null / Stable identity, or null when not registered.</returns>
    public ModelElementIdentity? Identity(object element) {
        ArgumentNullException.ThrowIfNull(element);
        lock (_gate) {
            return _bindings.TryGetValue(element, out ModelElementIdentity? identity)
                ? identity
                : null;
        }
    }

    /// <summary>解除元素上的全部身份绑定，使该 ID 可在元素移除后重新使用 / Release every identity binding on an element so its ID becomes reusable after removal.</summary>
    /// <param name="element">模型元素 / Model element.</param>
    /// <returns>解除结果 / Release result.</returns>
    internal Try Release(object? element) {
        if (element is null) {
            return Failure("模型元素不能为空 / Model element must not be null");
        }
        lock (_gate) {
            bool changed = _bindings.Remove(element);
            changed |= _fallbackBindings.Remove(element);
            foreach (ModelElementId id in _ids
                         .Where(pair => ReferenceEquals(pair.Value.Element, element))
                         .Select(pair => pair.Key)
                         .ToList()) {
                _ids.Remove(id);
                changed = true;
            }
            if (changed) {
                _revision = new object();
            }
            return Results.OkInstance;
        }
    }

    /// <summary>按稳定 ID 顺序返回全部绑定 / Return all bindings in stable ID order.</summary>
    /// <returns>已注册身份和回退身份 / Registered and fallback identities.</returns>
    public IReadOnlyList<ModelElementIdentity> Entries() {
        lock (_gate) {
            return _bindings.Values
                .Concat(_fallbackBindings.Values.SelectMany(bindings => bindings.Values))
                .OrderBy(identity => identity.Kind.ToString(), StringComparer.Ordinal)
                .ThenBy(identity => identity.Id.Value, StringComparer.Ordinal)
                .ToList();
        }
    }

    /// <summary>校验命名空间、schema 和重复身份 / Validate namespace, schema, and duplicate identities.</summary>
    /// <returns>校验结果 / Validation result.</returns>
    public Try Validate() {
        lock (_gate) {
            if (string.IsNullOrWhiteSpace(Namespace) || string.IsNullOrWhiteSpace(SchemaVersion)) {
                return Failure(
                    "模型身份 namespace/schema 不能为空 / Model identity namespace and schema must not be blank");
            }
            IReadOnlyList<ModelElementIdentity> entries = Entries();
            if (entries.Any(identity => string.IsNullOrWhiteSpace(identity.Id.Value))) {
                return Failure("模型元素 ID 不能为空 / Model element IDs must not be blank");
            }
            ModelElementIdentity? duplicate = entries
                .GroupBy(identity => identity.Id)
                .FirstOrDefault(group => group.Count() > 1)
                ?.First();
            if (duplicate is not null) {
                return Failure(
                    $"模型元素 ID 重复：{duplicate.Id.Value} / Duplicate model element ID: {duplicate.Id.Value}");
            }
            return Results.OkInstance;
        }
    }

    internal Result<T, ErrorCode, Error<ErrorCode>> ResolveAtomically<T>(
        Func<Result<T, ErrorCode, Error<ErrorCode>>> resolve) {
        lock (_gate) {
            Try validation = Validate();
            if (!validation.IsOk) {
                return validation switch {
                    Failed<Success, ErrorCode, Error<ErrorCode>> failed => Results.Failed<T>(failed.Error),
                    Fatal<Success, ErrorCode, Error<ErrorCode>> fatal => new Fatal<T, ErrorCode, Error<ErrorCode>>(fatal.Errors),
                    _ => Results.Failed<T>(new Err<ErrorCode>(ErrorCode.ApplicationError, "身份校验结果无效 / Invalid identity validation result")),
                };
            }
            var bindings = _fallbackBindings.ToDictionary(
                keySelector: pair => pair.Key,
                elementSelector: pair => new Dictionary<ModelElementKind, ModelElementIdentity>(pair.Value),
                comparer: ReferenceEqualityComparer.Instance);
            var ids = new Dictionary<ModelElementId, (ModelElementKind Kind, object Element)>(_ids);
            var counters = new Dictionary<(ModelElementKind Kind, string Base), int>(_fallbackCounters);
            Result<T, ErrorCode, Error<ErrorCode>> result;
            _resolutionDepth++;
            try {
                result = resolve();
            }
            catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
                result = Results.Failed<T>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    $"无法解析模型身份：{error.Message} / Cannot resolve model identities: {error.Message}"));
            }
            finally {
                _resolutionDepth--;
            }
            if (!result.IsOk) {
                _fallbackBindings.Clear();
                foreach (KeyValuePair<object, Dictionary<ModelElementKind, ModelElementIdentity>> pair in bindings) {
                    _fallbackBindings.Add(pair.Key, pair.Value);
                }
                _ids.Clear();
                foreach (KeyValuePair<ModelElementId, (ModelElementKind Kind, object Element)> pair in ids) {
                    _ids.Add(pair.Key, pair.Value);
                }
                _fallbackCounters.Clear();
                foreach (KeyValuePair<(ModelElementKind Kind, string Base), int> pair in counters) {
                    _fallbackCounters.Add(pair.Key, pair.Value);
                }
            }
            return result;
        }
    }

    private ModelElementId Resolve(
        object element,
        ModelElementKind kind,
        string fallback) {
        if (_bindings.TryGetValue(element, out ModelElementIdentity? identity) &&
            identity.Kind == kind) {
            return identity.Id;
        }
        if (!_fallbackBindings.TryGetValue(element, out Dictionary<ModelElementKind, ModelElementIdentity>? byKind)) {
            byKind = new Dictionary<ModelElementKind, ModelElementIdentity>();
            _fallbackBindings[element] = byKind;
        }
        if (byKind.TryGetValue(kind, out ModelElementIdentity? fallbackIdentity)) {
            return fallbackIdentity.Id;
        }

        string candidate = fallback;
        int suffix = _fallbackCounters.GetValueOrDefault((kind, fallback));
        while (_ids.ContainsKey(new ModelElementId(candidate))) {
            ++suffix;
            candidate = $"{fallback}:collision-{suffix}";
        }
        _fallbackCounters[(kind, fallback)] = suffix;
        ModelElementIdentity created = new(
            Kind: kind,
            Id: new ModelElementId(candidate),
            Scope: ModelElementScope.ModelLocal);
        byKind[kind] = created;
        _ids[created.Id] = (kind, element);
        return created.Id;
    }

    private Try Register(
        object? element,
        ModelElementKind kind,
        ModelElementId id,
        ModelElementOrigin? origin,
        IReadOnlyList<ModelElementOrigin>? provenance) {
        if (element is null) {
            return Failure("模型元素不能为空 / Model element must not be null");
        }
        lock (_gate) {
            if (string.IsNullOrWhiteSpace(id.Value)) {
                return Failure("模型元素 ID 不能为空 / Model element ID must not be blank");
            }
            if (!DerivedModelElementIdentity.IsWellFormedUtf16(id.Value)) {
                return Failure("模型元素 ID 包含无效 Unicode / Model element ID contains invalid Unicode");
            }
            if (ReservedPrefixes.Any(prefix => id.Value.StartsWith(prefix, StringComparison.Ordinal))) {
                return Failure(
                    $"模型元素 ID 使用了保留前缀：{id.Value} / Model element ID uses a reserved prefix: {id.Value}");
            }

            IReadOnlyList<ModelElementOrigin>? canonicalProvenance;
            try {
                canonicalProvenance = CanonicalProvenance(
                    origin: origin,
                    provenance: provenance);
            }
            catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
                return Failure("无法读取模型元素 provenance / Cannot read model element provenance");
            }
            if (canonicalProvenance is null) {
                return Failure("模型元素 provenance 不能为空 / Model element provenance entries must not be blank");
            }
            if (origin is not null && !canonicalProvenance.Contains(origin)) {
                return Failure("模型元素 primary origin 必须属于 provenance / Model element primary origin must be included in provenance");
            }

            if (_bindings.TryGetValue(element, out ModelElementIdentity? existing)) {
                bool identical =
                    existing.Kind == kind &&
                    existing.Id == id &&
                    Equals(existing.Origin, origin) &&
                    existing.Provenance.SequenceEqual(canonicalProvenance);
                return identical
                    ? Results.OkInstance
                    : Failure("模型元素重复绑定身份 / Model element is already bound to another identity");
            }
            if (_ids.TryGetValue(id, out (ModelElementKind Kind, object Element) existingIdentity) &&
                (existingIdentity.Kind != kind || !ReferenceEquals(existingIdentity.Element, element))) {
                return Failure(
                    $"模型元素 ID 重复：{id.Value} / Duplicate model element ID: {id.Value}");
            }
            // 防止回滚后稳定绑定与 ID 索引分离 / Prevent stable bindings from outliving their ID index on rollback.
            if (_resolutionDepth != 0) {
                return Failure("身份解析期间不能新增绑定 / Cannot add identity bindings during identity resolution");
            }

            if (_fallbackBindings.TryGetValue(element, out Dictionary<ModelElementKind, ModelElementIdentity>? byKind) &&
                byKind.Remove(kind, out ModelElementIdentity? fallbackIdentity)) {
                _ids.Remove(fallbackIdentity.Id);
                if (byKind.Count == 0) {
                    _fallbackBindings.Remove(element);
                }
            }

            ModelElementIdentity identity = new(
                Kind: kind,
                Id: id,
                Scope: ModelElementScope.Stable,
                Origin: origin,
                Provenance: canonicalProvenance);
            _bindings[element] = identity;
            _ids[id] = (kind, element);
            _revision = new object();
            return Results.OkInstance;
        }
    }

    private static IReadOnlyList<ModelElementOrigin>? CanonicalProvenance(
        ModelElementOrigin? origin,
        IReadOnlyList<ModelElementOrigin>? provenance) {
        IEnumerable<ModelElementOrigin> source =
            provenance is null || provenance.Count == 0
                ? origin is null ? Array.Empty<ModelElementOrigin>() : new[] { origin }
                : provenance;
        var entries = source.ToList();
        if (entries.Any(entry => entry is null ||
                string.IsNullOrWhiteSpace(entry.Kind) || string.IsNullOrWhiteSpace(entry.Key))) {
            return null;
        }
        return entries
            .Distinct()
            .OrderBy(entry => entry.Kind, StringComparer.Ordinal)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .ToList();
    }

    private static Try Failure(string message) =>
        Results.Failed<Success>(
            new Err<ErrorCode>(
                code: ErrorCode.IllegalArgument,
                message: message));
}
