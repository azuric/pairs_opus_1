using System;
using SmartQuant;
using SmartQuant.Strategy_;
using Parameters;
using StrategyManagement;
using System.Linq;
using System.Collections.Generic;
using SmartQuant.Providers;

namespace OpenQuant
{
    public class MyStrategy : Strategy_
    {
        private IStrategyManager strategyManager;
        private ITradeManager tradeManager;
        private DisplayParameters displayParameters;

        public StrategyParameters StrategyParameters;
        public IPositionManager PositionManager => strategyManager?.PositionManager;
        public IDualPositionManager DualPositionManager => strategyManager?.DualPositionManager;
        public DisplayParameters DisplayParameters => displayParameters;

        // Groups for visualization
        private Group barGroup;
        private Group fillGroup;
        private Group equityGroup;

        private int logLevel = 7;
        private double accountValue = 100000;

        private bool isInitialized = false;

        private Dictionary<int, Bar> latestBars = new Dictionary<int, Bar>();

        private bool isPairMode = false;
        private Instrument numeratorInstrument;
        private Instrument denominatorInstrument;
        private Instrument syntheticInstrument;
        private int tradeInstrumentId;  // Which instrument to actually trade
        private int[] instrumentOrder;

        public Instrument Instrument { get; private set; }

        public MyStrategy(Framework framework, string name) : base(framework, name)
        {
        }

        public MyStrategy(Framework framework, string name, IStrategyManager strategyManager)
            : base(framework, name)
        {
            InitializeWithManager(strategyManager);
        }

        public void InitializeWithManager(IStrategyManager strategyManager)
        {
            if (isInitialized) return;

            this.strategyManager = strategyManager;
            this.tradeManager = new StrategyManagement.TradeManager(this);

            if (strategyManager is BaseStrategyManager baseManager)
            {
                baseManager.SetTradeManager(tradeManager);
            }

            this.StrategyParameters = strategyManager.Parameters;
            this.displayParameters = new DisplayParameters(this.Name);

            isInitialized = true;
        }

        protected override void OnStrategyStart()
        {

            // Detect mode based on number and type of instruments
            DetectTradingMode();
            InitializeGroups();

            // Pass mode information to strategy manager
            if (strategyManager is BaseStrategyManager baseManager)
            {
                baseManager.SetTradingMode(isPairMode, tradeInstrumentId);
            }
            strategyManager.OnStrategyStart();

            Console.WriteLine($"Strategy {Name} started with {strategyManager.GetType().Name}");
        }

        private void DetectTradingMode()
        {
            var synthetics = Instruments.Where(i => i.Type == InstrumentType.Synthetic).ToList();

            if (synthetics.Count > 0)
            {
                isPairMode = true;
                // Order matters: [numerator, denominator, synthetic]
                instrumentOrder = new int[] {
                    numeratorInstrument.Id,
                    denominatorInstrument.Id,
                    syntheticInstrument.Id
                    };
            }
            else
            {
                isPairMode = false;

                foreach (Instrument i in Instruments)
                {
                    Instrument = i;
                }

                instrumentOrder = new int[] { Instrument.Id };
            }
        }

        protected override void OnStrategyStop()
        {
            strategyManager.OnStrategyStop();
            Console.WriteLine($"Strategy {Name} stopped");
        }

        protected override void OnBar(Bar bar)
        {
            // Store latest bar for this instrument
            latestBars[bar.InstrumentId] = bar;

            // Build array based on mode
            Bar[] barsToProcess = BuildBarArray();

            // Only process when we have all required bars
            if (barsToProcess != null)
            {
                // Log, update display, etc.
                Log(bar, barGroup, bar.DateTime);

                UpdateDisplayParameters(bar);

                // Pass array to strategy manager
                strategyManager.ProcessBar(barsToProcess, accountValue);

                strategyManager.OnBar(barsToProcess);

                // Update metrics using the appropriate bar
                DualPositionManager?.UpdateMetrics(isPairMode ? barsToProcess[2] : barsToProcess[0]);

                // Check reconciliation
                CheckAndReconcilePositions(barsToProcess);
            }
        }

        private Bar[] BuildBarArray()
        {
            Bar[] result = new Bar[instrumentOrder.Length];

            for (int i = 0; i < instrumentOrder.Length; i++)
            {
                if (latestBars.TryGetValue(instrumentOrder[i], out Bar bar))
                {
                    result[i] = bar;
                }
                else
                {
                    // Don't have all bars yet
                    return null;
                }
            }

            // For pairs, verify bars are time-synchronized
            if (isPairMode)
            {
                var times = result.Select(b => b.DateTime.Minute).Distinct();
                if (times.Count() > 1)
                {
                    // Bars from different minutes - wait for sync
                    return null;
                }
            }

            return result;
        }

        private void CheckAndReconcilePositions(Bar[] bars)
        {
            if (DualPositionManager == null) return;

            int discrepancy = DualPositionManager.CheckTheoActual();

            // If there's a discrepancy and no live order, place an order to reconcile
            if (discrepancy != 0 && !tradeManager.HasLiveOrder)
            {
                OrderSide side = discrepancy > 0 ? OrderSide.Buy : OrderSide.Sell;
                int quantity = Math.Abs(discrepancy);
                double price = strategyManager.GetEntryPrice(bars, side);

                Console.WriteLine($"Reconciling positions: placing {side} order for {quantity} @ {price}");

                // Create order to reconcile actual with theoretical
                tradeManager.CreateOrder(side, quantity, price, Instrument);

                // Update display
                displayParameters.OrderPrice = price;
                displayParameters.OrderQty = quantity;
            }
        }

        protected override void OnTrade(Trade trade)
        {
            strategyManager.OnTrade(trade);
        }

        protected override void OnAsk(Ask ask)
        {
            strategyManager.OnAsk(ask);
        }

        protected override void OnBid(Bid bid)
        {
            strategyManager.OnBid(bid);
        }

        protected override void OnFill(Fill fill)
        {
            Log(fill, fillGroup, fill.DateTime);
            strategyManager.OnFill(fill);

            displayParameters.FillPrice = fill.Price;
            displayParameters.Position = DualPositionManager?.ActualPositionManager.CurrentPosition ?? 0;

            if (logLevel <= 7)
            {
                Console.WriteLine($"Fill: {fill.DateTime:yyyy-MM-dd HH:mm:ss.fffffff}, " +
                                $"order={fill.Order.Id}, side={fill.Side}, qty={fill.Qty}, price={fill.Price}");
            }
        }

        protected override void OnOrderReplaced(Order order)
        {
            strategyManager.OnOrderEvent(order);
            LogOrderStatus(order);
        }

        protected override void OnNewOrder(Order order)
        {
            strategyManager.OnOrderEvent(order);
            LogOrderStatus(order);
        }

        protected override void OnOrderCancelled(Order order)
        {
            strategyManager.OnOrderEvent(order);
            LogOrderStatus(order);
        }

        protected override void OnOrderFilled(Order order)
        {
            strategyManager.OnOrderEvent(order);
            LogOrderStatus(order);
        }

        protected override void OnOrderRejected(Order order)
        {
            strategyManager.OnOrderEvent(order);
            Console.WriteLine($"Order rejected: {order.Id} - {order.Text}");
        }

        private void UpdateDisplayParameters(Bar bar)
        {
            displayParameters.DateTime = bar.DateTime;
            displayParameters.Price = bar.Close;
            displayParameters.Position = DualPositionManager?.ActualPositionManager.CurrentPosition ?? 0;
            displayParameters.Live = tradeManager.HasLiveOrder ? "ON" : "OFF";
        }

        private void InitializeGroups()
        {
            barGroup = new Group("Bars");
            barGroup.Add("Pad", 0);
            barGroup.Add("SelectorKey", Name);

            fillGroup = new Group("Fills");
            fillGroup.Add("Pad", 0);
            fillGroup.Add("SelectorKey", Name);

            equityGroup = new Group("Equity");
            equityGroup.Add("Pad", 1);
            equityGroup.Add("SelectorKey", Name);

            GroupManager.Add(barGroup);
            GroupManager.Add(fillGroup);
            GroupManager.Add(equityGroup);
        }

        private void LogOrderStatus(Order order)
        {
            Console.WriteLine($"Order {order.Id} status: {order.Status}");
        }
    }
}