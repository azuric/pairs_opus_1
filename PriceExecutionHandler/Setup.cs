using SmartQuant;
using SmartQuant.Strategy_;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Parameters;

namespace PriceExecutionHandler
{
    public partial class SellSide : SellSideStrategy_
    {
        private readonly IdArray<IdArray<Ask>> asks;
        private readonly IdArray<IdArray<Bid>> bids;

        private readonly InstrumentList mySubscriptions;

        private readonly Dictionary<int, Order> orderBuy2SellDict;
        private readonly Dictionary<int, Order> orderSell2BuyDict;
        private readonly Dictionary<int, int> ordersPerInstrument;
        private readonly Dictionary<int, int> positionPerInstrument;
        private readonly Dictionary<int, double[]> quotes;

        private readonly Stopwatch watch;

        // Bar tracking
        private readonly Dictionary<int, BarData> currentBars;
        private readonly Dictionary<int, double> lastPrices;
        private readonly Dictionary<int, BarConfig> barConfigs;

        // Phase 2: Synthetic instrument support
        private readonly Dictionary<int, int[]> synthDict;           // synthId -> [denId, numId]  
        private readonly Dictionary<int, List<int>> synthDictNum;    // numId -> List<synthIds>
        private readonly Dictionary<int, List<int>> synthDictDen;    // denId -> List<synthIds>

        private bool isRealTime;

        [Parameter] public bool debug = false;

        private bool isOrderLocked;
        private byte[] providers;

        public SellSide(Framework framework_, string name_)
            : base(framework_, name_)
        {
            barConfigs = new Dictionary<int, BarConfig>();
            currentBars = new Dictionary<int, BarData>();
            bids = new IdArray<IdArray<Bid>>();
            asks = new IdArray<IdArray<Ask>>();
            lastPrices = new Dictionary<int, double>();

            quotes = new Dictionary<int, double[]>();

            orderBuy2SellDict = new Dictionary<int, Order>();
            orderSell2BuyDict = new Dictionary<int, Order>();

            ordersPerInstrument = new Dictionary<int, int>();
            positionPerInstrument = new Dictionary<int, int>();

            // Initialize synthetic instrument dictionaries
            synthDict = new Dictionary<int, int[]>();
            synthDictNum = new Dictionary<int, List<int>>();
            synthDictDen = new Dictionary<int, List<int>>();

            providers = Providers;
            mySubscriptions = new InstrumentList();

            isOrderLocked = false;
            watch = new Stopwatch();
            watch.Start();
        }

        [Parameter]
        public bool enabled
        {
            get => Enabled;
            set => Enabled = value;
        }

        [Parameter] public override int AlgoId => 203;

        private byte[] Providers
        {
            get
            {
                return new[]
                {
                    ProviderId.DataSimulator
                };
            }
            set => providers = value;
        }

        private void Dispatcher_ProviderDisconnected(object sender_, ProviderEventArgs args_)
        {
        }

        public override void Connect()
        {
            if (!Enabled)
                return;

            Status = ProviderStatus.Connected;
        }

        #region Subscriptions

        public void SetBarConfiguration(string symbol, BarTypes type, int value)
        {
            var instrument = InstrumentManager.GetBySymbol(symbol);
            if (instrument != null)
            {
                barConfigs[instrument.Id] = new BarConfig { Type = type, Value = value };
                if (currentBars.ContainsKey(instrument.Id))
                {
                    currentBars[instrument.Id].Config = barConfigs[instrument.Id];
                }
            }
        }

        public override void Subscribe(InstrumentList instruments_)
        {
            foreach (Instrument instrument in instruments_)
                Subscribe(instrument);
        }

        protected override void OnSubscribe(Instrument instrument_)
        {
            Console.WriteLine(Name + "::Subscribe " + instrument_.Symbol);

            if (mySubscriptions[instrument_.Symbol] != null) return;

            // Check if this is a synthetic instrument
            if (instrument_.Type == InstrumentType.Synthetic)
            {
                SubscribeToSyntheticInstrument(instrument_);
            }
            else
            {
                SubscribeToRegularInstrument(instrument_);
            }
        }

        private void SubscribeToSyntheticInstrument(Instrument instrument_)
        {
            Console.WriteLine($"Subscribing to synthetic instrument: {instrument_.Symbol} (ID: {instrument_.Id})");

            // Add to subscriptions and quotes tracking (no bars for synthetics)
            mySubscriptions.Add(instrument_);
            quotes[instrument_.Id] = new double[2];

            // Check if we already have the synthetic mapping
            if (synthDict.ContainsKey(instrument_.Id))
            {
                Console.WriteLine($"Synthetic mapping already exists for {instrument_.Symbol}");

                // Ensure constituent instruments are subscribed
                int denId = synthDict[instrument_.Id][0];
                int numId = synthDict[instrument_.Id][1];

                Instrument denInstrument = InstrumentManager.GetById(denId);
                Instrument numInstrument = InstrumentManager.GetById(numId);

                if (denInstrument != null && mySubscriptions[denInstrument.Symbol] == null)
                {
                    Console.WriteLine($"Auto-subscribing to denominator: {denInstrument.Symbol}");
                    Subscribe(denInstrument);
                }

                if (numInstrument != null && mySubscriptions[numInstrument.Symbol] == null)
                {
                    Console.WriteLine($"Auto-subscribing to numerator: {numInstrument.Symbol}");
                    Subscribe(numInstrument);
                }
            }
            else
            {
                Console.WriteLine($"Warning: Synthetic instrument {instrument_.Symbol} subscribed but no mapping found. Use AddSyntheticInstrument first.");
            }

            Add(instrument_);
        }

        private void SubscribeToRegularInstrument(Instrument instrument_)
        {
            Console.WriteLine($"Subscribing to regular instrument: {instrument_.Symbol} (ID: {instrument_.Id})");

            // Set up bar configuration
            if (!barConfigs.ContainsKey(instrument_.Id))
            {
                barConfigs[instrument_.Id] = new BarConfig { Type = BarTypes.Time, Value = 60 };
            }

            // Initialize all data structures
            currentBars.Add(instrument_.Id, new BarData());
            mySubscriptions.Add(instrument_);
            quotes[instrument_.Id] = new double[2];
            lastPrices[instrument_.Id] = 0.0;

            if (bids[instrument_.Id] == null)
                bids[instrument_.Id] = new IdArray<Bid>();

            if (asks[instrument_.Id] == null)
                asks[instrument_.Id] = new IdArray<Ask>();

            Add(instrument_);
        }

        public override void Unsubscribe(InstrumentList instruments_)
        {
            foreach (Instrument _instrument in instruments_)
                Unsubscribe(_instrument);
        }

        public override void Unsubscribe(Instrument instrument_)
        {
            if (mySubscriptions[instrument_.Symbol] != null)
                mySubscriptions.Remove(instrument_);

            // Clean up synthetic mappings if this is a synthetic instrument
            if (instrument_.Type == InstrumentType.Synthetic && synthDict.ContainsKey(instrument_.Id))
            {
                int denId = synthDict[instrument_.Id][0];
                int numId = synthDict[instrument_.Id][1];

                // Remove from reverse mappings
                if (synthDictNum.ContainsKey(numId))
                {
                    synthDictNum[numId].Remove(instrument_.Id);
                    if (synthDictNum[numId].Count == 0)
                        synthDictNum.Remove(numId);
                }

                if (synthDictDen.ContainsKey(denId))
                {
                    synthDictDen[denId].Remove(instrument_.Id);
                    if (synthDictDen[denId].Count == 0)
                        synthDictDen.Remove(denId);
                }

                // Remove main mapping
                synthDict.Remove(instrument_.Id);

                Console.WriteLine($"Cleaned up synthetic mappings for {instrument_.Symbol}");
            }

            // Regular cleanup
            foreach (byte _providerId in providers)
            {
                if (instrument_.AltId.GetByProviderId(_providerId) == null)
                {
                    //handle error case
                }

                IDataProvider _provider = ProviderManager.GetDataProvider(_providerId);

                if (_provider == null)
                {
                    //do something if you want here if provider not found
                }

                Remove(instrument_, _provider);
            }

            bids.Remove(instrument_.Id);
            asks.Remove(instrument_.Id);

            // Clean up synthetic-specific data structures
            quotes.Remove(instrument_.Id);
            if (currentBars.ContainsKey(instrument_.Id))
                currentBars.Remove(instrument_.Id);
        }

        #endregion

        #region Synthetic Instrument Management

        /// <summary>
        /// Add a synthetic instrument that represents the ratio of numerator/denominator
        /// </summary>
        /// <param name="synthSymbol">Symbol of the synthetic instrument</param>
        /// <param name="numSymbol">Symbol of the numerator instrument</param>
        /// <param name="denSymbol">Symbol of the denominator instrument</param>
        public void AddSyntheticInstrument(string synthSymbol, string numSymbol, string denSymbol)
        {
            // Get instruments
            Instrument synthInstrument = InstrumentManager.GetBySymbol(synthSymbol);
            Instrument numInstrument = InstrumentManager.GetBySymbol(numSymbol);
            Instrument denInstrument = InstrumentManager.GetBySymbol(denSymbol);

            if (synthInstrument == null)
            {
                throw new ArgumentException($"Synthetic instrument '{synthSymbol}' not found in InstrumentManager");
            }

            if (numInstrument == null)
            {
                throw new ArgumentException($"Numerator instrument '{numSymbol}' not found in InstrumentManager");
            }

            if (denInstrument == null)
            {
                throw new ArgumentException($"Denominator instrument '{denSymbol}' not found in InstrumentManager");
            }

            if (synthInstrument.Type != InstrumentType.Synthetic)
            {
                throw new ArgumentException($"Instrument '{synthSymbol}' is not marked as Synthetic type");
            }

            // Build mappings
            synthDict[synthInstrument.Id] = new int[] { denInstrument.Id, numInstrument.Id };

            // Add to reverse mappings
            if (!synthDictNum.ContainsKey(numInstrument.Id))
                synthDictNum[numInstrument.Id] = new List<int>();
            synthDictNum[numInstrument.Id].Add(synthInstrument.Id);

            if (!synthDictDen.ContainsKey(denInstrument.Id))
                synthDictDen[denInstrument.Id] = new List<int>();
            synthDictDen[denInstrument.Id].Add(synthInstrument.Id);

            Console.WriteLine($"Added synthetic instrument mapping: {synthSymbol} = {numSymbol} / {denSymbol}");
            Console.WriteLine($"  Synthetic ID: {synthInstrument.Id}");
            Console.WriteLine($"  Numerator ID: {numInstrument.Id} ({numSymbol})");
            Console.WriteLine($"  Denominator ID: {denInstrument.Id} ({denSymbol})");

            // Auto-subscribe to constituent instruments if not already subscribed
            if (mySubscriptions[numSymbol] == null)
            {
                Console.WriteLine($"Auto-subscribing to numerator: {numSymbol}");
                Subscribe(numInstrument);
            }

            if (mySubscriptions[denSymbol] == null)
            {
                Console.WriteLine($"Auto-subscribing to denominator: {denSymbol}");
                Subscribe(denInstrument);
            }
        }

        /// <summary>
        /// Add synthetic instrument from parameters (for strategy configuration)
        /// </summary>
        /// <param name="parameters">Strategy parameters containing instrument pair info</param>
        public void AddInstrumentPair(StrategyParameters parameters)
        {
            // This assumes the parameters contain pair information
            // Implementation depends on how pairs are stored in JSON
            // Example: parameters might have "trade_instrument": "SYNTH", "numerator": "AAPL", "denominator": "SPY"

            if (parameters.additional_params != null)
            {
                if (parameters.additional_params.TryGetValue("numerator", out object numObj) &&
                    parameters.additional_params.TryGetValue("denominator", out object denObj))
                {
                    string numSymbol = numObj.ToString();
                    string denSymbol = denObj.ToString();
                    string synthSymbol = parameters.trade_instrument;

                    Console.WriteLine($"Adding synthetic instrument from parameters: {synthSymbol} = {numSymbol} / {denSymbol}");
                    AddSyntheticInstrument(synthSymbol, numSymbol, denSymbol);
                }
            }
        }

        /// <summary>
        /// Get constituent instrument IDs for a synthetic instrument
        /// </summary>
        /// <param name="synthId">Synthetic instrument ID</param>
        /// <returns>Array [denominatorId, numeratorId] or null if not found</returns>
        public int[] GetSyntheticConstituents(int synthId)
        {
            return synthDict.TryGetValue(synthId, out int[] constituents) ? constituents : null;
        }

        /// <summary>
        /// Check if an instrument is synthetic
        /// </summary>
        /// <param name="instrumentId">Instrument ID</param>
        /// <returns>True if synthetic</returns>
        public bool IsSynthetic(int instrumentId)
        {
            return synthDict.ContainsKey(instrumentId);
        }

        /// <summary>
        /// Get all synthetic instruments that use this instrument as numerator
        /// </summary>
        /// <param name="instrumentId">Constituent instrument ID</param>
        /// <returns>List of synthetic instrument IDs</returns>
        public List<int> GetSyntheticsUsingAsNumerator(int instrumentId)
        {
            return synthDictNum.TryGetValue(instrumentId, out List<int> synths) ? synths : new List<int>();
        }

        /// <summary>
        /// Get all synthetic instruments that use this instrument as denominator
        /// </summary>
        /// <param name="instrumentId">Constituent instrument ID</param>
        /// <returns>List of synthetic instrument IDs</returns>
        public List<int> GetSyntheticsUsingAsDenominator(int instrumentId)
        {
            return synthDictDen.TryGetValue(instrumentId, out List<int> synths) ? synths : new List<int>();
        }

        #endregion
    }
}