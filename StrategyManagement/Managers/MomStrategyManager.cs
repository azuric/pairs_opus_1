using System;
using System.Collections.Generic;
using SmartQuant;
using Parameters;
using SmartQuant.Strategy_;
using SmartQuant.Component;
using System.Linq;

using Newtonsoft.Json.Linq;

namespace StrategyManagement
{
    public class MomStrategyManager : BaseStrategyManager
    {
        // Statistics
        private readonly Queue<double> priceWindow;
        private int lookbackPeriod;
        private double entryThreshold;
        private double exitThreshold;
        private double movingAverage;
        private double standardDeviation;
        private double stopLossPercent;
        private double takeProfitPercent;
        private bool isStatisticsReady;
        private int FeatureCount { get; set; }
        public double[] Features { get; private set; }
        public SimpleMatrix<double> BinsMatrix { get; private set; }
        public double[] WeightsArray { get; protected set; }
        public double[] Data { get; private set; }

        private double dailyMad;
        private double mad;

        private DateTime currentDate;
        private double signal_ma;
        private double signal;
        private double alpha;

        public MomStrategyManager(Instrument tradeInstrument) : base("Mom", tradeInstrument)
        {
            priceWindow = new Queue<double>();
            isStatisticsReady = false;
        }

        public override void Initialize(StrategyParameters parameters)
        {
            base.Initialize(parameters); // This now handles both signal and trade configuration

            // Existing parameter initialization...
            lookbackPeriod = 240;
            entryThreshold = 0.5;
            exitThreshold = 0.01;
            stopLossPercent = 0.03;
            takeProfitPercent = 0.05;

            alpha = 2.0 / (lookbackPeriod + 1.0);

            // Parse strategy-specific parameters...
            // (existing parameter parsing code)

            // NEW: Enhanced logging
            Console.WriteLine($"MeanReversionStrategy {Name} initialized:");
            Console.WriteLine($"  Signal Source: {GetSignalSourceDescription()}");
            Console.WriteLine($"  Trade Instrument: {GetExecutionInstrumentDescription()}");
            Console.WriteLine($"  Lookback Period: {lookbackPeriod}");
            Console.WriteLine($"  Entry Threshold: {entryThreshold}");
            Console.WriteLine($"  Exit Threshold: {exitThreshold}");
            Console.WriteLine($"  Stop Loss: {stopLossPercent:P2}");
            Console.WriteLine($"  Take Profit: {takeProfitPercent:P2}");

            // NEW: Validate configuration
            ValidateSignalTradeConfiguration();

            FeatureCount = Convert.ToInt32(parameters.additional_params["featureCount"]);

            List<List<double>> Bins = null;

            Bins = ConvertParametersToBins(parameters);

            // Load and process bins from JSON
            double[][] binsArray = new double[FeatureCount][];

            if (Bins != null)
            {
                binsArray = Bins.ConvertAll(sublist => sublist.ToArray()).ToArray();

                // Optional: Print bins for debugging
                foreach (double[] bin in binsArray)
                {
                    Console.WriteLine($"[{string.Join(", ", bin)}]");
                }
            }

            // Convert and create bins matrix
            var binsArray_ = ConvertJaggedToMulti(binsArray);
            BinsMatrix = SimpleMatrix<double>.Build.DenseOfArray(binsArray_);

            Console.WriteLine("Matrix from JSON using Math.NET Numerics:");
            Console.WriteLine(BinsMatrix.ToString());

            // Load weights from JSON
            WeightsArray = new double[FeatureCount * 4];

            List<double> Weights = null;

            Weights = ConvertParametersToWeights(parameters);

            if (Weights != null)
            {
                WeightsArray = Weights.ToArray();
            }

            // Initialize features
            Features = new double[FeatureCount];
        }

        private List<double> ConvertParametersToWeights(StrategyParameters parameters)
        {
            if (parameters.additional_params.ContainsKey("weights"))
            {
                var value = parameters.additional_params["weights"];

                // If it's a JArray (from JSON deserialization)
                if (value is JArray jArray)
                    return jArray.ToObject<List<double>>();

                // If it's already a list (unlikely but safe)
                if (value is List<double> list)
                    return list;

                // If it's an IEnumerable
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

                // If it's a JArray (from JSON deserialization)
                if (value is JArray jArray)
                    return jArray.ToObject<List<List<double>>>();

                // If it's already the right type
                if (value is List<List<double>> list)
                    return list;

                // If it's a nested enumerable
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

        // NEW: Validation method
        private void ValidateSignalTradeConfiguration()
        {
            if (!isPairMode)
            {
                Console.WriteLine("Single instrument mode: Signal and trade instrument are the same.");
                return;
            }

            if (signalSource == executionInstrumentSource)
            {
                Console.WriteLine($"Using {signalSource} for both signals and trading.");
            }
            else
            {
                Console.WriteLine($"Cross-instrument strategy: Signals from {signalSource}, trading {executionInstrumentSource}");

                // Add specific warnings for certain combinations
                if (signalSource == SignalSource.Synthetic && executionInstrumentSource != SignalSource.Synthetic)
                {
                    Console.WriteLine("Note: Using synthetic signals to trade individual instruments. " +
                                    "Ensure proper position sizing and risk management.");
                }

                if (signalSource != SignalSource.Synthetic && executionInstrumentSource == SignalSource.Synthetic)
                {
                    Console.WriteLine("Note: Using individual instrument signals to trade synthetic. " +
                                    "Consider correlation and liquidity differences.");
                }
            }
        }

        //metric logic
        public override void ProcessBar(Bar[] bars)
        {
            Bar signalBar = GetSignalBar(bars);

            signal_ma = EMA(alpha, signalBar.Close, signal_ma);

            // Update statistics
            priceWindow.Enqueue(signalBar.Close);

            signal = 10000 * ((signalBar.Close / signal_ma) - 1.0);

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
                    dailyMad = mad;
                    currentDate = signalBar.CloseDateTime.Date;
                    mad = Math.Abs(signal);

                    isStatisticsReady = true;
                }
                else if (Math.Abs(signal) > mad)
                {
                    mad = Math.Abs(signal);
                }
            }

            Data = AlphaManager.GetData();

        }


        //trade logic
        public override void OnBar(Bar[] bars)
        {
            Bar signalBar = GetSignalBar(bars);

            CancelCurrentOrder();

            int currentTheoPosition = GetCurrentTheoPosition();

            // Check exits first
            if (currentTheoPosition != 0)
            {
                if (ShouldExitPosition(bars, currentTheoPosition))
                {
                    ExecuteTheoreticalExit(bars, currentTheoPosition);
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
            DualPositionManager?.TheoPositionManager?.UpdateTradeMetric(signalBar);
            DualPositionManager?.ActualPositionManager?.UpdateTradeMetric(signalBar);
        }

        private bool ShouldExitPosition(Bar[] bars, int currentPosition)
        {
            Bar signalBar = GetSignalBar(bars);

            Bar executionBar = GetExecutionInstrumentBar(bars);

            if (ShouldExitAllPositions(signalBar.DateTime))
                return true;

            if (!isStatisticsReady)
                return false;

            // Check stop/take profit
            double pnlPercent = CalculateUnrealizedPnLPercent(executionBar.Close);

            //if (pnlPercent < -stopLossPercent || pnlPercent > takeProfitPercent)
            //    return true;

            // Mean reversion exit
            double deviation = (signalBar.Close - movingAverage) / standardDeviation;

            if (currentPosition > 0)
                return signal < exitThreshold;
            else
                return signal > -exitThreshold;
        }

        public override bool ShouldEnterLongPosition(Bar[] bars)
        {
            Bar signalBar = GetSignalBar(bars);

            if (!IsWithinTradingHours(signalBar.DateTime) || !CanEnterNewPosition(signalBar.DateTime))
                return false;

            if (!isStatisticsReady)
                return false;

            //double deviation = (signalBar.Close - movingAverage) / standardDeviation;
            return signal > dailyMad * entryThreshold;
        }

        public override bool ShouldEnterShortPosition(Bar[] bars)
        {
            Bar signalBar = GetSignalBar(bars);

            if (!IsWithinTradingHours(signalBar.DateTime) || !CanEnterNewPosition(signalBar.DateTime))
                return false;

            if (!isStatisticsReady)
                return false;

            //double deviation = (signalBar.Close - movingAverage) / standardDeviation;
            return signal < -dailyMad * entryThreshold;
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

        private double CalculateUnrealizedPnLPercent(double currentPrice)
        {
            var theoManager = DualPositionManager?.TheoPositionManager;
            if (theoManager == null || theoManager.CurrentPosition == 0)
                return 0;

            if (theoManager.CurrentPosition > 0)
                return (currentPrice - theoManager.AveragePrice) / theoManager.AveragePrice;
            else
                return (theoManager.AveragePrice - currentPrice) / theoManager.AveragePrice;
        }

        private double EMA(double alpha, double x, double ema_x)
        {
            if (!double.IsNaN(ema_x))
                return ema_x += alpha * (x - ema_x);
            else
                return x;
        }

        // Feature update method
        public void UpdateFeatures(Double[][] dataArray)
        {
            Features = dataArray.SelectMany(subArray => subArray).ToArray();

            if (Features.Length > 0)
            {
                double[] featureVector = GetBinnedFeaturesArray(BinsMatrix, Features);

                double sum = 0;
                for (int i = 0; i < featureVector.Length; i++)
                {
                    sum += featureVector[i] * WeightsArray[i];
                }
            }
        }

        // Binning methods
        public SimpleMatrix<double> GetBins(SimpleMatrix<double> bin, double[] alphas)
        {
            int rows = bin.RowCount;
            int cols = bin.ColumnCount;
            int bins = bin.ColumnCount - 1;

            var outputMatrix = SimpleMatrix<double>.Build.Dense(rows, bins);

            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    double value = alphas[i];

                    for (int b = 0; b < bins; b++)
                    {
                        if (value > bin[i, b] && value <= bin[i, b + 1])
                        {
                            outputMatrix[i, b] = 1.0;
                        }
                        else
                        {
                            outputMatrix[i, b] = 0;
                        }
                    }
                }
            }

            return outputMatrix;
        }

        public double[] GetBinnedFeaturesArray(SimpleMatrix<double> bin, double[] alphas)
        {
            int rows = bin.RowCount;
            int cols = bin.ColumnCount;
            int bins = bin.ColumnCount - 1;

            var outputMatrix = new double[FeatureCount * 4];
            int index = 0;

            for (int i = 0; i < FeatureCount; i++)
            {
                for (int j = 0; j < 4; j++)
                {
                    double value = alphas[i];

                    if (value > bin[i, j] && value <= bin[i, j + 1])
                    {
                        outputMatrix[index] = 1.0;
                    }
                    else
                    {
                        outputMatrix[index] = 0.0;
                    }
                    index++;
                }
            }
            return outputMatrix;
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
    }
}