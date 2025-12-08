using System;
using SmartQuant;
using Parameters;

namespace StrategyManagement
{
    public class SimpleStrategyManager : BaseStrategyManager
    {
        private bool shouldEnter;
        private int barCount;

        public SimpleStrategyManager(Instrument tradeInstrument) : base("Simple", tradeInstrument)
        {
            shouldEnter = true;
            barCount = 0;
        }

        // Consolidated bar processing and trading logic
        public override void OnBar(Bar[] bars)
        {
            barCount++;
            Bar signalBar = GetSignalBar(bars);

            // 1. Log bar information
            Console.WriteLine($"Bar {barCount}: {signalBar.DateTime:yyyy-MM-dd HH:mm:ss} {signalBar.Close}");

            if (isPairMode && bars.Length > 2)
            {
                Console.WriteLine($"  Num: {bars[0].Close}, Den: {bars[1].Close}, Synth: {bars[2].Close}");
            }

            shouldEnter = true;

            // 2. Simple strategy logic (from old ProcessBar)
            int currentTheoPosition = GetCurrentTheoPosition();

            // Check exit conditions
            if (ShouldExitAllPositions(signalBar.DateTime) && currentTheoPosition != 0)
            {
                ExecuteTheoreticalExit(bars, currentTheoPosition);
                return;
            }

            // Check entry conditions
            if (currentTheoPosition == 0 && shouldEnter && !HasLiveOrder())
            {
                if (IsWithinTradingHours(signalBar.DateTime))
                {
                    ExecuteTheoreticalEntry(bars, OrderSide.Buy);
                }
            }
        }



    }
}