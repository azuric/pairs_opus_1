using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PriceExecutionHandler
{
    public enum BarTypes
    {
        Time,
        Volume
    }

    public class BarConfig
    {
        public BarTypes Type { get; set; }
        public int Value { get; set; }  // Seconds for time bars, volume threshold for volume bars
    }

    public class BarData
    {
        private double pendingVolume = 0;
        private double pendingTickVolume = 0;
        private double pendingDirection = 0;
        private int pendingTicks = 0;
        private double pendingVwapSum = 0;

        public double Open { get; set; }
        public double High { get; set; }
        public double Low { get; set; }
        public double Close { get; set; }
        public int Volume { get; set; }
        public double TickVolume { get; set; }
        public int Ticks { get; set; }
        public double Direction { get; set; }
        public DateTime BarStartTime { get; set; }
        public bool IsInitialized { get; set; }
        public BarConfig Config { get; set; }
        public int AccumulatedVolume { get; set; }
        public double VwapSum { get; private set; }

        // Store current bid/ask
        public double CurrentBid { get; set; }
        public double CurrentAsk { get; set; }

        // Computed properties
        public double NormalizedDirection => Volume > 0 ? Direction / Volume : 0;
        public double NormalizedTickVolume => Volume > 0 ? TickVolume / Volume : 0;
        public double AverageTickSize => Ticks > 0 ? Volume / (double)Ticks : 0;
        public double Vwap => Volume > 0 ? VwapSum / Volume : 0;

        public BarData()
        {
            Open = High = Low = Close = 0;

            // Apply pending values from previous bar
            Volume = (int)pendingVolume;
            TickVolume = pendingTickVolume;
            Direction = pendingDirection;
            Ticks = pendingTicks;
            AccumulatedVolume = (int)pendingVolume;
            VwapSum = pendingVwapSum;

            // Clear pending values
            pendingVolume = 0;
            pendingTickVolume = 0;
            pendingDirection = 0;
            pendingTicks = 0;
            pendingVwapSum = 0;

            Config = new BarConfig();
            Config.Type = 0;
            Config.Value = 60;

            IsInitialized = false;
        }


        public void Reset(double lastPrice, DateTime startTime)
        {
            BarStartTime = new DateTime(startTime.Year, startTime.Month, startTime.Day, startTime.Hour, startTime.Minute, 0);
            Open = High = Low = Close = lastPrice;

            // Apply pending values from previous bar
            Volume = (int)pendingVolume;
            TickVolume = pendingTickVolume;
            Direction = pendingDirection;
            Ticks = pendingTicks;
            AccumulatedVolume = (int)pendingVolume;
            VwapSum = pendingVwapSum;

            // Clear pending values
            pendingVolume = 0;
            pendingTickVolume = 0;
            pendingDirection = 0;
            pendingTicks = 0;
            pendingVwapSum = 0;

            Config.Type = BarTypes.Time;

            IsInitialized = true;
        }



        private bool IsTickUp(double price, double lastPrice)
        {
            // Tick is up if:
            // 1. Price is greater than or equal to ask OR
            // 2. Price is greater than last price when we have no ask
            // 3. If no last price but we have ask, compare with ask
            if (CurrentAsk > 0)
            {
                return price >= CurrentAsk;
            }
            return lastPrice > 0 ? price > lastPrice : false;
        }

        private bool IsTickDown(double price, double lastPrice)
        {
            // Tick is down if:
            // 1. Price is less than or equal to bid OR
            // 2. Price is less than last price when we have no bid
            // 3. If no last price but we have bid, compare with bid
            if (CurrentBid > 0)
            {
                return price <= CurrentBid;
            }
            return lastPrice > 0 ? price < lastPrice : false;
        }

        public void UpdatePrice(double price, int size, double lastPrice)
        {
            if (!IsInitialized)
            {
                Open = High = Low = Close = price;
                IsInitialized = true;
            }
            else
            {
                High = Math.Max(High, price);
                Low = Math.Min(Low, price);
            }
            Close = price;

            if (Config.Type == BarTypes.Volume)
            {
                int remainingSpace = Config.Value - AccumulatedVolume;
                if (size > remainingSpace)
                {
                    // Calculate ratio for partial volume
                    double ratio = (double)remainingSpace / size;

                    // Split current trade between current and next bar
                    int currentBarVolume = remainingSpace;
                    int nextBarVolume = size - remainingSpace;

                    // Update current bar
                    Volume += currentBarVolume;
                    AccumulatedVolume += currentBarVolume;

                    // Split VWAP calculation
                    double tradeVwapValue = price * size;
                    VwapSum += tradeVwapValue * ratio;
                    pendingVwapSum = tradeVwapValue * (1 - ratio);

                    // Calculate trade direction using new criteria
                    double tickVolumeForTrade = size;
                    double directionForTrade = 0;

                    if (IsTickUp(price, lastPrice))
                    {
                        directionForTrade = size;
                        Ticks++;
                    }
                    else if (IsTickDown(price, lastPrice))
                    {
                        directionForTrade = -size;
                        Ticks++;
                    }

                    // Apply to current bar with ratio
                    TickVolume += tickVolumeForTrade * ratio;
                    Direction += directionForTrade * ratio;

                    // Store remainder for next bar
                    pendingVolume = nextBarVolume;
                    pendingTickVolume = tickVolumeForTrade * (1 - ratio);
                    pendingDirection = directionForTrade * (1 - ratio);
                    pendingTicks = 1;
                }
                else
                {
                    // Full trade fits in current bar
                    Volume += size;
                    AccumulatedVolume += size;
                    TickVolume += size;
                    VwapSum += price * size;

                    // Use new direction criteria
                    if (IsTickUp(price, lastPrice))
                    {
                        Direction += size;
                        Ticks++;
                    }
                    else if (IsTickDown(price, lastPrice))
                    {
                        Direction -= size;
                        Ticks++;
                    }
                }
            }
            else  // Time-based bars
            {
                Volume += size;
                AccumulatedVolume += size;
                TickVolume += size;
                VwapSum += price * size;

                // Use new direction criteria
                if (IsTickUp(price, lastPrice))
                {
                    Direction += size;
                    Ticks++;
                }
                else if (IsTickDown(price, lastPrice))
                {
                    Direction -= size;
                    Ticks++;
                }
            }
        }

        public void UpdateQuotes(double bid, double ask)
        {
            if (bid > 0) CurrentBid = bid;
            if (ask > 0) CurrentAsk = ask;
        }

        public bool ShouldEmitBar(DateTime currentTime)
        {
            if (Config.Type == BarTypes.Time)
            {
                return (currentTime - BarStartTime).TotalSeconds >= Config.Value;
            }
            else // Volume
            {
                return AccumulatedVolume >= Config.Value;
            }
        }
    }
}
