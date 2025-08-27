using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using SmartQuant;
using SmartQuant.Strategy_;

namespace PriceExecutionHandler
{
    public partial class SellSide : SellSideStrategy_
    {
        #region Regular Instrument Processing

        protected override void OnTrade(Trade trade_)
        {
            if (trade_.Price == 0) return;

            var instrumentId = trade_.InstrumentId;
            var currentTime = trade_.DateTime;

            // Process bar data for regular instruments only
            if (currentBars.ContainsKey(instrumentId))
            {
                var currentBar = currentBars[instrumentId];

                // Check if we need to emit the bar
                if (currentBar.ShouldEmitBar(currentTime))
                {
                    EmitBarAndReset(instrumentId, currentTime);
                }

                // Update current bar
                currentBar.UpdatePrice(trade_.Price, trade_.Size, lastPrices[instrumentId]);
                lastPrices[instrumentId] = trade_.Price;
            }

            // Skip synthetic instrument bar processing
            Instrument instrument = InstrumentManager.GetById(instrumentId);
            if (instrument.Type == InstrumentType.Synthetic)
                return;

            // Emit trade for regular instruments
            DateTime dt = isRealTime ? trade_.ExchangeDateTime : trade_.DateTime;
            EmitTrade(dt, instrumentId, trade_.Price, trade_.Size);

            // Generate synthetic trades when this instrument is a numerator
            if (synthDictNum.TryGetValue(instrumentId, out List<int> numSynthList))
            {
                foreach (int synthId in numSynthList)
                {
                    GenerateAndEmitSyntheticTrade(synthId, dt);
                }
            }

            // Generate synthetic trades when this instrument is a denominator
            if (synthDictDen.TryGetValue(instrumentId, out List<int> denSynthList))
            {
                foreach (int synthId in denSynthList)
                {
                    GenerateAndEmitSyntheticTrade(synthId, dt);
                }
            }
        }

        protected override void OnBid(Bid bid_)
        {
            if (bid_.Price == 0) return;

            lock (this)
            {
                // Update bid tracking for all instruments
                bids[bid_.InstrumentId][bid_.ProviderId] = bid_;
                quotes[bid_.InstrumentId][0] = bid_.Price;

                // Update current bar's bid for regular instruments only
                if (currentBars.ContainsKey(bid_.InstrumentId))
                {
                    currentBars[bid_.InstrumentId].UpdateQuotes(bid_.Price, 0);
                }

                // Skip synthetic processing for synthetic instruments
                Instrument instrument = InstrumentManager.GetById(bid_.InstrumentId);
                if (instrument.Type == InstrumentType.Synthetic)
                    return;

                // Emit base bid
                DateTime dt = isRealTime ? bid_.ExchangeDateTime : bid_.DateTime;
                EmitBid(dt, bid_.InstrumentId, bid_.Price, bid_.Size);

                // Generate synthetic bids when this is numerator
                if (synthDictNum.TryGetValue(bid_.InstrumentId, out List<int> numSynthList))
                {
                    foreach (int synthId in numSynthList)
                    {
                        GenerateAndEmitSyntheticBid(synthId, dt);
                    }
                }

                // Generate synthetic asks when this is denominator bid
                if (synthDictDen.TryGetValue(bid_.InstrumentId, out List<int> denSynthList))
                {
                    foreach (int synthId in denSynthList)
                    {
                        GenerateAndEmitSyntheticAsk(synthId, dt);
                    }
                }
            }
        }

        protected override void OnAsk(Ask ask_)
        {
            if (ask_.Price == 0) return;

            lock (this)
            {
                // Update ask tracking for all instruments
                asks[ask_.InstrumentId][ask_.ProviderId] = ask_;
                quotes[ask_.InstrumentId][1] = ask_.Price;

                // Update current bar's ask for regular instruments only
                if (currentBars.ContainsKey(ask_.InstrumentId))
                {
                    currentBars[ask_.InstrumentId].UpdateQuotes(0, ask_.Price);
                }

                // Skip synthetic processing for synthetic instruments
                Instrument instrument = InstrumentManager.GetById(ask_.InstrumentId);
                if (instrument.Type == InstrumentType.Synthetic)
                    return;

                // Emit base ask
                DateTime dt = isRealTime ? ask_.ExchangeDateTime : ask_.DateTime;
                EmitAsk(dt, ask_.InstrumentId, ask_.Price, ask_.Size);

                // Generate synthetic asks when this is numerator ask
                if (synthDictNum.TryGetValue(ask_.InstrumentId, out List<int> numSynthList))
                {
                    foreach (int synthId in numSynthList)
                    {
                        GenerateAndEmitSyntheticAsk(synthId, dt);
                    }
                }

                // Generate synthetic bids when this is denominator ask
                if (synthDictDen.TryGetValue(ask_.InstrumentId, out List<int> denSynthList))
                {
                    foreach (int synthId in denSynthList)
                    {
                        GenerateAndEmitSyntheticBid(synthId, dt);
                    }
                }
            }
        }

        private void EmitBarAndReset(int instrumentId, DateTime currentTime)
        {
            var currentBar = currentBars[instrumentId];
            var config = currentBar.Config;

            // Only emit if bar has data
            if (currentBar.IsInitialized)
            {
                var barEndTime = config.Type == BarTypes.Time
                    ? currentBar.BarStartTime.AddSeconds(config.Value)
                    : currentTime;

                EmitBar(new Bar(
                    currentBar.BarStartTime,
                    currentTime,
                    0,
                    instrumentId,
                    SmartQuant.BarType.Time,
                    config.Value,
                    currentBar.Open,
                    currentBar.High,
                    currentBar.Low,
                    currentBar.Close,
                    currentBar.Volume)
                {
                    [0] = currentBar.Direction,
                    [1] = currentBar.TickVolume,
                    [2] = currentBar.Ticks,
                    [3] = currentBar.NormalizedDirection,
                    [4] = currentBar.NormalizedTickVolume,
                    [5] = currentBar.AverageTickSize,
                    [6] = currentBar.Vwap
                });
            }

            // Start new bar
            currentBar.Reset(currentBar.Close, currentTime);
        }

        #endregion

        #region Synthetic Instrument Generation

        private void GenerateAndEmitSyntheticTrade(int synthId, DateTime dt)
        {
            // Get constituent instruments
            if (!synthDict.TryGetValue(synthId, out int[] constituents))
                return;

            int denId = constituents[0];
            int numId = constituents[1];

            Instrument den = InstrumentManager.GetById(denId);
            Instrument num = InstrumentManager.GetById(numId);

            // Check both constituents have recent trades
            if (num.Trade == null || den.Trade == null)
                return;

            // Time synchronization check - ensure trades are from same minute
            if (num.Trade.DateTime.Minute != den.Trade.DateTime.Minute)
                return;

            // Calculate synthetic price
            double synthPrice = num.Trade.Price / den.Trade.Price;

            // Use minimum size of constituents
            int synthSize = Math.Min(num.Trade.Size, den.Trade.Size);

            if (synthPrice == 0.0)
                return;

            // Emit synthetic trade
            EmitTrade(new Trade(dt, 0, synthId, synthPrice, synthSize));
        }

        private void GenerateAndEmitSyntheticBid(int synthId, DateTime dt)
        {
            if (!synthDict.TryGetValue(synthId, out int[] constituents))
                return;

            int denId = constituents[0];
            int numId = constituents[1];

            Instrument den = InstrumentManager.GetById(denId);
            Instrument num = InstrumentManager.GetById(numId);

            // For synthetic bid: need numerator bid and denominator ask
            if (den.Ask == null || num.Bid == null)
                return;

            // Time sync check
            if (num.Bid.DateTime.Minute != den.Ask.DateTime.Minute)
                return;

            // Calculate synthetic bid price
            double synthBidPrice = num.Bid.Price / den.Ask.Price;
            int synthBidSize = Math.Min(num.Bid.Size, den.Ask.Size);

            // Track in quotes
            if (quotes.ContainsKey(synthId))
            {
                quotes[synthId][0] = synthBidPrice;
            }

            // Emit synthetic bid
            EmitBid(new Bid(dt, 0, synthId, synthBidPrice, synthBidSize));
        }

        private void GenerateAndEmitSyntheticAsk(int synthId, DateTime dt)
        {
            if (!synthDict.TryGetValue(synthId, out int[] constituents))
                return;

            int denId = constituents[0];
            int numId = constituents[1];

            Instrument den = InstrumentManager.GetById(denId);
            Instrument num = InstrumentManager.GetById(numId);

            // For synthetic ask: need numerator ask and denominator bid
            if (den.Bid == null || num.Ask == null)
                return;

            // Time sync check
            if (num.Ask.DateTime.Minute != den.Bid.DateTime.Minute)
                return;

            // Calculate synthetic ask price
            double synthAskPrice = num.Ask.Price / den.Bid.Price;
            int synthAskSize = Math.Min(num.Ask.Size, den.Bid.Size);

            // Track in quotes
            if (quotes.ContainsKey(synthId))
            {
                quotes[synthId][1] = synthAskPrice;
            }

            // Emit synthetic ask
            EmitAsk(new Ask(dt, 0, synthId, synthAskPrice, synthAskSize));
        }

        #endregion

        #region Bar Processing

        protected override void OnBar(Bar bar_)
        {
            // Skip processing for synthetic instruments themselves
            Instrument inst = InstrumentManager.GetById(bar_.InstrumentId);
            if (inst.Type == InstrumentType.Synthetic)
                return;

            // Generate synthetic bars based on constituent bars
            if (synthDictNum.TryGetValue(bar_.InstrumentId, out List<int> numInstrList))
            {
                foreach (int synthId in numInstrList)
                {
                    GenerateAndEmitSyntheticBar(synthId, bar_.CloseDateTime);
                }
            }

            if (synthDictDen.TryGetValue(bar_.InstrumentId, out List<int> denInstrList))
            {
                foreach (int synthId in denInstrList)
                {
                    GenerateAndEmitSyntheticBar(synthId, bar_.CloseDateTime);
                }
            }
        }

        private void GenerateAndEmitSyntheticBar(int synthId, DateTime dt)
        {
            if (!synthDict.TryGetValue(synthId, out int[] constituents))
                return;

            int denId = constituents[0];
            int numId = constituents[1];

            Instrument den = InstrumentManager.GetById(denId);
            Instrument num = InstrumentManager.GetById(numId);

            if (den.Bar == null || num.Bar == null)
                return;

            // Check time synchronization (within 50 seconds)
            if (Math.Abs((num.Bar.CloseDateTime - den.Bar.CloseDateTime).TotalSeconds) > 50)
                return;

            // Calculate OHLC ratios
            double ratioOpen = num.Bar.Open / den.Bar.Open;
            double ratioClose = num.Bar.Close / den.Bar.Close;
            double ratioHigh = Math.Max(ratioOpen, ratioClose);
            double ratioLow = Math.Min(ratioOpen, ratioClose);

            // Create synthetic bar
            Bar synthBar = new Bar(
                num.Bar.OpenDateTime,
                num.Bar.CloseDateTime,
                0,
                synthId,
                BarType.Time,
                60,
                ratioOpen,
                ratioHigh,
                ratioLow,
                ratioClose,
                100);

            EmitBar(synthBar);
        }

        #endregion

        #region Price Validation

        public bool CheckPriceIsValid(double price, double lastPrice)
        {
            if (lastPrice == 0)
                return true;

            // Check for price spike (more than 3% change)
            if (Math.Abs(Math.Log(price) - Math.Log(lastPrice)) > 0.03)
                return false;

            return true;
        }

        #endregion
    }
}