using System;
using System.Collections.Generic;
using System.Linq;
using SmartQuant;
using Parameters;
using SmartQuant.Statistics;
using System.Security.Cryptography;
using SmartQuant.Component;
using SmartQuant.Strategy_;
using System.Diagnostics;
using System.Security.Policy;

namespace StrategyManagement
{
    /// <summary>
    /// Multi-level momentum strategy manager with sophisticated entry/exit level management
    /// Each entry level has multiple exit levels managed independently
    /// UPDATED: Now properly uses BaseStrategyManager functions for pair trading
    /// </summary>
    public class MomentumMultiLevelStrategyManager : BaseStrategyManager
    {
        #region Private Fields

        private readonly Queue<Bar> barHistory;
        private readonly Queue<double> priceWindow;

        private int momentumPeriod;
        private double currentMomentum;


        private double stopLossPercent;
        private double takeProfitPercent;

        // Configuration from additional_params
        private List<double> entryLevels;
        private int lookBackPeriod;
        private double minimumThreshold;
        private double maximumThreshold;
        private int positionSize;
        private double movingAverage;
        private bool isStatisticsReady;
        
        private DateTime currentDate;
        private double mad;
        private double dailyMad;
        private double signal;
        private Dictionary<int, Level> LevelDict;
        private List<double> exitLevels;
        private List<Level> longLevelList;
        private List<Level> shortLevelList;

        private int orderNumber;
        private int currentLevel;
        #endregion

        #region Constructor

        public MomentumMultiLevelStrategyManager(Instrument tradeInstrument) : base("MomentumMultiLevel", tradeInstrument)
        {
            barHistory = new Queue<Bar>();
            LevelDict = new Dictionary<int, Level>();
            orderNumber = 0;
        }

        #endregion

        #region Initialization

        public override void Initialize(StrategyParameters parameters)
        {
            base.Initialize(parameters);

            // Default values
            momentumPeriod = 10;
            positionSize = 1;
            stopLossPercent = 0.02;
            takeProfitPercent = 0.05;


            // Parse additional parameters
            ParseAdditionalParameters();

            longLevelList = new List<Level>();
            shortLevelList = new List<Level>();

            for (int i = 0; i < entryLevels.Count; i++)
            {
                
                for (int j = 0; j < exitLevels.Count; j++)
                {
                    string iString = i.ToString();
                    string jString = j.ToString();
                    longLevelList.Add(new Level(iString + "." + jString + "." + "B", OrderSide.Buy, positionSize, entryLevels[i], exitLevels[j]));
                    shortLevelList.Add(new Level(iString + "." + jString + "." + "S", OrderSide.Sell, positionSize, entryLevels[i], exitLevels[j]));
                }
            }

            // NEW: Enhanced logging
            Console.WriteLine($"  MomentumMultiLevel {Name} initialized:");
            Console.WriteLine($"  Signal Source: {GetSignalSourceDescription()}");
            Console.WriteLine($"  Execution Instrument: {GetExecutionInstrumentDescription()}");
            Console.WriteLine($"  Entry Levels: [{string.Join(", ", entryLevels)}]");
            Console.WriteLine($"  Exit Levels: [{string.Join(", ", exitLevels)}]");
            Console.WriteLine($"  Momentum Period: {momentumPeriod}");
            Console.WriteLine($"  Base Position Size: {positionSize}");



            // NEW: Validate configuration
            ValidateSignalExecutionConfiguration();
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

            if (Parameters.additional_params.ContainsKey("look_back_period"))
                lookBackPeriod = Convert.ToInt32(Parameters.additional_params["look_back_period"]);

            if (Parameters.additional_params.ContainsKey("minimumThreshold"))
                minimumThreshold = (double)Parameters.additional_params["minimumThreshold"];

            if (Parameters.additional_params.ContainsKey("maximumThreshold"))
                maximumThreshold = (double)Parameters.additional_params["maximumThreshold"];
            // Parse other parameters
            if (Parameters.additional_params.ContainsKey("momentum_period"))
                momentumPeriod = Convert.ToInt32(Parameters.additional_params["momentum_period"]);

            if (Parameters.additional_params.ContainsKey("position_size"))
                positionSize = Convert.ToInt32(Parameters.additional_params["position_size"]);

            if (Parameters.additional_params.ContainsKey("stop_loss_percent"))
                stopLossPercent = Convert.ToDouble(Parameters.additional_params["stop_loss_percent"]);

            if (Parameters.additional_params.ContainsKey("take_profit_percent"))
                takeProfitPercent = Convert.ToDouble(Parameters.additional_params["take_profit_percent"]);

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

        // UPDATED: Now uses Bar[] array and base manager functions
        public override void ProcessBar(Bar[] bars, double accountValue)
        {
            Bar signalBar = GetSignalBar(bars);
            CancelCurrentOrder();
            int currentTheoPosition = GetCurrentTheoPosition();

            // Update momentum calculation using signal bar
            UpdateMomentum(signalBar);

            if (barHistory.Count < momentumPeriod)
                return;

            // CRITICAL FIX: Check forced exit time FIRST
            if (currentTheoPosition != 0 && ShouldExitAllPositions(signalBar.DateTime) && !HasLiveOrder())
            {
                Console.WriteLine($"FORCED EXIT at {signalBar.DateTime}: Closing all levels");
                ForceExitAllLevels(bars);
                return;
            }

            // Check for exit levels
            ProcessExitLevels(bars, accountValue);

            // Check for new entry levels
            ProcessEntryLevels(bars, accountValue);

        }

        // UPDATED: Now uses Bar[] array and base manager functions
        private void ProcessEntryLevels(Bar[] bars, double accountValue)
        {
            Bar signalBar = GetSignalBar(bars);

            if (!IsWithinTradingHours(signalBar.DateTime) || !CanEnterNewPosition(signalBar.DateTime))
                return;

            foreach (var entryLevel in longLevelList)
            {
                // int positionSize = CalculatePositionSizeForLevel(bars, accountValue, entryLevel);
                double entryPrice = entryLevel.EntryPrice;

                if (entryLevel.TheoEntryOrder.LiveTransit[0] == 0 && entryLevel.TheoEntryOrder.LiveTransit[1] == 0 && entryLevel.NetTheoPosition == 0)
                {
                    if (signal <= Math.Min(dailyMad * entryLevel.EntryPrice, -minimumThreshold) && signal > -maximumThreshold )
                    {
                        // UPDATED: Use base manager function for theoretical entry
                        ExecuteTheoreticalEntry(bars, OrderSide.Buy, accountValue);

                        entryLevel.TheoEntryOrder = new LevelOrder(orderNumber, positionSize, signalBar.Close, OrderSide.Buy, true);
                        entryLevel.TheoEntryOrder.LiveTransit[0] = 0;
                        entryLevel.TheoEntryOrder.LiveTransit[1] = 0;
                        entryLevel.NetTheoPosition = positionSize;
                        orderNumber++;

                        Console.WriteLine($"Multi-level entry: Level {entryLevel} - {OrderSide.Buy} {positionSize} @ {entryPrice:F4} " +
                                        $"(Signal: {currentMomentum:F4}, Momentum: {GetSignalSourceDescription()})");

                        // Place actual order if TradeManager is available
                        if (TradeManager != null)
                        {
                            if (entryLevel.NetPosition == 0)
                            {
                                int orderId = TradeManager.CreateOrder(OrderSide.Buy, positionSize, signalBar.Close, TradeInstrument);
                                entryLevel.ActualEntryOrder = new LevelOrder(orderId, positionSize, signalBar.Close, OrderSide.Buy, true);
                                LevelDict[orderId] = entryLevel;
                            }
                        }
                    }
                }
            }

            // Check for short entries

            foreach (var entryLevel in shortLevelList)
            {
                double entryPrice = entryLevel.EntryPrice;

                if (entryLevel.TheoEntryOrder.LiveTransit[0] == 0 && entryLevel.TheoEntryOrder.LiveTransit[1] == 0 && entryLevel.NetPosition == 0)
                {
                    if (signal >= Math.Max(dailyMad * entryLevel.EntryPrice, minimumThreshold) && signal < maximumThreshold)
                    {
                        // UPDATED: Use base manager function for theoretical entry
                        ExecuteTheoreticalEntry(bars, OrderSide.Sell, accountValue);

                        entryLevel.TheoEntryOrder = new LevelOrder(orderNumber, positionSize, signalBar.Close, OrderSide.Sell, true);
                        entryLevel.TheoEntryOrder.LiveTransit[0] = 0;
                        entryLevel.TheoEntryOrder.LiveTransit[1] = 0;
                        entryLevel.NetTheoPosition = -positionSize;
                        orderNumber++;

                        Console.WriteLine($"Multi-level entry: Level {entryLevel} - {OrderSide.Sell} {positionSize} @ {entryPrice:F4} " +
                                        $"(Signal: {currentMomentum:F4}, Momentum: {GetSignalSourceDescription()})");

                        // Place actual order if TradeManager is available
                        if (TradeManager != null)
                        {
                            if (entryLevel.NetPosition == 0)
                            {
                                int orderId = TradeManager.CreateOrder(OrderSide.Sell, positionSize, signalBar.Close, TradeInstrument);
                                entryLevel.ActualEntryOrder = new LevelOrder(orderId, positionSize, signalBar.Close, OrderSide.Sell, true);
                                LevelDict[orderId] = entryLevel;
                            }
                        }
                    }
                }
            }
        }

        // UPDATED: Now uses Bar[] array and base manager functions
        private void ProcessExitLevels(Bar[] bars, double accountValue)
        {
            Bar signalBar = GetSignalBar(bars);

            if (ShouldExitAllPositions(signalBar.DateTime))
            {
                ForceExitAllLevels(bars);
                return;
            }

            foreach (var exitLevel in longLevelList)
            {
                // int positionSize = CalculatePositionSizeForLevel(bars, accountValue, entryLevel);
                if (exitLevel.TheoExitOrder.LiveTransit[0] == 0 && exitLevel.TheoExitOrder.LiveTransit[1] == 0 && exitLevel.NetTheoPosition > 0)
                {
                    if (signal >= exitLevel.EntryPrice * exitLevel.ExitPrice)
                    {
                        // UPDATED: Use base manager function for theoretical entry
                        ExecuteTheoreticalExit(bars, (int)exitLevel.NetPosition);

                        exitLevel.TheoExitOrder = new LevelOrder(orderNumber, positionSize, signalBar.Close, OrderSide.Sell, true);
                        exitLevel.TheoEntryOrder.LiveTransit[0] = 0;
                        exitLevel.TheoEntryOrder.LiveTransit[1] = 0;
                        exitLevel.NetTheoPosition = 0;
                        orderNumber++;

                        Console.WriteLine($"Multi-level exit: Level {exitLevel} - {OrderSide.Sell} {positionSize} @ {exitLevel.ExitPrice:F4} " +
                                        $"(Signal: {currentMomentum:F4}, Momentum: {GetSignalSourceDescription()})");

                        // Place actual order if TradeManager is available
                        if (TradeManager != null)
                        {
                            if (exitLevel.NetPosition > 0)
                            {
                                int orderId = TradeManager.CreateOrder(OrderSide.Sell, positionSize, signalBar.Close, TradeInstrument);
                                exitLevel.ActualExitOrder = new LevelOrder(orderId, positionSize, signalBar.Close, OrderSide.Sell, false);
                                LevelDict[orderId] = exitLevel;
                            }
                        }
                    }
                }
            }

            // Check for short entries

            foreach (var exitLevel in shortLevelList)
            { 
                // int positionSize = CalculatePositionSizeForLevel(bars, accountValue, entryLevel);
                if (exitLevel.TheoExitOrder.LiveTransit[0] == 0 && exitLevel.TheoExitOrder.LiveTransit[1] == 0 && exitLevel.NetTheoPosition < 0)
                {
                    if (signal >= exitLevel.EntryPrice * exitLevel.ExitPrice)
                    {
                        // UPDATED: Use base manager function for theoretical entry
                        ExecuteTheoreticalExit(bars, (int)exitLevel.NetPosition);

                        exitLevel.TheoExitOrder = new LevelOrder(orderNumber, positionSize, signalBar.Close, OrderSide.Buy, true);
                        exitLevel.TheoEntryOrder.LiveTransit[0] = 0;
                        exitLevel.TheoEntryOrder.LiveTransit[1] = 0;
                        exitLevel.NetTheoPosition = 0;
                        orderNumber++;

                        Console.WriteLine($"Multi-level exit: Level {exitLevel} - {OrderSide.Buy} {positionSize} @ {exitLevel.ExitPrice:F4} " +
                                        $"(Signal: {currentMomentum:F4}, Momentum: {GetSignalSourceDescription()})");

                        // Place actual order if TradeManager is available
                        if (TradeManager != null)
                        {
                            if (exitLevel.NetPosition < 0)
                            {
                                int orderId = TradeManager.CreateOrder(OrderSide.Buy, positionSize, signalBar.Close, TradeInstrument);
                                exitLevel.ActualExitOrder = new LevelOrder(orderId, positionSize, signalBar.Close, OrderSide.Buy, false);
                                LevelDict[orderId] = exitLevel;
                            }
                        }
                    }
                }
            }
        }

        #endregion

        #region Order Management



        // UPDATED: Now uses Bar[] array
        private void PlaceEntryOrder(Level level, Bar[] bars)
        {
            // Implementation would depend on your TradeManager interface
            // This is a placeholder for the actual order placement logic

            // Example:
            // int orderId = TradeManager.PlaceOrder(level.Side, level.PositionSize, level.EntryPrice);
            // level.AddOrder(orderId, LevelOrderType.Entry, level.PositionSize, level.EntryPrice);
        }

        // UPDATED: Now uses Bar[] array
        private void PlaceExitOrder(Level level, int exitLevelIndex, int exitSize, Bar[] bars)
        {
            // Implementation would depend on your TradeManager interface
            // This is a placeholder for the actual order placement logic

            //OrderSide exitSide = level.ExitOrder.Side;
            //double exitPrice = GetExitPrice(bars, exitSide);

            // Example:
            // int orderId = TradeManager.PlaceOrder(exitSide, exitSize, exitPrice);
            // level.AddOrder(orderId, LevelOrderType.Exit, exitSize, exitPrice, exitLevelIndex);
        }

        // UPDATED: Now uses Bar[] array and base manager functions
        private void ForceExitAllLevels(Bar[] bars)
        {
            //var levelsToClose = levelManager.ForceCloseAllLevels();

            //foreach (var level in levelsToClose)
            //{
            //    if (level.CurrentPosition != 0)
            //    {
            //        // UPDATED: Use base manager function for theoretical exit
            //        int currentPosition = GetCurrentTheoPosition();
            //        if (currentPosition != 0)
            //        {
            //            ExecuteTheoreticalExit(bars, currentPosition);
            //        }

            //        Console.WriteLine($"Force exit level {level.Id}: Position {level.CurrentPosition}");
            //    }
            //}
        }

        #endregion

        #region Market Data Handlers

        // UPDATED: Now uses Bar[] array and GetSignalBar
        public override void OnBar(Bar[] bars)
        {
            Bar signalBar = GetSignalBar(bars);

            signal = (signalBar.Close / movingAverage) - 1.0;

            if (signalBar.CloseDateTime.Date != currentDate)
            {
                dailyMad = mad;
                currentDate = signalBar.CloseDateTime.Date;
                mad = Math.Abs(signal);

                isStatisticsReady = true;
            }
            else if (Math.Abs(signal) > mad)
            {
                mad = Math.Abs(signal);
            }

            // Update statistics
            priceWindow.Enqueue(signalBar.Close);

            if (priceWindow.Count > lookBackPeriod)
            {
                priceWindow.Dequeue();
            }

            if (priceWindow.Count >= lookBackPeriod)
            {
                CalculateStatistics();
                isStatisticsReady = true;
            }

            // Update metrics using execution instrument bar
            Bar executionBar = GetExecutionInstrumentBar(bars);
            DualPositionManager?.TheoPositionManager?.UpdateTradeMetric(executionBar);
            DualPositionManager?.ActualPositionManager?.UpdateTradeMetric(executionBar);
        }

        public override void OnTrade(Trade trade) { }
        public override void OnAsk(Ask ask) { }
        public override void OnBid(Bid bid) { }

        #endregion

        #region Decision Methods (Required by BaseStrategyManager)

        // UPDATED: Now uses Bar[] array
        public override bool ShouldEnterLongPosition(Bar[] bars)
        {
            // This method is required by BaseStrategyManager but not used in multi-level approach
            // The multi-level logic is handled in ProcessEntryLevels
            return false;
        }

        // UPDATED: Now uses Bar[] array
        public override bool ShouldEnterShortPosition(Bar[] bars)
        {
            // This method is required by BaseStrategyManager but not used in multi-level approach
            // The multi-level logic is handled in ProcessEntryLevels
            return false;
        }

        // UPDATED: Now uses Bar[] array
        public override bool ShouldExitLongPosition(Bar[] bars)
        {
            // This method is required by BaseStrategyManager but not used in multi-level approach
            // The multi-level logic is handled in ProcessExitLevels
            return false;
        }

        // UPDATED: Now uses Bar[] array
        public override bool ShouldExitShortPosition(Bar[] bars)
        {
            // This method is required by BaseStrategyManager but not used in multi-level approach
            // The multi-level logic is handled in ProcessExitLevels
            return false;
        }

        // UPDATED: Now uses Bar[] array
        public override int CalculatePositionSize(Bar[] bars, double accountValue)
        {
            // This is used by the base class, return base position size
            return positionSize;
        }

        // UPDATED: Now uses Bar[] array and GetExecutionInstrumentBar
        public override double GetEntryPrice(Bar[] bars, OrderSide side)
        {
            // Use the EXECUTION instrument bar for pricing, not the signal bar
            Bar executionBar = GetExecutionInstrumentBar(bars);
            return executionBar.Close;
        }

        // UPDATED: Now uses Bar[] array and GetExecutionInstrumentBar
        public override double GetExitPrice(Bar[] bars, OrderSide side)
        {
            // Use the EXECUTION instrument bar for pricing
            Bar executionBar = GetExecutionInstrumentBar(bars);
            return executionBar.Close;
        }

        #endregion

        #region Helper Methods

        private void UpdateMomentum(Bar signalBar)
        {
            if (barHistory.Count < 2)
            {
                currentMomentum = 0;
                return;
            }

            var oldestBar = barHistory.First();
            var newestBar = signalBar;

            currentMomentum = (newestBar.Close - oldestBar.Close) / oldestBar.Close;
        }

        // UPDATED: Now uses Bar[] array
        private int CalculatePositionSizeForLevel(Bar[] bars, double accountValue, double entryLevel)
        {
            // Scale position size based on entry level strength
            double momentumStrength = Math.Abs(currentMomentum);
            double levelMultiplier = entryLevel / entryLevels.Max();

            return Math.Max(1, (int)(positionSize * levelMultiplier));
        }

        public override void OnFill(Fill fill)
        {
            base.OnFill(fill);

            // Update level manager with fill information
            if (LevelDict != null)
            {
                Level level;
                if(LevelDict.TryGetValue(fill.Order.Id, out level))
                {
                    if(level.ActualEntryOrder.Id == fill.Order.Id)
                    {
                        if (fill.Side == OrderSide.Buy)
                        {
                            level.NetPosition += fill.Qty;
                        }
                        else
                        {
                            level.NetPosition -= fill.Qty;
                        }
                    }

                    if (level.ActualExitOrder.Id == fill.Order.Id)
                    {
                        if (fill.Side == OrderSide.Buy)
                        {
                            level.NetPosition += fill.Qty;
                        }
                        else
                        {
                            level.NetPosition -= fill.Qty;
                        }
                    }
                }
            }
        }

        public override void OnOrderEvent(Order order)
        {
            base.OnOrderEvent(order);

            // Update level manager with order status
            if (LevelDict != null)
            {
                Level level;
                if (LevelDict.TryGetValue(order.Id, out level))
                {
                    if (level.ActualEntryOrder.Id == order.Id)
                    {
                        switch (order.Status)
                        {
                            case OrderStatus.New:
                            case OrderStatus.Replaced:
                                level.ActualEntryOrder.LiveTransit[0] = 1;
                                level.ActualEntryOrder.LiveTransit[1] = 0;
                                break;

                            case OrderStatus.Filled:
                            case OrderStatus.Cancelled:
                                level.ActualEntryOrder.LiveTransit[0] = 0;
                                level.ActualEntryOrder.LiveTransit[1] = 0;
                                level.ActualEntryOrder = null;
                                break;

                            case OrderStatus.Rejected:
                                level.ActualEntryOrder.LiveTransit[0] = 0;
                                level.ActualEntryOrder.LiveTransit[1] = 0;
                                Console.WriteLine($"Order rejected: {order.Id} - {order.Text}");
                                break;
                        }
                    }

                    if (level.ActualExitOrder.Id == order.Id)
                    {
                        switch (order.Status)
                        {
                            case OrderStatus.New:
                            case OrderStatus.Replaced:
                                level.ActualExitOrder.LiveTransit[0] = 1;
                                level.ActualExitOrder.LiveTransit[1] = 0;
                                break;

                            case OrderStatus.Filled:
                            case OrderStatus.Cancelled:
                                level.ActualExitOrder.LiveTransit[0] = 0;
                                level.ActualExitOrder.LiveTransit[1] = 0;
                                break;

                            case OrderStatus.Rejected:
                                level.ActualExitOrder.LiveTransit[0] = 0;
                                level.ActualExitOrder.LiveTransit[1] = 0;
                                Console.WriteLine($"Order rejected: {order.Id} - {order.Text}");
                                break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Get current level manager statistics for monitoring
        /// </summary>
        //public LevelManagerStats GetLevelStats()
        //{
        //    return levelManager?.GetStats() ?? new LevelManagerStats();
        //}

        /// <summary>
        /// Get total unrealized PnL across all levels
        /// </summary>
        //public double GetTotalUnrealizedPnL(double currentPrice)
        //{
        //    return levelManager?.CalculateTotalUnrealizedPnL(currentPrice) ?? 0;
        //}

        private void CalculateStatistics()
        {
            double sum = 0;
            foreach (var price in priceWindow)
                sum += price;
            movingAverage = sum / priceWindow.Count;

            //double sumSquaredDeviations = 0;
            //foreach (var price in priceWindow)
            //    sumSquaredDeviations += Math.Pow(price - movingAverage, 2);
            //standardDeviation = Math.Sqrt(sumSquaredDeviations / priceWindow.Count);
        }

        #endregion
    }
}

