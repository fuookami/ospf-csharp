#nullable enable

using Fuookami.Ospf.MultiArray;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Math.MultiArray.Einsum
{
    /// <summary>
    /// 爱因斯坦求和错误层次 / Einstein summation error hierarchy.
    /// </summary>
    public abstract record EinsumError
    {
        /// <summary>错误消息 / Error message.</summary>
        public required string Message { get; init; }

        /// <summary>维度不匹配 / Dimension mismatch.</summary>
        public sealed record DimensionMismatch : EinsumError;

        /// <summary>形状不兼容 / Incompatible shapes.</summary>
        public sealed record IncompatibleShapes : EinsumError;

        /// <summary>重复索引 / Duplicate indices.</summary>
        public sealed record DuplicateIndices : EinsumError;

        /// <summary>不支持的操作 / Unsupported operation.</summary>
        public sealed record UnsupportedOperation : EinsumError;

        /// <summary>索引越界 / Index out of bounds.</summary>
        public sealed record IndexOutOfBounds : EinsumError;

        /// <summary>索引列表长度不匹配 / Index list length mismatch.</summary>
        public sealed record IndexListLengthMismatch : EinsumError;
    }

    /// <summary>
    /// 爱因斯坦求和解析与执行 / Einstein-summation parse and execute.
    /// 顶层 einsum/einstein 函数 + Einstein DSL 上下文。
    /// </summary>
    public static class EinsumParser
    {
        /// <summary>双操作数爱因斯坦求和 / Two-operand einsum (notation "ij,jk->ik" etc.).</summary>
        public static Result<object, ErrorCode, Error<ErrorCode>> Einsum<T, SA, SB>(
            AbstractMultiArray<T, SA> a, AbstractMultiArray<T, SB> b, string notation, T zero)
            where T : struct, IRing<T>
            where SA : IShape
            where SB : IShape
        {
            var parts = notation.Split("->");
            if (parts.Length != 2)
                return Unsupported<object>($"Invalid einsum notation: {notation}. Expected format: 'inputs->output'");

            var inputs = parts[0].Split(',');
            if (inputs.Length != 2)
                return Unsupported<object>($"Expected 2 inputs in notation, got {inputs.Length}");

            var inputA = inputs[0].Trim();
            var inputB = inputs[1].Trim();
            var output = parts[1].Trim();

            return (inputA.Length, inputB.Length, output.Length) switch
            {
                (2, 2, 2) => Operations.Matmul(a, b, zero).Map<object>(x => x),
                (1, 1, 0) when inputA == inputB => Operations.Dot(a, b, zero).Map<object>(x => x),
                (1, 1, 2) => Operations.Outer(a, b, zero).Map<object>(x => x),
                _ => Unsupported<object>($"Unsupported einsum pattern: {notation}."),
            };
        }

        /// <summary>单操作数爱因斯坦求和 / Single-operand einsum ("ii->" trace, "ij->ji" transpose).</summary>
        public static Result<object, ErrorCode, Error<ErrorCode>> Einsum<T, S>(
            AbstractMultiArray<T, S> a, string notation, T zero)
            where T : struct, IRing<T>
            where S : IShape
        {
            var parts = notation.Split("->");
            if (parts.Length != 2)
                return Unsupported<object>($"Invalid einsum notation: {notation}.");

            var input = parts[0].Trim();
            var output = parts[1].Trim();

            return (input, output) switch
            {
                ("ii", "") => Operations.Trace(a, zero).Map<object>(x => x),
                var (i, o) when i == "ij" && o == "ji" => Operations.Transpose(a).Map<object>(x => x),
                _ => Unsupported<object>($"Unsupported einsum pattern: {notation}."),
            };
        }

        /// <summary>创建双操作数 Einstein 上下文 / Create two-operand Einstein DSL context.</summary>
        public static Einstein<T, SA, SB> Einstein<T, SA, SB>(
            AbstractMultiArray<T, SA> a, AbstractMultiArray<T, SB> b, T zero)
            where T : struct, IRing<T>
            where SA : IShape
            where SB : IShape
            => new(a, b, zero);

        /// <summary>创建单操作数 Einstein 上下文 / Create single-operand Einstein DSL context.</summary>
        public static Einstein<T, S, S> Einstein<T, S>(
            AbstractMultiArray<T, S> a, T zero)
            where T : struct, IRing<T>
            where S : IShape
            => new(a, null, zero);

        internal static Result<U, ErrorCode, Error<ErrorCode>> Unsupported<U>(string message)
            => new Failed<U, ErrorCode, Error<ErrorCode>>(ErrorCode.IllegalArgument, message);
    }

    /// <summary>
    /// Einstein DSL 上下文 / Einstein DSL context.
    /// </summary>
    /// <typeparam name="T">元素环类型 / Element ring type.</typeparam>
    /// <typeparam name="SA">A 的形状类型 / Shape type of A.</typeparam>
    /// <typeparam name="SB">B 的形状类型 / Shape type of B.</typeparam>
    public sealed class Einstein<T, SA, SB>
        where T : struct, IRing<T>
        where SA : IShape
        where SB : IShape
    {
        private readonly AbstractMultiArray<T, SA> _a;
        private readonly AbstractMultiArray<T, SB>? _b;
        private readonly T _zero;

        internal Einstein(AbstractMultiArray<T, SA> a, AbstractMultiArray<T, SB>? b, T zero)
        {
            _a = a;
            _b = b;
            _zero = zero;
        }

        /// <summary>矩阵乘法 / Matrix multiplication.</summary>
        public Result<MultiArray<T, DynShape>, ErrorCode, Error<ErrorCode>> Matmul()
            => _b is not null
                ? Operations.Matmul(_a, _b, _zero)
                : EinsumParser.Unsupported<MultiArray<T, DynShape>>("matmul requires two operands");

        /// <summary>点积 / Dot product.</summary>
        public Result<T, ErrorCode, Error<ErrorCode>> Dot()
            => _b is not null
                ? Operations.Dot(_a, _b, _zero)
                : EinsumParser.Unsupported<T>("dot requires two operands");

        /// <summary>外积 / Outer product.</summary>
        public Result<MultiArray<T, DynShape>, ErrorCode, Error<ErrorCode>> Outer()
            => _b is not null
                ? Operations.Outer(_a, _b, _zero)
                : EinsumParser.Unsupported<MultiArray<T, DynShape>>("outer requires two operands");

        /// <summary>缩并 / Contraction.</summary>
        public Result<MultiArray<T, DynShape>, ErrorCode, Error<ErrorCode>> Contract(int axisA, int axisB)
            => _b is not null
                ? Operations.Contract(_a, axisA, _b, axisB, _zero)
                : EinsumParser.Unsupported<MultiArray<T, DynShape>>("contract requires two operands");

        /// <summary>迹 / Trace (single operand).</summary>
        public Result<T, ErrorCode, Error<ErrorCode>> Trace()
            => _b is null
                ? Operations.Trace(_a, _zero)
                : EinsumParser.Unsupported<T>("trace is a single operand operation");

        /// <summary>转置 / Transpose (single operand).</summary>
        public Result<MultiArray<T, DynShape>, ErrorCode, Error<ErrorCode>> Transpose()
            => _b is null
                ? Operations.Transpose(_a)
                : EinsumParser.Unsupported<MultiArray<T, DynShape>>("transpose is a single operand operation");
    }
}
