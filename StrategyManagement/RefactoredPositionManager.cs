using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SmartQuant;
using Parameters;

namespace StrategyManagement
{
    /// <summary>
    /// Updated position manager with custom file naming
    /// </summary>
    public class PositionManager : IPositionManager
    {
        private readonly StrategyParameters parameters;
        private readonly string fileName;
        private readonly string realtimeFileName;
        private readonly List<TradeMetrics> cycleMetrics;
        private TradeMetrics currentTradeMetric;
        private readonly object lockObject = new object();

        public int CurrentPosition { get; private set; }
        public double AveragePrice { get; private set; }
        public double RealizedPnL { get; private set; }
        public double UnrealizedPnL { get; private set; }
        public double LastPrice { get; private set; }
        public DateTime FirstEntryTime { get; private set; }
        public DateTime LastEntryTime { get; private set; }
        public IReadOnlyList<TradeMetrics> CycleMetrics => cycleMetrics.AsReadOnly();

        public PositionManager(StrategyParameters parameters, string fileName, string realtimeFileName = null)
        {
            this.parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
            this.fileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
            this.realtimeFileName = realtimeFileName ?? $"{fileName}_realtime.csv";
            this.cycleMetrics = new List<TradeMetrics>();
            Reset();
        }

        public void UpdatePosition(DateTime dateTime, OrderSide side, int quantity, double price)
        {
            lock (lockObject)
            {
                int signedQuantity = side == OrderSide.Buy ? quantity : -quantity;
                UpdatePositionInternal(dateTime, signedQuantity, price);
            }
        }

        private void UpdatePositionInternal(DateTime dateTime, int signedQuantity, double price)
        {
            // New position
            if (CurrentPosition == 0)
            {
                StartNewPosition(dateTime, signedQuantity, price);
            }
            // Flipping position
            else if (IsFlippingPosition(signedQuantity))
            {
                CloseCurrentPosition(dateTime, price);
                StartNewPosition(dateTime, signedQuantity + CurrentPosition, price);
            }
            // Adding to position
            else if (IsAddingToPosition(signedQuantity))
            {
                AddToPosition(dateTime, signedQuantity, price);
            }
            // Reducing or closing position
            else
            {
                ReduceOrClosePosition(dateTime, signedQuantity, price);
            }

            CurrentPosition += signedQuantity;
        }

        private bool IsFlippingPosition(int signedQuantity)
        {
            return (double)signedQuantity / CurrentPosition < -1;
        }

        private bool IsAddingToPosition(int signedQuantity)
        {
            return (double)signedQuantity / CurrentPosition > 0;
        }

        private void StartNewPosition(DateTime dateTime, int quantity, double price)
        {
            AveragePrice = price;
            FirstEntryTime = dateTime;
            LastEntryTime = dateTime;

            var side = quantity > 0 ? OrderSide.Buy : OrderSide.Sell;
            currentTradeMetric = new TradeMetrics(dateTime, price, Math.Abs(quantity), side);
        }

        private void AddToPosition(DateTime dateTime, int quantity, double price)
        {
            LastEntryTime = dateTime;

            // Update average price
            double totalValue = AveragePrice * CurrentPosition + price * quantity;
            AveragePrice = totalValue / (CurrentPosition + quantity);

            currentTradeMetric?.UpdateFill(Math.Abs(CurrentPosition + quantity), AveragePrice, dateTime);
        }

        private void CloseCurrentPosition(DateTime dateTime, double price)
        {
            if (currentTradeMetric != null)
            {
                currentTradeMetric.LastFill = dateTime;
                currentTradeMetric.ExitPrice = price;  // Set the actual exit price
                currentTradeMetric.CalculateFinalMetrics(parameters.inst_factor);  // Calculate final metrics

                cycleMetrics.Add(currentTradeMetric);

                if (parameters.is_write_metrics)
                {
                    SaveTradeMetricRealtime(currentTradeMetric);
                }

                // Debug logging
                Console.WriteLine($"Trade Closed: {currentTradeMetric.Side} {currentTradeMetric.MaxPosition} " +
                                $"Entry: {currentTradeMetric.EntryPrice}, Exit: {currentTradeMetric.ExitPrice}, " +
                                $"Delta: {currentTradeMetric.AveragePriceDelta}, PnL: {currentTradeMetric.PnL}");

                currentTradeMetric = null;
            }

            AveragePrice = 0;
        }

        private void ReduceOrClosePosition(DateTime dateTime, int quantity, double price)
        {
            // Calculate realized PnL for this portion
            RealizedPnL += CalculateRealizedPnL(quantity, price);

            if (currentTradeMetric != null)
            {
                currentTradeMetric.LastFill = dateTime;
                // Don't set exit price here unless fully closing
            }

            // If closing completely
            if (CurrentPosition + quantity == 0)
            {
                CloseCurrentPosition(dateTime, price);
            }
            else
            {
                // For partial closes, update the price but don't close the trade
                currentTradeMetric?.UpdatePrice(price, dateTime);
            }
        }

        private double CalculateRealizedPnL(int signedQuantity, double exitPrice)
        {
            // For long positions: PnL = (exit - entry) * quantity
            // For short positions: PnL = (entry - exit) * quantity
            // signedQuantity is negative for position-reducing trades

            if (CurrentPosition == 0) return 0;

            int originalSide = CurrentPosition > 0 ? 1 : -1;
            int closingQuantity = Math.Abs(signedQuantity);

            double pnlPerUnit = originalSide == 1 ?
                (exitPrice - AveragePrice) :
                (AveragePrice - exitPrice);

            return closingQuantity * pnlPerUnit * parameters.inst_factor;
        }

        public void UpdateTradeMetric(Bar bar)
        {
            Bar signalBar = bar;

            if (CurrentPosition != 0 && currentTradeMetric != null)
            {
                // Calculate unrealized PnL correctly
                int positionSide = CurrentPosition > 0 ? 1 : -1;
                UnrealizedPnL = Math.Abs(CurrentPosition) *
                               (positionSide * (signalBar.Close - AveragePrice)) *
                               parameters.inst_factor;

                // Update trade metric without passing side
                currentTradeMetric.UpdatePrice(signalBar.Close, signalBar.DateTime);
            }
            else
            {
                UnrealizedPnL = 0.0;
            }
        }

        public void SaveCycleMetrics()
        {
            string outputPath = Path.Combine(@"C:\tmp\Template", fileName + ".csv");

            using (var sw = new StreamWriter(outputPath, false))
            {
                // Write header
                sw.WriteLine("FirstFill,LastFill,Side,AvgPrice,ExitPrice,AvgPriceDelta,CycleTime,MAE,MFE,MaxPosition,TimeSinceLastFill,PnL");

                foreach (var metric in cycleMetrics)
                {
                    sw.WriteLine(FormatTradeMetric(metric));
                }
            }
        }

        private void SaveTradeMetricRealtime(TradeMetrics metric)
        {
            string outputPath = Path.Combine(@"C:\tmp\Template", realtimeFileName);

            bool fileExists = File.Exists(outputPath);
            using (var sw = new StreamWriter(outputPath, true))
            {
                if (!fileExists)
                {
                    sw.WriteLine("FirstFill,LastFill,Side,AvgPrice,ExitPrice,AvgPriceDelta,CycleTime,MAE,MFE,MaxPosition,TimeSinceLastFill,PnL");
                }
                sw.WriteLine(FormatTradeMetric(metric));
            }
        }

        private string FormatTradeMetric(TradeMetrics metric)
        {
            return string.Format("{0:yyyy-MM-dd HH:mm:ss.fffffff},{1:yyyy-MM-dd HH:mm:ss.fffffff},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11}",
                metric.FirstFill,
                metric.LastFill,
                metric.Side,
                metric.AveragePrice,
                metric.ExitPrice,
                metric.AveragePriceDelta,
                metric.CycleTime,
                metric.MaximumAdverseExcursion,
                metric.MaximumFavorableExcursion,
                metric.MaxPosition,
                metric.TimeSinceLastFill,
                metric.PnL);
        }

        public void Reset()
        {
            lock (lockObject)
            {
                CurrentPosition = 0;
                AveragePrice = 0;
                RealizedPnL = 0;
                UnrealizedPnL = 0;
                LastPrice = 0;
                FirstEntryTime = DateTime.MinValue;
                LastEntryTime = DateTime.MinValue;
                currentTradeMetric = null;
                cycleMetrics.Clear();
            }
        }
    }

    /// <summary>
    /// Refactored TradeMetrics class
    /// </summary>
    // Fixed TradeMetrics class
    public class TradeMetrics
    {
        public DateTime FirstFill { get; }
        public DateTime LastFill { get; set; }
        public OrderSide Side { get; }
        public double EntryPrice { get; }  // Original entry price - never changes
        public double AveragePrice { get; private set; }  // Can be updated for position additions
        public double ExitPrice { get; set; }
        public double AveragePriceDelta { get; private set; }
        public double CycleTime { get; private set; }
        public double MaximumAdverseExcursion { get; private set; }
        public double MaximumFavorableExcursion { get; private set; }
        public double MaxPosition { get; private set; }
        public double TimeSinceLastFill { get; private set; }
        public double PnL { get; private set; }

        public TradeMetrics(DateTime firstFill, double fillPrice, double position, OrderSide side)
        {
            FirstFill = firstFill;
            LastFill = firstFill;
            EntryPrice = fillPrice;  // Store original entry price
            AveragePrice = fillPrice;
            Side = side;
            MaxPosition = position;
            MaximumAdverseExcursion = 0;
            MaximumFavorableExcursion = 0;
            ExitPrice = 0;
            PnL = 0;
            AveragePriceDelta = 0;
        }

        public void UpdateFill(int position, double price, DateTime time)
        {
            if (position > MaxPosition)
            {
                MaxPosition = position;
            }

            LastFill = time;
            TimeSinceLastFill = 0;
            AveragePrice = price;  // Update average price for position additions
        }

        public void UpdatePrice(double price, DateTime current)
        {
            // Calculate unrealized P&L from ENTRY price (not average price)
            double currentDelta = Side == OrderSide.Buy ?
                price - EntryPrice :
                EntryPrice - price;

            // Track MAE and MFE based on entry price
            if (currentDelta > MaximumFavorableExcursion)
                MaximumFavorableExcursion = currentDelta;
            else if (currentDelta < MaximumAdverseExcursion)
                MaximumAdverseExcursion = currentDelta;

            CycleTime = (current - FirstFill).TotalMinutes;
            TimeSinceLastFill = (current - LastFill).TotalMinutes;

            // DON'T update AveragePriceDelta here - wait for trade close
        }

        public void CalculateFinalMetrics(double instrumentFactor)
        {
            // Calculate final P&L using actual exit price and ENTRY price
            AveragePriceDelta = Side == OrderSide.Buy ?
                ExitPrice - EntryPrice :
                EntryPrice - ExitPrice;

            PnL = MaxPosition * AveragePriceDelta * instrumentFactor;
        }
    }
}