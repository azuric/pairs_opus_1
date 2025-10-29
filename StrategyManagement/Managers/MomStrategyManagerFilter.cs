using MathNet.Numerics.LinearAlgebra;
using Newtonsoft.Json.Linq;
using Parameters;
using SmartQuant;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StrategyManagement.Managers
{
    public class MomStrategyManagerFilter : BaseStrategyManager
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
        private double signal_ma;
        private double signal;
        private double alpha;
        private double dailyMad;
        private double mad;

        // Filter components
        private int FeatureCount { get; set; }
        public double[] Features { get; private set; }
        public Matrix<double> BinsMatrix { get; private set; }
        public double[] WeightsArray { get; protected set; }
        public double[] Data { get; private set; }
        private double filterThreshold;  // Entry threshold for filter (0.10 for Buy, 0.25 for Sell)

        public MomStrategyManagerFilter(Instrument tradeInstrument) : base("mom_filter", tradeInstrument)
        {
            priceWindow = new Queue<double>();
            isStatisticsReady = false;
        }

        public override void Initialize(StrategyParameters parameters)
        {
            base.Initialize(parameters);

            // Basic parameters
            lookbackPeriod = 240;
            entryThreshold = 0.5;
            exitThreshold = 0.01;
            alpha = 2.0 / (lookbackPeriod + 1.0);

            // Parse threshold from parameters

            lookbackPeriod = Convert.ToInt32(parameters.threshold_entry[0][0]);
            entryThreshold = Convert.ToDouble(parameters.threshold_entry[0][1]);

            exitThreshold = Convert.ToDouble(parameters.threshold_exit[0][0]);


            // Filter threshold (0.10 for Buy, 0.25 for Sell)
            filterThreshold = entryThreshold;  // Using same threshold from config

            Console.WriteLine($"MomStrategy {Name} initialized:");
            Console.WriteLine($"  Signal Source: {GetSignalSourceDescription()}");
            Console.WriteLine($"  Trade Instrument: {GetExecutionInstrumentDescription()}");
            Console.WriteLine($"  Lookback Period: {lookbackPeriod}");
            Console.WriteLine($"  Entry Threshold: {entryThreshold}");
            Console.WriteLine($"  Exit Threshold: {exitThreshold}");
            Console.WriteLine($"  Filter Threshold: {filterThreshold}");

            // Initialize filter components
            FeatureCount = Convert.ToInt32(parameters.additional_params["featureCount"]);

            // Load bins
            List<List<double>> Bins = ConvertParametersToBins(parameters);
            double[][] binsArray = Bins.ConvertAll(sublist => sublist.ToArray()).ToArray();
            double[,] binsArray_ = ConvertJaggedToMulti(binsArray);
            BinsMatrix = Matrix<double>.Build.DenseOfArray(binsArray_);

            Console.WriteLine($"  Bins Matrix: {BinsMatrix.RowCount} x {BinsMatrix.ColumnCount}");

            // Load weights
            WeightsArray = new double[FeatureCount * 4];
            List<double> Weights = ConvertParametersToWeights(parameters);
            if (Weights != null)
            {
                WeightsArray = Weights.ToArray();
            }

            int nonZeroWeights = WeightsArray.Count(w => w > 0);
            Console.WriteLine($"  Weights: {WeightsArray.Length} total, {nonZeroWeights} non-zero");

            // Initialize features
            Features = new double[FeatureCount];
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

        // SIMPLIFIED: Everything in OnBar
        public override void OnBar(Bar[] bars)
        {
            Bar signalBar = GetSignalBar(bars);
            Bar executionBar = GetExecutionInstrumentBar(bars);

            // Update signal and statistics
            UpdateSignalAndStatistics(signalBar);

            // Get features from AlphaManager
            Data = base.AlphaManager.GetData();

            if (Data != null && Data.Length >= FeatureCount)
            {
                UpdateFeatures(Data);
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

            // Check entries
            if (currentTheoPosition == 0 && !HasLiveOrder())
            {
                if (ShouldEnterLongPosition(bars))
                {
                    ExecuteTheoreticalEntry(bars, OrderSide.Buy);
                }
                else if (ShouldEnterShortPosition(bars))
                {
                    ExecuteTheoreticalEntry(bars, OrderSide.Sell);
                }
            }

            // Update metrics
            UpdateMetrics(signalBar);
        }

        private void UpdateSignalAndStatistics(Bar signalBar)
        {
            // Update EMA
            signal_ma = EMA(alpha, signalBar.Close, signal_ma);

            // Update price window
            priceWindow.Enqueue(signalBar.Close);

            // Calculate signal
            signal = 10000 * ((signalBar.Close / signal_ma) - 1.0);

            if (priceWindow.Count > lookbackPeriod)
            {
                priceWindow.Dequeue();
            }

            if (priceWindow.Count >= lookbackPeriod)
            {
                CalculateStatistics();
                isStatisticsReady = true;

                // Update daily MAD
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

            // Exit logic based on signal
            if (currentPosition > 0)
                return signal < exitThreshold;
            else
                return signal > -exitThreshold;
        }

        public override bool ShouldEnterLongPosition(Bar[] bars)
        {
            Bar signalBar = GetSignalBar(bars);

            // Basic checks
            if (!IsWithinTradingHours(signalBar.DateTime) || !CanEnterNewPosition(signalBar.DateTime))
                return false;

            if (!isStatisticsReady)
                return false;

            // Check momentum signal
            if (signal <= mad * entryThreshold)
                return false;

            // Apply filter
            if (!PassesFilter())
                return false;

            return true;
        }

        public override bool ShouldEnterShortPosition(Bar[] bars)
        {
            Bar signalBar = GetSignalBar(bars);

            // Basic checks
            if (!IsWithinTradingHours(signalBar.DateTime) || !CanEnterNewPosition(signalBar.DateTime))
                return false;

            if (!isStatisticsReady)
                return false;

            // Check momentum signal
            if (signal >= -mad * entryThreshold)
                return false;

            // Apply filter (use same filter for now, can be different for Sell)
            if (!PassesFilter())
                return false;

            return true;
        }

        // FILTER LOGIC
        private bool PassesFilter()
        {
            if (Features == null || Features.Length != FeatureCount)
                return true;  // If features not available, allow trade (fail-safe)

            double filterScore = CalculateFilterScore();

            // For Buy: threshold is 0.10
            // For Sell: threshold is 0.25 (would need to be configured separately)
            bool passes = filterScore > filterThreshold;

            // Optional: Log filter decision
            // Console.WriteLine($"Filter Score: {filterScore:F4}, Threshold: {filterThreshold:F4}, Passes: {passes}");

            return passes;
        }

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

        public override bool ShouldExitLongPosition(Bar[] bars)
        {
            int pos = GetCurrentTheoPosition();
            return pos > 0 && ShouldExitPosition(bars, pos);
        }

        public override bool ShouldExitShortPosition(Bar[] bars)
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
        }

        private double EMA(double alpha, double x, double ema_x)
        {
            if (!double.IsNaN(ema_x))
                return ema_x + alpha * (x - ema_x);
            else
                return x;
        }

        // Feature update method
        public void UpdateFeatures(double[] dataArray)
        {
            if (dataArray.Length >= FeatureCount)
            {
                for (int i = 0; i < FeatureCount; i++)
                {
                    Features[i] = dataArray[i];
                }
            }
        }

        // Binning method
        public double[] GetBinnedFeaturesArray(Matrix<double> bin, double[] alphas)
        {
            int rows = bin.RowCount;
            int cols = bin.ColumnCount;
            int bins = cols - 1;  // 5 edges = 4 bins

            var outputArray = new double[FeatureCount * 4];
            int index = 0;

            for (int i = 0; i < FeatureCount; i++)
            {
                double value = alphas[i];

                for (int j = 0; j < 4; j++)
                {
                    // Check if value falls in this bin
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

        // Utility method for array conversion
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

        public override void ProcessBar(Bar[] bars)
        {
            //throw new NotImplementedException();
        }
    }

}
