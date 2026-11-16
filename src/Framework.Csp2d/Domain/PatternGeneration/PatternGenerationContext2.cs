// 组合式图案生成上下文 / The composite pattern generation context.

using System.Collections.Generic;
using Fuookami.Ospf.Framework.Csp2d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp2d.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Csp2d.Domain.PatternGeneration;

/// <summary>
/// 组合式图案生成上下文：按工艺分派生成器 / A composite pattern generation context dispatching generators by process.
/// </summary>
/// <remarks>
/// 工艺决定布局族，因此分派不是"随便挑一个生成器"，而是**必须挑与工艺相容的那个**：
/// 1. `Freeform` → 混高货架，不输出刀序（余料最少）。
/// 2. `GuillotineTwoStage` → 等高行货架，输出两级切分树。
/// 3. `GuillotineThreeStage` → 混高货架，输出三级切分树。
///
/// 另外总会**回放历史图案**（若有），使列生成在后续迭代里能复用已生成的列。
///
/// The process decides the layout family, so dispatching is not "pick any generator" but must pick the one **compatible with the process**: `Freeform` uses a
/// mixed-height shelf without a cut sequence, `GuillotineTwoStage` a uniform-height shelf with a two-stage tree, and `GuillotineThreeStage` a mixed-height shelf with a
/// three-stage tree. Historical patterns are always **replayed** so that later column-generation iterations can reuse existing columns.
/// </remarks>
public sealed class PatternGenerationContext2 : IPatternGenerator2 {
    private readonly IReadOnlyList<IPatternGenerator2> _freeformGenerators;
    private readonly IReadOnlyList<IPatternGenerator2> _twoStageGenerators;
    private readonly IReadOnlyList<IPatternGenerator2> _threeStageGenerators;

    /// <summary>构造上下文，缺省按工艺各配一个生成器 / Build the context with one default generator per process.</summary>
    /// <param name="freeformGenerators">任意排样生成器 / The arbitrary nesting generators.</param>
    /// <param name="twoStageGenerators">两级断头台生成器 / The two-stage guillotine generators.</param>
    /// <param name="threeStageGenerators">三级断头台生成器 / The three-stage guillotine generators.</param>
    public PatternGenerationContext2(
        IReadOnlyList<IPatternGenerator2>? freeformGenerators = null,
        IReadOnlyList<IPatternGenerator2>? twoStageGenerators = null,
        IReadOnlyList<IPatternGenerator2>? threeStageGenerators = null) {
        _freeformGenerators = freeformGenerators
            ?? new List<IPatternGenerator2> { new ShelfPatternGenerator2(sameHeightRows: false) };
        _twoStageGenerators = twoStageGenerators
            ?? new List<IPatternGenerator2> { new ShelfPatternGenerator2(sameHeightRows: true) };
        _threeStageGenerators = threeStageGenerators
            ?? new List<IPatternGenerator2> { new ThreeStagePatternGenerator2() };
    }

    /// <inheritdoc />
    public Result<IReadOnlyList<PatternGenerationResult2>, ErrorCode, Error<ErrorCode>> Generate(
        PatternGenerationRequest2 request) {
        IReadOnlyList<IPatternGenerator2> generators = request.Process switch {
            CuttingProcess2.GuillotineTwoStage => _twoStageGenerators,
            CuttingProcess2.GuillotineThreeStage => _threeStageGenerators,
            _ => _freeformGenerators,
        };
        if (generators.Count == 0) {
            return Results.Failed<IReadOnlyList<PatternGenerationResult2>>(new Csp2dCapabilityError(
                "工艺没有配置生成器 / No generator configured for this process: " + request.Process));
        }

        var collected = new List<PatternGenerationResult2>();
        foreach (IPatternGenerator2 generator in generators) {
            Result<IReadOnlyList<PatternGenerationResult2>, ErrorCode, Error<ErrorCode>> result =
                generator.Generate(request);
            if (result is not Ok<IReadOnlyList<PatternGenerationResult2>, ErrorCode, Error<ErrorCode>> ok) {
                return result;
            }
            collected.AddRange(ok.Value);
        }
        // 回放历史图案，避免重复生成已有列。 / Replay historical patterns to avoid regenerating existing columns.
        foreach (Pattern2<Flt64> pattern in request.History) {
            collected.Add(new PatternGenerationResult2(pattern, "historical"));
        }
        return Results.Ok<IReadOnlyList<PatternGenerationResult2>>(
            Csp2dPatternGenerationSupport.DistinctPatterns(collected, request.MaxCandidates));
    }
}
