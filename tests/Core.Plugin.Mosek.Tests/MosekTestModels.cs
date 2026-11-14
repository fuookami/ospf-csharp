#nullable enable

using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Core.Plugin.Mosek.Tests;

internal static class MosekTestModels {
    internal static LinearTriadModel SimpleLp() => new(
        variables: [
            new ModelViewVariable(
                index: 0,
                lowerBound: Flt64.Zero,
                upperBound: new Flt64(double.PositiveInfinity),
                type: Continuous.Instance,
                name: "x1"),
            new ModelViewVariable(
                index: 1,
                lowerBound: Flt64.Zero,
                upperBound: new Flt64(double.PositiveInfinity),
                type: Continuous.Instance,
                name: "x2"),
        ],
        constraints: new LinearConstraintBatch(
            lhs: new SparseMatrix([
                new SparseVector([
                    new SparseVectorEntry(0, Flt64.One),
                    new SparseVectorEntry(1, Flt64.One),
                ]),
            ]),
            signs: [ConstraintRelation.GreaterEqual],
            rhs: [Flt64.One],
            names: ["demand"],
            sources: [ConstraintSource.Origin]),
        objective: new Objective<LinearObjectiveCell>(
            category: ObjectCategory.Minimum,
            cells: [
                new LinearObjectiveCell(0, Flt64.One),
                new LinearObjectiveCell(1, Flt64.One),
            ],
            constant: Flt64.Zero),
        name: "mosek-test");

    internal static LinearTriadModel IntegerModel() => new(
        variables: [
            new ModelViewVariable(
                index: 0,
                lowerBound: Flt64.Zero,
                upperBound: new Flt64(10),
                type: Integer.Instance,
                name: "x",
                initialResult: new Flt64(3)),
            new ModelViewVariable(
                index: 1,
                lowerBound: Flt64.Zero,
                upperBound: new Flt64(10),
                type: Integer.Instance,
                name: "y",
                initialResult: Flt64.One),
        ],
        constraints: new LinearConstraintBatch(
            lhs: new SparseMatrix([
                new SparseVector([
                    new SparseVectorEntry(0, Flt64.One),
                    new SparseVectorEntry(1, Flt64.One),
                ]),
                new SparseVector([
                    new SparseVectorEntry(0, Flt64.One),
                ]),
            ]),
            signs: [
                ConstraintRelation.GreaterEqual,
                ConstraintRelation.LessEqual,
            ],
            rhs: [new Flt64(4), new Flt64(3)],
            names: ["demand", "capacity"],
            sources: [ConstraintSource.Origin, ConstraintSource.Origin]),
        objective: new Objective<LinearObjectiveCell>(
            category: ObjectCategory.Minimum,
            cells: [
                new LinearObjectiveCell(0, new Flt64(2)),
                new LinearObjectiveCell(1, new Flt64(3)),
            ],
            constant: new Flt64(5)),
        name: "mosek-mip-test");

    internal static LinearTriadModel InvalidVariableModel() => new(
        variables: [
            new ModelViewVariable(
                index: 0,
                lowerBound: new Flt64(double.NaN),
                upperBound: Flt64.One,
                type: Continuous.Instance,
                name: "invalid"),
        ],
        constraints: EmptyConstraints(),
        objective: new Objective<LinearObjectiveCell>(
            category: ObjectCategory.Minimum,
            cells: [],
            constant: Flt64.Zero),
        name: "mosek-invalid");

    internal static LinearConstraintBatch EmptyConstraints() => new(
        lhs: new SparseMatrix(),
        signs: [],
        rhs: [],
        names: [],
        sources: []);
}
