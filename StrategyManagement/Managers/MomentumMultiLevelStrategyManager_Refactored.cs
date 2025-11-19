using System;
using System.Collections.Generic;
using System.Linq;
using SmartQuant;
using Parameters;
using System.Threading;
using System.Security.Cryptography;
using SmartQuant.Statistics;
using System.Security.Policy;
using System.IO;

namespace StrategyManagement
{
    /// <summary>
    /// REFACTORED: Multi-level momentum strategy manager
    /// All decision logic consolidated in OnBar for debugging
    /// All try-catch removed for manual logic checking
    /// Comprehensive logging for every order and trade event
    /// </summary>
    public class MomentumMultiLevelStrategyManager : BaseStrategyManager
    {
        #region Logging
        
        private StreamWriter logWriter;
        private StreamWriter orderLogWriter;
        private StreamWriter tradeLogWriter;
        private StreamWriter levelLogWriter;
        private string logDirectory = "C:\\tmp\\Template\\debug_logs\\";
        private int barCounter = 0;
        
        #endregion

        #region Core Properties

        public string Name { get; private set; }
        public StrategyParameters Parameters { get; private set; }
        public IPositionManager PositionManager { get; private set; }
        public ITradeManager TradeManager { get; private set; }

        #endregion

        #region Private Fields

        private LevelManager levelManager;
        private int lookbackPeriod;
        private double currentMomentum;
        private bool isMeanReverting;

        // Configuration
        private List<double> entryLevels;
        private List<double> exitLevels;
        private Instrument tradeInstrument;

        // Trading state
        private int currentPosition;
        private double averageEntryPrice;
        private readonly Queue<double> priceWindow;
        private double movingAverage;
        private double signal;
        private DateTime currentDate;

        private double dailyMad;
        private double mad;
        private int positionSize = 3; // Default position size per level (allows 1 unit per exit level)

        private Dictionary<int, int> order2LevelId = new Dictionary<int, int>();
        private List<Level> levelList;

        /// <summary>
        /// Dictionary of active levels, keyed by level ID
        /// </summary>
        public Dictionary<int, Level> ActiveLevels { get; private set; }

        #endregion

        #region Constructor

        public MomentumMultiLevelStrategyManager(Instrument tradeInstrument) : base("multilevel", tradeInstrument)
        {
            Name = "multilevel";
            this.tradeInstrument = tradeInstrument;
            currentPosition = 0;
            averageEntryPrice = 0;
            priceWindow = new Queue<double>();
            order2LevelId = new Dictionary<int, int>();
            levelList = new List<Level>();
        }

        #endregion

        #region Initialization

        public override void Initialize(StrategyParameters parameters)
        {
            base.Initialize(parameters);
            
            // Create log directory
            Directory.CreateDirectory(logDirectory);

            // Initialize log files
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            logWriter = new StreamWriter(Path.Combine(logDirectory, $"multilevel_calculation_log_{timestamp}.txt"), false);
            orderLogWriter = new StreamWriter(Path.Combine(logDirectory, $"multilevel_order_log_{timestamp}.csv"), false);
            tradeLogWriter = new StreamWriter(Path.Combine(logDirectory, $"multilevel_trade_log_{timestamp}.csv"), false);
            levelLogWriter = new StreamWriter(Path.Combine(logDirectory, $"multilevel_level_log_{timestamp}.csv"), false);

            // Write headers
            WriteLogLine("=== MomentumMultiLevelStrategyManager Refactored - Calculation Log ===");
            WriteLogLine($"Initialized at: {DateTime.Now}");
            WriteLogLine("");

            orderLogWriter.WriteLine("BarNum,Timestamp,Event,LevelId,LevelIndex,Side,Price,Quantity,Signal,MAD,EntryThreshold,ExitThreshold,Position,ActiveLevels");
            orderLogWriter.Flush();

            tradeLogWriter.WriteLine("LevelId,LevelIndex,EntryTime,ExitTime,Side,EntryPrice,ExitPrice,Quantity,PnL,Signal,EntryLevel,ExitLevel,HoldBars");
            tradeLogWriter.Flush();

            levelLogWriter.WriteLine("BarNum,Timestamp,LevelId,LevelIndex,Status,Side,EntryPrice,CurrentPosition,RemainingQty,Signal,PnL");
            levelLogWriter.Flush();

            // Parse configuration
            ParseConfiguration();

            // Set default values
            lookbackPeriod = 10;
            isMeanReverting = false;
            base.positionSize = 3; // Set base class position size to match

            // Initialize level manager
            levelManager = new LevelManager(entryLevels, exitLevels, isMeanReverting);
            levelManager.MaxConcurrentLevels = 3;

            WriteLogLine($"Strategy {Name} initialized:");
            WriteLogLine($"  Entry Levels: [{string.Join(", ", entryLevels)}]");
            WriteLogLine($"  Exit Levels: [{string.Join(", ", exitLevels)}]");
            WriteLogLine($"  Lookback Period: {lookbackPeriod}");
            WriteLogLine($"  Position Size per Level: {positionSize}");
            WriteLogLine($"  Max Concurrent Levels: {levelManager.MaxConcurrentLevels}");
            WriteLogLine($"  Is Mean Reverting: {isMeanReverting}");
            WriteLogLine("");
        }

        public void SetTradingMode(bool isPairMode, int tradeInstrumentId)
        {
            WriteLogLine($"Trading mode set: Pairs={isPairMode}, InstrumentId={tradeInstrumentId}");
        }

        public void SetInstrumentOrder(int[] instrumentOrder)
        {
            WriteLogLine($"Instrument order set: [{string.Join(", ", instrumentOrder)}]");
        }

        #endregion

        #region Main OnBar - ALL DECISION LOGIC HERE

        public override void OnBar(Bar[] bars)
        {
            barCounter++;
            Bar signalBar = GetSignalBar(bars);

            WriteLogLine($"========== BAR {barCounter} - {signalBar.CloseDateTime:yyyy-MM-dd HH:mm:ss} ==========");
            WriteLogLine($"Signal Bar: O={signalBar.Open:F4} H={signalBar.High:F4} L={signalBar.Low:F4} C={signalBar.Close:F4}");

            // ==================== STEP 1: UPDATE PRICE WINDOW AND SIGNAL ====================
            WriteLogLine("");
            WriteLogLine("--- STEP 1: Update Price Window and Signal ---");
            
            priceWindow.Enqueue(signalBar.Close);
            WriteLogLine($"Price added to window: {signalBar.Close:F4}");

            if (priceWindow.Count > lookbackPeriod)
            {
                double removed = priceWindow.Dequeue();
                WriteLogLine($"Price removed from window: {removed:F4}");
            }

            WriteLogLine($"Price window: Count={priceWindow.Count}, Required={lookbackPeriod}");

            if (priceWindow.Count < lookbackPeriod)
            {
                WriteLogLine("Window not full yet - skipping bar");
                WriteLogLine("");
                WriteLogLine($"========== END BAR {barCounter} ==========");
                WriteLogLine("");
                return;
            }

            // ==================== STEP 2: CALCULATE STATISTICS ====================
            WriteLogLine("");
            WriteLogLine("--- STEP 2: Calculate Statistics ---");
            
            CalculateStatistics();
            WriteLogLine($"Moving Average: {movingAverage:F6}");
            
            double old_signal = signal;
            signal = (signalBar.Close / movingAverage) - 1.0;
            WriteLogLine($"Signal: ({signalBar.Close:F4} / {movingAverage:F6}) - 1.0 = {signal:F6} (was {old_signal:F6})");

            // ==================== STEP 3: UPDATE MAD ====================
            WriteLogLine("");
            WriteLogLine("--- STEP 3: Update MAD (Mean Absolute Deviation) ---");
            
            if (signalBar.CloseDateTime.Date != currentDate)
            {
                dailyMad = mad;
                currentDate = signalBar.CloseDateTime.Date;
                mad = Math.Abs(signal);
                WriteLogLine($"New day detected:");
                WriteLogLine($"  Previous dailyMad: {dailyMad:F6}");
                WriteLogLine($"  New MAD: {mad:F6}");
            }
            else if (Math.Abs(signal) > mad)
            {
                double old_mad = mad;
                mad = Math.Abs(signal);
                WriteLogLine($"MAD updated: {old_mad:F6} -> {mad:F6}");
            }
            else
            {
                WriteLogLine($"MAD unchanged: {mad:F6}");
            }

            WriteLogLine($"Current MAD: {mad:F6}, Daily MAD: {dailyMad:F6}");

            // ==================== STEP 4: DISPLAY CURRENT STATE ====================
            WriteLogLine("");
            WriteLogLine("--- STEP 4: Current State ---");
            WriteLogLine($"Current Position: {currentPosition}");
            WriteLogLine($"Average Entry Price: {averageEntryPrice:F4}");
            WriteLogLine($"Active Levels: {levelManager.ActiveLevelCount} / {levelManager.MaxConcurrentLevels}");
            
            if (levelManager.ActiveLevelCount > 0)
            {
                WriteLogLine("Active Level Details:");
                foreach (var kvp in levelManager.ActiveLevels)
                {
                    var level = kvp.Value;
                    int levelIndex = levelManager.ActiveLevels2Levels.ContainsKey(level.Id) ? levelManager.ActiveLevels2Levels[level.Id] : -1;
                    WriteLogLine($"  Level {level.Id} (Index {levelIndex}): {level.Side}, Pos={level.CurrentPosition}, Entry={level.EntryPrice:F4}");
                }
            }

            // ==================== STEP 5: CHECK FORCE EXIT CONDITIONS ====================
            WriteLogLine("");
            WriteLogLine("--- STEP 5: Check Force Exit Conditions ---");
            
            bool shouldForceExit = CheckForceExitConditions(signalBar.DateTime);
            
            if (shouldForceExit)
            {
                WriteLogLine("FORCE EXIT TRIGGERED - Exiting all positions");
                ExecuteForceExitAll(signalBar);
                LogLevelStates(signalBar);
                WriteLogLine("");
                WriteLogLine($"========== END BAR {barCounter} ==========");
                WriteLogLine("");
                return;
            }
            else
            {
                WriteLogLine("No force exit required");
            }

            // ==================== STEP 6: CHECK EXIT CONDITIONS FOR EACH LEVEL ====================
            WriteLogLine("");
            WriteLogLine("--- STEP 6: Check Exit Conditions for Active Levels ---");
            
            if (levelManager.ActiveLevelCount == 0)
            {
                WriteLogLine("No active levels to check for exits");
            }
            else
            {
                var triggeredExits = levelManager.GetAllTriggeredExitLevels(signal);
                WriteLogLine($"Triggered exits found: {triggeredExits.Count} levels");

                foreach (var kvp in triggeredExits)
                {
                    int levelId = kvp.Key;
                    var exitLevelIndices = kvp.Value;

                    var level = levelManager.GetLevel(levelId);
                    if (level == null)
                    {
                        WriteLogLine($"  Level {levelId}: NULL (skipping)");
                        continue;
                    }

                    int levelIndex = levelManager.ActiveLevels2Levels.ContainsKey(levelId) ? levelManager.ActiveLevels2Levels[levelId] : -1;
                    WriteLogLine($"  Level {levelId} (Index {levelIndex}): {exitLevelIndices.Count} exit levels triggered");

                    foreach (var exitLevelIndex in exitLevelIndices)
                    {
                        double exitThreshold = exitLevels[exitLevelIndex];
                        WriteLogLine($"    Exit Level Index {exitLevelIndex}: Threshold={exitThreshold}");
                        
                        int exitQty = level.GetExitQuantityForLevel(exitLevelIndex);
                        WriteLogLine($"    Exit Quantity: {exitQty}");
                        
                        if (exitQty > 0)
                        {
                            WriteLogLine($"    EXECUTING EXIT for Level {levelId}, Exit Index {exitLevelIndex}");
                            ExecuteExit(levelId, exitLevelIndex, signalBar);
                        }
                        else
                        {
                            WriteLogLine($"    No quantity to exit (already exited or invalid)");
                        }
                    }
                }
            }

            // ==================== STEP 7: CHECK ENTRY CONDITIONS FOR EACH LEVEL ====================
            WriteLogLine("");
            WriteLogLine("--- STEP 7: Check Entry Conditions ---");
            
            // Check basic entry conditions
            bool withinTradingHours = IsWithinTradingHours(signalBar.DateTime);
            bool positionLimitOk = levelManager.ActiveLevelCount < levelManager.MaxConcurrentLevels;
            bool noLiveOrder = base.TradeManager == null || !base.TradeManager.HasLiveOrder;
            
            WriteLogLine($"Entry Pre-checks:");
            WriteLogLine($"  Within Trading Hours: {withinTradingHours}");
            WriteLogLine($"  Position Limit OK: {positionLimitOk} ({levelManager.ActiveLevelCount} < {levelManager.MaxConcurrentLevels})");
            WriteLogLine($"  No Live Order: {noLiveOrder}");

            if (!withinTradingHours)
            {
                WriteLogLine("Skipping entry checks - outside trading hours");
            }
            else if (!positionLimitOk)
            {
                WriteLogLine("Skipping entry checks - position limit reached");
            }
            else if (!noLiveOrder)
            {
                WriteLogLine("Skipping entry checks - live order exists");
            }
            else
            {
                WriteLogLine("");
                WriteLogLine("Checking each entry level:");
                
                for (int i = 0; i < entryLevels.Count; i++)
                {
                    double entryLevel = entryLevels[i];
                    var level = levelManager.Levels[i];

                    WriteLogLine($"  Entry Level {i}: Threshold={entryLevel}");

                    if (level != null)
                    {
                        WriteLogLine($"    Level already active (ID {level.Id}) - skipping");
                        continue;
                    }

                    // Calculate thresholds
                    double longThreshold = -entryLevel * mad;
                    double shortThreshold = entryLevel * mad;

                    WriteLogLine($"    Long Threshold: -{entryLevel} * {mad:F6} = {longThreshold:F6}");
                    WriteLogLine($"    Short Threshold: {entryLevel} * {mad:F6} = {shortThreshold:F6}");
                    WriteLogLine($"    Signal: {signal:F6}");

                    // Check long entry
                    bool longTriggered = signal < longThreshold;
                    WriteLogLine($"    Long Check: signal ({signal:F6}) < longThreshold ({longThreshold:F6}) = {longTriggered}");

                    if (longTriggered)
                    {
                        WriteLogLine($"    LONG ENTRY TRIGGERED for Level {i}");
                        ExecuteEntry(i, entryLevel, OrderSide.Buy, signalBar);
                        
                        // Re-check position limit after entry
                        if (levelManager.ActiveLevelCount >= levelManager.MaxConcurrentLevels)
                        {
                            WriteLogLine($"    Position limit reached after entry - stopping entry checks");
                            break;
                        }
                        continue;
                    }

                    // Check short entry
                    bool shortTriggered = signal > shortThreshold;
                    WriteLogLine($"    Short Check: signal ({signal:F6}) > shortThreshold ({shortThreshold:F6}) = {shortTriggered}");

                    if (shortTriggered)
                    {
                        WriteLogLine($"    SHORT ENTRY TRIGGERED for Level {i}");
                        ExecuteEntry(i, entryLevel, OrderSide.Sell, signalBar);
                        
                        // Re-check position limit after entry
                        if (levelManager.ActiveLevelCount >= levelManager.MaxConcurrentLevels)
                        {
                            WriteLogLine($"    Position limit reached after entry - stopping entry checks");
                            break;
                        }
                        continue;
                    }

                    WriteLogLine($"    No entry triggered");
                }
            }

            // ==================== STEP 8: LOG LEVEL STATES ====================
            WriteLogLine("");
            WriteLogLine("--- STEP 8: Log Level States ---");
            LogLevelStates(signalBar);

            WriteLogLine("");
            WriteLogLine($"========== END BAR {barCounter} ==========");
            WriteLogLine("");
        }

        #endregion

        #region Force Exit Logic

        private bool CheckForceExitConditions(DateTime currentTime)
        {
            WriteLogLine("Checking force exit conditions:");

            // Time-based exit
            TimeSpan exitTime = Parameters?.exit_time ?? new TimeSpan(15, 45, 0);
            bool timeExit = currentTime.TimeOfDay >= exitTime;
            WriteLogLine($"  Time-based exit: {currentTime.TimeOfDay} >= {exitTime} = {timeExit}");

            // Risk-based exit
            bool riskExit = levelManager.ActiveLevelCount > levelManager.MaxConcurrentLevels;
            WriteLogLine($"  Risk-based exit: {levelManager.ActiveLevelCount} > {levelManager.MaxConcurrentLevels} = {riskExit}");

            bool shouldExit = timeExit || riskExit;
            WriteLogLine($"  Should Force Exit: {shouldExit}");

            return shouldExit;
        }

        private void ExecuteForceExitAll(Bar bar)
        {
            WriteLogLine($"ExecuteForceExitAll: Exiting {levelManager.ActiveLevelCount} active levels");

            // Get all active levels before we start modifying
            var activeLevels = levelManager.ActiveLevels.Values.ToList();

            foreach (var level in activeLevels)
            {
                int levelIndex = levelManager.ActiveLevels2Levels.ContainsKey(level.Id) ? levelManager.ActiveLevels2Levels[level.Id] : -1;
                WriteLogLine($"  Force exiting Level {level.Id} (Index {levelIndex}):");
                WriteLogLine($"    Current Position: {level.CurrentPosition}");
                WriteLogLine($"    Entry Price: {level.EntryPrice:F4}");
                WriteLogLine($"    Side: {level.Side}");

                if (level.CurrentPosition != 0)
                {
                    int totalExitSize = Math.Abs(level.CurrentPosition);
                    OrderSide exitSide = level.Side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy;
                    
                    WriteLogLine($"    Exit Size: {totalExitSize}");
                    WriteLogLine($"    Exit Side: {exitSide}");

                    // Calculate PnL
                    double pnl = 0.0;
                    if (level.Side == OrderSide.Buy)
                        pnl = (bar.Close - level.EntryPrice) * totalExitSize;
                    else
                        pnl = (level.EntryPrice - bar.Close) * totalExitSize;
                    
                    WriteLogLine($"    PnL: {pnl:F2}");

                    // Force exit in level manager
                    levelManager.ForceExitLevel(level.Id, totalExitSize, bar.Close, bar.DateTime);

                    // Log order
                    LogOrder(bar, "FORCE_EXIT", level.Id, levelIndex, exitSide, bar.Close, totalExitSize);

                    // Place order if we have a trade manager
                    if (base.TradeManager != null && !base.TradeManager.HasLiveOrder)
                    {
                        int orderId = base.TradeManager.CreateOrder(exitSide, totalExitSize, bar.Close, tradeInstrument);
                        WriteLogLine($"    Order ID: {orderId}");

                        if (orderId > 0)
                        {
                            level.AddOrder(orderId, LevelOrderType.Exit, totalExitSize, bar.Close, -1);
                        }
                    }

                    // Update position tracking
                    UpdatePositionTracking(exitSide, totalExitSize, bar.Close);
                }
                else
                {
                    WriteLogLine($"    No position to exit");
                }
            }

            // Reset position tracking
            currentPosition = 0;
            averageEntryPrice = 0;
            
            WriteLogLine($"Force exit complete. Position reset. Active levels: {levelManager.ActiveLevelCount}");
        }

        #endregion

        #region Entry Execution

        private void ExecuteEntry(int levelIndex, double entryLevel, OrderSide side, Bar bar)
        {
            WriteLogLine($"ExecuteEntry: Level Index={levelIndex}, EntryLevel={entryLevel}, Side={side}");

            // Check if we can place order
            if (base.TradeManager != null && base.TradeManager.HasLiveOrder)
            {
                WriteLogLine($"  Cannot place order - live order exists");
                return;
            }

            double entryPrice = bar.Close;
            WriteLogLine($"  Entry Price: {entryPrice:F4}");
            WriteLogLine($"  Position Size: {positionSize}");
            WriteLogLine($"  Signal: {signal:F6}");

            // Create level
            bool isLevel = levelManager.CreateLevel(levelIndex, entryLevel, side, positionSize, entryPrice, signal, bar.DateTime);

            if (!isLevel)
            {
                WriteLogLine($"  Failed to create level");
                return;
            }

            Level level = levelManager.Levels[levelIndex];
            WriteLogLine($"  Level created with ID: {level.Id}");

            // Log order
            LogOrder(bar, "ENTRY", level.Id, levelIndex, side, entryPrice, positionSize);

            // Create order
            if (base.TradeManager != null)
            {
                int orderId = base.TradeManager.CreateOrder(side, positionSize, bar.Close, tradeInstrument);
                WriteLogLine($"  Order ID: {orderId}");

                if (orderId >= 0)
                {
                    order2LevelId[orderId] = level.Id;
                    level.AddOrder(orderId, LevelOrderType.Entry, positionSize, bar.Close);
                    WriteLogLine($"  Order created and tracked");
                }
                else
                {
                    WriteLogLine($"  Failed to create order");
                }
            }

            // Update position tracking
            UpdatePositionTracking(side, positionSize, bar.Close);
            WriteLogLine($"  Position updated: Current={currentPosition}, AvgEntry={averageEntryPrice:F4}");
        }

        #endregion

        #region Exit Execution

        private void ExecuteExit(int levelId, int exitLevelIndex, Bar bar)
        {
            WriteLogLine($"ExecuteExit: Level ID={levelId}, Exit Index={exitLevelIndex}");

            var level = levelManager.GetLevel(levelId);

            if (level == null)
            {
                WriteLogLine($"  Level not found");
                return;
            }

            int exitSize = level.GetExitQuantityForLevel(exitLevelIndex);
            WriteLogLine($"  Exit Size: {exitSize}");

            if (exitSize <= 0)
            {
                WriteLogLine($"  No quantity to exit");
                return;
            }

            OrderSide exitSide = level.Side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy;
            WriteLogLine($"  Exit Side: {exitSide}");
            WriteLogLine($"  Exit Price: {bar.Close:F4}");

            // Calculate PnL for this exit
            double pnl = 0.0;
            if (level.Side == OrderSide.Buy)
                pnl = (bar.Close - level.EntryPrice) * exitSize;
            else
                pnl = (level.EntryPrice - bar.Close) * exitSize;
            
            WriteLogLine($"  PnL: {pnl:F2}");

            // Execute exit in level manager
            levelManager.ExecuteExit(levelId, exitLevelIndex, bar.Close, bar.DateTime);

            // Log order
            int levelIndex = levelManager.ActiveLevels2Levels.ContainsKey(levelId) ? levelManager.ActiveLevels2Levels[levelId] : -1;
            LogOrder(bar, "EXIT", levelId, levelIndex, exitSide, bar.Close, exitSize);

            // Log trade if level is fully closed
            if (level.CurrentPosition == 0)
            {
                LogTrade(level, bar);
            }

            // Place order if we have a trade manager
            if (base.TradeManager != null && !base.TradeManager.HasLiveOrder)
            {
                int orderId = TradeManager.CreateOrder(exitSide, exitSize, bar.Close, tradeInstrument);
                WriteLogLine($"  Order ID: {orderId}");

                if (orderId > 0)
                {
                    level.AddOrder(orderId, LevelOrderType.Exit, exitSize, bar.Close, exitLevelIndex);
                }
            }

            // Update position tracking
            UpdatePositionTracking(exitSide, exitSize, bar.Close);
            WriteLogLine($"  Position updated: Current={currentPosition}, AvgEntry={averageEntryPrice:F4}");
        }

        #endregion

        #region Logging Methods

        private void WriteLogLine(string message)
        {
            if (logWriter != null)
            {
                logWriter.WriteLine(message);
                logWriter.Flush();
            }
        }

        private void LogOrder(Bar bar, string eventType, int levelId, int levelIndex, OrderSide side, 
            double price, int quantity)
        {
            if (orderLogWriter != null)
            {
                string line = $"{barCounter},{bar.CloseDateTime:yyyy-MM-dd HH:mm:ss},{eventType},{levelId},{levelIndex}," +
                    $"{side},{price:F4},{quantity},{signal:F6},{mad:F6}," +
                    $"{string.Join("|", entryLevels)},{string.Join("|", exitLevels)}," +
                    $"{currentPosition},{levelManager.ActiveLevelCount}";
                orderLogWriter.WriteLine(line);
                orderLogWriter.Flush();
            }
        }

        private void LogTrade(Level level, Bar exitBar)
        {
            if (tradeLogWriter != null)
            {
                TimeSpan holdTime = exitBar.DateTime - level.EntryDateTime;
                int holdBars = (int)(holdTime.TotalMinutes / 1); // Assuming 1-minute bars

                double pnl = 0.0;
                // Calculate total PnL from level's trade history
                // This is simplified - you may want to track this in Level class
                
                int levelIndex = levelManager.ActiveLevels2Levels.ContainsKey(level.Id) ? levelManager.ActiveLevels2Levels[level.Id] : -1;
                string line = $"{level.Id},{levelIndex}," +
                    $"{level.EntryDateTime:yyyy-MM-dd HH:mm:ss},{exitBar.CloseDateTime:yyyy-MM-dd HH:mm:ss}," +
                    $"{level.Side},{level.EntryPrice:F4},{exitBar.Close:F4}," +
                    $"{level.PositionSize},{pnl:F2},{level.ActualEntrySignal:F6}," +
                    $"{level.EntrySignalThreshold},{string.Join("|", exitLevels)},{holdBars}";
                tradeLogWriter.WriteLine(line);
                tradeLogWriter.Flush();
            }
        }

        private void LogLevelStates(Bar bar)
        {
            if (levelLogWriter != null)
            {
                if (levelManager.ActiveLevelCount == 0)
                {
                    WriteLogLine("No active levels to log");
                }
                else
                {
                    foreach (var kvp in levelManager.ActiveLevels)
                    {
                        var level = kvp.Value;
                        
                        double pnl = 0.0;
                        if (level.Side == OrderSide.Buy)
                            pnl = (bar.Close - level.EntryPrice) * Math.Abs(level.CurrentPosition);
                        else
                            pnl = (level.EntryPrice - bar.Close) * Math.Abs(level.CurrentPosition);

                        int levelIndex = levelManager.ActiveLevels2Levels.ContainsKey(level.Id) ? levelManager.ActiveLevels2Levels[level.Id] : -1;
                        int remainingQty = level.GetTotalRemainingExitQuantity();
                        string line = $"{barCounter},{bar.CloseDateTime:yyyy-MM-dd HH:mm:ss}," +
                            $"{level.Id},{levelIndex},ACTIVE,{level.Side}," +
                            $"{level.EntryPrice:F4},{level.CurrentPosition},{remainingQty}," +
                            $"{signal:F6},{pnl:F2}";
                        levelLogWriter.WriteLine(line);
                        levelLogWriter.Flush();
                    }
                }
            }
        }

        #endregion

        #region Helper Methods

        private void CalculateStatistics()
        {
            if (priceWindow.Count == 0) return;

            double sum = 0;
            foreach (var price in priceWindow)
                sum += price;
            movingAverage = sum / priceWindow.Count;
        }

        private void UpdatePositionTracking(OrderSide side, int quantity, double price)
        {
            WriteLogLine($"UpdatePositionTracking: Side={side}, Qty={quantity}, Price={price:F4}");
            WriteLogLine($"  Before: Position={currentPosition}, AvgEntry={averageEntryPrice:F4}");

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

            WriteLogLine($"  After: Position={currentPosition}, AvgEntry={averageEntryPrice:F4}");
        }

        private bool IsWithinTradingHours(DateTime currentTime)
        {
            if (Parameters == null) return true;
            var timeOfDay = currentTime.TimeOfDay;
            bool within = timeOfDay >= Parameters.entry_time && timeOfDay <= Parameters.entry_allowedUntil;
            return within;
        }

        private void ParseConfiguration()
        {
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

                if (parameters.additional_params.ContainsKey("exit_levels"))
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
                exitLevels = new List<double> { 0.67, 0.33, 0.17 }; // 3 exits: 1 unit each at 67%, 33%, 17% retracement
        }

        #endregion

        #region Event Handlers

        public void OnFill(Fill fill)
        {
            WriteLogLine($"OnFill: {fill.Side} {fill.Qty} @ {fill.Price:F4}");
        }

        public void OnOrderEvent(Order order)
        {
            WriteLogLine($"OnOrderEvent: Order {order.Id}, Status={order.Status}");
            if (TradeManager != null)
                TradeManager.HandleOrderUpdate(order);
        }

        public void OnStrategyStart()
        {
            WriteLogLine($"Strategy {Name} started");
        }

        public void OnStrategyStop()
        {
            WriteLogLine($"Strategy {Name} stopped");
            CloseLogWriters();
        }

        public void OnTrade(Trade trade) { }
        public void OnAsk(Ask ask) { }
        public void OnBid(Bid bid) { }

        #endregion

        #region Required Interface Methods - Not Used (All logic in OnBar)

        public override void ProcessBar(Bar[] bars)
        {
            // Not used - all logic in OnBar
        }

        public void ProcessBar(Bar[] bars, double accountValue)
        {
            // Not used - all logic in OnBar
        }

        public override bool ShouldEnterLongPosition(Bar[] bars)
        {
            // Not used - all logic in OnBar
            return false;
        }

        public override bool ShouldEnterShortPosition(Bar[] bars)
        {
            // Not used - all logic in OnBar
            return false;
        }

        public override bool ShouldExitLongPosition(Bar[] bars)
        {
            // Not used - all logic in OnBar
            return false;
        }

        public override bool ShouldExitShortPosition(Bar[] bars)
        {
            // Not used - all logic in OnBar
            return false;
        }

        #endregion

        #region Cleanup

        private void CloseLogWriters()
        {
            if (logWriter != null)
            {
                logWriter.Close();
                logWriter = null;
            }
            if (orderLogWriter != null)
            {
                orderLogWriter.Close();
                orderLogWriter = null;
            }
            if (tradeLogWriter != null)
            {
                tradeLogWriter.Close();
                tradeLogWriter = null;
            }
            if (levelLogWriter != null)
            {
                levelLogWriter.Close();
                levelLogWriter = null;
            }
        }

        ~MomentumMultiLevelStrategyManager()
        {
            CloseLogWriters();
        }

        #endregion
    }
}
