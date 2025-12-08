using System;


namespace StrategyManagement
{
    /// <summary>
    /// Lightweight matrix class to replace MathNet.Numerics.LinearAlgebra.Matrix
    /// Only implements the specific functionality needed by MomStrategyManagerFilter
    /// </summary>
    /// <typeparam name="T">The data type of matrix elements (typically double)</typeparam>
    public class SimpleMatrix<T>
    {
        private readonly T[,] _data;

        /// <summary>
        /// Gets the number of rows in the matrix
        /// </summary>
        public int RowCount { get; }

        /// <summary>
        /// Gets the number of columns in the matrix
        /// </summary>
        public int ColumnCount { get; }

        /// <summary>
        /// Private constructor - use Build.DenseOfArray to create instances
        /// </summary>
        /// <param name="data">2D array containing matrix data</param>
        private SimpleMatrix(T[,] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            _data = data;
            RowCount = data.GetLength(0);
            ColumnCount = data.GetLength(1);
        }

        /// <summary>
        /// Indexer to access matrix elements by row and column
        /// </summary>
        /// <param name="row">Row index (0-based)</param>
        /// <param name="col">Column index (0-based)</param>
        /// <returns>The element at the specified position</returns>
        public T this[int row, int col]
        {
            get
            {
                if (row < 0 || row >= RowCount)
                    throw new ArgumentOutOfRangeException(nameof(row), $"Row index {row} is out of range [0, {RowCount})");
                if (col < 0 || col >= ColumnCount)
                    throw new ArgumentOutOfRangeException(nameof(col), $"Column index {col} is out of range [0, {ColumnCount})");

                return _data[row, col];
            }
            set
            {
                if (row < 0 || row >= RowCount)
                    throw new ArgumentOutOfRangeException(nameof(row), $"Row index {row} is out of range [0, {RowCount})");
                if (col < 0 || col >= ColumnCount)
                    throw new ArgumentOutOfRangeException(nameof(col), $"Column index {col} is out of range [0, {ColumnCount})");

                _data[row, col] = value;
            }
        }

        /// <summary>
        /// Builder class to provide factory methods for creating matrices
        /// </summary>
        public static class Build
        {
            /// <summary>
            /// Creates a dense matrix from a 2D array
            /// </summary>
            /// <param name="array">2D array containing the matrix data</param>
            /// <returns>A new SimpleMatrix instance</returns>
            public static SimpleMatrix<T> DenseOfArray(T[,] array)
            {
                if (array == null)
                    throw new ArgumentNullException(nameof(array));

                if (array.GetLength(0) == 0 || array.GetLength(1) == 0)
                    throw new ArgumentException("Array must have at least one row and one column");

                // Create a copy of the array to ensure immutability
                T[,] copy = new T[array.GetLength(0), array.GetLength(1)];
                Array.Copy(array, copy, array.Length);

                return new SimpleMatrix<T>(copy);
            }

            /// <summary>
            /// Creates a dense matrix with specified dimensions, initialized with default values
            /// </summary>
            /// <param name="rows">Number of rows</param>
            /// <param name="columns">Number of columns</param>
            /// <returns>A new SimpleMatrix instance with default values</returns>
            public static SimpleMatrix<T> Dense(int rows, int columns)
            {
                if (rows <= 0)
                    throw new ArgumentOutOfRangeException(nameof(rows), "Number of rows must be positive");
                if (columns <= 0)
                    throw new ArgumentOutOfRangeException(nameof(columns), "Number of columns must be positive");

                T[,] data = new T[rows, columns];
                return new SimpleMatrix<T>(data);
            }
        }

        /// <summary>
        /// Returns a string representation of the matrix (useful for debugging)
        /// </summary>
        public override string ToString()
        {
            return $"SimpleMatrix<{typeof(T).Name}> [{RowCount} x {ColumnCount}]";
        }
    }

}
