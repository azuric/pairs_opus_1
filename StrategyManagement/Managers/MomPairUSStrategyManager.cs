using System;
using System.Collections.Generic;
using SmartQuant;
using Parameters;
using SmartQuant.Strategy_;
using SmartQuant.Statistics;

namespace StrategyManagement
{
    public class MomPairUSStrategyManager : BaseStrategyManager
    {
        private double overNightSundayThreshLowS;

        // Statistics
        private double entryThreshold;
        private double exitThreshold;
        private double stopLossPercent;
        private double takeProfitPercent;
        private bool isStatisticsReady;

        private double signal_ma;
        private double signal;

        //parameters
        private double alpha;
        private double minimumThreshold;
        private double maximumThreshold;
        private double overNightThreshHigh;
        private double overNightThreshLow;
        private double overNightSundayThreshHigh;
        private double overNightSundayThreshLow;
        private double overNightThreshHighS;
        private double overNightThreshLowS;
        private double overNightSundayThreshHighS;
        private double OpenAtEleven;
        private double OpenAtElevenBps;
        private double OpenAtOne;
        private double OvernightBps;
        private double lookBackPeriod;
        private double dailyMad;
        private double mad;
        private int positionSize;

        private DateTime currentDate;
        private double closeAtNine;

        public MomPairUSStrategyManager(Instrument tradeInstrument) : base("MomPairUSReversion", tradeInstrument)
        {
            isStatisticsReady = false;
            currentDate = new DateTime();
        }

        public override void Initialize(StrategyParameters parameters)
        {
            base.Initialize(parameters); // This now handles both signal and trade configuration

            lookBackPeriod = (double)parameters.additional_params["lookBackPeriod"];
            alpha = 1.0 / lookBackPeriod;
            minimumThreshold = (double)parameters.additional_params["minimumThreshold"];
            maximumThreshold = (double)parameters.additional_params["maximumThreshold"];
            overNightThreshHigh = (double)parameters.additional_params["overNightThreshHigh"];
            overNightThreshLow = (double)parameters.additional_params["overNightThreshLow"];
            overNightSundayThreshHigh = (double)parameters.additional_params["overNightSundayThreshHigh"];
            overNightSundayThreshLow = (double)parameters.additional_params["overNightSundayThreshLow"];
            overNightThreshHighS = (double)parameters.additional_params["overNightThreshHighS"];
            overNightThreshLowS = (double)parameters.additional_params["overNightThreshLowS"];
            overNightSundayThreshHighS = (double)parameters.additional_params["overNightSundayThreshHighS"];
            overNightSundayThreshLowS = (double)parameters.additional_params["overNightSundayThreshLowS"];

            entryThreshold = (double)parameters.threshold_entry[0][0];
            exitThreshold = (double)parameters.threshold_exit[0][0];
            positionSize = (int)parameters.position_size;

            // Existing parameter initialization...
            //lookbackPeriod = 120;
            //entryThreshold = 2.0;
            //exitThreshold = 1.0;
            //stopLossPercent = 0.03;
            //takeProfitPercent = 0.05;

            // Parse strategy-specific parameters...
            // (existing parameter parsing code)

            // NEW: Enhanced logging
            Console.WriteLine($"  MomPairUSStrategy {Name} initialized:");
            Console.WriteLine($"  Signal Source: {GetSignalSourceDescription()}");
            Console.WriteLine($"  Trade Instrument: {GetExecutionInstrumentDescription()}");
            Console.WriteLine($"  Lookback Period: {lookBackPeriod}");
            Console.WriteLine($"  Entry Threshold: {entryThreshold}");
            Console.WriteLine($"  Exit Threshold: {exitThreshold}");
            Console.WriteLine($"  Stop Loss: {stopLossPercent:P2}");
            Console.WriteLine($"  Take Profit: {takeProfitPercent:P2}");

            // NEW: Validate configuration
            ValidateSignalTradeConfiguration();
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

        public override void ProcessBar(Bar[] bars, double accountValue)
        {
            Bar signalBar = GetSignalBar(bars);
            CancelCurrentOrder();
            int currentTheoPosition = GetCurrentTheoPosition();

            if (Math.Abs(currentTheoPosition) > 1)
                Console.WriteLine();

            // CRITICAL FIX: Check forced exit time FIRST
            if (currentTheoPosition != 0 && ShouldExitAllPositions(signalBar.DateTime) && !HasLiveOrder())
            {
                Console.WriteLine($"FORCED EXIT at {signalBar.DateTime}: Closing position {currentTheoPosition}");
                ExecuteTheoreticalExit(bars, currentTheoPosition);
                return;
            }

            // Check normal exits
            else if (currentTheoPosition != 0 && !HasLiveOrder())
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
                    ExecuteTheoreticalEntry(bars, OrderSide.Buy, accountValue);
                }
                else if (ShouldEnterShortPosition(bars))
                {
                    ExecuteTheoreticalEntry(bars, OrderSide.Sell, accountValue);
                }
            }
        }

        public override void OnBar(Bar[] bars)
        {
            Bar signalBar = GetSignalBar(bars);
            Bar tradeBar = GetExecutionInstrumentBar(bars);

            signal_ma = EMA(alpha, signalBar.Close, signal_ma);

            signal = (signalBar.Close / signal_ma) - 1.0;

            if (tradeBar.CloseDateTime.TimeOfDay == new TimeSpan(21, 0, 0))
                closeAtNine = tradeBar.Close;

            if (tradeBar.CloseDateTime.TimeOfDay == new TimeSpan(23, 0, 0))
            {
                OpenAtEleven = tradeBar.Close;
                OpenAtElevenBps = (OpenAtEleven - tradeBar.Close) / closeAtNine;
            }

            if (tradeBar.CloseDateTime.TimeOfDay == new TimeSpan(13, 0, 0))
            {
                OpenAtOne = tradeBar.Close;
                OvernightBps = (OpenAtOne - closeAtNine) / closeAtNine;
            }

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

            // Update metrics
            DualPositionManager?.TheoPositionManager?.UpdateTradeMetric(tradeBar);
            DualPositionManager?.ActualPositionManager?.UpdateTradeMetric(tradeBar);
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

            if (Math.Abs(currentPosition) > 1)
                Console.WriteLine();


            if (currentPosition > 0)
                return signal < exitThreshold;
            else
                return signal > exitThreshold;
        }

        public override bool ShouldEnterLongPosition(Bar[] bars)
        {
            Bar signalBar = GetSignalBar(bars);

            if (!IsWithinTradingHours(signalBar.DateTime) || !CanEnterNewPosition(signalBar.DateTime))
                return false;

            if (!isStatisticsReady)
                return false;

            bool isSignal = signal > Math.Max(dailyMad * entryThreshold, minimumThreshold) && signal < maximumThreshold;
            bool isTimeFilter = ((OpenAtElevenBps < overNightSundayThreshHigh && OpenAtElevenBps > overNightSundayThreshLow && signalBar.CloseDateTime.DayOfWeek == DayOfWeek.Sunday)
                || (OvernightBps < overNightThreshHigh && OvernightBps > overNightThreshLow && signalBar.CloseDateTime.DayOfWeek != DayOfWeek.Sunday));

            return isSignal && isTimeFilter;
        }

        public override bool ShouldEnterShortPosition(Bar[] bars)
        {
            Bar signalBar = GetSignalBar(bars);

            if (!IsWithinTradingHours(signalBar.DateTime) || !CanEnterNewPosition(signalBar.DateTime))
                return false;

            if (!isStatisticsReady)
                return false;

            bool isSignal = signal < Math.Min(-dailyMad * entryThreshold, -minimumThreshold) && signal > -maximumThreshold;
            bool isTimeFilter = ((OpenAtElevenBps > -overNightSundayThreshHighS && OpenAtElevenBps < -overNightSundayThreshLowS && signalBar.CloseDateTime.DayOfWeek == DayOfWeek.Sunday)
                || (OvernightBps > -overNightThreshHighS && OvernightBps < -overNightThreshLowS && signalBar.CloseDateTime.DayOfWeek != DayOfWeek.Sunday));

            return isSignal;
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

        public override int CalculatePositionSize(Bar[] bars, double accountValue)
        {
            return Parameters?.max_position_size ?? 1;
        }

        public override double GetEntryPrice(Bar[] bars, OrderSide side)
        {
            // Use the TRADE instrument bar for pricing, not the signal bar

            Bar tradeBar = GetExecutionInstrumentBar(bars);

            if (side == OrderSide.Buy)
                return tradeBar.Close;
            else
                return tradeBar.Close;
        }

        public override double GetExitPrice(Bar[] bars, OrderSide side)
        {
            // Use the TRADE instrument bar for pricing
            Bar tradeBar = GetExecutionInstrumentBar(bars);
            return tradeBar.Close;
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
    }
}