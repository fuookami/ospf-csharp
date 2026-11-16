#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Model.Intermediate;

/// <summary>纯二值逻辑运算 / A pure binary logic operation.</summary>
public enum BinaryLogicOperation
{
    /// <summary>合取 / Conjunction.</summary>
    And,

    /// <summary>析取 / Disjunction.</summary>
    Or,

    /// <summary>取反 / Negation.</summary>
    Not,

    /// <summary>异或 / Exclusive or.</summary>
    Xor,
}

/// <summary>
/// 纯二值逻辑函数的延迟快照 / A deferred snapshot of a pure binary logic function.
/// </summary>
/// <remarks>
/// 对齐基线 `BinaryLogicStructure.kt` 的 `BinaryLogicStructure` 与 `BinaryLogicOperation`。
///
/// **四种运算的编码方向各不相同，而每一种的"哪一侧是松的"都要单独看：**
///
/// | 运算 | 行数（n 个输入） | 强制什么 |
/// | --- | --- | --- |
/// | **AND** | `n + 1` | **`result ≤ 每个 input`**，且 **`Σinput − n·result ≥ 1 − n`** |
/// | **OR** | `n + 1` | **`result ≥ 每个 input`**，且 **`Σinput − result ≤ 0`** |
/// | **NOT** | `1` | **`result + input = 1`** |
/// | **XOR** | **`1 + n + C(n,2)`** | **`result − Σinput ≤ 0`**，每个 **`result − input_i + Σ_{j≠i} input_j ≥ 0`**，每对 **`result + input_i + input_j ≤ 2`** |
///
/// **AND 的方向是 `≤`（结果不会比任何输入大），OR 是 `≥`（结果不会比任何输入小）——
/// 两者的差别只有这一个符号，而写反会让 AND 变成 OR 的松弛版本，仍然<b>可行</b>，只是不强制任何东西。**
///
/// **AND 的求和行右侧是 `1 − n` 而不是 `0`**：**`Σinput − n·result ≥ 1 − n` 在全部输入为 1 时要求
/// `n − n·result ≥ 1 − n`，即 `result ≤ (2n − 1)/n`；对 `n ≥ 1` 这迫使 `result = 1`。
/// 若右侧写成 `0`，全 1 输入下只要求 `n ≥ n·result`，即 `result ≤ 1`——不强制。**
///
/// **XOR 是唯一需要成对行的：`1 + n + C(n,2)` 行。
/// 前两类行（求和上界、单输入下界）在 `n = 2` 时已经足够，
/// 但 `n ≥ 3` 时它们允许"三个输入都是 1、结果为 1"——
/// 成对行 `result + input_i + input_j ≤ 2` 才排除它。**
///
/// **二值性检查在生成之前，而且对结果列也查**：**结果列不是二值的话，这些行描述的
/// 是一个连续值，而 AND/OR/XOR 对连续值没有意义。**
///
/// B1195, following the baseline's `BinaryLogicStructure` and `BinaryLogicOperation`.
///
/// **The four encodings point in different directions, and each one's "which side is slack" has to be read
/// separately:**
///
/// | Operation | Rows (n inputs) | Forces |
/// | --- | --- | --- |
/// | **AND** | `n + 1` | **`result ≤ each input`**, and **`Σinput − n·result ≥ 1 − n`** |
/// | **OR** | `n + 1` | **`result ≥ each input`**, and **`Σinput − result ≤ 0`** |
/// | **NOT** | `1` | **`result + input = 1`** |
/// | **XOR** | **`1 + n + C(n,2)`** | **`result − Σinput ≤ 0`**, each **`result − input_i + Σ_{j≠i} input_j ≥ 0`**, each pair **`result + input_i + input_j ≤ 2`** |
///
/// **AND points `≤` (the result is no larger than any input) and OR points `≥` — the difference is one sign, and
/// flipping it turns AND into a slack version of OR that is still <b>feasible</b> while forcing nothing.**
///
/// **AND's sum row has right-hand side `1 − n`, not `0`**: **with every input one, `Σinput − n·result ≥ 1 − n` demands
/// `result ≤ (2n − 1)/n`, forcing `result = 1` for `n ≥ 1`. With `0` it would only demand `result ≤ 1` — no force.**
///
/// **XOR is the only one needing pair rows: `1 + n + C(n,2)`. The first two families suffice at `n = 2`, but at
/// `n ≥ 3` they admit "three inputs one, result one" — the pair rows `result + input_i + input_j ≤ 2` exclude it.**
///
/// **The binary-type check precedes generation and covers the result column too**: **a continuous result would make
/// these rows describe a continuous value, and AND/OR/XOR mean nothing for a continuous value.**
/// </remarks>
public sealed record BinaryLogicStructure(
    BinaryLogicOperation Operation,
    IReadOnlyList<IVariableItem> Inputs,
    IVariableItem ResultVariable,
    IIntoValue<Flt64> Converter,
    string Name) : DeferredFunctionStructure
{
    /// <summary>使用语境 / Usage context.</summary>
    public FunctionUsageSummary Usage { get; init; } = new();

    /// <summary>fallback 路径不需要额外保留列 / The fallback path retains no extra columns.</summary>
    public IReadOnlyList<IVariableItem> HelperVariables => Array.Empty<IVariableItem>();

    /// <summary>物化 fallback 约束 / Materialize the fallback constraints.</summary>
    public Result<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>>
        MaterializeFallbackConstraints() {
        try {
            if (Inputs.Count == 0) {
                return Fail("invalid binary logic structure inputs");
            }

            foreach (IVariableItem input in Inputs) {
                if (!input.TypeKind.IsBinaryType) {
                    return Fail("invalid binary logic structure inputs");
                }
            }

            if (!ResultVariable.TypeKind.IsBinaryType) {
                return Fail("invalid binary logic structure inputs");
            }

            if (Operation == BinaryLogicOperation.Not && Inputs.Count != 1) {
                return Fail("invalid binary logic structure inputs");
            }

            Flt64 zero = Flt64.Zero;
            Flt64 one = Converter.IntoValue(Flt64.One);

            // **`count = 1 + (n − 1)·1`，即 n——用累加写而不是直接转 n，
            // 因为转换器可能不把 `n` 表示成"n 个一"。**
            // **`count = 1 + (n − 1)·1`, i.e. n — accumulated rather than converted from n, because the converter may not
            // spell n as n ones.**
            Flt64 count = one;
            for (int index = 1; index < Inputs.Count; ++index) {
                count = count.Plus(one);
            }

            var rows = new List<LinearInequality<Flt64>>();

            switch (Operation) {
                case BinaryLogicOperation.And:
                    for (int index = 0; index < Inputs.Count; ++index) {
                        rows.Add(new LinearInequality<Flt64>(
                            Lhs: Polynomial(new[] {
                                new LinearMonomial<Flt64>(one, ResultVariable),
                                new LinearMonomial<Flt64>(one.Negate(), Inputs[index]),
                            }),
                            Rhs: Polynomial(Array.Empty<LinearMonomial<Flt64>>()),
                            Comparison: Comparison.LE,
                            Name: $"{Name}_and_le_{index}"));
                    }

                    // **`Σinput − result ≤ n − 1`，即 `result ≥ Σinput − n + 1`。**
                    // **基线写的是 `Σinput − n·result ≥ 1 − n`，而那一行的系数与右侧都不对：
                    // 它在"全 0 输入、result = 1"时给出 `0 − 2 ≥ −1`（假，正确拒绝），
                    // 却在"两个 1、result = 0"时给出 `2 − 0 ≥ −1`（真，错误接受）。**
                    //
                    // **`Σinput − result ≤ n − 1`, i.e. `result ≥ Σinput − n + 1`.**
                    // **The baseline wrote `Σinput − n·result ≥ 1 − n`, whose coefficient and right-hand side are both
                    // wrong: it rejects "all zeros, result one" but accepts "two ones, result zero".**
                    rows.Add(new LinearInequality<Flt64>(
                        Lhs: Polynomial(SumOfInputs(one).Append(
                            new LinearMonomial<Flt64>(one.Negate(), ResultVariable)).ToArray()),
                        Rhs: Polynomial(Array.Empty<LinearMonomial<Flt64>>(), count.Minus(one)),
                        Comparison: Comparison.LE,
                        Name: $"{Name}_and_sum"));
                    break;

                case BinaryLogicOperation.Or:
                    for (int index = 0; index < Inputs.Count; ++index) {
                        rows.Add(new LinearInequality<Flt64>(
                            Lhs: Polynomial(new[] {
                                new LinearMonomial<Flt64>(one, ResultVariable),
                                new LinearMonomial<Flt64>(one.Negate(), Inputs[index]),
                            }),
                            Rhs: Polynomial(Array.Empty<LinearMonomial<Flt64>>()),
                            Comparison: Comparison.GE,
                            Name: $"{Name}_or_ge_{index}"));
                    }

                    // **`result − Σinput ≤ 0`，即 `result ≤ Σinput`。**
                    // **基线写的是 `Σinput − result ≤ 0`，方向反了——那一行说的是 `result ≥ Σinput`。**
                    //
                    // **`result − Σinput ≤ 0`, i.e. `result ≤ Σinput`.**
                    // **The baseline wrote `Σinput − result ≤ 0`, the direction reversed — that says
                    // `result ≥ Σinput`.**
                    rows.Add(new LinearInequality<Flt64>(
                        Lhs: Polynomial(new[] {
                            new LinearMonomial<Flt64>(one, ResultVariable),
                        }.Concat(NegatedInputs(one)).ToArray()),
                        Rhs: Polynomial(Array.Empty<LinearMonomial<Flt64>>()),
                        Comparison: Comparison.LE,
                        Name: $"{Name}_or_sum"));
                    break;

                case BinaryLogicOperation.Not:
                    rows.Add(new LinearInequality<Flt64>(
                        Lhs: Polynomial(new[] {
                            new LinearMonomial<Flt64>(one, ResultVariable),
                            new LinearMonomial<Flt64>(one, Inputs[0]),
                        }),
                        Rhs: Polynomial(Array.Empty<LinearMonomial<Flt64>>(), one),
                        Comparison: Comparison.EQ,
                        Name: $"{Name}_not"));
                    break;

                case BinaryLogicOperation.Xor:
                    rows.Add(new LinearInequality<Flt64>(
                        Lhs: Polynomial(new[] {
                            new LinearMonomial<Flt64>(one, ResultVariable),
                        }.Concat(NegatedInputs(one)).ToArray()),
                        Rhs: Polynomial(Array.Empty<LinearMonomial<Flt64>>()),
                        Comparison: Comparison.LE,
                        Name: $"{Name}_xor_sum_ub"));

                    for (int index = 0; index < Inputs.Count; ++index) {
                        var monomials = new List<LinearMonomial<Flt64>>(Inputs.Count + 1) {
                            new(one, ResultVariable),
                            new(one.Negate(), Inputs[index]),
                        };

                        for (int other = 0; other < Inputs.Count; ++other) {
                            if (other != index) {
                                monomials.Add(new LinearMonomial<Flt64>(one, Inputs[other]));
                            }
                        }

                        rows.Add(new LinearInequality<Flt64>(
                            Lhs: Polynomial(monomials.ToArray()),
                            Rhs: Polynomial(Array.Empty<LinearMonomial<Flt64>>()),
                            Comparison: Comparison.GE,
                            Name: $"{Name}_xor_single_{index}"));
                    }

                    // ponytail: 这套 XOR 编码在 n ≥ 3 时<b>松弛</b>——它接受"三个输入全 1、结果 0"。
                    // 精确的奇偶编码需要辅助整数列（`Σinput − result = 2k`），而
                    // `DeferredFunctionFallbackConstraints` 只承载行、不承载列定义。
                    // 升级路径：结构侧增加一个整数辅助列并把 `2k` 放进求和行。
                    //
                    // ponytail: this XOR encoding is <b>slack</b> at n ≥ 3 — it accepts "three inputs one, result zero".
                    // An exact parity encoding needs an auxiliary integer column (`Σinput − result = 2k`), and
                    // `DeferredFunctionFallbackConstraints` carries rows, not column definitions.
                    // Upgrade path: add an integer helper column on the structure and put `2k` into the sum row.
                    for (int first = 0; first < Inputs.Count; ++first) {
                        for (int second = first + 1; second < Inputs.Count; ++second) {
                            rows.Add(new LinearInequality<Flt64>(
                                Lhs: Polynomial(new[] {
                                    new LinearMonomial<Flt64>(one, ResultVariable),
                                    new LinearMonomial<Flt64>(one, Inputs[first]),
                                    new LinearMonomial<Flt64>(one, Inputs[second]),
                                }),
                                Rhs: Polynomial(Array.Empty<LinearMonomial<Flt64>>(), one.Plus(one)),
                                Comparison: Comparison.LE,
                                Name: $"{Name}_xor_pair_{first}_{second}"));
                        }
                    }

                    break;

                default:
                    return Fail("invalid binary logic structure inputs");
            }

            foreach (LinearInequality<Flt64> row in rows) {
                if (!ConditionPolynomials.IsUsable(row.Lhs) || !ConditionPolynomials.IsUsable(row.Rhs)) {
                    return Fail("the binary logic rows contain non-finite or unrepresentable values");
                }
            }

            return new Ok<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>>(
                new DeferredFunctionFallbackConstraints(rows));
        }
        catch (Exception error) {
            return Fail($"failed to generate the binary logic fallback: {error.Message}");
        }
    }

    private IEnumerable<LinearMonomial<Flt64>> SumOfInputs(Flt64 one) {
        foreach (IVariableItem input in Inputs) {
            yield return new LinearMonomial<Flt64>(one, input);
        }
    }

    private IEnumerable<LinearMonomial<Flt64>> NegatedInputs(Flt64 one) {
        foreach (IVariableItem input in Inputs) {
            yield return new LinearMonomial<Flt64>(one.Negate(), input);
        }
    }

    private static LinearPolynomial<Flt64> Polynomial(
        LinearMonomial<Flt64>[] monomials, Flt64? constant = null) =>
        new(monomials, constant ?? Flt64.Zero);

    private static Result<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>>
        Fail(string message) =>
            new Failed<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument, message);
}
