#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Model.Callback
{
    // ===== ICallBackModelPolicy<V> =====

    public interface ICallBackModelPolicy<V>
        where V : struct, IRealNumber<V>, INumberField<V>
    {
        Func<V, V, Order> Comparator { get; }
        Order? CompareObjective(V? lhs, V? rhs);
        IReadOnlyList<IReadOnlyList<V>> InitialSolutions(ulong initialSolutionAmount, ulong variableAmount) =>
            Array.Empty<IReadOnlyList<V>>();
    }

    // ===== FunctionalCallBackModelPolicy<V> =====

    public sealed class FunctionalCallBackModelPolicy<V> : ICallBackModelPolicy<V>
        where V : struct, IRealNumber<V>, INumberField<V>
    {
        private readonly Func<V, V, bool>? _objectiveComparator;
        private readonly Func<ulong, ulong, V>? _initialSolutionGenerator;

        public Func<V, V, Order> Comparator { get; }

        public FunctionalCallBackModelPolicy(
            Func<V, V, bool>? objectiveComparator = null,
            Func<ulong, ulong, V>? initialSolutionGenerator = null)
        {
            _objectiveComparator = objectiveComparator;
            _initialSolutionGenerator = initialSolutionGenerator;
            Comparator = (lhs, rhs) =>
            {
                if (_objectiveComparator is null) return new Order.Equal();
                if (_objectiveComparator(lhs, rhs)) return new Order.Less();
                if (_objectiveComparator(rhs, lhs)) return new Order.Greater();
                return new Order.Equal();
            };
        }

        public Order? CompareObjective(V? lhs, V? rhs)
        {
            if (lhs is not null && rhs is null) return new Order.Less();
            if (lhs is null && rhs is not null) return new Order.Greater();
            if (lhs is not null && rhs is not null) return Comparator(lhs.Value, rhs.Value);
            return null;
        }

        public IReadOnlyList<IReadOnlyList<V>> InitialSolutions(ulong initialSolutionAmount, ulong variableAmount)
        {
            if (_initialSolutionGenerator is null) return Array.Empty<IReadOnlyList<V>>();
            var solutions = new List<IReadOnlyList<V>>();
            for (ulong s = 0; s < initialSolutionAmount; s++)
            {
                var solution = new List<V>();
                for (ulong v = 0; v < variableAmount; v++)
                    solution.Add(_initialSolutionGenerator(s, v));
                solutions.Add(solution);
            }
            return solutions;
        }
    }

    // ===== CallBackModel<V> =====

    public sealed class CallBackModel<V> : ICallBackModelInterface<V>
        where V : struct, IRealNumber<V>, INumberField<V>
    {
        private readonly List<(Func<IReadOnlyList<V>, bool?> Extractor, string Name)> _constraints = new();
        // Obj = V (struct, unconstrained in interface), so Obj? = V (not Nullable<V>)
        private readonly List<(Func<IReadOnlyList<V>, V> Extractor, string Name)> _objectiveFunctions = new();
        private readonly ICallBackModelPolicy<V> _policy;
        private readonly IFlt64ValueConverter<V> _converter;

        public ObjectCategory ObjectCategory { get; }
        public Fuookami.Ospf.Core.Token.IAbstractMutableTokenTable<V> Tokens { get; }
        public IReadOnlyList<(Func<IReadOnlyList<V>, bool?> Extractor, string Name)> Constraints => _constraints;
        // Obj? for unconstrained Obj = V (struct) is just V, not V?
        public IReadOnlyList<(Func<IReadOnlyList<V>, V> Extractor, string Name)> ObjectiveFunctions => _objectiveFunctions;

        public V DefaultObjective => ObjectCategory == ObjectCategory.Minimum
            ? NegativeInfinity() : Infinity();

        internal CallBackModel(
            ObjectCategory objectCategory,
            Fuookami.Ospf.Core.Token.IAbstractMutableTokenTable<V> tokens,
            ICallBackModelPolicy<V> policy,
            IFlt64ValueConverter<V> converter)
        {
            ObjectCategory = objectCategory;
            Tokens = tokens;
            _policy = policy;
            _converter = converter;
        }

        public static CallBackModel<V> Create(
            ObjectCategory objectCategory,
            IFlt64ValueConverter<V> converter,
            Func<ulong, ulong, V>? initialSolutionGenerator = null)
        {
            Func<V, V, bool> comparator = objectCategory == ObjectCategory.Maximum
                ? (lhs, rhs) => converter.FromValue(lhs) >= converter.FromValue(rhs)
                : (lhs, rhs) => converter.FromValue(lhs) <= converter.FromValue(rhs);
            var policy = new FunctionalCallBackModelPolicy<V>(comparator, initialSolutionGenerator);
            var tokens = new Fuookami.Ospf.Core.Token.ConcurrentManualTokenTable<V>(LinearCategory.Instance, new List<IIntermediateSymbol>());
            return new CallBackModel<V>(objectCategory, tokens, policy, converter);
        }

        public static CallBackModel<V> Create(
            AbstractMetaModel<V> metaModel,
            IFlt64ValueConverter<V> converter,
            Func<ulong, ulong, V>? initialSolutionGenerator = null)
        {
            var model = Create(metaModel.ObjectCategory, converter, initialSolutionGenerator);
            foreach (var constraint in metaModel.Constraints)
                model._constraints.Add((solution => null, constraint.ToString()));
            return model;
        }

        public IFlt64ValueConverter<V> Converter() => _converter;
        public V NegativeInfinity() => NumericConstantsRegistry.For<V>().NegativeInfinity ?? default;
        public V Infinity() => NumericConstantsRegistry.For<V>().PositiveInfinity ?? default;

        public IReadOnlyList<IReadOnlyList<V>> InitialSolutions(ulong initialSolutionAmount = 1) =>
            _policy.InitialSolutions(initialSolutionAmount, (ulong)Tokens.TokensInSolver.Count);

        V IAbstractCallBackModelInterface<V, V, V>.Operation(V lhs, V rhs) => lhs.Plus(rhs);
        V IAbstractCallBackModelInterface<V, V, V>.ObjectiveValue() => _converter.Zero;
        V IAbstractCallBackModelInterface<V, V, V>.ObjectiveValue(V obj) => obj;

        // ObjValue? for unconstrained ObjValue=V(struct) is just V
        V IAbstractCallBackModelInterface<V, V, V>.Objective(IReadOnlyList<V> solution)
        {
            V result = _converter.Zero;
            foreach (var (extractor, _) in _objectiveFunctions)
            {
                var obj = extractor(solution);
                result = result.Plus(obj);
            }
            return result;
        }

        // ObjValue? for unconstrained ObjValue=V(struct) is just V
        Order? IAbstractCallBackModelInterface<V, V, V>.CompareObjective(V lhs, V rhs) =>
            _policy.CompareObjective(lhs, rhs);

        public bool? ConstraintSatisfied(IReadOnlyList<V> solution)
        {
            foreach (var token in Tokens.Tokens)
            {
                var index = Tokens.IndexOf(token);
                if (index is null || index.Value >= solution.Count) return false;
            }
            foreach (var (extractor, _) in _constraints)
            {
                var result = extractor(solution);
                if (result == false) return false;
                if (result is null) return null;
            }
            return true;
        }

        public Try AddConstraint(Func<IReadOnlyList<V>, bool?> extractor, string name)
        {
            _constraints.Add((extractor, name));
            return Results.Ok(Results.SuccessInstance);
        }

        public Try AddObjective(ObjectCategory category, Func<IReadOnlyList<V>, V> func, string? name = null)
        {
            _objectiveFunctions.Add((solution =>
            {
                var v = func(solution);
                return category == ObjectCategory ? v : default(V).Minus(v);
            }, name ?? ""));
            return Results.Ok(Results.SuccessInstance);
        }

        public Try Maximize(Func<IReadOnlyList<V>, V> func, string? name = null) =>
            AddObjective(ObjectCategory.Maximum, func, name);

        public Try Minimize(Func<IReadOnlyList<V>, V> func, string? name = null) =>
            AddObjective(ObjectCategory.Minimum, func, name);

        public void SetSolution(IReadOnlyList<V> solution) => Tokens.SetSolution(solution);
        public void SetSolution(IReadOnlyDictionary<IVariableItem, V> solution) => Tokens.SetSolution(solution);
        public void Flush() => Tokens.Flush();
        public void ClearSolution() => Tokens.TokenList.ClearSolution();
        public void Dispose() => Tokens.Dispose();
    }

    // ===== MultiObjectCallBackModel<V> =====

    public sealed class MultiObjectCallBackModel<V> : IMultiObjectiveModelInterface<V>
        where V : struct, IRealNumber<V>, INumberField<V>
    {
        private readonly List<(Func<IReadOnlyList<V>, bool?> Extractor, string Name)> _constraints = new();
        // Obj = List<...> (reference type), so Obj? = List<...>? (nullable reference)
        private readonly List<(Func<IReadOnlyList<V>, List<(MultiObjectLocation<V> Location, V Value)>?> Extractor, string Name)> _objectiveFunctions = new();
        private readonly IFlt64ValueConverter<V> _converter;
        private readonly Func<ulong, ulong, V>? _initialSolutionGenerator;

        public ObjectCategory ObjectCategory { get; }
        public IReadOnlyList<MultiObjectLocation<V>> ObjectiveLocation { get; }
        public Fuookami.Ospf.Core.Token.IAbstractMutableTokenTable<V> Tokens { get; }
        public IReadOnlyList<(Func<IReadOnlyList<V>, bool?> Extractor, string Name)> Constraints => _constraints;
        public IReadOnlyList<(Func<IReadOnlyList<V>, List<(MultiObjectLocation<V> Location, V Value)>?> Extractor, string Name)> ObjectiveFunctions => _objectiveFunctions;

        public List<V> DefaultObjective
        {
            get
            {
                var inf = ObjectCategory == ObjectCategory.Minimum ? NegativeInfinity() : Infinity();
                return Enumerable.Repeat(inf, ObjectiveLocation.Count).ToList();
            }
        }

        internal MultiObjectCallBackModel(
            ObjectCategory objectCategory,
            IReadOnlyList<MultiObjectLocation<V>> objectiveLocation,
            Fuookami.Ospf.Core.Token.IAbstractMutableTokenTable<V> tokens,
            IFlt64ValueConverter<V> converter,
            Func<ulong, ulong, V>? initialSolutionGenerator = null)
        {
            ObjectCategory = objectCategory;
            ObjectiveLocation = objectiveLocation;
            Tokens = tokens;
            _converter = converter;
            _initialSolutionGenerator = initialSolutionGenerator;
        }

        public static MultiObjectCallBackModel<V> Create(
            ObjectCategory objectCategory,
            IReadOnlyList<MultiObjectLocation<V>> objectiveLocation,
            IFlt64ValueConverter<V> converter,
            Func<ulong, ulong, V>? initialSolutionGenerator = null)
        {
            var tokens = new Fuookami.Ospf.Core.Token.ConcurrentManualTokenTable<V>(LinearCategory.Instance, new List<IIntermediateSymbol>());
            return new MultiObjectCallBackModel<V>(objectCategory, objectiveLocation, tokens, converter, initialSolutionGenerator);
        }

        public IFlt64ValueConverter<V> Converter() => _converter;
        public V NegativeInfinity() => NumericConstantsRegistry.For<V>().NegativeInfinity ?? default;
        public V Infinity() => NumericConstantsRegistry.For<V>().PositiveInfinity ?? default;

        public IReadOnlyList<IReadOnlyList<V>> InitialSolutions(ulong initialSolutionAmount = 1)
        {
            if (_initialSolutionGenerator is null) return Array.Empty<IReadOnlyList<V>>();
            var solutions = new List<IReadOnlyList<V>>();
            for (ulong s = 0; s < initialSolutionAmount; s++)
            {
                var solution = new List<V>();
                for (ulong v = 0; v < (ulong)Tokens.TokensInSolver.Count; v++)
                    solution.Add(_initialSolutionGenerator(s, v));
                solutions.Add(solution);
            }
            return solutions;
        }

        List<V> IAbstractCallBackModelInterface<List<(MultiObjectLocation<V> Location, V Value)>, List<V>, V>.ObjectiveValue() =>
            Enumerable.Repeat(_converter.Zero, ObjectiveLocation.Count).ToList();

        List<V> IAbstractCallBackModelInterface<List<(MultiObjectLocation<V> Location, V Value)>, List<V>, V>.ObjectiveValue(
            List<(MultiObjectLocation<V> Location, V Value)> obj)
        {
            var value = Enumerable.Repeat(_converter.Zero, ObjectiveLocation.Count).ToList();
            foreach (var (location, objective) in obj)
            {
                var index = 0; foreach (var loc in ObjectiveLocation) { if (loc.Priority == location.Priority) break; index++; }
                if (index < 0 || index >= ObjectiveLocation.Count) continue;
                value[index] = value[index].Plus(objective.Times(location.Weight));
            }
            return value;
        }

        List<V> IAbstractCallBackModelInterface<List<(MultiObjectLocation<V> Location, V Value)>, List<V>, V>.Operation(
            List<V> lhs, List<V> rhs) =>
            Enumerable.Range(0, ObjectiveLocation.Count).Select(i => lhs[i].Plus(rhs[i])).ToList();

        List<V>? IAbstractCallBackModelInterface<List<(MultiObjectLocation<V> Location, V Value)>, List<V>, V>.Objective(
            IReadOnlyList<V> solution)
        {
            var result = Enumerable.Repeat(_converter.Zero, ObjectiveLocation.Count).ToList();
            foreach (var (extractor, _) in _objectiveFunctions)
            {
                var obj = extractor(solution);
                if (obj is null) return null;
                for (int i = 0; i < ObjectiveLocation.Count; i++)
                    result[i] = result[i].Plus(obj[i].Value);
            }
            return result;
        }

        Order? IAbstractCallBackModelInterface<List<(MultiObjectLocation<V> Location, V Value)>, List<V>, V>.CompareObjective(
            List<V>? lhs, List<V>? rhs)
        {
            if (lhs is not null && rhs is null) return new Order.Less();
            if (lhs is null && rhs is not null) return new Order.Greater();
            if (lhs is not null && rhs is not null)
            {
                var size = System.Math.Min(lhs.Count, rhs.Count);
                for (int i = 0; i < size; i++)
                {
                    var l = _converter.FromValue(lhs[i]);
                    var r = _converter.FromValue(rhs[i]);
                    if (l == r) continue;
                    return ObjectCategory == ObjectCategory.Minimum
                        ? (l < r ? new Order.Less() : new Order.Greater())
                        : (l > r ? new Order.Less() : new Order.Greater());
                }
                if (lhs.Count < rhs.Count) return new Order.Less();
                if (lhs.Count > rhs.Count) return new Order.Greater();
                return new Order.Equal();
            }
            return null;
        }

        public bool? ConstraintSatisfied(IReadOnlyList<V> solution)
        {
            foreach (var (extractor, _) in _constraints)
            {
                var result = extractor(solution);
                if (result == false) return false;
                if (result is null) return null;
            }
            return true;
        }

        public void Flush() => Tokens.Flush();
        public void Dispose() => Tokens.Dispose();
    }
}
