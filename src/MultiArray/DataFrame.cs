#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.MultiArray
{
    /// <summary>
    /// 可空值包装类
    /// Nullable value wrapper class
    /// </summary>
    public sealed record NullableValue<T>(T? Value)
    {
        /// <inheritdoc/>
        public override string ToString() => Value?.ToString() ?? "null";
    }

    /// <summary>
    /// DataFrame - 带命名列的 2D 数据结构
    /// DataFrame - 2D data structure with named columns
    /// </summary>
    public class DataFrame<T> : ICollection<IReadOnlyList<T?>>
    {
        private readonly List<List<T?>> _data;
        private readonly Dictionary<string, int> _columnIndex;

        /// <summary>构造函数 / Constructor</summary>
        public DataFrame(int nrows, int ncols, IReadOnlyList<string> columnNames)
        {
            if (columnNames.Count != ncols)
                throw new ArgumentException($"Column name count ({columnNames.Count}) must equal column count ({ncols})");

            NRows = nrows;
            NCols = ncols;
            ColumnNames = columnNames.ToList();
            _data = Enumerable.Range(0, nrows).Select(_ => new List<T?>(new T?[ncols])).ToList();
            _columnIndex = columnNames.Select((name, idx) => (name, idx)).ToDictionary(x => x.name, x => x.idx);
        }

        /// <summary>行数 / Number of rows</summary>
        public int NRows { get; }

        /// <summary>列数 / Number of columns</summary>
        public int NCols { get; }

        /// <summary>列名列表 / Column names</summary>
        public IReadOnlyList<string> ColumnNames { get; }

        /// <inheritdoc/>
        public int Count => NRows;

        /// <inheritdoc/>
        public bool IsReadOnly => false;

        /// <summary>通过列名获取列索引 / Get column index by name</summary>
        public int? GetColumnIndex(string name) => _columnIndex.TryGetValue(name, out var idx) ? idx : null;

        /// <summary>获取指定位置的值 / Get value at position</summary>
        public T? Get(int row, int col)
        {
            if (row < 0 || row >= NRows || col < 0 || col >= NCols)
                throw new ArgumentOutOfRangeException($"Index out of bounds: ({row}, {col})");
            return _data[row][col];
        }

        /// <summary>通过行和列名安全获取值 / Safely get value by row and column name</summary>
        public Result<T?, ErrorCode, Error<ErrorCode>> GetByNameSafe(int row, string columnName)
        {
            if (!_columnIndex.TryGetValue(columnName, out var col))
                return new Failed<T?, ErrorCode, Error<ErrorCode>>(ErrorCode.DataNotFound, $"Column name not found: {columnName}");
            return new Ok<T?, ErrorCode, Error<ErrorCode>>(Get(row, col));
        }

        /// <summary>设置指定位置的值 / Set value at position</summary>
        public void Set(int row, int col, T? value)
        {
            if (row < 0 || row >= NRows || col < 0 || col >= NCols)
                throw new ArgumentOutOfRangeException($"Index out of bounds: ({row}, {col})");
            _data[row][col] = value;
        }

        /// <summary>通过行和列名安全设置值 / Safely set value by row and column name</summary>
        public Result<Success, ErrorCode, Error<ErrorCode>> SetByNameSafe(int row, string columnName, T? value)
        {
            if (!_columnIndex.TryGetValue(columnName, out var col))
                return new Failed<Success, ErrorCode, Error<ErrorCode>>(ErrorCode.DataNotFound, $"Column name not found: {columnName}");
            Set(row, col, value);
            return Results.OkInstance;
        }

        /// <summary>获取指定行的所有值 / Get all values in a row</summary>
        public IReadOnlyList<T?> GetRow(int row)
        {
            if (row < 0 || row >= NRows)
                throw new ArgumentOutOfRangeException($"Row index out of bounds: {row}");
            return _data[row].ToList();
        }

        /// <summary>获取指定列的所有值 / Get all values in a column</summary>
        public IReadOnlyList<T?> GetColumn(int col)
        {
            if (col < 0 || col >= NCols)
                throw new ArgumentOutOfRangeException($"Column index out of bounds: {col}");
            return Enumerable.Range(0, NRows).Select(r => _data[r][col]).ToList();
        }

        /// <summary>通过列名安全获取列 / Safely get column by name</summary>
        public Result<IReadOnlyList<T?>, ErrorCode, Error<ErrorCode>> GetColumnByNameSafe(string columnName)
        {
            if (!_columnIndex.TryGetValue(columnName, out var col))
                return new Failed<IReadOnlyList<T?>, ErrorCode, Error<ErrorCode>>(ErrorCode.DataNotFound, $"Column name not found: {columnName}");
            return new Ok<IReadOnlyList<T?>, ErrorCode, Error<ErrorCode>>(GetColumn(col));
        }

        /// <summary>转换为可空元素的 MultiArray / Convert to MultiArray with nullable elements</summary>
        public MultiArray<NullableValue<T>, Shape2> ToNullableMultiArray()
        {
            var array = MutableMultiArray<NullableValue<T>, Shape2>.Factory.NewWith(
                Shape2.Invoke(NRows, NCols), new NullableValue<T>(default));
            for (int i = 0; i < NRows; i++)
                for (int j = 0; j < NCols; j++)
                    array[new[] { i, j }] = new NullableValue<T>(_data[i][j]);
            return array.ToImmutable();
        }

        /// <summary>获取指定范围的子 DataFrame / Get sub DataFrame of specified range</summary>
        public DataFrame<T> SubDataFrame(System.Range rows, System.Range cols)
        {
            var rowStart = rows.Start.IsFromEnd ? NRows - rows.Start.Value : rows.Start.Value;
            var rowEnd = rows.End.IsFromEnd ? NRows - rows.End.Value : rows.End.Value;
            var colStart = cols.Start.IsFromEnd ? NCols - cols.Start.Value : cols.Start.Value;
            var colEnd = cols.End.IsFromEnd ? NCols - cols.End.Value : cols.End.Value;

            var newNRows = rowEnd - rowStart;
            var newNCols = colEnd - colStart;
            var newColNames = ColumnNames.Skip(colStart).Take(newNCols).ToList();

            var newDf = new DataFrame<T>(newNRows, newNCols, newColNames);
            for (int newRow = 0; newRow < newNRows; newRow++)
                for (int newCol = 0; newCol < newNCols; newCol++)
                    newDf.Set(newRow, newCol, Get(rowStart + newRow, colStart + newCol));
            return newDf;
        }

        /// <summary>选择指定列 / Select specified columns</summary>
        public Result<DataFrame<T>, ErrorCode, Error<ErrorCode>> SelectSafe(params string[] columnNames)
        {
            var colIndices = new List<int>();
            foreach (var name in columnNames)
            {
                if (!_columnIndex.TryGetValue(name, out var col))
                    return new Failed<DataFrame<T>, ErrorCode, Error<ErrorCode>>(ErrorCode.DataNotFound, $"Column name not found: {name}");
                colIndices.Add(col);
            }

            var newDf = new DataFrame<T>(NRows, columnNames.Length, columnNames);
            for (int row = 0; row < NRows; row++)
                for (int newCol = 0; newCol < colIndices.Count; newCol++)
                    newDf.Set(row, newCol, Get(row, colIndices[newCol]));
            return new Ok<DataFrame<T>, ErrorCode, Error<ErrorCode>>(newDf);
        }

        /// <summary>过滤行 / Filter rows</summary>
        public DataFrame<T> Filter(Func<IReadOnlyList<T?>, bool> predicate)
        {
            var selectedRows = Enumerable.Range(0, NRows).Where(r => predicate(GetRow(r))).ToList();
            var newDf = new DataFrame<T>(selectedRows.Count, NCols, ColumnNames);
            for (int newRow = 0; newRow < selectedRows.Count; newRow++)
                for (int col = 0; col < NCols; col++)
                    newDf.Set(newRow, col, Get(selectedRows[newRow], col));
            return newDf;
        }

        /// <summary>复制并添加行 / Copy and add a row</summary>
        public DataFrame<T> CopyWithAddedRow(IReadOnlyList<T?> values)
        {
            if (values.Count != NCols)
                throw new ArgumentException($"Value count ({values.Count}) must equal column count ({NCols})");

            var newDf = new DataFrame<T>(NRows + 1, NCols, ColumnNames);
            for (int i = 0; i < NRows; i++)
                for (int j = 0; j < NCols; j++)
                    newDf.Set(i, j, Get(i, j));
            for (int j = 0; j < NCols; j++)
                newDf.Set(NRows, j, values[j]);
            return newDf;
        }

        /// <summary>安全转换为 Map 表示 / Safely convert to Map</summary>
        public Result<IReadOnlyDictionary<string, IReadOnlyList<T?>>, ErrorCode, Error<ErrorCode>> ToMapSafe()
        {
            var ret = new Dictionary<string, IReadOnlyList<T?>>();
            foreach (var name in ColumnNames)
            {
                var result = GetColumnByNameSafe(name);
                if (result is Failed<IReadOnlyList<T?>, ErrorCode, Error<ErrorCode>> f)
                    return new Failed<IReadOnlyDictionary<string, IReadOnlyList<T?>>, ErrorCode, Error<ErrorCode>>(f.Error);
                ret[name] = ((Ok<IReadOnlyList<T?>, ErrorCode, Error<ErrorCode>>)result).Value;
            }
            return new Ok<IReadOnlyDictionary<string, IReadOnlyList<T?>>, ErrorCode, Error<ErrorCode>>(ret);
        }

        /// <inheritdoc/>
        public IEnumerator<IReadOnlyList<T?>> GetEnumerator()
        {
            for (int i = 0; i < NRows; i++)
                yield return _data[i].ToList();
        }

        /// <inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <inheritdoc/>
        public bool Contains(IReadOnlyList<T?> item) => _data.Any(row => row.SequenceEqual(item));

        /// <inheritdoc/>
        public bool ContainsAll(IEnumerable<IReadOnlyList<T?>> items) => items.All(Contains);

        /// <inheritdoc/>
        public void CopyTo(IReadOnlyList<T?>[] array, int arrayIndex)
        {
            int idx = arrayIndex;
            foreach (var row in this)
                array[idx++] = row;
        }

        void ICollection<IReadOnlyList<T?>>.Add(IReadOnlyList<T?> item) => throw new NotSupportedException();
        void ICollection<IReadOnlyList<T?>>.Clear() => throw new NotSupportedException();
        bool ICollection<IReadOnlyList<T?>>.Remove(IReadOnlyList<T?> item) => throw new NotSupportedException();

        /// <summary>从 Map 创建 DataFrame / Create from Map</summary>
        public static DataFrame<T> FromMap(IReadOnlyDictionary<string, IReadOnlyList<T?>> data)
        {
            var columnNames = data.Keys.ToList();
            var nrows = data.Values.FirstOrDefault()?.Count ?? 0;
            var ncols = columnNames.Count;

            var df = new DataFrame<T>(nrows, ncols, columnNames);
            for (int colIdx = 0; colIdx < columnNames.Count; colIdx++)
            {
                var values = data[columnNames[colIdx]];
                for (int rowIdx = 0; rowIdx < values.Count; rowIdx++)
                    df.Set(rowIdx, colIdx, values[rowIdx]);
            }
            return df;
        }

        /// <summary>创建空 DataFrame / Create empty DataFrame</summary>
        public static DataFrame<T> Empty(params string[] columnNames) => new(0, columnNames.Length, columnNames);

        /// <summary>使用构建器创建 DataFrame / Create DataFrame using builder</summary>
        public static DataFrame<T> Build(Action<DataFrameBuilder<T>> builderAction, params string[] columnNames)
        {
            var builder = new DataFrameBuilder<T>(columnNames);
            builderAction(builder);
            return builder.Build();
        }
    }

    /// <summary>
    /// DataFrame 构建器
    /// DataFrame builder
    /// </summary>
    public class DataFrameBuilder<T>
    {
        private readonly IReadOnlyList<string> _columnNames;
        private readonly List<List<T?>> _rows = new();

        /// <summary>构造函数 / Constructor</summary>
        public DataFrameBuilder(IReadOnlyList<string> columnNames)
        {
            _columnNames = columnNames;
        }

        /// <summary>添加一行 / Add a row</summary>
        public void Row(params T?[] values)
        {
            if (values.Length != _columnNames.Count)
                throw new ArgumentException($"Value count ({values.Length}) must equal column count ({_columnNames.Count})");
            _rows.Add(values.ToList());
        }

        /// <summary>添加多行 / Add multiple rows</summary>
        public void Rows(IReadOnlyList<IReadOnlyList<T?>> values)
        {
            foreach (var row in values)
            {
                if (row.Count != _columnNames.Count)
                    throw new ArgumentException($"Value count ({row.Count}) must equal column count ({_columnNames.Count})");
                _rows.Add(row.ToList());
            }
        }

        /// <summary>构建 DataFrame / Build DataFrame</summary>
        public DataFrame<T> Build()
        {
            var df = new DataFrame<T>(_rows.Count, _columnNames.Count, _columnNames);
            for (int rowIdx = 0; rowIdx < _rows.Count; rowIdx++)
                for (int colIdx = 0; colIdx < _columnNames.Count; colIdx++)
                    df.Set(rowIdx, colIdx, _rows[rowIdx][colIdx]);
            return df;
        }
    }
}
