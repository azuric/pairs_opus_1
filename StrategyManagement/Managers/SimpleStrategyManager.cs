using System;
using SmartQuant;
using Parameters;

namespace StrategyManagement
{
    public class SimpleStrategyManager : BaseStrategyManager
    {
        private bool shouldEnter;
        private int barCount;

        public SimpleStrategyManager() : base("Simple")
        {
            shouldEnter = true;
            barCount = 0;
        }

        public override void ProcessBar(Bar[] bars, double accountValue)
        {
            // Simple strategy just maintains a position
            int currentTheoPosition = GetCurrentTheoPosition();

            if (currentTheoPosition == 0 && shouldEnter && !HasLiveOrder())
            {
                ExecuteTheoreticalEntry(bars, OrderSide.Buy, accountValue);
            }
            else if (ShouldExitAllPositions(GetSignalBar(bars).DateTime) && currentTheoPosition != 0)
            {
                ExecuteTheoreticalExit(bars, currentTheoPosition);
            }
        }

        public override void OnBar(Bar[] bars)
        {
            barCount++;
            Bar signalBar = GetSignalBar(bars);

            Console.WriteLine($"Bar {barCount}: {signalBar.DateTime:yyyy-MM-dd HH:mm:ss} {signalBar.Close}");

            if (isPairMode && bars.Length > 2)
            {
                Console.WriteLine($"  Num: {bars[0].Close}, Den: {bars[1].Close}, Synth: {bars[2].Close}");
            }

            shouldEnter = true;
        }

        public override bool ShouldEnterLongPosition(Bar[] bars)
        {
            Bar signalBar = GetSignalBar(bars);

            if (!IsWithinTradingHours(signalBar.DateTime))
                return false;

            return GetCurrentTheoPosition() == 0 && shouldEnter;
        }

        public override bool ShouldEnterShortPosition(Bar[] bars)
        {
            return false; // Simple strategy doesn't short
        }

        public override bool ShouldExitLongPosition(Bar[] bars)
        {
            return ShouldExitAllPositions(GetSignalBar(bars).DateTime);
        }

        public override bool ShouldExitShortPosition(Bar[] bars)
        {
            return false;
        }

        public override int CalculatePositionSize(Bar[] bars, double accountValue)
        {
            return 1;
        }

        public override double GetEntryPrice(Bar[] bars, OrderSide side)
        {
            return GetSignalBar(bars).Close;
        }

        public override double GetExitPrice(Bar[] bars, OrderSide side)
        {
            return GetSignalBar(bars).Close;
        }
    }
}