using System;
using SmartQuant;
using SmartQuant.Strategy_;
using Parameters;
using StrategyManagement;
using System.Linq;
using System.Collections.Generic;
using SmartQuant.Providers;
using System.IO;

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
        private string tradeInstrumentSymbol;
        private int[] instrumentOrder;

        private DateTime currentDateTime;
        private FileStream fs;

        // Fixed: Added missing field declarations
        private DateTime lastDayDateTime = DateTime.MinValue;
        private bool isRealTime = false;
        private Dictionary<int, Instrument> instrumentDict = new Dictionary<int, Instrument>();
        private List<DateTime> dateList = new List<DateTime>();
        private DateTime _now;
        //private Bar[] barsToProcess;

        public Instrument Instrument { get; private set; }

        public MyStrategy(Framework framework, string name) : base(framework, name)
        {
            // Initialize collections
            instrumentDict = new Dictionary<int, Instrument>();
            dateList = new List<DateTime>();
            lastDayDateTime = DateTime.MinValue;
        }

        public MyStrategy(Framework framework, string name, IStrategyManager strategyManager)
            : base(framework, name)
        {
            // Initialize collections
            instrumentDict = new Dictionary<int, Instrument>();
            dateList = new List<DateTime>();
            lastDayDateTime = DateTime.MinValue;

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
            ValidateInstrumentConfiguration();

            // Initialize instrument dictionary
            foreach (var instrument in Instruments)
            {
                instrumentDict[instrument.Id] = instrument;
            }

            //barsToProcess = new Bar[instrumentDict.Count];

            // Detect mode based on number and type of instruments
            DetectTradingMode();
            InitializeGroups();

            // Pass mode information to strategy manager
            if (strategyManager is BaseStrategyManager baseManager)
            {
                baseManager.SetTradingMode(isPairMode, tradeInstrumentId);

                // NEW: Pass instrument order to strategy manager
                baseManager.SetInstrumentOrder(instrumentOrder);
                // UPDATED: Enhanced logging with new method names
                //Console.WriteLine($"Strategy {Name} started with {strategyManager.GetType().Name}");
                //Console.WriteLine($"Configuration: {baseManager.GetConfigurationDescription()}");
                //Console.WriteLine($"Signal Source: {baseManager.GetSignalSourceDescription()}");
                //Console.WriteLine($"Execution Instrument: {baseManager.GetExecutionInstrumentDescription()}");
            }
            else
            {
                Console.WriteLine($"Strategy {Name} started with {strategyManager.GetType().Name}");
            }

            strategyManager.OnStrategyStart();
        }

        private void DetectTradingMode()
        {
            try
            {
                var synthetics = Instruments.Where(i => i.Type == InstrumentType.Synthetic).ToList();

                if (synthetics.Count > 0)
                {
                    isPairMode = true;

                    // Initialize instruments
                    syntheticInstrument = synthetics.First();
                    var nonSynthetics = Instruments.Where(i => i.Type != InstrumentType.Synthetic).ToList();

                    if (nonSynthetics.Count >= 2)
                    {
                        numeratorInstrument = nonSynthetics[0];
                        denominatorInstrument = nonSynthetics[1];

                        Console.WriteLine($"Pairs mode detected:");
                        Console.WriteLine($"  Numerator: {numeratorInstrument.Symbol} (ID: {numeratorInstrument.Id})");
                        Console.WriteLine($"  Denominator: {denominatorInstrument.Symbol} (ID: {denominatorInstrument.Id})");
                        Console.WriteLine($"  Synthetic: {syntheticInstrument.Symbol} (ID: {syntheticInstrument.Id})");
                    }
                    else
                    {
                        throw new InvalidOperationException($"Pairs mode requires at least 2 non-synthetic instruments. Found {nonSynthetics.Count}");
                    }

                    instrumentOrder = new int[] {
                        numeratorInstrument.Id,
                        denominatorInstrument.Id,
                        syntheticInstrument.Id
                        };

                    // UPDATED: Set trading instrument based on strategy configuration
                    // For now, default to synthetic, but this should be configurable
                    tradeInstrumentId = syntheticInstrument.Id;
                    tradeInstrumentSymbol = syntheticInstrument.Symbol;

                    // NEW: Set the main Instrument field for backward compatibility
                    Instrument = syntheticInstrument; // This prevents null reference in single-instrument code paths
                }
                else
                {
                    isPairMode = false;

                    if (Instruments.Count() > 0)
                    {
                        Instrument = Instruments.First();
                        Console.WriteLine($"Single instrument mode: {Instrument.Symbol} (ID: {Instrument.Id})");
                    }
                    else
                    {
                        throw new InvalidOperationException("No instruments configured");
                    }

                    instrumentOrder = new int[] { Instrument.Id };
                    tradeInstrumentId = Instrument.Id;
                    tradeInstrumentSymbol = Instrument.Symbol;
                }

                Console.WriteLine($"Trading mode detection completed. Pair mode: {isPairMode}, Trading instrument ID: {tradeInstrumentId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in DetectTradingMode: {ex.Message}");
                Console.WriteLine($"Available instruments: {string.Join(", ", Instruments.Select(i => $"{i.Symbol}({i.Type})"))}");
                throw;
            }
        }

        // Add this method to MyStrategy class
        private void ValidateInstrumentConfiguration()
        {
            Console.WriteLine($"Validating instrument configuration...");
            Console.WriteLine($"Total instruments: {Instruments.Count()}");

            foreach (var instrument in Instruments)
            {
                Console.WriteLine($"  - {instrument.Symbol}: Type={instrument.Type}, ID={instrument.Id}");
            }

            var synthetics = Instruments.Where(i => i.Type == InstrumentType.Synthetic).ToList();
            var nonSynthetics = Instruments.Where(i => i.Type != InstrumentType.Synthetic).ToList();

            Console.WriteLine($"Synthetic instruments: {synthetics.Count}");
            Console.WriteLine($"Non-synthetic instruments: {nonSynthetics.Count}");

            if (synthetics.Count > 0 && nonSynthetics.Count < 2)
            {
                Console.WriteLine("WARNING: Synthetic instrument found but insufficient non-synthetic instruments for pairs trading");
            }
        }

        protected override void OnStrategyStop()
        {
            strategyManager.OnStrategyStop();
            Console.WriteLine($"Strategy {Name} stopped");
        }

        protected override void OnBar(Bar bar)
        {
            _now = bar.CloseDateTime;
            currentDateTime = bar.DateTime;
            TimeSpan _barTime = bar.DateTime.TimeOfDay;

            // Store latest bar for this instrument
            latestBars[bar.InstrumentId] = bar;

            Instrument instrument = Instruments.GetById(bar.InstrumentId);

            // Check for new day
            if (bar.DateTime.Date > lastDayDateTime)
            {
                //NewDay(bar);
                lastDayDateTime = bar.DateTime.Date;
            }

            // Build array based on mode
            var barsToProcess = BuildBarArray();

            // Only process when we have all required bars
            if (barsToProcess != null)
            {
                // Log, update display, etc.
                Log(bar, barGroup, bar.DateTime);

                UpdateDisplayParameters(bar);

                // Pass array to strategy manager
                strategyManager.ProcessBar(barsToProcess);

                strategyManager.OnBar(barsToProcess);

                // Check reconciliation
                if(StrategyParameters.useCheckAndReconcile) 
                    CheckAndReconcilePositions(barsToProcess);

            }
        }

        //// Fixed: Added missing NewDay method
        //private void NewDay(Bar bar)
        //{
        //    Console.WriteLine($"New trading day: {bar.DateTime.Date}");
        //    WriteDataForNewDay(bar.DateTime);

        //    // Add any other new day initialization logic here
        //    // For example, reset daily counters, logs, etc.
        //}

        //private void WriteDataForNewDay(DateTime datetime)
        //{
        //    if (!StrategyParameters.is_writing)
        //        return;

        //    string _fd = StrategyParameters.data_file + @"\Data\" + tradeInstrumentSymbol + @"_\";
        //    Directory.CreateDirectory(Path.GetDirectoryName(_fd));

        //    fs = new FileStream(_fd + "\\data_" + "_" + datetime.ToString("yyyyMMdd") + ".csv", FileMode.Append, FileAccess.Write, FileShare.Write);

        //    StreamWriter _sw = new StreamWriter(fs);
        //    {
        //        // Fixed: Changed iManager to strategyManager and added null check
        //        if (strategyManager != null && strategyManager is BaseStrategyManager baseManager)
        //        {
        //            // Note: You'll need to ensure historicAlphas exists in your BaseStrategyManager
        //            // or replace this with the correct property/method
        //            var historicAlphas = baseManager.GetHistoricAlphas(); // Assuming this method exists
        //            for (int _i = 0; _i < historicAlphas.Count; _i++)
        //                _sw.WriteLine(historicAlphas[_i]);
        //        }
        //        _sw.Close();
        //    }
        //}

        // Fixed: Added proper return value and parameter
        private bool ExcludeDates(Bar bar)
        {
            if (bar.DateTime.Date > lastDayDateTime)
            {
                //NewDay(bar);
                lastDayDateTime = bar.DateTime.Date;
            }

            // Check if this date should be excluded
            bool excludedDates = dateList.Contains(bar.DateTime.Date);
            return excludedDates;
        }

        // Fixed: Added proper return value and fixed variable references
        private bool HasProcessedSpread(Bar currentBar)
        {
            bool _canTrade = !isRealTime || (_now > currentDateTime);

            if (currentBar.DateTime.Date > lastDayDateTime)
            {
                //NewDay(currentBar);
                lastDayDateTime = currentBar.DateTime.Date;
            }

            bool excludedDates = false;
            if (dateList.Contains(currentBar.DateTime.Date))
                excludedDates = true;

            bool _hasProcessedSpread = false;
            if (instrumentDict.ContainsKey(currentBar.InstrumentId))
            {
                _hasProcessedSpread = instrumentDict[currentBar.InstrumentId].Type == InstrumentType.Synthetic;
            }

            return _hasProcessedSpread;
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

                // FIXED: Get the correct trading instrument
                Instrument tradingInstrument = GetTradingInstrument();

                var price = tradingInstrument.Bar.Close;

                Console.WriteLine($"Reconciling positions: placing {side} order for {quantity} @ {price}");

                if (tradingInstrument != null)
                {
                    tradeManager.CreateOrder(side, quantity, price, tradingInstrument);

                    // Update display
                    displayParameters.OrderPrice = price;
                    displayParameters.OrderQty = quantity;
                }
                else
                {
                    Console.WriteLine("ERROR: Cannot reconcile positions - trading instrument is null");
                }
            }
        }

        private Instrument GetTradingInstrument()
        {
            try
            {
                if (!isPairMode)
                {
                    return Instrument;
                }
                else
                {
                    // UPDATED: Get execution instrument ID from strategy manager
                    int targetExecutionInstrumentId = 0;

                    if (strategyManager is BaseStrategyManager baseManager)
                    {
                        targetExecutionInstrumentId = baseManager.GetExecutionInstrumentId(); // NEW METHOD NAME
                    }
                    else
                    {
                        // Fallback to original logic
                        targetExecutionInstrumentId = tradeInstrumentId;
                    }

                    if (targetExecutionInstrumentId == 0)
                    {
                        Console.WriteLine("Warning: Execution instrument ID not set, defaulting to synthetic");
                        return syntheticInstrument;
                    }

                    // Find the instrument that matches the execution instrument ID
                    if (numeratorInstrument?.Id == targetExecutionInstrumentId)
                    {
                        Console.WriteLine($"Execution instrument: {numeratorInstrument.Symbol} (Numerator)");
                        return numeratorInstrument;
                    }
                    else if (denominatorInstrument?.Id == targetExecutionInstrumentId)
                    {
                        Console.WriteLine($"Execution instrument: {denominatorInstrument.Symbol} (Denominator)");
                        return denominatorInstrument;
                    }
                    else if (syntheticInstrument?.Id == targetExecutionInstrumentId)
                    {
                        Console.WriteLine($"Execution instrument: {syntheticInstrument.Symbol} (Synthetic)");
                        return syntheticInstrument;
                    }
                    else
                    {
                        var matchingInstrument = Instruments.FirstOrDefault(i => i.Id == targetExecutionInstrumentId);
                        if (matchingInstrument != null)
                        {
                            Console.WriteLine($"Execution instrument: {matchingInstrument.Symbol} (Found by ID)");
                            return matchingInstrument;
                        }

                        Console.WriteLine($"Warning: Could not find execution instrument with ID {targetExecutionInstrumentId}, using synthetic");
                        return syntheticInstrument;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetTradingInstrument: {ex.Message}");
                return null;
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