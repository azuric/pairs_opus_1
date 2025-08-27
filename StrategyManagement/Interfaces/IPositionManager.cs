using System;
using System.Collections.Generic;
using SmartQuant;
using Parameters;

namespace StrategyManagement
{
    /// <summary>
    /// Updated interface for position management
    /// </summary>
    public interface IPositionManager
    {
        /// <summary>
        /// Current position size (positive for long, negative for short)
        /// </summary>
        int CurrentPosition { get; }

        /// <summary>
        /// Average entry price
        /// </summary>
        double AveragePrice { get; }

        /// <summary>
        /// Realized P&L
        /// </summary>
        double RealizedPnL { get; }

        /// <summary>
        /// Unrealized P&L
        /// </summary>
        double UnrealizedPnL { get; }

        /// <summary>
        /// Last traded price
        /// </summary>
        double LastPrice { get; }

        /// <summary>
        /// Time of first entry
        /// </summary>
        DateTime FirstEntryTime { get; }

        /// <summary>
        /// Time of last entry
        /// </summary>
        DateTime LastEntryTime { get; }

        /// <summary>
        /// List of trade metrics
        /// </summary>
        IReadOnlyList<TradeMetrics> CycleMetrics { get; }

        /// <summary>
        /// Update position based on fill
        /// </summary>
        void UpdatePosition(DateTime dateTime, OrderSide side, int quantity, double price);

        /// <summary>
        /// Update metrics based on current bar
        /// </summary>
        void UpdateTradeMetric(Bar bar);

        /// <summary>
        /// Save cycle metrics to file
        /// </summary>
        void SaveCycleMetrics();

        /// <summary>
        /// Reset position manager
        /// </summary>
        void Reset();
    }

    /// <summary>
    /// Interface for dual position management
    /// </summary>
    public interface IDualPositionManager
    {
        /// <summary>
        /// Theoretical position manager (perfect fills)
        /// </summary>
        IPositionManager TheoPositionManager { get; }

        /// <summary>
        /// Actual position manager (real fills)
        /// </summary>
        IPositionManager ActualPositionManager { get; }

        /// <summary>
        /// Check and reconcile theoretical vs actual positions
        /// </summary>
        int CheckTheoActual();

        /// <summary>
        /// Update theoretical position
        /// </summary>
        void UpdateTheoPosition(DateTime dateTime, OrderSide side, int quantity, double price);

        /// <summary>
        /// Update actual position from fill
        /// </summary>
        void UpdateActualPosition(DateTime dateTime, OrderSide side, int quantity, double price);

        /// <summary>
        /// Update metrics for both managers
        /// </summary>
        void UpdateMetrics(Bar bar);

        /// <summary>
        /// Save all metrics
        /// </summary>
        void SaveAllMetrics();

        /// <summary>
        /// Get position discrepancy
        /// </summary>
        int GetPositionDiscrepancy();
    }
}