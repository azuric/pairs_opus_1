using Newtonsoft.Json;
using Parameters;
using SmartQuant;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StrategyManagement
{
    /// <summary>
    /// Ornstein-Uhlenbeck (OU) Mean Reversion Strategy Manager
    /// Implements OU optimal stopping feature for mean reversion trading
    /// With integrated HAR volatility forecasting
    /// </summary>
    public class OUStrategyManager : BaseStrategyManager
    {
        #region Strategy Parameters

        // OU Feature Calculation
        private readonly Queue<double> priceWindow;
        private int entryWindow;           // ENTRY_WINDOW: Lookback for OU calculation (minutes) - 48 hours
        private double threshold;          // THRESHOLD: Z-score threshold for entry
        private int exitWindow;            // EXIT_WINDOW: Exit MA period (minutes) - 8 hours
        private double stopAtrMult;        // STOP_ATR_MULT: Stop loss multiplier
        private double intercept;
        private double[] coefficients;

        // OU Statistics
        private double movingAverage;
        private double standardDeviation;
        private double featureOU;          // Z-score: (close - ma) / std
        private double featureOUInv;       // Inverted for long-when-oversold logic
        private bool isStatisticsReady;

        // ATR for stop loss
        private readonly Queue<double> atrWindow;
        private double currentATR;
        private double stopPrice;
        private double targetPrice;

        // Exit MA
        private readonly Queue<double> exitMAWindow;
        private double exitMA;

        #endregion

        #region Volatility Model Components

        // 30-minute bar aggregation (for volatility calculation)
        private readonly List<Bar> currentBar30m;
        private DateTime lastBar30mTime;

        // Yang-Zhang volatility calculation (30-min bars)
        private readonly Queue<BarVolatility> volBars30m;
        private const int YZ_WINDOW = 2;

        // HAR features (30-min bars) - these are on 30-min timeframe
        private readonly Queue<double> deseasonalizedVol;  // Target volatility for HAR
        private const int LAG_D_BARS = 13;   // ~6.5 hours at 30-min bars
        private const int LAG_W_BARS = 65;   // ~32.5 hours at 30-min bars

        // Asymmetric volatility features (30-min bars)
        private double lastReturn30m;
        private double volPos;
        private double volNeg;

        // Seasonal factors (static lookup by time of day)
        private readonly Dictionary<TimeSpan, double> seasonalFactors;

        // Current volatility forecast (updated every 30 minutes)
        private double forecastVolDeseason;
        private double forecastVol;
        private double forecastVolPrice;

        // Helper class for Yang-Zhang calculation
        private class BarVolatility
        {
            public double Open { get; set; }
            public double High { get; set; }
            public double Low { get; set; }
            public double Close { get; set; }
            public double YZVol { get; set; }
            public DateTime Time { get; set; }
        }

        #endregion

        #region Constructor

        public OUStrategyManager(Instrument tradeInstrument) : base("ou_strategy", tradeInstrument)
        {
            priceWindow = new Queue<double>();
            atrWindow = new Queue<double>();
            exitMAWindow = new Queue<double>();
            coefficients = new double[5];
            isStatisticsReady = false;

            // Volatility model initialization
            currentBar30m = new List<Bar>();
            volBars30m = new Queue<BarVolatility>();
            deseasonalizedVol = new Queue<double>();
            seasonalFactors = new Dictionary<TimeSpan, double>();
            lastBar30mTime = DateTime.MinValue;
        }

        #endregion

        #region Initialization

        public override void Initialize(StrategyParameters parameters)
        {
            base.Initialize(parameters); // Handles signal and execution configuration

            // Default OU parameters
            entryWindow = 1380;      // 23 hours
            threshold = 3.0;         // 2 standard deviations
            exitWindow = 480;        // 8 hours
            stopAtrMult = 2.0;       // 2x ATR stop

            // Parse strategy-specific parameters if provided
            if (parameters != null && parameters.additional_params != null)
            {
                if (parameters.additional_params.ContainsKey("entry_window"))
                    entryWindow = Convert.ToInt32(parameters.additional_params["entry_window"]);
                if (parameters.additional_params.ContainsKey("threshold"))
                    threshold = Convert.ToDouble(parameters.additional_params["threshold"]);
                if (parameters.additional_params.ContainsKey("exit_window"))
                    exitWindow = Convert.ToInt32(parameters.additional_params["exit_window"]);
                if (parameters.additional_params.ContainsKey("stop_atr_mult"))
                    stopAtrMult = Convert.ToDouble(parameters.additional_params["stop_atr_mult"]);

                // Volatility model parameters
                if (parameters.additional_params.ContainsKey("intercept"))
                    intercept = Convert.ToDouble(parameters.additional_params["intercept"]);

                if (parameters.additional_params.ContainsKey("coefficients"))
                {
                    string coeffJson = parameters.additional_params["coefficients"].ToString();
                    coefficients = JsonConvert.DeserializeObject<double[]>(coeffJson);
                }

                // Load seasonal factors if provided
                if (parameters.additional_params.ContainsKey("factors"))
                {
                    string seasonalJson = parameters.additional_params["factors"].ToString();
                    var seasonalDict = JsonConvert.DeserializeObject<Dictionary<string, double>>(seasonalJson);

                    foreach (var kvp in seasonalDict)
                    {
                        TimeSpan time = TimeSpan.Parse(kvp.Key);
                        seasonalFactors[time] = kvp.Value;
                    }
                }
            }

            Console.WriteLine($"OUStrategyManager {Name} initialized:");
            Console.WriteLine($"  Signal Source: {GetSignalSourceDescription()}");
            Console.WriteLine($"  Execution Instrument: {GetExecutionInstrumentDescription()}");
            Console.WriteLine($"  Entry Window: {entryWindow} minutes ({entryWindow / 60.0:F1} hours)");
            Console.WriteLine($"  Threshold: {threshold} std devs");
            Console.WriteLine($"  Exit Window: {exitWindow} minutes ({exitWindow / 60.0:F1} hours)");
            Console.WriteLine($"  Stop Loss: {stopAtrMult}x ATR");
            Console.WriteLine($"  Volatility Model: Intercept={intercept:F6}, Coefficients={coefficients.Length}");

            ValidateSignalTradeConfiguration();
        }

        #endregion

        #region Configuration Validation

        private void ValidateSignalTradeConfiguration()
        {
            if (!isPairMode)
            {
                Console.WriteLine("Single instrument mode: Signal and execution instrument are the same.");
                return;
            }

            if (signalSource == executionInstrumentSource)
            {
                Console.WriteLine($"Using {signalSource} for both signals and trading.");
            }
            else
            {
                Console.WriteLine($"Cross-instrument strategy: Signals from {signalSource}, trading {executionInstrumentSource}");

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

        #endregion

        #region Main Processing

        public void ProcessBar(Bar[] bars)
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
                    // Set stop price on entry
                    Bar executionBar = GetExecutionInstrumentBar(bars);
                    stopPrice = executionBar.Close - (forecastVolPrice * stopAtrMult);
                    targetPrice = executionBar.Close + (forecastVolPrice * stopAtrMult);

                    ExecuteTheoreticalEntry(bars, OrderSide.Buy);
                    Console.WriteLine($"Stop price set at: {stopPrice:F4} (Entry: {executionBar.Close:F4}, ATR: {currentATR:F4})");
                }
            }
        }

        public override void OnBar(Bar[] bars)
        {
            Bar signalBar = GetSignalBar(bars);
            Bar executionBar = GetExecutionInstrumentBar(bars);

            // Update volatility model (processes 1-min to 30-min aggregation)
            UpdateVolatilityModel(signalBar);

            // Update OU feature calculation
            UpdateOUFeature(signalBar);

            // Update ATR for stop loss
            UpdateATR(executionBar);

            // Update exit MA
            UpdateExitMA(signalBar);

            ProcessBar(bars);

            // Update metrics
            DualPositionManager?.TheoPositionManager?.UpdateTradeMetric(signalBar);
            DualPositionManager?.ActualPositionManager?.UpdateTradeMetric(signalBar);
        }

        #endregion

        #region Volatility Model - 30-Minute Bar Aggregation

        /// <summary>
        /// Aggregate 1-minute bars to 30-minute bars and update volatility model
        /// </summary>
        private void UpdateVolatilityModel(Bar bar1min)
        {
            // Determine which 30-minute period this bar belongs to
            DateTime bar30mPeriod = RoundDownTo30Minutes(bar1min.DateTime);

            // If we're in a new 30-minute period, finalize the previous one
            if (bar30mPeriod != lastBar30mTime && currentBar30m.Count > 0)
            {
                // Create completed 30-min bar
                Bar bar30m = Create30MinuteBar(currentBar30m);

                // Calculate Yang-Zhang volatility and update HAR features
                UpdateVolatilityFeatures(bar30m);

                // Generate forecast
                GenerateVolatilityForecast(bar30m);

                // Clear for next period
                currentBar30m.Clear();
                lastBar30mTime = bar30mPeriod;
            }

            // Add current 1-min bar to accumulator
            if (currentBar30m.Count == 0)
            {
                lastBar30mTime = bar30mPeriod;
            }
            currentBar30m.Add(bar1min);
        }

        private DateTime RoundDownTo30Minutes(DateTime dt)
        {
            int minute = (dt.Minute / 30) * 30;
            return new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, minute, 0);
        }

        private Bar Create30MinuteBar(List<Bar> bars1min)
        {
            if (bars1min.Count == 0) return null;

            double o = bars1min[0].Open;
            double h = bars1min.Max(b => b.High);
            double l = bars1min.Min(b => b.Low);
            double c = bars1min[bars1min.Count - 1].Close;
            long v = (long)bars1min.Sum(b => b.Volume);
            DateTime openTime = bars1min[bars1min.Count - 1].OpenDateTime;
            DateTime closeTime = bars1min[bars1min.Count - 1].CloseDateTime;
            return new Bar(openTime,closeTime, 0, 0, 1800, o, h, l, c, v, 0);
        }

        #endregion

        #region Volatility Model - Yang-Zhang Calculation

        /// <summary>
        /// Calculate Yang-Zhang volatility for a 30-minute bar
        /// </summary>
        private void UpdateVolatilityFeatures(Bar bar30m)
        {
            // Create volatility bar structure
            var volBar = new BarVolatility
            {
                Open = bar30m.Open,
                High = bar30m.High,
                Low = bar30m.Low,
                Close = bar30m.Close,
                Time = bar30m.DateTime
            };

            // Calculate Yang-Zhang volatility if we have enough history
            if (volBars30m.Count >= YZ_WINDOW)
            {
                volBar.YZVol = CalculateYangZhangVolatility(volBar);

                // Deseasonalize
                double seasonalFactor = GetSeasonalFactor(bar30m.DateTime.TimeOfDay);
                double volDeseason = volBar.YZVol / seasonalFactor;

                // Store deseasonalized vol
                deseasonalizedVol.Enqueue(volDeseason);
                if (deseasonalizedVol.Count > LAG_W_BARS)
                {
                    deseasonalizedVol.Dequeue();
                }

                // Update asymmetric features (simple return)
                double simpleReturn = (bar30m.Close / volBars30m.Last().Close) - 1.0;
                double retPos = simpleReturn > 0 ? simpleReturn : 0;
                double retNeg = simpleReturn < 0 ? Math.Abs(simpleReturn) : 0;

                // Convert to volatility units (rolling window of 1 bar as per Python)
                volPos = Math.Sqrt(retPos * retPos);
                volNeg = Math.Sqrt(retNeg * retNeg);

                lastReturn30m = simpleReturn;
            }

            // Add to history
            volBars30m.Enqueue(volBar);
            if (volBars30m.Count > LAG_W_BARS + YZ_WINDOW)
            {
                volBars30m.Dequeue();
            }
        }

        /// <summary>
        /// Calculate Yang-Zhang volatility estimator
        /// </summary>
        private double CalculateYangZhangVolatility(BarVolatility currentBar)
        {
            var bars = volBars30m.ToList();
            bars.Add(currentBar);

            if (bars.Count < YZ_WINDOW) return 0;

            // Take last YZ_WINDOW bars (now includes currentBar)
            var window = bars.Skip(Math.Max(0, bars.Count - YZ_WINDOW)).ToList();

            double sumOJ = 0;
            double sumOC = 0;
            double sumRS = 0;

            for (int i = 0; i < window.Count; i++)
            {
                double o = Math.Log(window[i].Open);
                double h = Math.Log(window[i].High);
                double l = Math.Log(window[i].Low);
                double c = Math.Log(window[i].Close);

                // Overnight volatility (gap)
                if (i > 0)
                {
                    double c_prev = Math.Log(window[i - 1].Close);
                    double sigma_oj = Math.Pow(o - c_prev, 2);
                    sumOJ += sigma_oj;
                }

                // Open to close volatility
                double sigma_oc = Math.Pow(c - o, 2);
                sumOC += sigma_oc;

                // Rogers-Satchell volatility
                double sigma_rs = (h - c) * (h - o) + (l - c) * (l - o);
                sumRS += sigma_rs;
            }

            double k = 0.34 / (1.34 + (YZ_WINDOW + 1.0) / (YZ_WINDOW - 1.0));

            double term1 = sumOJ / YZ_WINDOW;
            double term2 = sumOC / YZ_WINDOW;
            double term3 = sumRS / YZ_WINDOW;

            double yz_var = term1 + k * term2 + (1 - k) * term3;

            return Math.Sqrt(Math.Abs(yz_var));  // Abs for safety
        }

        /// <summary>
        /// Get seasonal factor by finding the time bucket the current time falls into.
        /// Iterates keys to find (Key <= time < NextKey).
        /// </summary>
        private double GetSeasonalFactor(TimeSpan timeOfDay)
        {
            if (seasonalFactors == null || seasonalFactors.Count == 0)
                return 1.0;

            // 1. Sort keys to ensure we iterate in time order
            var sortedKeys = seasonalFactors.Keys.OrderBy(k => k).ToList();

            // 2. Iterate to find the correct interval
            for (int i = 0; i < sortedKeys.Count; i++)
            {
                TimeSpan currentKey = sortedKeys[i];
                TimeSpan nextKey = (i < sortedKeys.Count - 1)
                    ? sortedKeys[i + 1]
                    : currentKey.Add(TimeSpan.FromMinutes(30)); // Handle last key (assume 30m duration)

                // Check if our time is inside this bucket [CurrentKey, NextKey)
                if (timeOfDay >= currentKey && timeOfDay < nextKey)
                {
                    double factor = seasonalFactors[currentKey];
                    return factor > 0 ? factor : 1.0;
                }
            }

            // 3. Fallback: If time is outside all known buckets (e.g. pre-market/after-hours)
            // You might want to return 1.0, or the closest factor. 
            // Here we return 1.0 to be safe (neutral volatility assumption).
            return 1.0;
        }

        #endregion

        #region Volatility Model - Forecast Generation

        /// <summary>
        /// Generate volatility forecast using HAR model
        /// Features: lag_1, lag_d, lag_w, vol_pos, vol_neg
        /// </summary>
        private void GenerateVolatilityForecast(Bar bar30m)
        {
            // Need enough history for all features
            if (deseasonalizedVol.Count < LAG_W_BARS)
            {
                forecastVol = 0;
                forecastVolPrice = 0;
                return;
            }

            var volList = deseasonalizedVol.ToList();

            // Calculate HAR features
            double lag1 = volList[volList.Count - 1];  // Most recent

            // lag_d: average of last 13 bars (~6.5 hours)
            double lagD = volList.Skip(Math.Max(0, volList.Count - LAG_D_BARS)).Take(LAG_D_BARS).Average();

            // lag_w: average of last 65 bars (~32.5 hours)
            double lagW = volList.Skip(Math.Max(0, volList.Count - LAG_W_BARS)).Take(LAG_W_BARS).Average();

            // Build feature vector: [lag_1, lag_d, lag_w, vol_pos, vol_neg]
            double[] features = new double[]
            {
                lag1,
                lagD,
                lagW,
                volPos,
                volNeg
            };

            // Linear regression prediction: intercept + sum(coef * feature)
            forecastVolDeseason = intercept;
            for (int i = 0; i < Math.Min(features.Length, coefficients.Length); i++)
            {
                forecastVolDeseason += coefficients[i] * features[i];
            }

            // Reseasonalize: multiply by seasonal factor
            double seasonalFactor = GetSeasonalFactor(bar30m.DateTime.TimeOfDay);
            forecastVol = forecastVolDeseason * seasonalFactor;

            // Convert to price units
            forecastVolPrice = forecastVol * bar30m.Close;

            // Console.WriteLine(bar30m.DateTime + " Volatitlity " + forecastVol);
            // Optional: Log for debugging
            // Console.WriteLine($"Vol Forecast: {forecastVol:F6} (deseason={forecastVolDeseason:F6}, " +
            //                  $"seasonal={seasonalFactor:F4}, price_units={forecastVolPrice:F4})");
        }

        #endregion

        #region OU Feature Calculation

        /// <summary>
        /// Calculate OU optimal stopping feature
        /// Approximates OU mean reversion using rolling z-score
        /// Negative z-score = Price below mean = Long signal
        /// </summary>
        private void UpdateOUFeature(Bar signalBar)
        {
            priceWindow.Enqueue(signalBar.Close);

            if (priceWindow.Count > entryWindow)
            {
                priceWindow.Dequeue();
            }

            if (priceWindow.Count >= entryWindow)
            {
                CalculateOUStatistics();
                isStatisticsReady = true;
            }
        }

        private void CalculateOUStatistics()
        {
            if (priceWindow.Count == 0) return;

            // Calculate moving average
            double sum = 0;
            foreach (var price in priceWindow)
                sum += price;

            movingAverage = sum / priceWindow.Count;

            // Calculate standard deviation
            double sumSquaredDeviations = 0;
            foreach (var price in priceWindow)
                sumSquaredDeviations += Math.Pow(price - movingAverage, 2);

            standardDeviation = Math.Sqrt(sumSquaredDeviations / priceWindow.Count);

            // Calculate OU feature (z-score)
            // Get the latest price (last in queue)
            double currentPrice = 0;
            foreach (var price in priceWindow)
                currentPrice = price; // Will end up with last price

            featureOU = (currentPrice - movingAverage) / (forecastVolPrice + 1e-9);

            // Invert for long-when-oversold logic
            // Negative z-score (price below mean) becomes positive signal
            featureOUInv = -featureOU;
        }

        #endregion

        #region ATR Calculation

        /// <summary>
        /// Calculate Average True Range for stop loss
        /// </summary>
        private void UpdateATR(Bar executionBar)
        {
            // Calculate True Range
            double high = executionBar.High;
            double low = executionBar.Low;
            double prevClose = executionBar.Close; // Simplified - in real implementation, track previous close

            double tr = Math.Max(high - low, Math.Abs(high - prevClose));

            atrWindow.Enqueue(tr);

            if (atrWindow.Count > 14)
            {
                atrWindow.Dequeue();
            }

            if (atrWindow.Count >= 14)
            {
                double sum = 0;
                foreach (var trValue in atrWindow)
                    sum += trValue;

                currentATR = sum / atrWindow.Count;
            }
        }

        #endregion

        #region Exit MA Calculation

        /// <summary>
        /// Calculate exit moving average
        /// </summary>
        private void UpdateExitMA(Bar signalBar)
        {
            exitMAWindow.Enqueue(signalBar.Close);

            if (exitMAWindow.Count > exitWindow)
            {
                exitMAWindow.Dequeue();
            }

            if (exitMAWindow.Count >= exitWindow)
            {
                double sum = 0;
                foreach (var price in exitMAWindow)
                    sum += price;

                exitMA = sum / exitMAWindow.Count;
            }
        }

        #endregion

        #region Entry Logic

        public bool ShouldEnterLongPosition(Bar[] bars)
        {
            Bar signalBar = GetSignalBar(bars);

            // Check time constraints
            if (!IsWithinTradingHours(signalBar.DateTime) || !CanEnterNewPosition(signalBar.DateTime))
                return false;

            // Need statistics ready
            if (!isStatisticsReady || atrWindow.Count < 14)
                return false;

            // OU Entry Signal: feature_ou_inv > threshold
            // This means price is significantly below mean (oversold)
            // featureOUInv = -(close - ma) / std
            // When close < ma by threshold std devs, featureOUInv > threshold
            bool ouSignal = featureOUInv > threshold;

            if (ouSignal)
            {
                Console.WriteLine($"OU Long Signal: featureOUInv={featureOUInv:F4} > threshold={threshold:F4} " +
                                $"(price={signalBar.Close:F4}, ma={movingAverage:F4}, std={standardDeviation:F4}, " +
                                $"forecastVol={forecastVol:F6}, forecastVolPrice={forecastVolPrice:F4})");
            }

            return ouSignal;
        }

        public bool ShouldEnterShortPosition(Bar[] bars)
        {
            Bar signalBar = GetSignalBar(bars);

            // Check time constraints
            if (!IsWithinTradingHours(signalBar.DateTime) || !CanEnterNewPosition(signalBar.DateTime))
                return false;

            // Need statistics ready
            if (!isStatisticsReady || atrWindow.Count < 14)
                return false;

            // OU Entry Signal for short: feature_ou_inv < -threshold
            // This means price is significantly above mean (overbought)
            // When close > ma by threshold std devs, featureOUInv < -threshold
            bool ouSignal = featureOUInv < -threshold;

            if (ouSignal)
            {
                Console.WriteLine($"OU Short Signal: featureOUInv={featureOUInv:F4} < -threshold={-threshold:F4} " +
                                $"(price={signalBar.Close:F4}, ma={movingAverage:F4}, std={standardDeviation:F4})");
            }

            return ouSignal;
        }

        #endregion

        #region Exit Logic

        private bool ShouldExitPosition(Bar[] bars, int currentPosition)
        {
            Bar signalBar = GetSignalBar(bars);
            Bar executionBar = GetExecutionInstrumentBar(bars);

            // Force exit at end of trading day
            if (ShouldExitAllPositions(signalBar.DateTime))
            {
                Console.WriteLine("Exiting position: End of trading day");
                return true;
            }

            // Need statistics ready
            if (!isStatisticsReady || exitMAWindow.Count < exitWindow)
                return false;

            // Check stop loss
            if (currentPosition > 0)
            {
                // Long position: stop if price drops below stop price
                if (executionBar.Close < stopPrice)
                {
                    Console.WriteLine($"Stop Loss Hit (Long): price={executionBar.Close:F4} < stop={stopPrice:F4}");
                    return true;
                }

                // Mean reversion target: exit when price crosses above target
                if (signalBar.Close > targetPrice)
                {
                    Console.WriteLine($"Target Hit (Long): price={signalBar.Close:F4} > target={targetPrice:F4}");
                    return true;
                }
            }

            return false;
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

        #endregion

        #region Public Accessors for Volatility

        /// <summary>
        /// Get current volatility forecast (in return units)
        /// </summary>
        public double GetForecastVolatility()
        {
            return forecastVol;
        }

        /// <summary>
        /// Get current volatility forecast in price units
        /// </summary>
        public double GetForecastVolatilityPrice()
        {
            return forecastVolPrice;
        }

        #endregion
    }
}