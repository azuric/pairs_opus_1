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
        public Dictionary<int, Level> ActiveLevels { get; private set; }

        /// <summary>
        /// Dictionary of levels, keyed by level ID
        /// </summary>
        public Dictionary<int, int> ActiveLevels2Levels { get; set; }

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
        /// Entry
        /// </summary>
        public Level[] Levels { get; set; }

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
        public int ActiveLevelCount;

        public int currentLevelId;

        #endregion

        #region Constructor


        public LevelManager(List<double> entryLevels, List<double> exitLevels, bool isMeanReverting = false)
        {
            EntryLevels = new List<double>(entryLevels);
            ExitLevels = new List<double>(exitLevels);
            IsMeanReverting = isMeanReverting;

            Levels = new Level[entryLevels.Count];

            ActiveLevels = new Dictionary<int, Level>();
            ActiveLevels2Levels = new Dictionary<int, int>();
            CompletedLevels = new List<Level>();

            currentLevelId = 0;
        }

        #endregion

        #region Level Creation and Management

        /// <summary>
        /// Check which entry levels should be triggered based on current signal
        ///// </summary>
        //public List<double> GetTriggeredEntryLevels(double currentSignal, OrderSide side)
        //{
        //    var triggeredLevels = new List<double>();

        //    foreach (var entryLevel in EntryLevels)
        //    {
        //        // Check if we already have an active level for this threshold
        //        int levelId = currentLevelId;

        //        if (ActiveLevels.ContainsKey(levelId))
        //            continue;

        //        // Check if we've reached the maximum concurrent levels
        //        if (ActiveLevels.Count >= MaxConcurrentLevels)
        //            break;

        //        // Create a temporary level to check entry condition
        //        var tempLevel = new Level(levelId, entryLevel, ExitLevels, IsMeanReverting);
        //        if (tempLevel.ShouldEnter(currentSignal, side))
        //        {
        //            triggeredLevels.Add(entryLevel);
        //        }
        //    }

        //    return triggeredLevels;
        //}

        /// <summary>
        /// Create and activate a new level
        /// </summary>
        public Level CreateLevel(int entryLevel, double entryThreshold, OrderSide side, int positionSize,
                                double entryPrice, double actualSignal, DateTime dateTime)
        {
            Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] LevelManager.CreateLevel - Creating level: EntryLevel={entryLevel}, Threshold={entryThreshold}, Side={side}");

            int levelId = currentLevelId;
            currentLevelId++;

            Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] LevelManager.CreateLevel - Assigned level ID: {levelId}");

            var level = new Level(levelId, entryThreshold, ExitLevels, IsMeanReverting);

            level.ExecuteEntry(dateTime, side, positionSize, entryPrice, actualSignal);

            Levels[entryLevel] = level;
            ActiveLevels[levelId] = level;
            ActiveLevels2Levels[levelId] = entryLevel;

            ActiveLevelCount++;

            Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] LevelManager.CreateLevel - Level {levelId} activated. Total active levels: {ActiveLevels.Count}");

            return level;
        }

        /// <summary>
        /// Get all triggered exit levels across all active levels
        /// </summary>
        public Dictionary<int, List<int>> GetAllTriggeredExitLevels(double currentSignal)
        {
            Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] LevelManager.GetAllTriggeredExitLevels - Checking {ActiveLevels.Count} active levels for exits with signal {currentSignal:F6}");

            var triggeredExits = new Dictionary<int, List<int>>();

            foreach (var kvp in ActiveLevels)
            {
                var level = kvp.Value;
                var triggeredLevels = level.GetTriggeredExitLevels(currentSignal);

                if (triggeredLevels.Count > 0)
                {
                    triggeredExits[kvp.Key] = triggeredLevels;
                    Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] LevelManager.GetAllTriggeredExitLevels - Level {kvp.Key} has {triggeredLevels.Count} triggered exits");
                }
            }

            Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] LevelManager.GetAllTriggeredExitLevels - Total levels with triggered exits: {triggeredExits.Count}");
            return triggeredExits;
        }

        /// <summary>
        /// Execute exit for a specific level and exit level index
        /// </summary>
        public int ExecuteExit(int levelId, int exitLevelIndex, double exitPrice, DateTime dateTime)
        {
            Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] LevelManager.ExecuteExit - Executing exit for level {levelId}, exit index {exitLevelIndex}");

            if (!ActiveLevels.ContainsKey(levelId))
            {
                Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] LevelManager.ExecuteExit - Level {levelId} not found in active levels");
                return 0;
            }

            var level = ActiveLevels[levelId];

            int exitSize = level.ExecuteExit(exitLevelIndex, exitPrice, dateTime);

            Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] LevelManager.ExecuteExit - Level {levelId} exit executed: Size={exitSize}");

            // Check if level is now complete
            if (level.IsLevelComplete)
            {
                Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] LevelManager.ExecuteExit - Level {levelId} is now complete, moving to completed levels");
                CompletedLevels.Add(level);
                ActiveLevels.Remove(levelId);
                Levels[ActiveLevels2Levels[levelId]] = null;
                Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] LevelManager.ExecuteExit - Active levels count: {ActiveLevels.Count}, Completed levels count: {CompletedLevels.Count}");
            }

            return exitSize;
        }

        #endregion

        #region Order Management

        /// <summary>
        /// Add an order to a specific level
        /// </summary>
        public void AddOrderToLevel(int levelId, int orderId, LevelOrderType orderType,
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
        public int FindLevelForOrder(int orderId)
        {
            foreach (var kvp in ActiveLevels)
            {
                if (kvp.Value.Orders.ContainsKey(orderId))
                {
                    return kvp.Key;
                }
            }
            return -1;
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
        public Level GetLevel(int levelId)
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

