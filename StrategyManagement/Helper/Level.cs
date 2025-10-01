using System.Collections.Generic;
using System;
using System.Linq;
using SmartQuant;

namespace StrategyManagement
{
    /// <summary>
    /// Represents a single entry level with multiple exit levels
    /// Each level manages its own position and orders independently
    /// </summary>
    public class Level
    {
        #region Properties

        /// <summary>
        /// Unique identifier for this level
        /// </summary>
        public int Id { get; private set; }

        /// <summary>
        /// The signal threshold that triggered this level's entry
        /// </summary>
        public double EntrySignalThreshold { get; private set; }

        /// <summary>
        /// The actual signal value when entry was triggered
        /// </summary>
        public double ActualEntrySignal { get; set; }

        /// <summary>
        /// The price at which this level was entered
        /// </summary>
        public double EntryPrice { get; set; }

        /// <summary>
        /// The side of the position (Long/Short)
        /// </summary>
        public OrderSide Side { get; set; }

        /// <summary>
        /// The size of the position for this level
        /// </summary>
        public int PositionSize { get; set; }

        /// <summary>
        /// Current net position for this level (can be partial due to exits)
        /// </summary>
        public int CurrentPosition { get; set; }

        /// <summary>
        /// Exit level multipliers (e.g., 0.5, 0.25 means exit at 50% and 25% of entry signal)
        /// </summary>
        public List<double> ExitLevels { get; private set; }

        /// <summary>
        /// Track which exit levels have been triggered
        /// Key: exit level index, Value: remaining quantity for that exit
        /// </summary>
        public Dictionary<int, int> ExitLevelStatus { get; private set; }

        /// <summary>
        /// Track orders for this level
        /// Key: order ID, Value: order details
        /// </summary>
        public Dictionary<int, LevelOrder> Orders { get; private set; }

        /// <summary>
        /// DateTime when this level was created/entered
        /// </summary>
        public DateTime EntryDateTime { get; set; }

        /// <summary>
        /// Whether this level has been fully entered
        /// </summary>
        public bool IsEntryComplete { get; set; }

        /// <summary>
        /// Whether this level is completely closed (all exits filled)
        /// </summary>
        public bool IsLevelComplete => CurrentPosition == 0;

        /// <summary>
        /// Whether the strategy is mean reverting (affects exit logic)
        /// </summary>
        public bool IsMeanReverting { get; set; }

        #endregion

        #region Constructor

        public Level(int id, double entrySignalThreshold, List<double> exitLevels, bool isMeanReverting = false)
        {
            Id = id;
            EntrySignalThreshold = entrySignalThreshold;
            ExitLevels = new List<double>(exitLevels);
            IsMeanReverting = isMeanReverting;

            ExitLevelStatus = new Dictionary<int, int>();
            Orders = new Dictionary<int, LevelOrder>();

            CurrentPosition = 0;
            IsEntryComplete = false;
        }

        #endregion

        #region Entry Methods


        /// <summary>
        /// Execute entry for this level
        /// </summary>
        public void ExecuteEntry(DateTime dateTime, OrderSide side, int positionSize, double entryPrice, double actualSignal)
        {
            EntryDateTime = dateTime;
            Side = side;
            PositionSize = positionSize;
            CurrentPosition = positionSize * (side == OrderSide.Buy ? 1 : -1);
            EntryPrice = entryPrice;
            ActualEntrySignal = actualSignal;
            IsEntryComplete = true;

            // Initialize exit level status
            InitializeExitLevels();
        }

        private void InitializeExitLevels()
        {
            ExitLevelStatus.Clear();

            int exitSizePerLevel = Math.Abs(CurrentPosition) / ExitLevels.Count;
            int remainder = Math.Abs(CurrentPosition) % ExitLevels.Count;

            for (int i = 0; i < ExitLevels.Count; i++)
            {
                int sizeForThisLevel = exitSizePerLevel + (i < remainder ? 1 : 0);
                ExitLevelStatus[i] = sizeForThisLevel;
            }
        }

        #endregion

        #region Exit Methods

        /// <summary>
        /// Check which exit levels should be triggered based on current signal
        /// </summary>
        public List<int> GetTriggeredExitLevels(double currentSignal)
        {
            if (!IsEntryComplete || IsLevelComplete) return new List<int>();

            var triggeredLevels = new List<int>();

            for (int i = 0; i < ExitLevels.Count; i++)
            {
                if (ExitLevelStatus.ContainsKey(i) && ExitLevelStatus[i] > 0)
                {
                    if (ShouldExitAtLevel(currentSignal, i))
                    {
                        triggeredLevels.Add(i);
                    }
                }
            }

            return triggeredLevels;
        }

        private bool ShouldExitAtLevel(double currentSignal, int exitLevelIndex)
        {
            double exitThreshold = EntrySignalThreshold * ExitLevels[exitLevelIndex];

            //if (IsMeanReverting)
            //{
                // For mean reverting, exit when signal moves back toward mean
                if (Side == OrderSide.Buy)
                    return currentSignal >= -exitThreshold;
                else
                    return currentSignal <= exitThreshold;
            //}
            //else
            //{
            //    // For momentum, exit when signal weakens
            //    if (Side == OrderSide.Buy)
            //        return currentSignal <= exitThreshold;
            //    else
            //        return currentSignal >= -exitThreshold;
            //}
        }

        /// <summary>
        /// Execute exit for a specific exit level
        /// </summary>
        public int ExecuteExit(int exitLevelIndex, double exitPrice, DateTime dateTime)
        {
            if (!ExitLevelStatus.ContainsKey(exitLevelIndex) || ExitLevelStatus[exitLevelIndex] <= 0)
                return 0;

            int exitSize = ExitLevelStatus[exitLevelIndex];
            ExitLevelStatus[exitLevelIndex] = 0;

            // Update current position
            if (Side == OrderSide.Buy)
                CurrentPosition -= exitSize;
            else
                CurrentPosition += exitSize;

            return exitSize;
        }

        #endregion

        #region Order Management

        /// <summary>
        /// Add an order to this level's tracking
        /// </summary>
        public void AddOrder(int orderId, LevelOrderType orderType, int quantity, double price, int exitLevelIndex = -1)
        {
            Orders[orderId] = new LevelOrder
            {
                OrderId = orderId,
                OrderType = orderType,
                Quantity = quantity,
                Price = price,
                ExitLevelIndex = exitLevelIndex,
                Status = OrderStatus.PendingNew
            };
        }

        /// <summary>
        /// Update order status
        /// </summary>
        public void UpdateOrderStatus(int orderId, OrderStatus status)
        {
            if (Orders.ContainsKey(orderId))
            {
                Orders[orderId].Status = status;
            }
        }

        /// <summary>
        /// Remove completed orders
        /// </summary>
        public void CleanupCompletedOrders()
        {
            var completedOrders = Orders.Where(kvp =>
                kvp.Value.Status == OrderStatus.Filled ||
                kvp.Value.Status == OrderStatus.Cancelled ||
                kvp.Value.Status == OrderStatus.Rejected)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var orderId in completedOrders)
            {
                Orders.Remove(orderId);
            }
        }

        /// <summary>
        /// Check if this level has any pending orders
        /// </summary>
        public bool HasPendingOrders()
        {
            return Orders.Any(kvp =>
                kvp.Value.Status == OrderStatus.PendingNew ||
                kvp.Value.Status == OrderStatus.New ||
                kvp.Value.Status == OrderStatus.PartiallyFilled);
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// Get the total remaining exit quantity for all levels
        /// </summary>
        public int GetTotalRemainingExitQuantity()
        {
            return ExitLevelStatus.Values.Sum();
        }

        /// <summary>
        /// Get exit quantity for a specific level
        /// </summary>
        public int GetExitQuantityForLevel(int exitLevelIndex)
        {
            return ExitLevelStatus.ContainsKey(exitLevelIndex) ? ExitLevelStatus[exitLevelIndex] : 0;
        }

        /// <summary>
        /// Calculate unrealized PnL for this level
        /// </summary>
        public double CalculateUnrealizedPnL(double currentPrice)
        {
            if (CurrentPosition == 0) return 0;

            if (Side == OrderSide.Buy)
                return CurrentPosition * (currentPrice - EntryPrice);
            else
                return Math.Abs(CurrentPosition) * (EntryPrice - currentPrice);
        }

        public override string ToString()
        {
            return $"Level {Id}: Threshold={EntrySignalThreshold}, Position={CurrentPosition}/{PositionSize}, " +
                   $"Entry=${EntryPrice:F2}, Complete={IsLevelComplete}";
        }

        #endregion
    }

    /// <summary>
    /// Represents an order associated with a specific level
    /// </summary>
    public class LevelOrder
    {
        public int OrderId { get; set; }
        public LevelOrderType OrderType { get; set; }
        public int Quantity { get; set; }
        public double Price { get; set; }
        public int ExitLevelIndex { get; set; } = -1; // -1 for entry orders
        public OrderStatus Status { get; set; }
        public DateTime CreatedTime { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// Types of orders for level management
    /// </summary>
    public enum LevelOrderType
    {
        Entry,
        Exit
    }
}