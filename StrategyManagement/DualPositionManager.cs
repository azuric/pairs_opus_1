// DualPositionManager.cs - New implementation
using System;
using SmartQuant;
using Parameters;

namespace StrategyManagement
{
    /// <summary>
    /// Manages both theoretical and actual positions
    /// </summary>
    public class DualPositionManager : IDualPositionManager
    {
        private readonly PositionManager theoPositionManager;
        private readonly PositionManager actualPositionManager;
        private readonly object lockObject = new object();

        public IPositionManager TheoPositionManager => theoPositionManager;
        public IPositionManager ActualPositionManager => actualPositionManager;

        public DualPositionManager(StrategyParameters parameters, string strategyName)
        {
            // Create two separate position managers with different file names
            theoPositionManager = new PositionManager(
                parameters,
                $"{strategyName}_theo_" + parameters.trade_instrument,
                "theo_trades.csv"
            );

            actualPositionManager = new PositionManager(
                parameters,
                $"{strategyName}_actual",
                "actual_trades.csv"
            );
        }

        public void UpdateTheoPosition(DateTime dateTime, OrderSide side, int quantity, double price)
        {
            lock (lockObject)
            {
                theoPositionManager.UpdatePosition(dateTime, side, quantity, price);

                // Log theoretical fill
                Console.WriteLine($"THEO Fill: {dateTime:yyyy-MM-dd HH:mm:ss.fffffff} " +
                                $"{side} {quantity} @ {price} | " +
                                $"Pos: {theoPositionManager.CurrentPosition}");
            }
        }

        public void UpdateActualPosition(DateTime dateTime, OrderSide side, int quantity, double price)
        {
            lock (lockObject)
            {
                actualPositionManager.UpdatePosition(dateTime, side, quantity, price);

                // Log actual fill
                Console.WriteLine($"ACTUAL Fill: {dateTime:yyyy-MM-dd HH:mm:ss.fffffff} " +
                                $"{side} {quantity} @ {price} | " +
                                $"Pos: {actualPositionManager.CurrentPosition}");
            }
        }

        public int CheckTheoActual()
        {
            lock (lockObject)
            {
                int theoPos = theoPositionManager.CurrentPosition;
                int actualPos = actualPositionManager.CurrentPosition;
                int discrepancy = theoPos - actualPos;

                if (discrepancy != 0)
                {
                    Console.WriteLine($"Position Discrepancy: Theo={theoPos}, Actual={actualPos}, " +
                                    $"Diff={discrepancy}");
                }

                return discrepancy;
            }
        }

        public void UpdateMetrics(Bar bar)
        {
            lock (lockObject)
            {
                theoPositionManager.UpdateTradeMetric(bar);
                actualPositionManager.UpdateTradeMetric(bar);
            }
        }

        public void SaveAllMetrics()
        {
            theoPositionManager.SaveCycleMetrics();
            actualPositionManager.SaveCycleMetrics();

            // Also save comparison metrics
            SaveComparisonMetrics();
        }

        public int GetPositionDiscrepancy()
        {
            lock (lockObject)
            {
                return theoPositionManager.CurrentPosition - actualPositionManager.CurrentPosition;
            }
        }

        private void SaveComparisonMetrics()
        {
            string outputPath = System.IO.Path.Combine(@"C:\tmp\Template", "performance_comparison.csv");

            using (var sw = new System.IO.StreamWriter(outputPath, false))
            {
                sw.WriteLine("Metric,Theoretical,Actual,Difference,Percentage");

                double theoPnL = theoPositionManager.RealizedPnL + theoPositionManager.UnrealizedPnL;
                double actualPnL = actualPositionManager.RealizedPnL + actualPositionManager.UnrealizedPnL;
                double pnlDiff = theoPnL - actualPnL;
                double pnlPct = actualPnL != 0 ? (pnlDiff / Math.Abs(actualPnL)) * 100 : 0;

                sw.WriteLine($"Total PnL,{theoPnL:F2},{actualPnL:F2},{pnlDiff:F2},{pnlPct:F2}%");
                sw.WriteLine($"Realized PnL,{theoPositionManager.RealizedPnL:F2}," +
                           $"{actualPositionManager.RealizedPnL:F2}," +
                           $"{theoPositionManager.RealizedPnL - actualPositionManager.RealizedPnL:F2}");
                sw.WriteLine($"Unrealized PnL,{theoPositionManager.UnrealizedPnL:F2}," +
                           $"{actualPositionManager.UnrealizedPnL:F2}," +
                           $"{theoPositionManager.UnrealizedPnL - actualPositionManager.UnrealizedPnL:F2}");
                sw.WriteLine($"Trade Count,{theoPositionManager.CycleMetrics.Count}," +
                           $"{actualPositionManager.CycleMetrics.Count}," +
                           $"{theoPositionManager.CycleMetrics.Count - actualPositionManager.CycleMetrics.Count}");
            }
        }
    }
}