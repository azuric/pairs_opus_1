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
    /// Mean Reversion Strategy with 24-feature filter
    /// Extends MeanReversionStrategyManager with binning-based filtering similar to MomStrategyManagerFilter
    /// Features: diff_ema, run_ema, range, std_emv, snr_ema, tickvolume_ema (4 periods each)
    /// </summary>
    public class MeanReversionStrategyManagerFilter : BaseStrategyManager
    {
        // Core parameters
        private readonly Queue<double> priceWindow;
        private int lookbackPeriod;
        private double entryThreshold;
        private double exitThreshold;
        private double movingAverage;
        private double standardDeviation;
        private bool isStatisticsReady;

        // Signal tracking
        private DateTime currentDate;
        private double deviation;

        // Filter components (24 features)
        private int FeatureCount { get; set; }  // Should be 24
        private int[] FeatureIndices { get; set; }  // Maps 24 features to AlphaManager Data indices
        public double[] Features { get; private set; }  // Extracted 24 features
        public Matrix<double> BinsMatrix { get; private set; }  // 24 x 5 (bin edges)
        public double[] WeightsArray { get; protected set; }  // 96 weights (24 features × 4 bins)
        public double[] Data { get; private set; }  // Full AlphaManager data array
        private double filterThreshold;

        // Signal state tracking (prevents multiple entries per signal)
        private bool longSignalActive = false;
        private bool shortSignalActive = false;
        private bool longSignalFilterPassed = false;
        private bool shortSignalFilterPassed = false;

        private StreamWriter metricsWriter;
        private bool isWritingMetrics = true;
        private string metricsFilePath = "C:\\tmp\\Template\\mr_filter_metrics_csharp.csv";
        private int tradeCount;

        public MeanReversionStrategyManagerFilter(Instrument tradeInstrument) : base("mean_reversion_filter", tradeInstrument)
        {
            priceWindow = new Queue<double>();
            isStatisticsReady = false;
        }

        public override void Initialize(StrategyParameters parameters)
        {
            base.Initialize(parameters);

            // Basic parameters
            lookbackPeriod = 120;
            entryThreshold = 2.0;
            exitThreshold = 0.1;

            // Parse threshold from parameters
            if (parameters.threshold_entry != null && parameters.threshold_entry.Length > 0)
            {
                lookbackPeriod = Convert.ToInt32(parameters.threshold_entry[0][0]);
                entryThreshold = Convert.ToDouble(parameters.threshold_entry[0][1]);
            }

            if (parameters.threshold_exit != null && parameters.threshold_exit.Length > 0)
            {
                exitThreshold = Convert.ToDouble(parameters.threshold_exit[0][0]);
            }

            Console.WriteLine($"MeanReversionStrategyFilter {Name} initialized:");
            Console.WriteLine($"  Lookback Period: {lookbackPeriod}");
            Console.WriteLine($"  Entry Threshold: {entryThreshold}");
            Console.WriteLine($"  Exit Threshold: {exitThreshold}");

            // Initialize filter components
            FeatureCount = Convert.ToInt32(parameters.additional_params["featureCount"]);

            if (FeatureCount != 24)
            {
                Console.WriteLine($"  WARNING: Expected 24 features, got {FeatureCount}");
            }

            // Load feature indices mapping (24 indices)
            if (parameters.additional_params.ContainsKey("featureIndices"))
            {
                List<int> featureIndicesList = ConvertParametersToIntList(parameters, "featureIndices");
                FeatureIndices = featureIndicesList.ToArray();
                Console.WriteLine($"  Feature Indices loaded: {FeatureIndices.Length} indices");
                Console.WriteLine($"    First 5: [{string.Join(", ", FeatureIndices.Take(5))}]");
                Console.WriteLine($"    Last 5: [{string.Join(", ", FeatureIndices.Skip(19).Take(5))}]");
            }
            else
            {
                // Fallback: assume sequential indices
                FeatureIndices = Enumerable.Range(0, FeatureCount).ToArray();
                Console.WriteLine($"  Feature Indices: Not specified, using 0-{FeatureCount - 1}");
            }

            // Load bins (24 features × 5 edges)
            List<List<double>> Bins = ConvertParametersToBins(parameters);
            double[][] binsArray = Bins.ConvertAll(sublist => sublist.ToArray()).ToArray();
            double[,] binsArray_ = ConvertJaggedToMulti(binsArray);
            BinsMatrix = Matrix<double>.Build.DenseOfArray(binsArray_);

            Console.WriteLine($"  Bins Matrix: {BinsMatrix.RowCount} x {BinsMatrix.ColumnCount}");

            if (BinsMatrix.RowCount != 24)
            {
                Console.WriteLine($"  WARNING: Expected 24 bin rows, got {BinsMatrix.RowCount}");
            }

            // Load weights (96 = 24 features × 4 bins)
            WeightsArray = new double[FeatureCount * 4];
            List<double> Weights = ConvertParametersToWeights(parameters);
            if (Weights != null)
            {
                WeightsArray = Weights.ToArray();
            }

            int nonZeroWeights = WeightsArray.Count(w => Math.Abs(w) > 1e-10);
            Console.WriteLine($"  Weights: {WeightsArray.Length} total, {nonZeroWeights} non-zero");

            if (WeightsArray.Length != 96)
            {
                Console.WriteLine($"  WARNING: Expected 96 weights (24×4), got {WeightsArray.Length}");
            }

            // Load filter threshold
            if (parameters.additional_params.ContainsKey("filterThreshold"))
            {
                filterThreshold = Convert.ToDouble(parameters.additional_params["filterThreshold"]);
            }
            else
            {
                filterThreshold = 0.0;
            }
            Console.WriteLine($"  Filter Threshold: {filterThreshold}");

            // Initialize features array (24 features)
            Features = new double[FeatureCount];

            InitializeMetricsWriter();

            Console.WriteLine($"  Filter enabled with signal state tracking");
            Console.WriteLine($"  Expected features: diff_ema, run_ema, range, std_emv, snr_ema, tickvolume_ema (4 periods each)");
        }

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

        public override void OnBar(Bar[] bars)
        {
            Bar signalBar = GetSignalBar(bars);

            // Update statistics
            UpdateStatistics(signalBar);

            // Get features from AlphaManager and extract our 24 features
            Data = AlphaManager.GetData();

            if (Data != null && Data.Length > 0)
            {
                UpdateFeaturesFromData(Data);
            }

            // Cancel any pending orders
            CancelCurrentOrder();

            int currentTheoPosition = GetCurrentTheoPosition();

            // Check exits first
            if (currentTheoPosition != 0)
            {
                if (ShouldExitPosition(bars, currentTheoPosition))
                {
                    ExecuteTheoreticalExit(bars, currentTheoPosition);
                    UpdateMetrics(signalBar);
                    return;
                }
            }

            // Check entries - ONLY if no position
            if (currentTheoPosition == 0 && !HasLiveOrder())
            {
                if (ShouldEnterLongPosition(bars))
                {
                    ExecuteTheoreticalEntry(bars, OrderSide.Buy);
                    // Reset signal state after entry to prevent re-entry
                    WriteTradeMetrics(bars, OrderSide.Buy, 0);
                    longSignalActive = false;
                    longSignalFilterPassed = false;
                }
                else if (ShouldEnterShortPosition(bars))
                {
                    ExecuteTheoreticalEntry(bars, OrderSide.Sell);
                    // Reset signal state after entry to prevent re-entry
                    shortSignalActive = false;
                    shortSignalFilterPassed = false;
                }
            }

            // Update metrics
            UpdateMetrics(signalBar);
        }

        private void UpdateStatistics(Bar signalBar)
        {
            priceWindow.Enqueue(signalBar.Close);

            if (priceWindow.Count > lookbackPeriod)
            {
                priceWindow.Dequeue();
            }

            if (priceWindow.Count >= lookbackPeriod)
            {
                CalculateStatistics();
                isStatisticsReady = true;

                if (signalBar.CloseDateTime.Date != currentDate)
                {
                    currentDate = signalBar.CloseDateTime.Date;
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

        private bool ShouldExitPosition(Bar[] bars, int currentPosition)
        {
            Bar signalBar = GetSignalBar(bars);

            if (ShouldExitAllPositions(signalBar.DateTime))
                return true;

            if (!isStatisticsReady)
                return false;

            // Mean reversion exit based on deviation threshold
            if (currentPosition > 0)
                return deviation > -exitThreshold;
            else
                return deviation < exitThreshold;
        }

        public bool ShouldEnterLongPosition(Bar[] bars)
        {
            Bar signalBar = GetSignalBar(bars);

            // Basic checks
            if (!IsWithinTradingHours(signalBar.DateTime) || !CanEnterNewPosition(signalBar.DateTime))
                return false;

            if (!isStatisticsReady)
                return false;

            // Check if signal is below threshold (mean reversion: buy when price is low)
            bool signalTriggered = deviation < -entryThreshold;

            // SIGNAL STATE TRACKING - Check filter only once per signal breach
            if (signalTriggered && !longSignalActive)
            {
                // NEW SIGNAL - Check filter once
                longSignalActive = true;
                longSignalFilterPassed = PassesFilter();

                //Console.WriteLine($"[NEW LONG SIGNAL] {signalBar.DateTime:yyyy-MM-dd HH:mm:ss} Deviation={deviation:F2} Threshold={-entryThreshold:F2} Filter={longSignalFilterPassed}");
            }

            // If signal dropped above threshold, reset for next signal
            if (!signalTriggered && longSignalActive)
            {
                longSignalActive = false;
                longSignalFilterPassed = false;
            }

            // Only enter if signal is active AND filter passed
            return longSignalActive && longSignalFilterPassed;
        }

        public bool ShouldEnterShortPosition(Bar[] bars)
        {
            Bar signalBar = GetSignalBar(bars);

            // Basic checks
            if (!IsWithinTradingHours(signalBar.DateTime) || !CanEnterNewPosition(signalBar.DateTime))
                return false;

            if (!isStatisticsReady)
                return false;

            // Check if signal is above threshold (mean reversion: sell when price is high)
            bool signalTriggered = deviation > entryThreshold;

            // SIGNAL STATE TRACKING - Check filter only once per signal breach
            if (signalTriggered && !shortSignalActive)
            {
                // NEW SIGNAL - Check filter once
                shortSignalActive = true;
                shortSignalFilterPassed = PassesFilter();

                //Console.WriteLine($"[NEW SHORT SIGNAL] {signalBar.DateTime:yyyy-MM-dd HH:mm:ss} Deviation={deviation:F2} Threshold={entryThreshold:F2} Filter={shortSignalFilterPassed}");
            }

            // If signal rose below threshold, reset for next signal
            if (!signalTriggered && shortSignalActive)
            {
                shortSignalActive = false;
                shortSignalFilterPassed = false;
            }

            // Only enter if signal is active AND filter passed
            return shortSignalActive && shortSignalFilterPassed;
        }

        public bool ShouldExitLongPosition(Bar[] bars)
        {
            int pos = GetCurrentTheoPosition();
            return pos > 0 && ShouldExitPosition(bars, pos);
        }

        public bool ShouldExitShortPosition(Bar[] bars)
        {
            int pos = GetCurrentTheoPosition();
            return pos < 0 && ShouldExitPosition(bars, pos);
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

            // Calculate current deviation for entry/exit decisions
            if (priceWindow.Count > 0)
            {
                double currentPrice = priceWindow.Last();
                if (standardDeviation > 0)
                    deviation = (currentPrice - movingAverage) / standardDeviation;
                else
                    deviation = 0;
            }
        }

        /// <summary>
        /// Helper method to get bin for a feature value
        /// Returns the bin index (0-3) that the value falls into
        /// </summary>
        private int GetBinForFeature(double value, int featureIndex)
        {
            if (featureIndex < 0 || featureIndex >= BinsMatrix.RowCount)
                return 0;

            for (int binIdx = 0; binIdx < 4; binIdx++)
            {
                if (value > BinsMatrix[featureIndex, binIdx] && value <= BinsMatrix[featureIndex, binIdx + 1])
                {
                    return binIdx;
                }
            }

            // Value is outside all bins, return 0
            return 0;
        }

        /// <summary>
        /// Extract 24 features from AlphaManager's full data array
        /// Uses FeatureIndices to map our features to the correct positions
        /// </summary>
        public void UpdateFeaturesFromData(double[] dataArray)
        {
            if (dataArray == null || FeatureIndices == null)
                return;

            // Extract features at the specified indices
            for (int i = 0; i < FeatureCount && i < FeatureIndices.Length; i++)
            {
                int sourceIndex = FeatureIndices[i];
                if (sourceIndex >= 0 && sourceIndex < dataArray.Length)
                {
                    Features[i] = dataArray[sourceIndex];
                }
                else
                {
                    // Index out of range, set to 0
                    Features[i] = 0.0;
                }
            }
        }

        /// <summary>
        /// Convert features to binned representation
        /// Returns array of 96 values (24 features × 4 bins)
        /// Each feature gets one-hot encoded into 4 bins
        /// </summary>
        public double[] GetBinnedFeaturesArray(Matrix<double> bin, double[] alphas)
        {
            int rows = bin.RowCount;
            int cols = bin.ColumnCount;
            int bins = cols - 1;

            var outputArray = new double[FeatureCount * 4];
            int index = 0;

            for (int i = 0; i < FeatureCount; i++)
            {
                double value = alphas[i];

                for (int j = 0; j < 4; j++)
                {
                    if (value > bin[i, j] && value <= bin[i, j + 1])
                    {
                        outputArray[index] = 1.0;
                    }
                    else
                    {
                        outputArray[index] = 0.0;
                    }
                    index++;
                }
            }
            return outputArray;
        }

        /// <summary>
        /// Convert jagged array to multidimensional array
        /// </summary>
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

        /// <summary>
        /// Check if current features pass the filter
        /// </summary>
        private bool PassesFilter()
        {
            if (Features == null || Features.Length != FeatureCount)
            {
                //Console.WriteLine($"  [FILTER] Features not available (length={Features?.Length ?? 0}, expected={FeatureCount}) - ALLOWING trade (fail-safe)");
                return true;
            }

            double filterScore = CalculateFilterScore();
            bool passes = filterScore >= filterThreshold;

            //Console.WriteLine($"  [FILTER] Score={filterScore:F6} Threshold={filterThreshold:F6} Pass={passes}");

            return passes;
        }

        /// <summary>
        /// Calculate the filter score based on binned features and weights
        /// </summary>
        private double CalculateFilterScore()
        {
            double[] binnedFeatures = GetBinnedFeaturesArray(BinsMatrix, Features);

            double score = 0.0;
            for (int i = 0; i < binnedFeatures.Length && i < WeightsArray.Length; i++)
            {
                score += binnedFeatures[i] * WeightsArray[i];
            }

            return score;
        }

        public void ProcessBar(Bar[] bars)
        {
            //throw new NotImplementedException();
        }

        // Call this in your Initialize method
        private void InitializeMetricsWriter()
        {
            if (!isWritingMetrics) return;

            metricsWriter = new StreamWriter(metricsFilePath, false);

            // Write header
            var header = new List<string>
            {
                "trade_num",
                "timestamp",
                "side",
                "pnl"
            };

            // Add feature value columns
            string[] featureNames = new string[]
            {
                "diff_ema_120", "diff_ema_240", "diff_ema_480", "diff_ema_720",
                "run_ema_120", "run_ema_240", "run_ema_480", "run_ema_720",
                "range_120", "range_240", "range_480", "range_720",
                "std_emv_120", "std_emv_240", "std_emv_480", "std_emv_720",
                "snr_ema_120", "snr_ema_240", "snr_ema_480", "snr_ema_720",
                "tickvolume_ema_120", "tickvolume_ema_240", "tickvolume_ema_480", "tickvolume_ema_720"
            };

            foreach (var name in featureNames)
            {
                header.Add(name);
            }

            // Add bin columns
            foreach (var name in featureNames)
            {
                header.Add($"{name}_bin");
            }

            // Add contribution columns
            foreach (var name in featureNames)
            {
                header.Add($"{name}_contribution");
            }

            // Add filter score
            header.Add("filter_score");
            header.Add("passes_filter");
            header.Add("threshold");

            metricsWriter.WriteLine(string.Join(",", header));
            metricsWriter.Flush();

            Console.WriteLine($"[METRICS] Initialized metrics writer: {metricsFilePath}");
        }

        // Call this when a trade is executed
        private void WriteTradeMetrics(Bar[] bars, OrderSide side, double pnl)
        {
            if (!isWritingMetrics || metricsWriter == null) return;

            try
            {
                var values = new List<string>();

                // Trade info
                values.Add(tradeCount.ToString());
                values.Add(bars[0].CloseDateTime.ToString("yyyy-MM-dd HH:mm:ss"));
                values.Add(side.ToString());
                values.Add(pnl.ToString("F12"));

                // Get current features
                double[] features = AlphaManager?.GetData();

                if (features == null || features.Length < 66)
                {
                    Console.WriteLine($"[METRICS] WARNING: Features not available for trade {tradeCount}");
                    // Write placeholder row
                    for (int i = 0; i < 24 * 3 + 3; i++)
                    {
                        values.Add("NaN");
                    }
                    metricsWriter.WriteLine(string.Join(",", values));
                    metricsWriter.Flush();
                    return;
                }

                // Extract the 24 features we care about
                double[] extractedFeatures = new double[FeatureCount];
                for (int i = 0; i < FeatureCount && i < FeatureIndices.Length; i++)
                {
                    int sourceIndex = FeatureIndices[i];
                    if (sourceIndex < features.Length)
                    {
                        extractedFeatures[i] = features[sourceIndex];
                    }
                    else
                    {
                        extractedFeatures[i] = double.NaN;
                    }
                }

                // Write feature values
                foreach (var feature in extractedFeatures)
                {
                    values.Add(feature.ToString("G17")); // High precision
                }

                // Calculate bins and contributions
                int[] bins = new int[FeatureCount];
                double[] contributions = new double[FeatureCount];
                double filterScore = 0.0;

                double[] binnedArray = GetBinnedFeaturesArray(BinsMatrix, extractedFeatures);

                for (int i = 0; i < FeatureCount; i++)
                {
                    bins[i] = GetBinForFeature(extractedFeatures[i], i);
                    double contribution = 0.0;

                    // Sum contributions from all 4 bins for this feature
                    for (int j = 0; j < 4; j++)
                    {
                        int weightIndex = i * 4 + j;
                        if (weightIndex < WeightsArray.Length)
                        {
                            contribution += binnedArray[i * 4 + j] * WeightsArray[weightIndex];
                        }
                    }

                    contributions[i] = contribution;
                    filterScore += contribution;
                }

                // Write bin indices
                foreach (var bin in bins)
                {
                    values.Add(bin.ToString());
                }

                // Write contributions
                foreach (var contrib in contributions)
                {
                    values.Add(contrib.ToString("G17"));
                }

                // Write filter score and pass/fail
                values.Add(filterScore.ToString("G17"));
                values.Add((filterScore >= filterThreshold).ToString());
                values.Add(filterThreshold.ToString("G17"));

                metricsWriter.WriteLine(string.Join(",", values));
                metricsWriter.Flush();
                tradeCount++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[METRICS] Error writing metrics: {ex.Message}");
            }
        }
    }
}
