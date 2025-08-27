using System;
using System.Collections.Generic;
using SmartQuant;
using Parameters;

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

        public MeanReversionStrategyManager() : base("MeanReversion")
        {
            priceWindow = new Queue<double>();
            isStatisticsReady = false;
        }

        public override void Initialize(StrategyParameters parameters)
        {
            base.Initialize(parameters);

            lookbackPeriod = 120;
            entryThreshold = 2.0;
            exitThreshold = 1.0;
            stopLossPercent = 0.03;
            takeProfitPercent = 0.05;

            if (parameters.threshold_entry != null && parameters.threshold_entry.Length > 0)
            {
                lookbackPeriod = (int)parameters.threshold_entry[0][0];
                if (parameters.threshold_entry[0].Length > 1)
                    entryThreshold = parameters.threshold_entry[0][1];
            }

            if (parameters.threshold_exit != null && parameters.threshold_exit.Length > 0)
            {
                exitThreshold = parameters.threshold_exit[0][0];
                if (parameters.threshold_exit[0].Length > 1)
                    stopLossPercent = parameters.threshold_exit[0][1];
                if (parameters.threshold_exit[0].Length > 2)
                    takeProfitPercent = parameters.threshold_exit[0][2];
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

            if (ShouldExitAllPositions(signalBar.DateTime))
                return true;

            if (!isStatisticsReady)
                return false;

            // Check stop/take profit
            double pnlPercent = CalculateUnrealizedPnLPercent(signalBar.Close);
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
            Bar signalBar = GetSignalBar(bars);

            if (side == OrderSide.Buy)
                return signalBar.Close - (Parameters.inst_tick_size * 2);
            else
                return signalBar.Close + (Parameters.inst_tick_size * 2);
        }

        public override double GetExitPrice(Bar[] bars, OrderSide side)
        {
            Bar signalBar = GetSignalBar(bars);
            return signalBar.Close;
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