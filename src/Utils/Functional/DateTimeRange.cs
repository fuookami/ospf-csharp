#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;

namespace Fuookami.Ospf.Utils.Functional
{
    /// <summary>
    /// 日期时间范围迭代器 / DateTime range iterator.
    /// Mirrors ospf-kotlin LocalDateClosedRangeIterator.
    /// </summary>
    public sealed class DateTimeRangeIterator : IEnumerator<DateTime>
    {
        private readonly DateTime _start;
        private readonly DateTime _end;
        private readonly TimeSpan _step;
        private DateTime _current;
        private bool _started;

        /// <summary>构造函数 / Constructor.</summary>
        public DateTimeRangeIterator(DateTime start, DateTime end, TimeSpan step)
        {
            _start = start;
            _end = end;
            _step = step;
            _current = start;
            _started = false;
        }

        /// <inheritdoc/>
        public DateTime Current => _current;

        /// <inheritdoc/>
        object IEnumerator.Current => _current;

        /// <inheritdoc/>
        public bool MoveNext()
        {
            if (!_started)
            {
                _started = true;
                return _current <= _end;
            }
            _current = _current.Add(_step);
            return _current <= _end;
        }

        /// <inheritdoc/>
        public void Reset()
        {
            _current = _start;
            _started = false;
        }

        /// <inheritdoc/>
        public void Dispose() { }
    }
}
