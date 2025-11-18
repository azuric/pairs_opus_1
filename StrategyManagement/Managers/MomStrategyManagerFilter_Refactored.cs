using System;
using System.Collections.Generic;
using SmartQuant;
using Parameters;
using SmartQuant.Strategy_;
using SmartQuant.Component;
using System.Linq;
using MathNet.Numerics.LinearAlgebra;
using Newtonsoft.Json.Linq;
using System.IO;

namespace StrategyManagement
{
    /// <summary>
    /// REFACTORED: GC Momentum Strategy with 24-feature filter
    /// All decision logic consolidated in OnBar for debugging
    /// All try-catch removed for manual logic checking
    /// Comprehensive logging for every order and trade event
    /// </summary>
    public class MomStrategyManagerFilter : BaseStrategyManager
    {
        // ==================== LOGGING ====================
        private StreamWriter logWriter;
        private StreamWriter orderLogWriter;
        private StreamWriter tradeLogWriter;
        private string logDirectory = "C:\\tmp\\Template\\debug_logs\\";
        private int barCounter = 0;
        
        // ==================== CORE PARAMETERS ====================
        private readonly Queue<double> priceWindow;
        private int lookbackPeriod;
        private double entryThreshold;
        private double exitThreshold;
        private double movingAverage;
        private double standardDeviation;
        private bool isStatisticsReady;

        // ==================== SIGNAL TRACKING ====================
        private DateTime currentDate;
        private double signal_ma;
        private double signal;
        private double alpha;
        private double dailyMad;
        private double mad;

        // ==================== FILTER COMPONENTS (24 features) ====================
        private int FeatureCount { get; set; }
        private int[] FeatureIndices { get; set; }
        public double[] Features { get; private set; }
        public Matrix<double> BinsMatrix { get; private set; }
        public double[] WeightsArray { get; protected set; }
        public double[] Data { get; private set; }
        private double filterThreshold;

        // ==================== SIGNAL STATE TRACKING ====================
        private bool longSignalActive = false;
        private bool shortSignalActive = false;
        private bool longSignalFilterPassed = false;
        private bool shortSignalFilterPassed = false;

        // ==================== TRADE TRACKING ====================
        private int tradeCount = 0;
        private double lastEntryPrice = 0.0;
        private DateTime lastEntryTime;
        private OrderSide lastEntrySide;

        public MomStrategyManagerFilter(Instrument tradeInstrument) : base("mom_filter", tradeInstrument)
        {
            priceWindow = new Queue<double>();
            isStatisticsReady = false;
        }

        public override void Initialize(StrategyParameters parameters)
        {
            base.Initialize(parameters);

            // Create log directory
            Directory.CreateDirectory(logDirectory);

            // Initialize log files
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            logWriter = new StreamWriter(Path.Combine(logDirectory, $"calculation_log_{timestamp}.txt"), false);
            orderLogWriter = new StreamWriter(Path.Combine(logDirectory, $"order_log_{timestamp}.csv"), false);
            tradeLogWriter = new StreamWriter(Path.Combine(logDirectory, $"trade_log_{timestamp}.csv"), false);

            // Write headers
            WriteLogLine("=== MomStrategyManagerFilter Refactored - Calculation Log ===");
            WriteLogLine($"Initialized at: {DateTime.Now}");
            WriteLogLine("");

            orderLogWriter.WriteLine("BarNum,Timestamp,Event,Side,Price,Quantity,Signal,MAD,Threshold,FilterScore,FilterPass,Position");
            orderLogWriter.Flush();

            tradeLogWriter.WriteLine("TradeNum,EntryTime,ExitTime,Side,EntryPrice,ExitPrice,PnL,Signal,FilterScore," +
                "diff_ema_120,diff_ema_240,diff_ema_480,diff_ema_720," +
                "run_ema_120,run_ema_240,run_ema_480,run_ema_720," +
                "range_120,range_240,range_480,range_720," +
                "std_emv_120,std_emv_240,std_emv_480,std_emv_720," +
                "snr_ema_120,snr_ema_240,snr_ema_480,snr_ema_720," +
                "tickvolume_ema_120,tickvolume_ema_240,tickvolume_ema_480,tickvolume_ema_720," +
                "bin_0,bin_1,bin_2,bin_3,bin_4,bin_5,bin_6,bin_7,bin_8,bin_9,bin_10,bin_11,bin_12,bin_13,bin_14,bin_15,bin_16,bin_17,bin_18,bin_19,bin_20,bin_21,bin_22,bin_23," +
                "contrib_0,contrib_1,contrib_2,contrib_3,contrib_4,contrib_5,contrib_6,contrib_7,contrib_8,contrib_9,contrib_10,contrib_11,contrib_12,contrib_13,contrib_14,contrib_15,contrib_16,contrib_17,contrib_18,contrib_19,contrib_20,contrib_21,contrib_22,contrib_23");
            tradeLogWriter.Flush();

            // Basic parameters
            lookbackPeriod = 240;
            entryThreshold = 0.5;
            exitThreshold = 0.01;
            alpha = 2.0 / (lookbackPeriod + 1.0);

            // Parse threshold from parameters
            lookbackPeriod = Convert.ToInt32(parameters.threshold_entry[0][0]);
            entryThreshold = Convert.ToDouble(parameters.threshold_entry[0][1]);
            exitThreshold = Convert.ToDouble(parameters.threshold_exit[0][0]);

            WriteLogLine($"Lookback Period: {lookbackPeriod}");
            WriteLogLine($"Entry Threshold: {entryThreshold}");
            WriteLogLine($"Exit Threshold: {exitThreshold}");
            WriteLogLine($"Alpha: {alpha}");
            WriteLogLine("");

            // Initialize filter components
            FeatureCount = Convert.ToInt32(parameters.additional_params["featureCount"]);

            if (FeatureCount != 24)
            {
                WriteLogLine($"WARNING: Expected 24 features, got {FeatureCount}");
            }

            // Load feature indices mapping (24 indices)
            if (parameters.additional_params.ContainsKey("featureIndices"))
            {
                List<int> featureIndicesList = ConvertParametersToIntList(parameters, "featureIndices");
                FeatureIndices = featureIndicesList.ToArray();
                WriteLogLine($"Feature Indices loaded: {FeatureIndices.Length} indices");
                WriteLogLine($"  First 5: [{string.Join(", ", FeatureIndices.Take(5))}]");
                WriteLogLine($"  Last 5: [{string.Join(", ", FeatureIndices.Skip(19).Take(5))}]");
            }
            else
            {
                FeatureIndices = Enumerable.Range(0, FeatureCount).ToArray();
                WriteLogLine($"Feature Indices: Not specified, using 0-{FeatureCount - 1}");
            }

            // Load bins (24 features × 5 edges)
            List<List<double>> Bins = ConvertParametersToBins(parameters);
            double[][] binsArray = Bins.ConvertAll(sublist => sublist.ToArray()).ToArray();
            double[,] binsArray_ = ConvertJaggedToMulti(binsArray);
            BinsMatrix = Matrix<double>.Build.DenseOfArray(binsArray_);

            WriteLogLine($"Bins Matrix: {BinsMatrix.RowCount} x {BinsMatrix.ColumnCount}");

            if (BinsMatrix.RowCount != 24)
            {
                WriteLogLine($"WARNING: Expected 24 bin rows, got {BinsMatrix.RowCount}");
            }

            // Load weights (96 = 24 features × 4 bins)
            WeightsArray = new double[FeatureCount * 4];
            List<double> Weights = ConvertParametersToWeights(parameters);
            if (Weights != null)
            {
                WeightsArray = Weights.ToArray();
            }

            int nonZeroWeights = WeightsArray.Count(w => Math.Abs(w) > 1e-10);
            WriteLogLine($"Weights: {WeightsArray.Length} total, {nonZeroWeights} non-zero");

            if (WeightsArray.Length != 96)
            {
                WriteLogLine($"WARNING: Expected 96 weights (24×4), got {WeightsArray.Length}");
            }

            // Load filter threshold
            if (parameters.additional_params.ContainsKey("filterThreshold"))
            {
                filterThreshold = Convert.ToDouble(parameters.additional_params["filterThreshold"]);
            }
            else
            {
                filterThreshold = entryThreshold;
            }
            WriteLogLine($"Filter Threshold: {filterThreshold}");
            WriteLogLine("");
            WriteLogLine("=== Initialization Complete ===");
            WriteLogLine("");

            // Initialize features array (24 features)
            Features = new double[FeatureCount];
        }

        // ==================== MAIN ONBAR LOGIC - ALL DECISIONS HERE ====================
        public override void OnBar(Bar[] bars)
        {
            barCounter++;
            Bar signalBar = GetSignalBar(bars);
            Bar executionBar = GetExecutionInstrumentBar(bars);

            WriteLogLine($"========== BAR {barCounter} - {signalBar.CloseDateTime:yyyy-MM-dd HH:mm:ss} ==========");
            WriteLogLine($"Signal Bar: O={signalBar.Open:F2} H={signalBar.High:F2} L={signalBar.Low:F2} C={signalBar.Close:F2}");

            // ==================== STEP 1: UPDATE SIGNAL AND STATISTICS ====================
            WriteLogLine("");
            WriteLogLine("--- STEP 1: Update Signal and Statistics ---");
            
            double old_signal_ma = signal_ma;
            signal_ma = EMA(alpha, signalBar.Close, signal_ma);
            WriteLogLine($"Signal MA: {old_signal_ma:F6} -> {signal_ma:F6} (alpha={alpha:F6}, price={signalBar.Close:F2})");

            priceWindow.Enqueue(signalBar.Close);
            signal = 10000 * ((signalBar.Close / signal_ma) - 1.0);
            WriteLogLine($"Signal: 10000 * ({signalBar.Close:F2} / {signal_ma:F6} - 1.0) = {signal:F6}");

            if (priceWindow.Count > lookbackPeriod)
            {
                priceWindow.Dequeue();
                WriteLogLine($"Price window dequeued, count={priceWindow.Count}");
            }

            if (priceWindow.Count >= lookbackPeriod)
            {
                CalculateStatistics();
                isStatisticsReady = true;

                if (signalBar.CloseDateTime.Date != currentDate)
                {
                    dailyMad = mad;
                    currentDate = signalBar.CloseDateTime.Date;
                    mad = Math.Abs(signal);
                    WriteLogLine($"New day: dailyMad={dailyMad:F6}, mad reset to {mad:F6}");
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

                WriteLogLine($"Statistics: MA={movingAverage:F6}, StdDev={standardDeviation:F6}");
            }
            else
            {
                WriteLogLine($"Statistics not ready: window count={priceWindow.Count}, need={lookbackPeriod}");
            }

            // ==================== STEP 2: GET FEATURES FROM ALPHAMANAGER ====================
            WriteLogLine("");
            WriteLogLine("--- STEP 2: Get Features from AlphaManager ---");
            
            Data = AlphaManager.GetData();

            if (Data != null && Data.Length > 0)
            {
                WriteLogLine($"AlphaManager Data: Length={Data.Length}");
                UpdateFeaturesFromData(Data);
                
                WriteLogLine("Extracted 24 Features:");
                string[] featureNames = new string[]
                {
                    "diff_ema_120", "diff_ema_240", "diff_ema_480", "diff_ema_720",
                    "run_ema_120", "run_ema_240", "run_ema_480", "run_ema_720",
                    "range_120", "range_240", "range_480", "range_720",
                    "std_emv_120", "std_emv_240", "std_emv_480", "std_emv_720",
                    "snr_ema_120", "snr_ema_240", "snr_ema_480", "snr_ema_720",
                    "tickvolume_ema_120", "tickvolume_ema_240", "tickvolume_ema_480", "tickvolume_ema_720"
                };
                
                for (int i = 0; i < FeatureCount; i++)
                {
                    WriteLogLine($"  [{i}] {featureNames[i]}: {Features[i]:G17} (from Data[{FeatureIndices[i]}])");
                }
            }
            else
            {
                WriteLogLine("AlphaManager Data: NULL or empty");
            }

            // ==================== STEP 3: CANCEL PENDING ORDERS ====================
            WriteLogLine("");
            WriteLogLine("--- STEP 3: Cancel Pending Orders ---");
            CancelCurrentOrder();
            WriteLogLine("Pending orders cancelled");

            // ==================== STEP 4: GET CURRENT POSITION ====================
            WriteLogLine("");
            WriteLogLine("--- STEP 4: Get Current Position ---");
            int currentTheoPosition = GetCurrentTheoPosition();
            WriteLogLine($"Current Theoretical Position: {currentTheoPosition}");

            // ==================== STEP 5: CHECK EXIT CONDITIONS ====================
            WriteLogLine("");
            WriteLogLine("--- STEP 5: Check Exit Conditions ---");
            
            if (currentTheoPosition != 0)
            {
                WriteLogLine($"Position exists: {currentTheoPosition}");
                
                // Check if should exit all positions
                bool shouldExitAll = ShouldExitAllPositions(signalBar.DateTime);
                WriteLogLine($"ShouldExitAllPositions: {shouldExitAll}");
                
                if (shouldExitAll)
                {
                    WriteLogLine("EXITING: ShouldExitAllPositions triggered");
                    ExecuteExit(bars, currentTheoPosition, "ExitAll");
                    UpdateMetrics(signalBar);
                    return;
                }

                // Check statistics ready
                if (!isStatisticsReady)
                {
                    WriteLogLine("No exit: Statistics not ready");
                }
                else
                {
                    // Check signal-based exit
                    bool shouldExit = false;
                    if (currentTheoPosition > 0)
                    {
                        shouldExit = signal < exitThreshold;
                        WriteLogLine($"Long position exit check: signal ({signal:F6}) < exitThreshold ({exitThreshold:F6}) = {shouldExit}");
                    }
                    else
                    {
                        shouldExit = signal > -exitThreshold;
                        WriteLogLine($"Short position exit check: signal ({signal:F6}) > -exitThreshold ({-exitThreshold:F6}) = {shouldExit}");
                    }

                    if (shouldExit)
                    {
                        WriteLogLine("EXITING: Signal exit condition met");
                        ExecuteExit(bars, currentTheoPosition, "SignalExit");
                        UpdateMetrics(signalBar);
                        return;
                    }
                    else
                    {
                        WriteLogLine("No exit: Signal exit condition not met");
                    }
                }
            }
            else
            {
                WriteLogLine("No position to exit");
            }

            // ==================== STEP 6: CHECK ENTRY CONDITIONS ====================
            WriteLogLine("");
            WriteLogLine("--- STEP 6: Check Entry Conditions ---");
            
            if (currentTheoPosition == 0 && !HasLiveOrder())
            {
                WriteLogLine("No position and no live orders - checking entry conditions");

                // Check basic conditions
                bool withinTradingHours = IsWithinTradingHours(signalBar.DateTime);
                bool canEnterNew = CanEnterNewPosition(signalBar.DateTime);
                WriteLogLine($"IsWithinTradingHours: {withinTradingHours}");
                WriteLogLine($"CanEnterNewPosition: {canEnterNew}");

                if (!withinTradingHours || !canEnterNew)
                {
                    WriteLogLine("No entry: Trading hours or position limit restrictions");
                }
                else if (!isStatisticsReady)
                {
                    WriteLogLine("No entry: Statistics not ready");
                }
                else
                {
                    // ==================== CHECK LONG ENTRY ====================
                    WriteLogLine("");
                    WriteLogLine("--- Checking LONG Entry ---");
                    
                    double longThreshold = mad * entryThreshold;
                    bool longSignalTriggered = signal > longThreshold;
                    WriteLogLine($"Long signal check: signal ({signal:F6}) > mad*entryThreshold ({longThreshold:F6}) = {longSignalTriggered}");

                    // SIGNAL STATE TRACKING - Check filter only once per signal breach
                    if (longSignalTriggered && !longSignalActive)
                    {
                        WriteLogLine("NEW LONG SIGNAL DETECTED - Checking filter");
                        longSignalActive = true;
                        
                        // Calculate filter score
                        double filterScore = CalculateFilterScore();
                        longSignalFilterPassed = filterScore >= filterThreshold;
                        
                        WriteLogLine($"Filter Score: {filterScore:F6}");
                        WriteLogLine($"Filter Threshold: {filterThreshold:F6}");
                        WriteLogLine($"Filter Passed: {longSignalFilterPassed}");
                        
                        LogFilterDetails(filterScore);
                    }

                    // If signal dropped below threshold, reset for next signal
                    if (!longSignalTriggered && longSignalActive)
                    {
                        WriteLogLine("Long signal dropped below threshold - resetting signal state");
                        longSignalActive = false;
                        longSignalFilterPassed = false;
                    }

                    // Check if should enter long
                    bool shouldEnterLong = longSignalActive && longSignalFilterPassed;
                    WriteLogLine($"Should Enter Long: {shouldEnterLong} (signalActive={longSignalActive}, filterPassed={longSignalFilterPassed})");

                    if (shouldEnterLong)
                    {
                        WriteLogLine("ENTERING LONG POSITION");
                        ExecuteEntry(bars, OrderSide.Buy);
                        longSignalActive = false;
                        longSignalFilterPassed = false;
                        UpdateMetrics(signalBar);
                        return;
                    }

                    // ==================== CHECK SHORT ENTRY ====================
                    WriteLogLine("");
                    WriteLogLine("--- Checking SHORT Entry ---");
                    
                    double shortThreshold = -mad * entryThreshold;
                    bool shortSignalTriggered = signal < shortThreshold;
                    WriteLogLine($"Short signal check: signal ({signal:F6}) < -mad*entryThreshold ({shortThreshold:F6}) = {shortSignalTriggered}");

                    // SIGNAL STATE TRACKING - Check filter only once per signal breach
                    if (shortSignalTriggered && !shortSignalActive)
                    {
                        WriteLogLine("NEW SHORT SIGNAL DETECTED - Checking filter");
                        shortSignalActive = true;
                        
                        // Calculate filter score
                        double filterScore = CalculateFilterScore();
                        shortSignalFilterPassed = filterScore >= filterThreshold;
                        
                        WriteLogLine($"Filter Score: {filterScore:F6}");
                        WriteLogLine($"Filter Threshold: {filterThreshold:F6}");
                        WriteLogLine($"Filter Passed: {shortSignalFilterPassed}");
                        
                        LogFilterDetails(filterScore);
                    }

                    // If signal rose above threshold, reset for next signal
                    if (!shortSignalTriggered && shortSignalActive)
                    {
                        WriteLogLine("Short signal rose above threshold - resetting signal state");
                        shortSignalActive = false;
                        shortSignalFilterPassed = false;
                    }

                    // Check if should enter short
                    bool shouldEnterShort = shortSignalActive && shortSignalFilterPassed;
                    WriteLogLine($"Should Enter Short: {shouldEnterShort} (signalActive={shortSignalActive}, filterPassed={shortSignalFilterPassed})");

                    if (shouldEnterShort)
                    {
                        WriteLogLine("ENTERING SHORT POSITION");
                        ExecuteEntry(bars, OrderSide.Sell);
                        shortSignalActive = false;
                        shortSignalFilterPassed = false;
                        UpdateMetrics(signalBar);
                        return;
                    }

                    WriteLogLine("No entry: Neither long nor short conditions met");
                }
            }
            else
            {
                if (currentTheoPosition != 0)
                    WriteLogLine("No entry check: Already in position");
                else
                    WriteLogLine("No entry check: Live order exists");
            }

            // ==================== STEP 7: UPDATE METRICS ====================
            WriteLogLine("");
            WriteLogLine("--- STEP 7: Update Metrics ---");
            UpdateMetrics(signalBar);
            
            WriteLogLine("");
            WriteLogLine($"========== END BAR {barCounter} ==========");
            WriteLogLine("");
        }

        // ==================== EXECUTION METHODS ====================
        private void ExecuteEntry(Bar[] bars, OrderSide side)
        {
            Bar signalBar = GetSignalBar(bars);
            
            WriteLogLine($"ExecuteEntry: Side={side}, Price={signalBar.Close:F2}");
            
            // Log order
            double filterScore = CalculateFilterScore();
            LogOrder(signalBar, "ENTRY", side, signalBar.Close, 1, filterScore, true, 0);
            
            // Execute theoretical entry
            ExecuteTheoreticalEntry(bars, side);
            
            // Track entry for PnL calculation
            lastEntryPrice = signalBar.Close;
            lastEntryTime = signalBar.CloseDateTime;
            lastEntrySide = side;
            
            WriteLogLine($"Entry executed: Price={lastEntryPrice:F2}, Time={lastEntryTime}");
        }

        private void ExecuteExit(Bar[] bars, int currentPosition, string exitReason)
        {
            Bar signalBar = GetSignalBar(bars);
            
            WriteLogLine($"ExecuteExit: Reason={exitReason}, Position={currentPosition}, Price={signalBar.Close:F2}");
            
            // Calculate PnL
            double pnl = 0.0;
            if (lastEntryPrice > 0)
            {
                if (lastEntrySide == OrderSide.Buy)
                    pnl = signalBar.Close - lastEntryPrice;
                else
                    pnl = lastEntryPrice - signalBar.Close;
                
                WriteLogLine($"PnL: {pnl:F2} (Entry={lastEntryPrice:F2}, Exit={signalBar.Close:F2}, Side={lastEntrySide})");
            }
            
            // Log order
            OrderSide exitSide = currentPosition > 0 ? OrderSide.Sell : OrderSide.Buy;
            double filterScore = CalculateFilterScore();
            LogOrder(signalBar, exitReason, exitSide, signalBar.Close, Math.Abs(currentPosition), filterScore, true, 0);
            
            // Log trade
            LogTrade(signalBar, pnl, filterScore);
            
            // Execute theoretical exit
            ExecuteTheoreticalExit(bars, currentPosition);
            
            // Reset entry tracking
            lastEntryPrice = 0.0;
            
            WriteLogLine($"Exit executed: PnL={pnl:F2}");
        }

        // ==================== LOGGING METHODS ====================
        private void WriteLogLine(string message)
        {
            if (logWriter != null)
            {
                logWriter.WriteLine(message);
                logWriter.Flush();
            }
        }

        private void LogOrder(Bar bar, string eventType, OrderSide side, double price, int quantity, 
            double filterScore, bool filterPass, int position)
        {
            if (orderLogWriter != null)
            {
                string line = $"{barCounter},{bar.CloseDateTime:yyyy-MM-dd HH:mm:ss},{eventType},{side}," +
                    $"{price:F2},{quantity},{signal:F6},{mad:F6},{entryThreshold:F6},{filterScore:F6},{filterPass},{position}";
                orderLogWriter.WriteLine(line);
                orderLogWriter.Flush();
            }
        }

        private void LogTrade(Bar bar, double pnl, double filterScore)
        {
            if (tradeLogWriter != null)
            {
                tradeCount++;
                
                List<string> values = new List<string>();
                values.Add(tradeCount.ToString());
                values.Add(lastEntryTime.ToString("yyyy-MM-dd HH:mm:ss"));
                values.Add(bar.CloseDateTime.ToString("yyyy-MM-dd HH:mm:ss"));
                values.Add(lastEntrySide.ToString());
                values.Add(lastEntryPrice.ToString("F2"));
                values.Add(bar.Close.ToString("F2"));
                values.Add(pnl.ToString("F2"));
                values.Add(signal.ToString("F6"));
                values.Add(filterScore.ToString("F6"));
                
                // Add 24 feature values
                for (int i = 0; i < FeatureCount; i++)
                {
                    values.Add(Features[i].ToString("G17"));
                }
                
                // Calculate bins and contributions
                int[] bins = new int[FeatureCount];
                double[] contributions = new double[FeatureCount];
                
                for (int i = 0; i < FeatureCount; i++)
                {
                    int bin = GetBinForFeature(Features[i], i);
                    bins[i] = bin;
                    
                    int weightIndex = i * 4 + bin;
                    double contribution = (weightIndex < WeightsArray.Length) ? WeightsArray[weightIndex] : 0.0;
                    contributions[i] = contribution;
                }
                
                // Add bins
                for (int i = 0; i < FeatureCount; i++)
                {
                    values.Add(bins[i].ToString());
                }
                
                // Add contributions
                for (int i = 0; i < FeatureCount; i++)
                {
                    values.Add(contributions[i].ToString("F6"));
                }
                
                tradeLogWriter.WriteLine(string.Join(",", values));
                tradeLogWriter.Flush();
            }
        }

        private void LogFilterDetails(double filterScore)
        {
            WriteLogLine("");
            WriteLogLine("Filter Calculation Details:");
            
            string[] featureNames = new string[]
            {
                "diff_ema_120", "diff_ema_240", "diff_ema_480", "diff_ema_720",
                "run_ema_120", "run_ema_240", "run_ema_480", "run_ema_720",
                "range_120", "range_240", "range_480", "range_720",
                "std_emv_120", "std_emv_240", "std_emv_480", "std_emv_720",
                "snr_ema_120", "snr_ema_240", "snr_ema_480", "snr_ema_720",
                "tickvolume_ema_120", "tickvolume_ema_240", "tickvolume_ema_480", "tickvolume_ema_720"
            };
            
            for (int i = 0; i < FeatureCount; i++)
            {
                double featureValue = Features[i];
                int bin = GetBinForFeature(featureValue, i);
                int weightIndex = i * 4 + bin;
                double weight = (weightIndex < WeightsArray.Length) ? WeightsArray[weightIndex] : 0.0;
                
                double edge0 = BinsMatrix[i, 0];
                double edge1 = BinsMatrix[i, 1];
                double edge2 = BinsMatrix[i, 2];
                double edge3 = BinsMatrix[i, 3];
                double edge4 = BinsMatrix[i, 4];
                
                WriteLogLine($"  [{i}] {featureNames[i]}:");
                WriteLogLine($"      Value: {featureValue:G17}");
                WriteLogLine($"      Bins: [{edge0:F6}, {edge1:F6}, {edge2:F6}, {edge3:F6}, {edge4:F6}]");
                WriteLogLine($"      Bin: {bin}");
                WriteLogLine($"      Weight[{weightIndex}]: {weight:F6}");
                WriteLogLine($"      Contribution: {weight:F6}");
            }
            
            WriteLogLine($"  Total Filter Score: {filterScore:F6}");
        }

        // ==================== CALCULATION METHODS ====================
        private double CalculateFilterScore()
        {
            if (Features == null || Features.Length != FeatureCount)
            {
                WriteLogLine($"Filter calculation skipped: Features not available");
                return 0.0;
            }

            double score = 0.0;
            
            for (int i = 0; i < FeatureCount; i++)
            {
                double featureValue = Features[i];
                int bin = GetBinForFeature(featureValue, i);
                int weightIndex = i * 4 + bin;
                
                if (weightIndex < WeightsArray.Length)
                {
                    score += WeightsArray[weightIndex];
                }
            }

            return score;
        }

        private int GetBinForFeature(double value, int featureIndex)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return 0;

            if (featureIndex >= BinsMatrix.RowCount)
                return 0;

            // Get bin edges for this feature
            double edge0 = BinsMatrix[featureIndex, 0];
            double edge1 = BinsMatrix[featureIndex, 1];
            double edge2 = BinsMatrix[featureIndex, 2];
            double edge3 = BinsMatrix[featureIndex, 3];
            double edge4 = BinsMatrix[featureIndex, 4];

            // First bin: INCLUSIVE on both sides (to match pandas include_lowest=True)
            if (value >= edge0 && value <= edge1)
                return 0;

            // Other bins: EXCLUSIVE on left, INCLUSIVE on right
            if (value > edge1 && value <= edge2)
                return 1;

            if (value > edge2 && value <= edge3)
                return 2;

            if (value > edge3 && value <= edge4)
                return 3;

            // Fallback: if outside range, put in nearest bin
            if (value < edge0)
                return 0;

            return 3;
        }

        private void CalculateStatistics()
        {
            if (priceWindow.Count == 0) return;

            double sum = 0;
            foreach (var price in priceWindow)
                sum += price;
            movingAverage = sum / priceWindow.Count;

            double sumSquaredDeviations = 0;
            foreach (var price in priceWindow)
                sumSquaredDeviations += Math.Pow(price - movingAverage, 2);
            standardDeviation = Math.Sqrt(sumSquaredDeviations / priceWindow.Count);
        }

        private double EMA(double alpha, double x, double ema_x)
        {
            if (!double.IsNaN(ema_x))
                return ema_x + alpha * (x - ema_x);
            else
                return x;
        }

        private void UpdateFeaturesFromData(double[] dataArray)
        {
            if (dataArray == null || FeatureIndices == null)
                return;

            for (int i = 0; i < FeatureCount && i < FeatureIndices.Length; i++)
            {
                int sourceIndex = FeatureIndices[i];
                if (sourceIndex >= 0 && sourceIndex < dataArray.Length)
                {
                    Features[i] = dataArray[sourceIndex];
                }
                else
                {
                    Features[i] = 0.0;
                }
            }
        }

        private void UpdateMetrics(Bar signalBar)
        {
            if (DualPositionManager?.TheoPositionManager != null)
                DualPositionManager.TheoPositionManager.UpdateTradeMetric(signalBar);

            if (DualPositionManager?.ActualPositionManager != null)
                DualPositionManager.ActualPositionManager.UpdateTradeMetric(signalBar);
        }

        // ==================== PARAMETER PARSING METHODS ====================
        private List<int> ConvertParametersToIntList(StrategyParameters parameters, string key)
        {
            if (parameters.additional_params.ContainsKey(key))
            {
                var value = parameters.additional_params[key];
                if (value is JArray jArray)
                    return jArray.ToObject<List<int>>();
                if (value is List<int> list)
                    return list;
                if (value is IEnumerable<object> enumerable)
                    return enumerable.Select(x => Convert.ToInt32(x)).ToList();
            }
            return null;
        }

        private List<double> ConvertParametersToWeights(StrategyParameters parameters)
        {
            if (parameters.additional_params.ContainsKey("weights"))
            {
                var value = parameters.additional_params["weights"];
                if (value is JArray jArray)
                    return jArray.ToObject<List<double>>();
                if (value is List<double> list)
                    return list;
                if (value is IEnumerable<object> enumerable)
                    return enumerable.Select(x => Convert.ToDouble(x)).ToList();
            }
            return null;
        }

        public List<List<double>> ConvertParametersToBins(StrategyParameters parameters)
        {
            if (parameters.additional_params.ContainsKey("bins"))
            {
                var value = parameters.additional_params["bins"];
                if (value is JArray jArray)
                    return jArray.ToObject<List<List<double>>>();
                if (value is List<List<double>> list)
                    return list;
                if (value is IEnumerable<object> enumerable)
                {
                    return enumerable
                        .Select(inner => ((IEnumerable<object>)inner)
                            .Select(x => Convert.ToDouble(x))
                            .ToList())
                        .ToList();
                }
            }
            return null;
        }

        public double[,] ConvertJaggedToMulti(double[][] jaggedArray)
        {
            if (jaggedArray.Length == 0 || jaggedArray[0].Length == 0)
            {
                throw new ArgumentException("The jagged array is empty or the first row is empty.");
            }

            int rows = jaggedArray.Length;
            int cols = jaggedArray[0].Length;

            double[,] multiArray = new double[rows, cols];

            for (int i = 0; i < rows; i++)
            {
                if (jaggedArray[i].Length != cols)
                {
                    throw new ArgumentException($"Row {i} does not match the expected length of {cols}.");
                }

                for (int j = 0; j < cols; j++)
                {
                    multiArray[i, j] = jaggedArray[i][j];
                }
            }
            return multiArray;
        }

        // ==================== LEGACY METHODS (Required by base class but not used) ====================
        public override void ProcessBar(Bar[] bars)
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

        // ==================== CLEANUP ====================
        ~MomStrategyManagerFilter()
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
        }
    }
}
