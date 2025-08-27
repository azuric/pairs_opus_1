using System;
using SmartQuant;
using Parameters;

namespace StrategyManagement
{
    public abstract class BaseStrategyManager : IStrategyManager
    {
        // Core properties
        public string Name { get; protected set; }
        public StrategyParameters Parameters { get; protected set; }
        public IPositionManager PositionManager => DualPositionManager?.ActualPositionManager;
        public IDualPositionManager DualPositionManager { get; protected set; }
        public ITradeManager TradeManager { get; protected set; }

        // Trading mode
        protected bool isPairMode;
        protected int tradeInstrumentId;

        protected BaseStrategyManager(string name)
        {
            Name = name;
        }

        #region Initialization

        public virtual void Initialize(StrategyParameters parameters)
        {
            Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
            DualPositionManager = new DualPositionManager(parameters, Name);
        }

        public void SetTradeManager(ITradeManager tradeManager)
        {
            TradeManager = tradeManager ?? throw new ArgumentNullException(nameof(tradeManager));
        }

        public void SetTradingMode(bool isPairMode, int tradeInstrumentId)
        {
            this.isPairMode = isPairMode;
            this.tradeInstrumentId = tradeInstrumentId;
        }

        #endregion

        #region Bar Extraction Helpers

        protected Bar GetSignalBar(Bar[] bars)
        {
            // In pair mode, synthetic (index 2) is the signal
            // In single mode, the instrument (index 0) is the signal
            return isPairMode && bars.Length > 2 ? bars[2] : bars[0];
        }

        protected Bar GetNumeratorBar(Bar[] bars)
        {
            return isPairMode && bars.Length > 0 ? bars[0] : null;
        }

        protected Bar GetDenominatorBar(Bar[] bars)
        {
            return isPairMode && bars.Length > 1 ? bars[1] : null;
        }

        protected Bar GetTradedInstrumentBar(Bar[] bars)
        {
            // Returns the bar for the instrument we're actually trading
            if (!isPairMode) return bars[0];

            // In pair mode, need to figure out which bar corresponds to tradeInstrumentId
            // This would require passing instrument IDs with bars or maintaining mapping
            // For now, assume synthetic is always traded in pair mode
            return bars[2];
        }

        #endregion

        #region Time Management

        protected bool IsWithinTradingHours(DateTime currentTime)
        {
            var timeOfDay = currentTime.TimeOfDay;
            return timeOfDay >= Parameters.start_time && timeOfDay <= Parameters.end_time;
        }

        protected bool CanEnterNewPosition(DateTime currentTime)
        {
            var timeOfDay = currentTime.TimeOfDay;
            return timeOfDay <= Parameters.entry_allowedUntil;
        }

        protected bool ShouldExitAllPositions(DateTime currentTime)
        {
            var timeOfDay = currentTime.TimeOfDay;
            return timeOfDay >= Parameters.exit_time;
        }

        #endregion

        #region Position Management

        protected void ExecuteTheoreticalEntry(Bar[] bars, OrderSide side, double accountValue)
        {
            Bar signalBar = GetSignalBar(bars);
            int positionSize = CalculatePositionSize(bars, accountValue);
            double entryPrice = GetEntryPrice(bars, side);

            DualPositionManager?.UpdateTheoPosition(signalBar.DateTime, side, positionSize, entryPrice);
        }

        protected void ExecuteTheoreticalExit(Bar[] bars, int currentPosition)
        {
            Bar signalBar = GetSignalBar(bars);
            OrderSide exitSide = currentPosition > 0 ? OrderSide.Sell : OrderSide.Buy;
            int exitSize = Math.Abs(currentPosition);
            double exitPrice = GetExitPrice(bars, exitSide);

            DualPositionManager?.UpdateTheoPosition(signalBar.DateTime, exitSide, exitSize, exitPrice);
        }

        protected int GetCurrentTheoPosition()
        {
            return DualPositionManager?.TheoPositionManager.CurrentPosition ?? 0;
        }

        protected bool HasLiveOrder()
        {
            return TradeManager?.HasLiveOrder ?? false;
        }

        protected void CancelCurrentOrder()
        {
            if (TradeManager?.HasLiveOrder == true)
            {
                TradeManager.CancelOrder(TradeManager.CurrentOrderId);
            }
        }

        #endregion

        #region Event Handlers

        public virtual void OnFill(Fill fill)
        {
            DualPositionManager?.UpdateActualPosition(
                fill.DateTime,
                fill.Side,
                (int)fill.Qty,
                fill.Price
            );
        }

        public virtual void OnOrderEvent(Order order)
        {
            TradeManager?.HandleOrderUpdate(order);
        }

        public virtual void OnStrategyStart()
        {
            Console.WriteLine($"Strategy {Name} started. Pair mode: {isPairMode}");
        }

        public virtual void OnStrategyStop()
        {
            if (Parameters?.is_write_metrics == true)
            {
                DualPositionManager?.SaveAllMetrics();
            }
            Console.WriteLine($"Strategy {Name} stopped");
        }

        // Default implementations for market data - override if needed
        public virtual void OnBar(Bar[] bars) { }
        public virtual void OnTrade(Trade trade) { }
        public virtual void OnAsk(Ask ask) { }
        public virtual void OnBid(Bid bid) { }

        #endregion

        #region Abstract Methods - Must be implemented by concrete strategies

        public abstract void ProcessBar(Bar[] bars, double accountValue);
        public abstract bool ShouldEnterLongPosition(Bar[] bars);
        public abstract bool ShouldEnterShortPosition(Bar[] bars);
        public abstract bool ShouldExitLongPosition(Bar[] bars);
        public abstract bool ShouldExitShortPosition(Bar[] bars);
        public abstract int CalculatePositionSize(Bar[] bars, double accountValue);
        public abstract double GetEntryPrice(Bar[] bars, OrderSide side);
        public abstract double GetExitPrice(Bar[] bars, OrderSide side);

        #endregion
    }
}