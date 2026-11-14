#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using Xunit;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Variable;
namespace Fuookami.Ospf.Core.Plugin.Tests;

public class RelaxationOwnershipTest {
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RelaxedConstraints_ShouldSurviveEitherDisposalOrder(bool benders, bool disposeOriginalFirst) {
        string testAssembly = typeof(RelaxationOwnershipTest).Assembly.GetName().Name!;
        string pluginAssembly = testAssembly[..^".Tests".Length];
        string plugin = pluginAssembly[(pluginAssembly.LastIndexOf('.') + 1)..];
        string className = benders ? $"{plugin}BendersDecompositionSolver" : $"{plugin}ColumnGenerationSolver";
        Type type = Assembly.Load(pluginAssembly).GetType($"{pluginAssembly}.{className}", throwOnError: true)!;
        MethodInfo? method = type.GetMethod(
            benders ? "FixAndRelaxToLP" : "RelaxToLP",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var row = new SparseVector();
        row.Add(0, Flt64.One);
        var original = new LinearTriadModel(
            variables: [new ModelViewVariable(
                index: 0,
                lowerBound: Flt64.Zero,
                upperBound: Flt64.One,
                type: Binary.Instance,
                name: "x"
            )],
            constraints: new LinearConstraintBatch(
                lhs: new SparseMatrix([row]),
                signs: [ConstraintRelation.LessEqual],
                rhs: [Flt64.One],
                names: ["row"],
                sources: [ConstraintSource.Origin]
            ),
            objective: new Objective<LinearObjectiveCell>(ObjectCategory.Minimum, []),
            name: "original"
        );
        object?[] arguments = benders ? [original, new Dictionary<int, Flt64> { [0] = Flt64.Zero }] : [original];
        // 没有私有松弛辅助的插件必须复用 Core 的 RelaxToContinuous，固定列在机制转储阶段代换。
        // Plugins without a private helper must reuse Core RelaxToContinuous, substituting fixed columns while dumping.
        LinearTriadModel relaxed = method is null
            ? original.RelaxToContinuous().Value
            : Assert.IsType<LinearTriadModel>(method.Invoke(null, arguments));
        Assert.NotSame(original.Constraints, relaxed.Constraints);
        SparseMatrix originalSparse = ((LinearConstraintBatch)original.Constraints).SparseLhs;
        SparseMatrix relaxedSparse = ((LinearConstraintBatch)relaxed.Constraints).SparseLhs;
        Assert.NotSame(originalSparse, relaxedSparse);
        Assert.NotSame(originalSparse.Rows[0], relaxedSparse.Rows[0]);
        Assert.Same(Continuous.Instance, relaxed.Variables[0].Type);
        Assert.Equal(benders && method is not null ? Flt64.Zero : Flt64.One, relaxed.Variables[0].UpperBound);

        originalSparse.Rows[0].Entries[0] = new SparseVectorEntry(0, new Flt64(2));
        Assert.Equal(Flt64.One, relaxedSparse.Rows[0].Entries[0].Value);
        LinearTriadModel first = disposeOriginalFirst ? original : relaxed;
        LinearTriadModel second = disposeOriginalFirst ? relaxed : original;
        first.Dispose();
        Assert.Empty(first.Constraints.Names);
        Assert.Equal("row", Assert.Single(second.Constraints.Names));
        Assert.Single(second.Constraints.Lhs[0]);
        second.Dispose();
    }
}
