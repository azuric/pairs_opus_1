using System;
using System.Collections.Generic;
using System.Linq;
using SmartQuant;
using Parameters;

namespace StrategyManagement
{
    public class MomentumStrategyManager : BaseStrategyManager
    {
        private readonly Queue<Bar> barHistory;
        private int momentumPeriod;
        private double entryMomentumThreshold;
        private double exitMomentumThreshold;
        private double currentMomentum;
        private double stopLossPercent;
        private double takeProfitPercent;



        public MomentumStrategyManager(Instrument tradeInstrument) : base("Momentum", tradeInstrument)
        {
            barHistory = new Queue<Bar>();
        }

        public override void Initialize(StrategyParameters parameters)
        {
            base.Initialize(parameters);

            momentumPeriod = 120;
            entryMomentumThreshold = 0.02;
            exitMomentumThreshold = 0.0;
            stopLossPercent = 0.02;
            takeProfitPercent = 0.05;

            if (parameters.threshold_entry != null && parameters.threshold_entry.Length > 0)
            {
                momentumPeriod = (int)parameters.threshold_entry[0][0];
                if (parameters.threshold_entry[0].Length > 1)
                    entryMomentumThreshold = parameters.threshold_entry[0][1];
            }

            if (parameters.threshold_exit != null && parameters.threshold_exit.Length > 0)
            {
                exitMomentumThreshold = parameters.threshold_exit[0][0];
                if (parameters.threshold_exit[0].Length > 1)
                    stopLossPercent = parameters.threshold_exit[0][1];
            }
        }

        // Consolidated bar processing and trading logic
        public override void OnBar(Bar[] bars)
        {
            Bar signalBar = GetSignalBar(bars);

            // 1. Calculate momentum (from old OnBar)
            barHistory.Enqueue(signalBar);
            if (barHistory.Count > momentumPeriod)
            {
                barHistory.Dequeue();
            }

            if (barHistory.Count >= momentumPeriod)
            {
                CalculateMomentum();
            }

            // If in pair mode, could also track constituent momentum
            if (isPairMode && bars.Length > 2)
            {
                Bar numBar = GetNumeratorBar(bars);
                Bar denBar = GetDenominatorBar(bars);
                // Could track separate momentum for analysis
            }

            // 2. Cancel any existing orders
            CancelCurrentOrder();

            int currentTheoPosition = GetCurrentTheoPosition();

            // 3. Check exit conditions (from old ProcessBar)
            if (currentTheoPosition != 0)
            {
                if (ShouldExitPosition(bars))
                {
                    ExecuteTheoreticalExit(bars, currentTheoPosition);
                    return;
                }
            }

            // 4. Check entry conditions (from old ShouldEnter methods)
            if (currentTheoPosition == 0 && !HasLiveOrder() && barHistory.Count >= momentumPeriod)
            {
                if (IsWithinTradingHours(signalBar.DateTime) && CanEnterNewPosition(signalBar.DateTime))
                {
                    if (currentMomentum > entryMomentumThreshold)
                    {
                        ExecuteTheoreticalEntry(bars, OrderSide.Buy);
                    }
                    else if (currentMomentum < -entryMomentumThreshold)
                    {
                        ExecuteTheoreticalEntry(bars, OrderSide.Sell);
                    }
                }
            }
        }

        private bool ShouldExitPosition(Bar[] bars)
        {
            Bar signalBar = GetSignalBar(bars);
            int position = GetCurrentTheoPosition();

            if (ShouldExitAllPositions(signalBar.DateTime))
                return true;

            double pnlPercent = CalculatePnLPercent(signalBar.Close);
            //if (pnlPercent < -stopLossPercent || pnlPercent > takeProfitPercent)
            //    return true;

            if (position > 0)
                return currentMomentum < exitMomentumThreshold;

            else if (position < 0)
                return currentMomentum > -exitMomentumThreshold;

            return false;
        }



        private void CalculateMomentum()
        {
            if (barHistory.Count < 2)
            {
                currentMomentum = 0;
                return;
            }

            var oldestBar = barHistory.First();
            var newestBar = barHistory.Last();
            currentMomentum = (newestBar.Close - oldestBar.Close) / oldestBar.Close;
        }

        private double CalculatePnLPercent(double currentPrice)
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