#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Operator;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections;
using System.Collections.Generic;

namespace Fuookami.Ospf.Math;
/// <summary>
/// 整数范围迭代器 / Integer range iterator
/// </summary>
internal sealed class IntegerIterator<I> : IEnumerator<I>
    where I : struct, IInteger<I>, IPlusGroup<I>, IRem<I, I> {
    private readonly I _last;
    private readonly I _step;
    private I _current;
    private bool _started;
    private bool _finished;

    internal IntegerIterator(I first, I last, I step) {
        _current = first;
        _last = last;
        _step = step;
        _started = false;
        _finished = false;
    }

    public I Current => _current;

    object? IEnumerator.Current => Current;

    public bool MoveNext() {
        if (_finished) {
            return false;
        }

        if (!_started) {
            _started = true;
            return true;
        }
        _current = _current.Plus(_step);
        if (_current.Gr(_last)) {
            _finished = true;
            return false;
        }
        return true;
    }

    public void Reset() => throw new NotSupportedException();

    public void Dispose() { }
}

/// <summary>
/// 整数范围 / Integer range
/// </summary>
public sealed class IntegerRange<I> : IEnumerable<I>, IContains<I>
    where I : struct, IInteger<I>, IPlusGroup<I>, IRem<I, I> {
    /// <summary>起始值 / Start value</summary>
    public I Start { get; }
    /// <summary>结束值（包含）/ End value (inclusive)</summary>
    public I EndInclusive { get; }
    /// <summary>步长 / Step</summary>
    public I Step { get; }

    /// <summary>
    /// 创建整数范围 / Create integer range
    /// </summary>
    public IntegerRange(I start, I endInclusive, I step) {
        Start = start;
        EndInclusive = endInclusive;
        Step = step;
    }

    /// <summary>第一个元素 / First element</summary>
    public I First => Start;

    /// <summary>最后一个元素 / Last element</summary>
    public I Last => EndInclusive;

    /// <summary>设置步长 / Set step</summary>
    public IntegerRange<I> WithStep(I step) =>
        new(Start, EndInclusive, step);

    /// <summary>安全设置步长 / Safely set step</summary>
    public Result<IntegerRange<I>, ErrorCode, Error<ErrorCode>> StepSafe(I step) {
        // Basic zero check via self-equality and comparison
        if (step.Eq(Start) && step.Eq(EndInclusive) && !step.Eq(Step)) {
            return Results.Failed<IntegerRange<I>>(new Err<ErrorCode>(ErrorCode.IllegalArgument, "Step cannot be zero"));
        }
        return Results.Ok(WithStep(step));
    }

    /// <summary>设置步长（可能为 null）/ Set step (nullable)</summary>
    public IntegerRange<I>? StepOrNull(I step) => WithStep(step);

    /// <summary>获取枚举器 / Get enumerator</summary>
    public IEnumerator<I> GetEnumerator() =>
        new IntegerIterator<I>(Start, Last, Step);

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>是否包含指定值 / Whether contains the specified value</summary>
    public bool Contains(I value) {
        if (value.Ls(Start) || value.Gr(Last)) {
            return false;
        }
        return true;
    }

    /// <summary>是否为空 / Whether empty</summary>
    public bool IsEmpty() => Start.Gr(Last);

    /// <summary>转换为字符串 / Convert to string</summary>
    public override string ToString() => $"[{Start}..{Last} step {Step}]";
}

/// <summary>
/// 数值无符号整数范围迭代器 / Numeric unsigned integer range iterator
/// </summary>
internal sealed class NumericIntegerIterator<NI, I> : IEnumerator<NI>
    where I : struct, IUIntegerNumber<I> {
    private readonly I _last;
    private readonly I _step;
    private readonly Func<I, NI> _ctor;
    private I _current;
    private bool _started;
    private bool _finished;

    internal NumericIntegerIterator(I first, I last, I step, Func<I, NI> ctor) {
        _current = first;
        _last = last;
        _step = step;
        _ctor = ctor;
        _started = false;
        _finished = false;
    }

    public NI Current => _ctor(_current);

    object? IEnumerator.Current => Current;

    public bool MoveNext() {
        if (_finished) {
            return false;
        }

        if (!_started) {
            _started = true;
            return true;
        }
        _current = _current.Plus(_step);
        if (_current.Gr(_last)) {
            _finished = true;
            return false;
        }
        return true;
    }

    public void Reset() => throw new NotSupportedException();

    public void Dispose() { }
}

/// <summary>
/// 数值无符号整数范围 / Numeric unsigned integer range
/// </summary>
public sealed class NumericUIntegerRange<NI, I> : IEnumerable<NI>, IContains<NI>
    where I : struct, IUIntegerNumber<I>, IPlusGroup<I>, IRem<I, I> {
    /// <summary>起始值 / Start value</summary>
    public NI Start { get; }
    /// <summary>结束值（包含）/ End value (inclusive)</summary>
    public NI EndInclusive { get; }
    /// <summary>步长 / Step</summary>
    public I Step { get; }
    private readonly Func<I, NI> _ctor;
    private readonly Func<NI, I> _converter;

    /// <summary>
    /// 创建数值无符号整数范围 / Create numeric unsigned integer range
    /// </summary>
    public NumericUIntegerRange(NI start, NI endInclusive, NI step, Func<I, NI> ctor, Func<NI, I> converter) {
        Start = start;
        EndInclusive = endInclusive;
        _ctor = ctor;
        _converter = converter;
        Step = converter(step);
    }

    /// <summary>第一个元素 / First element</summary>
    public I First => _converter(Start);
    /// <summary>最后一个元素 / Last element</summary>
    public I Last => _converter(EndInclusive);

    /// <summary>设置步长 / Set step</summary>
    public NumericUIntegerRange<NI, I> WithStep(NI step) =>
        new(Start, EndInclusive, step, _ctor, _converter);

    /// <summary>安全设置步长 / Safely set step</summary>
    public Result<NumericUIntegerRange<NI, I>, ErrorCode, Error<ErrorCode>> StepSafe(NI step) => Results.Ok(WithStep(step));

    /// <summary>设置步长（可能为 null）/ Set step (nullable)</summary>
    public NumericUIntegerRange<NI, I>? StepOrNull(NI step) => WithStep(step);

    /// <summary>获取枚举器 / Get enumerator</summary>
    public IEnumerator<NI> GetEnumerator() =>
        new NumericIntegerIterator<NI, I>(First, Last, Step, _ctor);

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>是否包含指定值 / Whether contains the specified value</summary>
    public bool Contains(NI value) {
        I inner = _converter(value);
        return inner.Geq(First) && inner.Leq(Last);
    }

    /// <summary>是否为空 / Whether empty</summary>
    public bool IsEmpty() => First.Gr(Last);

    /// <summary>转换为字符串 / Convert to string</summary>
    public override string ToString() => $"[{First}..{Last} step {Step}]";
}
