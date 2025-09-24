using System;
using System.Collections.Generic;
using System.Linq;
using SmartQuant;

namespace StrategyManagement
{
    /// <summary>
    /// Manages multiple levels for a multi-level strategy
    /// Handles creation, tracking, and cleanup of levels
    /// </summary>
    public class LevelManager
    {
        #region Properties

        /// <summary>
        /// Dictionary of active levels, keyed by level ID
        /// </summary>
        public Dictionary<string, Level> ActiveLevels { get; private set; }

        /// <summary>
        /// List of completed levels (for historical tracking)
        /// </summary>
        public List<Level> CompletedLevels { get; private set; }

        /// <summary>
        /// Entry level thresholds configured for this strategy
        /// </summary>
        public List<double> EntryLevels { get; private set; }

        /// <summary>
        /// Exit level multipliers configured for this strategy
        /// </summary>
        public List<double> ExitLevels { get; private set; }

        /// <summary>
        /// Whether the strategy is mean reverting
        /// </summary>
        public bool IsMeanReverting { get; set; }

        /// <summary>
        /// Maximum number of concurrent levels allowed
        /// </summary>
        public int MaxConcurrentLevels { get; set; } = 10;

        /// <summary>
        /// Total current position across all levels
        /// </summary>
        public int TotalCurrentPosition => ActiveLevels.Values.Sum(l => l.CurrentPosition);

        /// <summary>
        /// Total number of active levels
        /// </summary>
        public int ActiveLevelCount => ActiveLevels.Count;

        #endregion

        #region Constructor

        public LevelManager(List<double> entryLevels, List<double> exitLevels, bool isMeanReverting = false)
        {
            EntryLevels = new List<double>(entryLevels);
            ExitLevels = new List<double>(exitLevels);
            IsMeanReverting = isMeanReverting;
            
            ActiveLevels = new Dictionary<string, Level>();
            CompletedLevels = new List<Level>();
        }

        #endregion

        #region Level Creation and Management

        /// <summary>
        /// Check which entry levels should be triggered based on current signal
        /// </summary>
        public List<double> GetTriggeredEntryLevels(double currentSignal, OrderSide side)
        {
            var triggeredLevels = new List<double>();

            foreach (var entryLevel in EntryLevels)
            {
                // Check if we already have an active level for this threshold
                string levelId = GenerateLevelId(entryLevel, side);
                if (ActiveLevels.ContainsKey(levelId))
                    continue;

                // Check if we've reached the maximum concurrent levels
                if (ActiveLevels.Count >= MaxConcurrentLevels)
                    break;

                // Create a temporary level to check entry condition
                var tempLevel = new Level(levelId, entryLevel, ExitLevels, IsMeanReverting);
                if (tempLevel.ShouldEnter(currentSignal, side))
                {
                    triggeredLevels.Add(entryLevel);
                }
            }

            return triggeredLevels;
        }

        /// <summary>
        /// Create and activate a new level
        /// </summary>
        public Level CreateLevel(double entryThreshold, OrderSide side, int positionSize, 
                                double entryPrice, double actualSignal, DateTime dateTime)
        {
            string levelId = GenerateLevelId(entryThreshold, side);
            
            // Don't create if level already exists
            if (ActiveLevels.ContainsKey(levelId))
                return ActiveLevels[levelId];

            var level = new Level(levelId, entryThreshold, ExitLevels, IsMeanReverting);
            level.ExecuteEntry(dateTime, side, positionSize, entryPrice, actualSignal);
            
            ActiveLevels[levelId] = level;
            return level;
        }

        /// <summary>
        /// Get all triggered exit levels across all active levels
        /// </summary>
        public Dictionary<string, List<int>> GetAllTriggeredExitLevels(double currentSignal)
        {
            var triggeredExits = new Dictionary<string, List<int>>();

            foreach (var kvp in ActiveLevels)
            {
                var level = kvp.Value;
                var triggeredLevels = level.GetTriggeredExitLevels(currentSignal);
                
                if (triggeredLevels.Count > 0)
                {
                    triggeredExits[kvp.Key] = triggeredLevels;
                }
            }

            return triggeredExits;
        }

        /// <summary>
        /// Execute exit for a specific level and exit level index
        /// </summary>
        public int ExecuteExit(string levelId, int exitLevelIndex, double exitPrice, DateTime dateTime)
        {
            if (!ActiveLevels.ContainsKey(levelId))
                return 0;

            var level = ActiveLevels[levelId];
            int exitSize = level.ExecuteExit(exitLevelIndex, exitPrice, dateTime);

            // Check if level is now complete
            if (level.IsLevelComplete)
            {
                CompletedLevels.Add(level);
                ActiveLevels.Remove(levelId);
            }

            return exitSize;
        }

        #endregion

        #region Order Management

        /// <summary>
        /// Add an order to a specific level
        /// </summary>
        public void AddOrderToLevel(string levelId, int orderId, LevelOrderType orderType, 
                                   int quantity, double price, int exitLevelIndex = -1)
        {
            if (ActiveLevels.ContainsKey(levelId))
            {
                ActiveLevels[levelId].AddOrder(orderId, orderType, quantity, price, exitLevelIndex);
            }
        }

        /// <summary>
        /// Update order status for a specific level
        /// </summary>
        public void UpdateOrderStatus(int orderId, OrderStatus status)
        {
            foreach (var level in ActiveLevels.Values)
            {
                if (level.Orders.ContainsKey(orderId))
                {
                    level.UpdateOrderStatus(orderId, status);
                    break;
                }
            }
        }

        /// <summary>
        /// Find which level an order belongs to
        /// </summary>
        public string FindLevelForOrder(int orderId)
        {
            foreach (var kvp in ActiveLevels)
            {
                if (kvp.Value.Orders.ContainsKey(orderId))
                {
                    return kvp.Key;
                }
            }
            return null;
        }

        /// <summary>
        /// Check if any level has pending orders
        /// </summary>
        public bool HasAnyPendingOrders()
        {
            return ActiveLevels.Values.Any(level => level.HasPendingOrders());
        }

        /// <summary>
        /// Cleanup completed orders across all levels
        /// </summary>
        public void CleanupCompletedOrders()
        {
            foreach (var level in ActiveLevels.Values)
            {
                level.CleanupCompletedOrders();
            }
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// Generate a unique level ID based on entry threshold and side
        /// </summary>
        private string GenerateLevelId(double entryThreshold, OrderSide side)
        {
            string sideStr = side == OrderSide.Buy ? "L" : "S";
            return $"{sideStr}_{entryThreshold:F3}_{DateTime.Now.Ticks}";
        }

        /// <summary>
        /// Get level by ID
        /// </summary>
        public Level GetLevel(string levelId)
        {
            return ActiveLevels.ContainsKey(levelId) ? ActiveLevels[levelId] : null;
        }

        /// <summary>
        /// Get all levels for a specific side
        /// </summary>
        public List<Level> GetLevelsForSide(OrderSide side)
        {
            return ActiveLevels.Values.Where(l => l.Side == side).ToList();
        }

        /// <summary>
        /// Calculate total unrealized PnL across all levels
        /// </summary>
        public double CalculateTotalUnrealizedPnL(double currentPrice)
        {
            return ActiveLevels.Values.Sum(level => level.CalculateUnrealizedPnL(currentPrice));
        }

        /// <summary>
        /// Get summary statistics
        /// </summary>
        public LevelManagerStats GetStats()
        {
            return new LevelManagerStats
            {
                ActiveLevels = ActiveLevels.Count,
                CompletedLevels = CompletedLevels.Count,
                TotalPosition = TotalCurrentPosition,
                LongLevels = ActiveLevels.Values.Count(l => l.Side == OrderSide.Buy),
                ShortLevels = ActiveLevels.Values.Count(l => l.Side == OrderSide.Sell),
                PendingOrders = ActiveLevels.Values.Sum(l => l.Orders.Count)
            };
        }

        /// <summary>
        /// Force close all levels (emergency exit)
        /// </summary>
        public List<Level> ForceCloseAllLevels()
        {
            var levelsToClose = ActiveLevels.Values.ToList();
            
            foreach (var level in levelsToClose)
            {
                CompletedLevels.Add(level);
            }
            
            ActiveLevels.Clear();
            return levelsToClose;
        }

        public override string ToString()
        {
            var stats = GetStats();
            return $"LevelManager: {stats.ActiveLevels} active ({stats.LongLevels}L/{stats.ShortLevels}S), " +
                   $"{stats.CompletedLevels} completed, Position: {stats.TotalPosition}";
        }

        #endregion
    }

    /// <summary>
    /// Statistics for the level manager
    /// </summary>
    public class LevelManagerStats
    {
        public int ActiveLevels { get; set; }
        public int CompletedLevels { get; set; }
        public int TotalPosition { get; set; }
        public int LongLevels { get; set; }
        public int ShortLevels { get; set; }
        public int PendingOrders { get; set; }
    }
}

