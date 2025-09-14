using System;
using System.Collections.Generic;
using SmartQuant;
using Parameters;
using SmartQuant.Strategy_;

namespace StrategyManagement
{
    public class MeanReversionStrategyManager : BaseStrategyManager
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

        public MeanReversionStrategyManager(Instrument tradeInstrument) : base("MeanReversion", tradeInstrument)
        {
            priceWindow = new Queue<double>();
            isStatisticsReady = false;
        }

        public override void Initialize(StrategyParameters parameters)
        {
            base.Initialize(parameters); // This now handles both signal and trade configuration

            // Existing parameter initialization...
            lookbackPeriod = 120;
            entryThreshold = 2.0;
            exitThreshold = 1.0;
            stopLossPercent = 0.03;
            takeProfitPercent = 0.05;

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

            // Update statistics
            priceWindow.Enqueue(signalBar.Close);

            if (priceWindow.Count > lookbackPeriod)
            {
                priceWindow.Dequeue();
            }

            if (priceWindow.Count >= lookbackPeriod)
            {
                CalculateStatistics();
                isStatisticsReady = true;
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

            if (pnlPercent < -stopLossPercent || pnlPercent > takeProfitPercent)
                return true;

            // Mean reversion exit
            double deviation = (signalBar.Close - movingAverage) / standardDeviation;

            if (currentPosition > 0)
                return deviation > -exitThreshold;
            else
                return deviation < exitThreshold;
        }

        public override bool ShouldEnterLongPosition(Bar[] bars)
        {
            Bar signalBar = GetSignalBar(bars);

            if (!IsWithinTradingHours(signalBar.DateTime) || !CanEnterNewPosition(signalBar.DateTime))
                return false;

            if (!isStatisticsReady)
                return false;

            double deviation = (signalBar.Close - movingAverage) / standardDeviation;
            return deviation < -entryThreshold;
        }

        public override bool ShouldEnterShortPosition(Bar[] bars)
        {
            Bar signalBar = GetSignalBar(bars);

            if (!IsWithinTradingHours(signalBar.DateTime) || !CanEnterNewPosition(signalBar.DateTime))
                return false;

            if (!isStatisticsReady)
                return false;

            double deviation = (signalBar.Close - movingAverage) / standardDeviation;
            return deviation > entryThreshold;
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
                return tradeBar.Close;// (Parameters.inst_tick_size * 2);
            else
                return tradeBar.Close;//Parameters.inst_tick_size * 2);
        }

        public override double GetExitPrice(Bar[] bars, OrderSide side)
        {
            // Use the TRADE instrument bar for pricing
            Bar tradeBar = GetExecutionInstrumentBar(bars);
            return tradeBar.Close;
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
    }
}