#nullable enable

using System;
using System.Collections.Concurrent;

namespace Fuookami.Ospf.Math.Symbol.Expression.Operation
{
    /// <summary>
    /// 数值运算接口 / Numeric Operations Interface.
    /// Kotlin interface NumericOps&lt;T : Any&gt;; C# interface INumericOps&lt;T&gt; where T : struct.
    /// </summary>
    public interface INumericOps<T> where T : struct
    {
        T Negate(T value);
        T Abs(T value);
        T Add(T left, T right);
        T Subtract(T left, T right);
        T Multiply(T left, T right);
        T? Divide(T left, T right);
        T? Modulo(T left, T right);
        T? Power(T left, T right);
    }

    /// <summary>Non-generic interface for boxed numeric dispatch.</summary>
    internal interface IBoxedNumericOps
    {
        object Negate(object value);
        object Abs(object value);
        object Add(object left, object right);
        object Subtract(object left, object right);
        object Multiply(object left, object right);
        object? Divide(object left, object right);
        object? Modulo(object left, object right);
        object? Power(object left, object right);
    }

    /// <summary>Boxed adapter wrapping INumericOps&lt;T&gt; as IBoxedNumericOps.</summary>
    internal sealed class BoxedNumericOps<T> : IBoxedNumericOps where T : struct
    {
        private readonly INumericOps<T> _inner;
        public INumericOps<T> Inner => _inner;
        public BoxedNumericOps(INumericOps<T> inner) => _inner = inner;

        public object Negate(object value) => _inner.Negate((T)value)!;
        public object Abs(object value) => _inner.Abs((T)value)!;
        public object Add(object left, object right) => _inner.Add((T)left, (T)right)!;
        public object Subtract(object left, object right) => _inner.Subtract((T)left, (T)right)!;
        public object Multiply(object left, object right) => _inner.Multiply((T)left, (T)right)!;
        public object? Divide(object left, object right) => _inner.Divide((T)left, (T)right);
        public object? Modulo(object left, object right) => _inner.Modulo((T)left, (T)right);
        public object? Power(object left, object right) => _inner.Power((T)left, (T)right);
    }

    /// <summary>
    /// 数值分派器 / Numeric Dispatcher.
    /// Kotlin object; C# sealed class with Instance singleton.
    /// </summary>
    public sealed class NumericDispatcher
    {
        /// <summary>单例实例 / Singleton instance.</summary>
        public static readonly NumericDispatcher Instance = new();

        private readonly ConcurrentDictionary<Type, IBoxedNumericOps> _registry = new();

        private NumericDispatcher() => RegisterBuiltins();

        /// <summary>注册数值运算处理器 / Register a numeric operation handler.</summary>
        public void Register<T>(INumericOps<T> ops) where T : struct
            => _registry[typeof(T)] = new BoxedNumericOps<T>(ops);

        /// <summary>获取指定类型的运算处理器 / Get operation handler for type.</summary>
        public INumericOps<T>? OpsFor<T>(Type type) where T : struct
            => _registry.TryGetValue(type, out var boxed) && boxed is BoxedNumericOps<T> typed ? typed.Inner : null;

        /// <summary>获取值的运算处理器 / Get operation handler for a value's type.</summary>
        public INumericOps<T>? OpsFor<T>(T value) where T : struct => OpsFor<T>(typeof(T));

        /// <summary>执行一元运算 / Execute unary operation.</summary>
        public object? EvaluateUnary(UnaryOperator operator_, object operand)
        {
            if (operator_ == UnaryOperator.Positive) return operand;
            if (!_registry.TryGetValue(operand.GetType(), out var ops)) return null;

            return operator_ switch
            {
                UnaryOperator.Negate => ops.Negate(operand),
                UnaryOperator.Abs => ops.Abs(operand),
                _ => null,
            };
        }

        /// <summary>执行二元运算 / Execute binary operation.</summary>
        public object? EvaluateBinary(BinaryOperator operator_, object left, object right)
        {
            if (left.GetType() != right.GetType()) return null;
            if (!_registry.TryGetValue(left.GetType(), out var ops)) return null;

            return operator_ switch
            {
                BinaryOperator.Add => ops.Add(left, right),
                BinaryOperator.Subtract => ops.Subtract(left, right),
                BinaryOperator.Multiply => ops.Multiply(left, right),
                BinaryOperator.Divide => ops.Divide(left, right),
                BinaryOperator.Modulo => ops.Modulo(left, right),
                BinaryOperator.Power => ops.Power(left, right),
                _ => null,
            };
        }

        private void RegisterBuiltins()
        {
            Register(new Int32Ops());
            Register(new Int64Ops());
            Register(new FloatOps());
            Register(new DoubleOps());
        }
    }

    // ========== Built-in implementations ==========

    internal sealed class Int32Ops : INumericOps<int>
    {
        public int Negate(int value) => -value;
        public int Abs(int value) => global::System.Math.Abs(value);
        public int Add(int left, int right) => left + right;
        public int Subtract(int left, int right) => left - right;
        public int Multiply(int left, int right) => left * right;
        public int? Divide(int left, int right) => right != 0 ? left / right : null;
        public int? Modulo(int left, int right) => right != 0 ? left % right : null;
        public int? Power(int left, int right) => (int)global::System.Math.Pow(left, right);
    }

    internal sealed class Int64Ops : INumericOps<long>
    {
        public long Negate(long value) => -value;
        public long Abs(long value) => global::System.Math.Abs(value);
        public long Add(long left, long right) => left + right;
        public long Subtract(long left, long right) => left - right;
        public long Multiply(long left, long right) => left * right;
        public long? Divide(long left, long right) => right != 0 ? left / right : null;
        public long? Modulo(long left, long right) => right != 0 ? left % right : null;
        public long? Power(long left, long right) => (long)global::System.Math.Pow(left, right);
    }

    internal sealed class FloatOps : INumericOps<float>
    {
        public float Negate(float value) => -value;
        public float Abs(float value) => global::System.Math.Abs(value);
        public float Add(float left, float right) => left + right;
        public float Subtract(float left, float right) => left - right;
        public float Multiply(float left, float right) => left * right;
        public float? Divide(float left, float right) => right != 0f ? left / right : null;
        public float? Modulo(float left, float right) => right != 0f ? left % right : null;
        public float? Power(float left, float right) => (float)global::System.Math.Pow(left, right);
    }

    internal sealed class DoubleOps : INumericOps<double>
    {
        public double Negate(double value) => -value;
        public double Abs(double value) => global::System.Math.Abs(value);
        public double Add(double left, double right) => left + right;
        public double Subtract(double left, double right) => left - right;
        public double Multiply(double left, double right) => left * right;
        public double? Divide(double left, double right) => right != 0.0 ? left / right : null;
        public double? Modulo(double left, double right) => right != 0.0 ? left % right : null;
        public double? Power(double left, double right) => global::System.Math.Pow(left, right);
    }
}
