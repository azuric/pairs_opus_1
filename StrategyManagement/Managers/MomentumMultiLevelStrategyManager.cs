using System;
using System.Collections.Generic;
using System.Linq;
using SmartQuant;
using Parameters;
using System.Threading;

namespace StrategyManagement
{
    /// <summary>
    /// Simplified multi-level momentum strategy manager that follows user preferences:
    /// 1. Simple code and strategy
    /// 2. Clear control of entry and exit logic
    /// 3. Does not abstract away logic to base managers
    /// 4. Violates SOLID principles intentionally for clarity and control
    /// </summary>
    public class MomentumMultiLevelStrategyManager : BaseStrategyManager
    {
        #region Core Properties

        public string Name { get; private set; }
        public StrategyParameters Parameters { get; private set; }
        public IPositionManager PositionManager { get; private set; }
        public ITradeManager TradeManager { get; private set; }

        #endregion

        #region Private Fields

        private readonly Queue<Bar> barHistory;
        private LevelManager levelManager;
        private int momentumPeriod;
        private double currentMomentum;
        private bool isMeanReverting;
        private int basePositionSize;
        private double stopLossPercent;
        private double takeProfitPercent;

        // Configuration
        private List<double> entryLevels;
        private List<double> exitLevels;
        private Instrument tradeInstrument;

        // Trading state
        private int currentPosition;
        private double averageEntryPrice;
        private DateTime lastTradeTime;

        #endregion

        #region Constructor

        public MomentumMultiLevelStrategyManager(Instrument tradeInstrument) : base("MultiLevel", tradeInstrument)
        {
            Name = "SimplifiedMomentumMultiLevel";
            this.tradeInstrument = tradeInstrument;
            barHistory = new Queue<Bar>();
            currentPosition = 0;
            averageEntryPrice = 0;
        }

        #endregion

        #region Initialization

        public void Initialize(StrategyParameters parameters)
        {
            Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));

            // Set default values
            momentumPeriod = 10;
            basePositionSize = (int)parameters.position_size;
            stopLossPercent = 0.02;
            takeProfitPercent = 0.05;
            isMeanReverting = false;

            // Parse configuration
            ParseConfiguration();

            // Initialize level manager
            levelManager = new LevelManager(entryLevels, exitLevels, isMeanReverting);
            levelManager.MaxConcurrentLevels = 10;

            Console.WriteLine($"Strategy {Name} initialized with {entryLevels.Count} entry levels and {exitLevels.Count} exit levels");
        }

        public void SetTradeManager(ITradeManager tradeManager)
        {
            TradeManager = tradeManager ?? throw new ArgumentNullException(nameof(tradeManager));
        }

        public void SetTradingMode(bool isPairMode, int tradeInstrumentId)
        {
            // Simplified - we don't need complex pair mode logic for this example
            Console.WriteLine($"Trading mode set: Pairs={isPairMode}, InstrumentId={tradeInstrumentId}");
        }

        public void SetInstrumentOrder(int[] instrumentOrder)
        {
            // Simplified - store for reference but don't complicate the logic
            Console.WriteLine($"Instrument order set: [{string.Join(", ", instrumentOrder)}]");
        }

        #endregion

        #region Main Processing - Clear Entry/Exit Logic

        public override void ProcessBar(Bar[] bars)
        {
            if (bars == null || bars.Length == 0) return;

            Bar currentBar = bars[0]; // Simple - use first bar

            // Update momentum
            UpdateMomentum(currentBar);

            if (barHistory.Count < momentumPeriod)
                return;

            // Clear, explicit entry/exit logic - no abstraction
            ProcessEntryDecisions(currentBar);
            ProcessExitDecisions(currentBar);

            // Cleanup
            levelManager.CleanupCompletedOrders();
        }

        /// <summary>
        /// Clear, explicit entry logic - user has full control
        /// </summary>
        private void ProcessEntryDecisions(Bar bar)
        {
            // Simple time checks
            if (!IsWithinTradingHours(bar.DateTime))
                return;

            // Check for long entries - explicit logic, no abstraction
            if (ShouldEnterLong(bar))
            {
                var longEntryLevels = levelManager.GetTriggeredEntryLevels(currentMomentum, OrderSide.Buy);
                foreach (var entryLevel in longEntryLevels)
                {
                    ExecuteEntryOrder(entryLevel, OrderSide.Buy, bar);
                }
            }

            // Check for short entries - explicit logic, no abstraction
            if (ShouldEnterShort(bar))
            {
                var shortEntryLevels = levelManager.GetTriggeredEntryLevels(currentMomentum, OrderSide.Sell);
                foreach (var entryLevel in shortEntryLevels)
                {
                    ExecuteEntryOrder(entryLevel, OrderSide.Sell, bar);
                }
            }
        }

        /// <summary>
        /// Clear, explicit exit logic - user has full control
        /// </summary>
        private void ProcessExitDecisions(Bar bar)
        {
            // Force exit at end of day - simple and clear
            if (ShouldForceExitAll(bar.DateTime))
            {
                ForceExitAllPositions(bar);
                return;
            }

            // Process level-based exits
            var triggeredExits = levelManager.GetAllTriggeredExitLevels(currentMomentum);
            foreach (var kvp in triggeredExits)
            {
                string levelId = kvp.Key;
                var exitLevelIndices = kvp.Value;

                foreach (var exitLevelIndex in exitLevelIndices)
                {
                    ExecuteExitOrder(levelId, exitLevelIndex, bar);
                }
            }
        }

        #endregion

        #region Simple Entry/Exit Decision Methods - No Abstraction

        /// <summary>
        /// Simple, clear long entry decision - user controls this logic
        /// </summary>
        private bool ShouldEnterLong(Bar bar)
        {
            // Simple momentum check
            bool momentumCondition = isMeanReverting ?
                currentMomentum <= -Math.Abs(entryLevels.Min()) :
                currentMomentum >= entryLevels.Min();

            // Simple position limit check
            bool positionCheck = levelManager.ActiveLevelCount < levelManager.MaxConcurrentLevels;

            // Simple time check
            bool timeCheck = IsWithinTradingHours(bar.DateTime);

            return momentumCondition && positionCheck && timeCheck;
        }

        /// <summary>
        /// Simple, clear short entry decision - user controls this logic
        /// </summary>
        private bool ShouldEnterShort(Bar bar)
        {
            // Simple momentum check
            bool momentumCondition = isMeanReverting ?
                currentMomentum >= Math.Abs(entryLevels.Min()) :
                currentMomentum <= -entryLevels.Min();

            // Simple position limit check
            bool positionCheck = levelManager.ActiveLevelCount < levelManager.MaxConcurrentLevels;

            // Simple time check
            bool timeCheck = IsWithinTradingHours(bar.DateTime);

            return momentumCondition && positionCheck && timeCheck;
        }

        /// <summary>
        /// Simple force exit check - clear and explicit
        /// </summary>
        private bool ShouldForceExitAll(DateTime currentTime)
        {
            // Simple end-of-day exit
            return currentTime.TimeOfDay >= Parameters.exit_time;
        }

        #endregion

        #region Order Execution - Simple and Direct

        /// <summary>
        /// Execute entry order - simple, direct, no abstraction
        /// </summary>
        private void ExecuteEntryOrder(double entryLevel, OrderSide side, Bar bar)
        {
            try
            {
                // Calculate position size - simple formula
                int positionSize = CalculateSimplePositionSize(entryLevel);

                // Create level
                var level = levelManager.CreateLevel(entryLevel, side, positionSize,
                                                   bar.Close, currentMomentum, bar.DateTime);

                // Place order if we have a trade manager
                if (TradeManager != null && !TradeManager.HasLiveOrder)
                {
                    int orderId = TradeManager.CreateOrder(side, positionSize, bar.Close, tradeInstrument);

                    if (orderId > 0)
                    {
                        level.AddOrder(orderId, LevelOrderType.Entry, positionSize, bar.Close);
                        Console.WriteLine($"Entry order: {side} {positionSize} @ {bar.Close:F4} (Level {entryLevel})");
                    }
                }

                // Update our simple position tracking
                UpdatePositionTracking(side, positionSize, bar.Close);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error executing entry order: {ex.Message}");
            }
        }

        /// <summary>
        /// Execute exit order - simple, direct, no abstraction
        /// </summary>
        private void ExecuteExitOrder(string levelId, int exitLevelIndex, Bar bar)
        {
            try
            {
                var level = levelManager.GetLevel(levelId);
                if (level == null) return;

                int exitSize = level.GetExitQuantityForLevel(exitLevelIndex);
                if (exitSize <= 0) return;

                OrderSide exitSide = level.Side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy;

                // Place order if we have a trade manager
                if (TradeManager != null && !TradeManager.HasLiveOrder)
                {
                    int orderId = TradeManager.CreateOrder(exitSide, exitSize, bar.Close, tradeInstrument);

                    if (orderId > 0)
                    {
                        level.AddOrder(orderId, LevelOrderType.Exit, exitSize, bar.Close, exitLevelIndex);
                        Console.WriteLine($"Exit order: {exitSide} {exitSize} @ {bar.Close:F4} (Level {levelId})");
                    }
                }

                // Execute the exit in level manager
                levelManager.ExecuteExit(levelId, exitLevelIndex, bar.Close, bar.DateTime);

                // Update our simple position tracking
                UpdatePositionTracking(exitSide, exitSize, bar.Close);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error executing exit order: {ex.Message}");
            }
        }

        /// <summary>
        /// Force exit all positions - simple and direct
        /// </summary>
        private void ForceExitAllPositions(Bar bar)
        {
            try
            {
                var levelsToClose = levelManager.ForceCloseAllLevels();

                foreach (var level in levelsToClose)
                {
                    if (level.CurrentPosition != 0)
                    {
                        OrderSide exitSide = level.Side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy;
                        int exitSize = Math.Abs(level.CurrentPosition);

                        if (TradeManager != null)
                        {
                            TradeManager.CreateOrder(exitSide, exitSize, bar.Close, tradeInstrument);
                        }

                        Console.WriteLine($"Force exit: {exitSide} {exitSize} @ {bar.Close:F4}");
                    }
                }

                // Reset position tracking
                currentPosition = 0;
                averageEntryPrice = 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in force exit: {ex.Message}");
            }
        }

        #endregion

        #region Simple Helper Methods

        private void ParseConfiguration()
        {
            // Simple parameter parsing - no complex abstraction
            if (Parameters.additional_params != null)
            {
                if (Parameters.additional_params.ContainsKey("entry_levels"))
                {
                    var entryObj = Parameters.additional_params["entry_levels"];
                    if (entryObj is List<object> entryList)
                        entryLevels = entryList.Select(x => Convert.ToDouble(x)).ToList();
                    else if (entryObj is double[] entryArray)
                        entryLevels = entryArray.ToList();
                }

                if (Parameters.additional_params.ContainsKey("exit_levels"))
                {
                    var exitObj = Parameters.additional_params["exit_levels"];
                    if (exitObj is List<object> exitList)
                        exitLevels = exitList.Select(x => Convert.ToDouble(x)).ToList();
                    else if (exitObj is double[] exitArray)
                        exitLevels = exitArray.ToList();
                }

                if (Parameters.additional_params.ContainsKey("momentum_period"))
                    momentumPeriod = Convert.ToInt32(Parameters.additional_params["momentum_period"]);

                if (Parameters.additional_params.ContainsKey("is_mean_reverting"))
                    isMeanReverting = Convert.ToBoolean(Parameters.additional_params["is_mean_reverting"]);
            }

            // Set defaults if not configured
            if (entryLevels == null || entryLevels.Count == 0)
                entryLevels = new List<double> { 0.5, 0.75, 1.0 };

            if (exitLevels == null || exitLevels.Count == 0)
                exitLevels = new List<double> { 0.5, 0.25 };
        }

        private void UpdateMomentum(Bar bar)
        {
            barHistory.Enqueue(bar);
            if (barHistory.Count > momentumPeriod)
                barHistory.Dequeue();

            if (barHistory.Count >= 2)
            {
                var oldestBar = barHistory.First();
                var newestBar = bar;
                currentMomentum = (newestBar.Close - oldestBar.Close) / oldestBar.Close;
            }
        }

        private int CalculateSimplePositionSize(double entryLevel)
        {
            // Simple position sizing - no complex abstraction
            double levelMultiplier = entryLevel / entryLevels.Max();
            return Math.Max(1, (int)(basePositionSize * levelMultiplier));
        }

        private void UpdatePositionTracking(OrderSide side, int quantity, double price)
        {
            // Simple position tracking
            if (side == OrderSide.Buy)
            {
                if (currentPosition <= 0)
                {
                    averageEntryPrice = price;
                    currentPosition = quantity;
                }
                else
                {
                    averageEntryPrice = (averageEntryPrice * currentPosition + price * quantity) / (currentPosition + quantity);
                    currentPosition += quantity;
                }
            }
            else // Sell
            {
                if (currentPosition >= 0)
                {
                    averageEntryPrice = price;
                    currentPosition = -quantity;
                }
                else
                {
                    averageEntryPrice = (averageEntryPrice * Math.Abs(currentPosition) + price * quantity) / (Math.Abs(currentPosition) + quantity);
                    currentPosition -= quantity;
                }
            }
        }

        private bool IsWithinTradingHours(DateTime currentTime)
        {
            if (Parameters == null) return true;
            var timeOfDay = currentTime.TimeOfDay;
            return timeOfDay >= Parameters.entry_time && timeOfDay <= Parameters.entry_allowedUntil;
        }

        #endregion

        #region Event Handlers - Simple and Direct

        public void OnFill(Fill fill)
        {
            // Simple fill handling
            Console.WriteLine($"Fill: {fill.Side} {fill.Qty} @ {fill.Price:F4}");
            lastTradeTime = fill.DateTime;
        }

        public void OnOrderEvent(Order order)
        {
            // Simple order event handling
            if (TradeManager != null)
                TradeManager.HandleOrderUpdate(order);
        }

        public void OnStrategyStart()
        {
            Console.WriteLine($"Strategy {Name} started - Simple and Clear Control");
        }

        public void OnStrategyStop()
        {
            Console.WriteLine($"Strategy {Name} stopped");
        }

        // Simple market data handlers
        public void OnBar(Bar[] bars) { }
        public void OnTrade(Trade trade) { }
        public void OnAsk(Ask ask) { }
        public void OnBid(Bid bid) { }

        #endregion

        #region Required Interface Methods - Minimal Implementation

        public void ProcessBar(Bar[] bars, double accountValue)
        {
            ProcessBar(bars); // Delegate to simpler version
        }

        public override bool ShouldEnterLongPosition(Bar[] bars)
        {
            return bars?.Length > 0 && ShouldEnterLong(bars[0]);
        }

        public override bool ShouldEnterShortPosition(Bar[] bars)
        {
            return bars?.Length > 0 && ShouldEnterShort(bars[0]);
        }

        public override bool ShouldExitLongPosition(Bar[] bars)
        {
            return currentPosition > 0 && (bars?.Length > 0 && ShouldForceExitAll(bars[0].DateTime));
        }

        public override bool ShouldExitShortPosition(Bar[] bars)
        {
            return currentPosition < 0 && (bars?.Length > 0 && ShouldForceExitAll(bars[0].DateTime));
        }

        #endregion

    }
}
