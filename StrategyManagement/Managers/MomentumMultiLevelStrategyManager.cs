using System;
using System.Collections.Generic;
using System.Linq;
using SmartQuant;
using Parameters;
using System.Threading;

namespace StrategyManagement
{
    /// <summary>
    /// Multi-level momentum strategy manager with sophisticated entry/exit level management
    /// Each entry level has multiple exit levels managed independently
    /// </summary>
    public class MomentumMultiLevelStrategyManager : BaseStrategyManager
    {
        #region Private Fields

        private readonly Queue<Bar> barHistory;
        private LevelManager levelManager;
        private int momentumPeriod;
        private double currentMomentum;
        private bool isMeanReverting;
        private int basePositionSize;
        private double stopLossPercent;
        private double takeProfitPercent;

        // Configuration from additional_params
        private List<double> entryLevels;
        private List<double> exitLevels;

        #endregion

        #region Constructor

        public MomentumMultiLevelStrategyManager() : base("MomentumMultiLevel")
        {
            barHistory = new Queue<Bar>();
        }

        #endregion

        #region Initialization

        public override void Initialize(StrategyParameters parameters)
        {
            base.Initialize(parameters);

            // Default values
            momentumPeriod = 10;
            basePositionSize = 1;
            stopLossPercent = 0.02;
            takeProfitPercent = 0.05;
            isMeanReverting = false;

            // Parse additional parameters
            ParseAdditionalParameters();

            // Initialize level manager
            levelManager = new LevelManager(entryLevels, exitLevels, isMeanReverting);
            levelManager.MaxConcurrentLevels = 10; // Can be configured via additional_params
        }

        private void ParseAdditionalParameters()
        {
            if (Parameters.additional_params == null)
            {
                SetDefaultLevels();
                return;
            }

            // Parse entry levels
            if (Parameters.additional_params.ContainsKey("entry_levels"))
            {
                if (Parameters.additional_params["entry_levels"] is List<object> entryList)
                {
                    entryLevels = entryList.Select(x => Convert.ToDouble(x)).ToList();
                }
                else if (Parameters.additional_params["entry_levels"] is double[] entryArray)
                {
                    entryLevels = entryArray.ToList();
                }
            }

            // Parse exit levels
            if (Parameters.additional_params.ContainsKey("exit_levels"))
            {
                if (Parameters.additional_params["exit_levels"] is List<object> exitList)
                {
                    exitLevels = exitList.Select(x => Convert.ToDouble(x)).ToList();
                }
                else if (Parameters.additional_params["exit_levels"] is double[] exitArray)
                {
                    exitLevels = exitArray.ToList();
                }
            }

            // Parse other parameters
            if (Parameters.additional_params.ContainsKey("momentum_period"))
                momentumPeriod = Convert.ToInt32(Parameters.additional_params["momentum_period"]);

            if (Parameters.additional_params.ContainsKey("base_position_size"))
                basePositionSize = Convert.ToInt32(Parameters.additional_params["base_position_size"]);

            if (Parameters.additional_params.ContainsKey("is_mean_reverting"))
                isMeanReverting = Convert.ToBoolean(Parameters.additional_params["is_mean_reverting"]);

            if (Parameters.additional_params.ContainsKey("stop_loss_percent"))
                stopLossPercent = Convert.ToDouble(Parameters.additional_params["stop_loss_percent"]);

            if (Parameters.additional_params.ContainsKey("take_profit_percent"))
                takeProfitPercent = Convert.ToDouble(Parameters.additional_params["take_profit_percent"]);

            if (Parameters.additional_params.ContainsKey("max_concurrent_levels"))
                levelManager.MaxConcurrentLevels = Convert.ToInt32(Parameters.additional_params["max_concurrent_levels"]);

            // Validate levels
            if (entryLevels == null || entryLevels.Count == 0)
                SetDefaultLevels();
        }

        private void SetDefaultLevels()
        {
            entryLevels = new List<double> { 0.5, 0.75, 1.0 };
            exitLevels = new List<double> { 0.5, 0.25 };
        }

        #endregion

        #region Main Processing

        public override void ProcessBar(Bar[] bars)
        {
            Bar signalBar = GetSignalBar(bars);

            // Update momentum calculation
            UpdateMomentum(signalBar);

            if (barHistory.Count < momentumPeriod)
                return;

            // Check for new entry levels
            ProcessEntryLevels(bars);

            // Check for exit levels
            ProcessExitLevels(bars);

            // Cleanup completed orders
            levelManager.CleanupCompletedOrders();
        }

        private void ProcessEntryLevels(Bar[] bars)
        {
            Bar bar = GetExecutionInstrumentBar(bars);

            if (!IsWithinTradingHours(bar.DateTime) || !CanEnterNewPosition(bar.DateTime))
                return;

            // Check for long entries
            var longEntryLevels = levelManager.GetTriggeredEntryLevels(currentMomentum, OrderSide.Buy);
            foreach (var entryLevel in longEntryLevels)
            {
                double entryPrice = bar.Close;
                
                var level = levelManager.CreateLevel(entryLevel, OrderSide.Buy, positionSize, 
                                                   entryPrice, currentMomentum, bar.DateTime);
                
                // Execute theoretical entry
                ExecuteTheoreticalEntry(bars, OrderSide.Buy);
                
                // Place actual order if TradeManager is available
                if (TradeManager != null && !HasLiveOrder())
                {
                    PlaceEntryOrder(level, bar);
                }
            }

            // Check for short entries
            var shortEntryLevels = levelManager.GetTriggeredEntryLevels(currentMomentum, OrderSide.Sell);
            foreach (var entryLevel in shortEntryLevels)
            {
                double entryPrice = bar.Close ;
                
                var level = levelManager.CreateLevel(entryLevel, OrderSide.Sell, positionSize, 
                                                   entryPrice, currentMomentum, bar.DateTime);
                
                // Execute theoretical entry
                ExecuteTheoreticalEntry(bars, OrderSide.Sell);
                
                // Place actual order if TradeManager is available
                if (TradeManager != null && !HasLiveOrder())
                {
                    PlaceEntryOrder(level, bar);
                }
            }
        }

        private void ProcessExitLevels(Bar[] bars)
        {
            Bar bar = GetSignalBar(bars);

            if (ShouldExitAllPositions(bar.DateTime))
            {
                ForceExitAllLevels(bars);
                return;
            }

            var triggeredExits = levelManager.GetAllTriggeredExitLevels(currentMomentum);
            
            foreach (var kvp in triggeredExits)
            {
                string levelId = kvp.Key;
                var exitLevelIndices = kvp.Value;
                
                foreach (var exitLevelIndex in exitLevelIndices)
                {
                    var level = levelManager.GetLevel(levelId);
                    if (level == null) continue;
                    
                    int exitSize = level.GetExitQuantityForLevel(exitLevelIndex);
                    if (exitSize <= 0) continue;
                    
                    double exitPrice = bar.Close;

                    // Execute theoretical exit
                    OrderSide exitSide = level.Side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy;

                    int currentPosition = base.PositionManager.CurrentPosition;

                    ExecuteTheoreticalExit(bars, currentPosition);
                    
                    // Execute level exit
                    levelManager.ExecuteExit(levelId, exitLevelIndex, exitPrice, bar.DateTime);
                    
                    // Place actual order if TradeManager is available
                    if (TradeManager != null && !HasLiveOrder())
                    {
                        PlaceExitOrder(level, exitLevelIndex, exitSize, bar);
                    }
                }
            }
        }

        #endregion

        #region Order Management

        private void PlaceEntryOrder(Level level, Bar bar)
        {
            // Implementation would depend on your TradeManager interface
            // This is a placeholder for the actual order placement logic
            
            // Example:
            // int orderId = TradeManager.PlaceOrder(level.Side, level.PositionSize, level.EntryPrice);
            // level.AddOrder(orderId, LevelOrderType.Entry, level.PositionSize, level.EntryPrice);
        }

        private void PlaceExitOrder(Level level, int exitLevelIndex, int exitSize, Bar bar)
        {
            // Implementation would depend on your TradeManager interface
            // This is a placeholder for the actual order placement logic
            
            OrderSide exitSide = level.Side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy;
            double exitPrice = bar.Close;

            // Example:
            // int orderId = TradeManager.PlaceOrder(exitSide, exitSize, exitPrice);
            // level.AddOrder(orderId, LevelOrderType.Exit, exitSize, exitPrice, exitLevelIndex);
        }

        private void ForceExitAllLevels(Bar[] bars)
        {
            var levelsToClose = levelManager.ForceCloseAllLevels();

            Bar bar = GetExecutionInstrumentBar(bars);
            
            foreach (var level in levelsToClose)
            {
                if (level.CurrentPosition != 0)
                {
                    OrderSide exitSide = level.Side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy;
                    int exitSize = Math.Abs(level.CurrentPosition);
                    double exitPrice = bar.Close;

                    ExecuteTheoreticalExit(bars, exitSize);
                }
            }
        }

        #endregion

        #region Market Data Handlers

        public override void OnBar(Bar[] bars)
        {
            // Update bar history for momentum calculation
            var bar = GetSignalBar(bars);

            barHistory.Enqueue(bar);
            if (barHistory.Count > momentumPeriod)
            {
                barHistory.Dequeue();
            }
        }

        public override void OnTrade(Trade trade) { }
        public override void OnAsk(Ask ask) { }
        public override void OnBid(Bid bid) { }

        #endregion

        #region Decision Methods (Required by BaseStrategyManager)

        public override bool ShouldEnterLongPosition(Bar[] bars)
        {
            // This method is required by BaseStrategyManager but not used in multi-level approach
            // The multi-level logic is handled in ProcessEntryLevels
            return false;
        }

        public override bool ShouldEnterShortPosition(Bar[] bars)
        {
            // This method is required by BaseStrategyManager but not used in multi-level approach
            // The multi-level logic is handled in ProcessEntryLevels
            return false;
        }

        public override bool ShouldExitLongPosition(Bar[] bars)
        {
            // This method is required by BaseStrategyManager but not used in multi-level approach
            // The multi-level logic is handled in ProcessExitLevels
            return false;
        }

        public override bool ShouldExitShortPosition(Bar[] bars)
        {
            // This method is required by BaseStrategyManager but not used in multi-level approach
            // The multi-level logic is handled in ProcessExitLevels
            return false;
        }

        #endregion

        #region Helper Methods

        private void UpdateMomentum(Bar bar)
        {
            if (barHistory.Count < 2)
            {
                currentMomentum = 0;
                return;
            }

            var oldestBar = barHistory.First();
            var newestBar = bar;

            currentMomentum = (newestBar.Close - oldestBar.Close) / oldestBar.Close;
        }

        private int CalculatePositionSizeForLevel(Bar bar, double accountValue, double entryLevel)
        {
            // Scale position size based on entry level strength
            double momentumStrength = Math.Abs(currentMomentum);
            double levelMultiplier = entryLevel / entryLevels.Max();
            
            return Math.Max(1, (int)(basePositionSize * levelMultiplier));
        }

        public override void OnFill(Fill fill)
        {
            base.OnFill(fill);
            
            // Update level manager with fill information
            if (levelManager != null)
            {
                levelManager.UpdateOrderStatus(fill.Order.Id, OrderStatus.Filled);
            }
        }

        public override void OnOrderEvent(Order order)
        {
            base.OnOrderEvent(order);
            
            // Update level manager with order status
            if (levelManager != null)
            {
                levelManager.UpdateOrderStatus(order.Id, order.Status);
            }
        }

        /// <summary>
        /// Get current level manager statistics for monitoring
        /// </summary>
        public LevelManagerStats GetLevelStats()
        {
            return levelManager?.GetStats() ?? new LevelManagerStats();
        }

        /// <summary>
        /// Get total unrealized PnL across all levels
        /// </summary>
        public double GetTotalUnrealizedPnL(double currentPrice)
        {
            return levelManager?.CalculateTotalUnrealizedPnL(currentPrice) ?? 0;
        }

        #endregion
    }
}

