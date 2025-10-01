using System;
using System.Collections.Generic;
using System.Linq;
using SmartQuant;
using Parameters;
using System.Threading;
using System.Security.Cryptography;
using SmartQuant.Statistics;
using System.Security.Policy;

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
        private int lookbackPeriod;
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
        private readonly Queue<double> priceWindow;
        private double movingAverage;
        private double signal;
        private DateTime currentDate;

        private double minimumThreshold;
        private double maximumThreshold;
        private double lookBackPeriod;
        private double dailyMad;
        private double mad;
        private bool isStatisticsReady;

        private Dictionary<int, int> order2LevelId = new Dictionary<int, int>();

        #endregion

        #region Constructor

        public MomentumMultiLevelStrategyManager(Instrument tradeInstrument) : base("multilevel", tradeInstrument)
        {
            Name = "multilevel";
            this.tradeInstrument = tradeInstrument;
            barHistory = new Queue<Bar>();
            currentPosition = 0;
            averageEntryPrice = 0;
            priceWindow = new Queue<double>();
        }

        #endregion

        #region Initialization

        public override void Initialize(StrategyParameters parameters)
        {
            base.Initialize(parameters);
            // Parse configuration
            ParseConfiguration();

            // Set default values
            lookbackPeriod = 10;
            basePositionSize = (int)parameters.position_size;
            stopLossPercent = 0.02;
            takeProfitPercent = 0.05;
            isMeanReverting = false;

            // Initialize level manager
            levelManager = new LevelManager(entryLevels, exitLevels, isMeanReverting);
            levelManager.MaxConcurrentLevels = 10;

            Console.WriteLine($"Strategy {Name} initialized with {entryLevels.Count} entry levels and {exitLevels.Count} exit levels");
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

        public override void OnBar(Bar[] bars)
        {
            Bar signalBar = GetSignalBar(bars);

            priceWindow.Enqueue(signalBar.Close);

            if (priceWindow.Count > lookbackPeriod)
            {
                priceWindow.Dequeue();
            }

            if (priceWindow.Count >= lookbackPeriod)
            {
                CalculateStatistics();
                signal = (signalBar.Close / movingAverage) - 1.0;

                if (signalBar.CloseDateTime.Date != currentDate)
                {
                    dailyMad = mad;
                    currentDate = signalBar.CloseDateTime.Date;
                    mad = Math.Abs(signal);
                }
                else if (Math.Abs(signal) > mad)
                {
                    mad = Math.Abs(signal);
                }

                ProcessExitDecisions(signal, signalBar);

                ProcessEntryDecisions(signal, bars);
            }
        }

        public override void ProcessBar(Bar[] bars)
        {
        }

        /// <summary>
        /// Clear, explicit entry logic - user has full control
        /// </summary>
        private void ProcessEntryDecisions(double signal, Bar[] bars)
        {
            Bar bar = GetSignalBar(bars);
            // Simple time checks
            if (!IsWithinTradingHours(bar.DateTime))
                return;

            // Check for long entries - explicit logic, no abstraction
            // Simple position limit check
            bool positionCheck = levelManager.ActiveLevelCount < levelManager.MaxConcurrentLevels;

            // Simple time check
            bool timeCheck = IsWithinTradingHours(bar.DateTime);

            for(int i = 0; i< entryLevels.Count; i++)
            {
                double entryLevel = entryLevels[i];

                var level = levelManager.Levels[i];

                if (level == null)
                {
                    if (signal < -entryLevel * mad)
                    {
                        if (positionCheck && timeCheck)
                            ExecuteEntryOrder(i, entryLevel, OrderSide.Buy, bars);
                    }

                    // Check for short entries - explicit logic, no abstraction
                    if (signal > entryLevel * mad)
                    {
                        if (positionCheck && timeCheck)
                            ExecuteEntryOrder(i, entryLevel, OrderSide.Sell, bars);
                    }
                }
            }
        }

        /// <summary>
        /// Clear, explicit exit logic - user has full control
        /// </summary>
        private void ProcessExitDecisions(double signal, Bar bar)
        {
            // Force exit at end of day - simple and clear
            if (ShouldForceExitAll(bar.DateTime))
            {
                ForceExitAllPositions(bar);
                return;
            }

            // Process level-based exits
            var triggeredExits = levelManager.GetAllTriggeredExitLevels(signal);

            foreach (var kvp in triggeredExits)
            {
                int levelId = kvp.Key;
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
            return false;
        }

        /// <summary>
        /// Simple, clear short entry decision - user controls this logic
        /// </summary>
        private bool ShouldEnterShort(Bar bar)
        {
            return false;
        }

        /// <summary>
        /// Simple force exit check - clear and explicit
        /// </summary>
        private bool ShouldForceExitAll(DateTime currentTime)
        {
            return false;
        }

        #endregion

        #region Order Execution - Simple and Direct

        /// <summary>
        /// Execute entry order - simple, direct, no abstraction
        /// </summary>
        private void ExecuteEntryOrder(int levelIndex, double entryLevel, OrderSide side, Bar[] bars)
        {
            try
            {
                Bar bar = GetSignalBar(bars);

                // Place order if we have a trade manager
                if (base.TradeManager != null && !base.TradeManager.HasLiveOrder)
                {
                    double entryPrice = bar.Close;

                    var level = levelManager.CreateLevel(levelIndex, entryLevel, OrderSide.Buy, positionSize, entryPrice, currentMomentum, bar.DateTime);

                    int orderId = base.TradeManager.CreateOrder(side, positionSize, bar.Close, tradeInstrument);

                    order2LevelId[orderId] = level.Id;

                    // Execute theoretical entry
                    ExecuteTheoreticalEntry(bars, OrderSide.Buy);

                    if (orderId >= 0)
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
        private void ExecuteExitOrder(int levelId, int exitLevelIndex, Bar bar)
        {
            try
            {
                var level = levelManager.GetLevel(levelId);

                if (level == null) return;

                int exitSize = level.GetExitQuantityForLevel(exitLevelIndex);
                if (exitSize <= 0) return;

                OrderSide exitSide = level.Side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy;

                // Execute the exit in level manager
                levelManager.ExecuteExit(levelId, exitLevelIndex, bar.Close, bar.DateTime);

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

            var parameters = base.Parameters;

            if (parameters.additional_params != null)
            {
                if (parameters.additional_params.ContainsKey("entry_levels"))
                {
                    var entryObj = parameters.additional_params["entry_levels"];
                    if (entryObj is List<object> entryList)
                        entryLevels = entryList.Select(x => Convert.ToDouble(x)).ToList();
                    else if (entryObj is double[] entryArray)
                        entryLevels = entryArray.ToList();
                }

                if  (parameters.additional_params.ContainsKey("exit_levels"))
                {
                    var exitObj = parameters.additional_params["exit_levels"];
                    if (exitObj is List<object> exitList)
                        exitLevels = exitList.Select(x => Convert.ToDouble(x)).ToList();
                    else if (exitObj is double[] exitArray)
                        exitLevels = exitArray.ToList();
                }

                if (parameters.additional_params.ContainsKey("momentum_period"))
                    lookbackPeriod = Convert.ToInt32(parameters.additional_params["momentum_period"]);

                if (parameters.additional_params.ContainsKey("is_mean_reverting"))
                    isMeanReverting = Convert.ToBoolean(parameters.additional_params["is_mean_reverting"]);
            }

            // Set defaults if not configured
            if (entryLevels == null || entryLevels.Count == 0)
                entryLevels = new List<double> { 0.5, 0.75, 1.0 };

            if (exitLevels == null || exitLevels.Count == 0)
                exitLevels = new List<double> { 0.5, 0.25 };
        }

        private void CalculateStatistics()
        {
            if (priceWindow.Count == 0) return;

            double sum = 0;
            foreach (var price in priceWindow)
                sum += price;
            movingAverage = sum / priceWindow.Count;

            //double sumSquaredDeviations = 0;
            //foreach (var price in priceWindow)
            //    sumSquaredDeviations += Math.Pow(price - movingAverage, 2);
            //standardDeviation = Math.Sqrt(sumSquaredDeviations / priceWindow.Count);
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
