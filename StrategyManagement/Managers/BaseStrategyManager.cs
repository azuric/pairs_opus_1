using System;
using SmartQuant;
using Parameters;

namespace StrategyManagement
{
    /// <summary>
    /// Enhanced base strategy manager with support for separate signal source and execution instruments
    /// CORRECTED: Uses execution_instrument parameter instead of conflicting with trade_instrument
    /// </summary>
    public abstract class BaseStrategyManager : IStrategyManager
    {
        #region Core Properties

        /// <summary>
        /// Strategy name
        /// </summary>
        public string Name { get; protected set; }

        /// <summary>
        /// Strategy parameters
        /// </summary>
        public StrategyParameters Parameters { get; protected set; }

        /// <summary>
        /// Position manager for actual positions
        /// </summary>
        public IPositionManager PositionManager => DualPositionManager?.ActualPositionManager;

        /// <summary>
        /// Dual position manager handling both theoretical and actual positions
        /// </summary>
        public IDualPositionManager DualPositionManager { get; protected set; }

        /// <summary>
        /// Trade manager for order execution
        /// </summary>
        public ITradeManager TradeManager { get; protected set; }

        /// <summary>
        /// Trade manager for order execution
        /// </summary>
        //public AlphaManager AlphaManager { get; protected set; }

        #endregion

        #region Trading Mode Properties

        /// <summary>
        /// Whether strategy is in pairs trading mode
        /// </summary>
        protected bool isPairMode;

        /// <summary>
        /// ID of the instrument being traded (for backward compatibility)
        /// </summary>
        protected int tradeInstrumentId;

        /// <summary>
        /// Array of instrument IDs in order: [numerator, denominator, synthetic]
        /// </summary>
        protected int[] instrumentOrder;

        /// <summary>
        /// Position Size
        /// </summary>
        protected int positionSize;

        #endregion

        #region Signal and Execution Configuration

        /// <summary>
        /// Which instrument to use for signal generation
        /// </summary>
        protected SignalSource signalSource = SignalSource.Synthetic;

        /// <summary>
        /// Which instrument to actually execute trades on
        /// </summary>
        protected SignalSource executionInstrumentSource = SignalSource.Synthetic;

        #endregion

        #region Constructor

        protected BaseStrategyManager(string name, Instrument tradeInstrument)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
        }

        #endregion

        #region Initialization

        /// <summary>
        /// Initialize the strategy with parameters
        /// </summary>
        /// <param name="parameters">Strategy parameters</param>
        public virtual void Initialize(StrategyParameters parameters)
        {
            Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
            DualPositionManager = new DualPositionManager(parameters, Name);
            positionSize = (int)parameters.position_size;
            // Parse signal source and execution instrument configuration
            ParseSignalAndExecutionConfiguration(parameters);

            Console.WriteLine($"BaseStrategyManager {Name} initialized successfully");
        }

        /// <summary>
        /// Set the trade manager
        /// </summary>
        /// <param name="tradeManager">Trade manager instance</param>
        public void SetTradeManager(ITradeManager tradeManager)
        {
            TradeManager = tradeManager ?? throw new ArgumentNullException(nameof(tradeManager));
            Console.WriteLine($"Trade manager set for strategy {Name}");
        }

        /// <summary>
        /// Set trading mode and instrument configuration
        /// </summary>
        /// <param name="isPairMode">Whether in pairs trading mode</param>
        /// <param name="tradeInstrumentId">ID of instrument to trade</param>
        public void SetTradingMode(bool isPairMode, int tradeInstrumentId)
        {
            this.isPairMode = isPairMode;
            this.tradeInstrumentId = tradeInstrumentId;

            Console.WriteLine($"Trading mode set: Pairs={isPairMode}, TradeInstrumentId={tradeInstrumentId}");
        }

        /// <summary>
        /// Set instrument order for pairs trading
        /// </summary>
        /// <param name="instrumentOrder">Array of instrument IDs: [numerator, denominator, synthetic]</param>
        public void SetInstrumentOrder(int[] instrumentOrder)
        {
            this.instrumentOrder = instrumentOrder ?? throw new ArgumentNullException(nameof(instrumentOrder));

            if (isPairMode && instrumentOrder.Length < 3)
            {
                Console.WriteLine($"Warning: Pairs mode but only {instrumentOrder.Length} instruments provided");
            }

            Console.WriteLine($"Instrument order set: [{string.Join(", ", instrumentOrder)}]");
        }

        #endregion

        #region Signal and Execution Configuration Parsing

        /// <summary>
        /// Parse signal source and execution instrument configuration from parameters
        /// CORRECTED: Uses execution_instrument parameter instead of trade_instrument
        /// </summary>
        /// <param name="parameters">Strategy parameters</param>
        private void ParseSignalAndExecutionConfiguration(StrategyParameters parameters)
        {
            try
            {
                // Parse signal source
                signalSource = ParseInstrumentSource(parameters.signal_source, "signal_source", SignalSource.Synthetic);

                // Parse execution instrument - if not specified, use same as signal source
                string executionInstrumentConfig = parameters.execution_instrument ?? parameters.signal_source ?? "synth";
                executionInstrumentSource = ParseInstrumentSource(executionInstrumentConfig, "execution_instrument", signalSource);

                Console.WriteLine($"Strategy {Name} configuration:");
                Console.WriteLine($"  Trade Instrument (Synthetic): {parameters.trade_instrument}");
                Console.WriteLine($"  Signal Source: {signalSource}");
                Console.WriteLine($"  Execution Instrument: {executionInstrumentSource}");

                if (signalSource != executionInstrumentSource)
                {
                    Console.WriteLine($"  Note: Cross-instrument strategy - signals from {signalSource}, executing on {executionInstrumentSource}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing signal/execution configuration for {Name}: {ex.Message}");
                Console.WriteLine("Using default configuration: signal_source=Synthetic, execution_instrument=Synthetic");
                signalSource = SignalSource.Synthetic;
                executionInstrumentSource = SignalSource.Synthetic;
            }
        }

        /// <summary>
        /// Parse instrument source from string configuration
        /// </summary>
        /// <param name="sourceConfig">Configuration string</param>
        /// <param name="parameterName">Parameter name for logging</param>
        /// <param name="defaultValue">Default value if parsing fails</param>
        /// <returns>Parsed SignalSource</returns>
        private SignalSource ParseInstrumentSource(string sourceConfig, string parameterName, SignalSource defaultValue)
        {
            if (string.IsNullOrEmpty(sourceConfig))
            {
                Console.WriteLine($"Strategy {Name}: No {parameterName} specified, using default {defaultValue}");
                return defaultValue;
            }

            switch (sourceConfig.ToLower().Trim())
            {
                case "num":
                case "numerator":
                    return SignalSource.Numerator;
                case "den":
                case "denominator":
                    return SignalSource.Denominator;
                case "synth":
                case "synthetic":
                    return SignalSource.Synthetic;
                default:
                    Console.WriteLine($"Warning: Invalid {parameterName} '{sourceConfig}' for strategy {Name}. " +
                                    "Valid values: 'num', 'den', 'synth'. Using default {defaultValue}.");
                    return defaultValue;
            }
        }

        #endregion

        #region Bar Extraction Helpers

        /// <summary>
        /// Get the bar to use for signal generation based on configured signal source
        /// </summary>
        /// <param name="bars">Array of bars: [numerator, denominator, synthetic] in pairs mode, [instrument] in single mode</param>
        /// <returns>Bar from the configured signal source instrument</returns>
        protected Bar GetSignalBar(Bar[] bars)
        {
            if (bars == null || bars.Length == 0)
            {
                throw new ArgumentException("Bars array cannot be null or empty");
            }

            // Single instrument mode - always use first bar regardless of signal_source setting
            if (!isPairMode || bars.Length < 2)
            {
                return bars[0];
            }

            // Pairs mode - use configured signal source
            return GetBarBySource(bars, signalSource, "signal");
        }

        /// <summary>
        /// Get the bar for the instrument being executed/traded
        /// </summary>
        /// <param name="bars">Array of bars</param>
        /// <returns>Bar from the configured execution instrument</returns>
        public Bar GetExecutionInstrumentBar(Bar[] bars)
        {
            if (bars == null || bars.Length == 0)
            {
                throw new ArgumentException("Bars array cannot be null or empty");
            }

            // Single instrument mode - always use first bar
            if (!isPairMode || bars.Length < 2)
            {
                return bars[0];
            }

            // Pairs mode - use configured execution instrument source
            return GetBarBySource(bars, executionInstrumentSource, "execution");
        }

        /// <summary>
        /// Helper method to get bar by source with error handling
        /// </summary>
        /// <param name="bars">Array of bars</param>
        /// <param name="source">Source to get bar from</param>
        /// <param name="purpose">Purpose for logging (signal/execution)</param>
        /// <returns>Bar from specified source</returns>
        private Bar GetBarBySource(Bar[] bars, SignalSource source, string purpose)
        {
            switch (source)
            {
                case SignalSource.Numerator:
                    return bars[0]; // First instrument (numerator)

                case SignalSource.Denominator:
                    if (bars.Length > 1)
                        return bars[1]; // Second instrument (denominator)
                    else
                    {
                        Console.WriteLine($"Warning: {purpose} source Denominator requested but only {bars.Length} bars available. Using Numerator.");
                        return bars[0];
                    }

                case SignalSource.Synthetic:
                    if (bars.Length > 2)
                        return bars[2]; // Synthetic instrument
                    else
                    {
                        Console.WriteLine($"Warning: {purpose} source Synthetic requested but only {bars.Length} bars available. Using available bar.");
                        return bars[bars.Length - 1];
                    }

                default:
                    Console.WriteLine($"Warning: Unknown {purpose} source {source}. Using synthetic fallback.");
                    return bars.Length > 2 ? bars[2] : bars[0];
            }
        }

        /// <summary>
        /// Get numerator bar (first instrument in pairs)
        /// </summary>
        /// <param name="bars">Array of bars</param>
        /// <returns>Numerator bar or null if not available</returns>
        protected Bar GetNumeratorBar(Bar[] bars)
        {
            return isPairMode && bars != null && bars.Length > 0 ? bars[0] : null;
        }

        /// <summary>
        /// Get denominator bar (second instrument in pairs)
        /// </summary>
        /// <param name="bars">Array of bars</param>
        /// <returns>Denominator bar or null if not available</returns>
        protected Bar GetDenominatorBar(Bar[] bars)
        {
            return isPairMode && bars != null && bars.Length > 1 ? bars[1] : null;
        }

        /// <summary>
        /// Get synthetic bar (calculated ratio in pairs)
        /// </summary>
        /// <param name="bars">Array of bars</param>
        /// <returns>Synthetic bar or null if not available</returns>
        protected Bar GetSyntheticBar(Bar[] bars)
        {
            return isPairMode && bars != null && bars.Length > 2 ? bars[2] : null;
        }

        #endregion

        #region Instrument ID Helpers

        /// <summary>
        /// Get the instrument ID for execution/trading
        /// </summary>
        /// <returns>Instrument ID to use for trading</returns>
        public int GetExecutionInstrumentId()
        {
            if (!isPairMode)
            {
                return tradeInstrumentId; // Single instrument mode
            }

            if (instrumentOrder == null || instrumentOrder.Length < 3)
            {
                Console.WriteLine("Warning: Instrument order not properly set for pairs mode");
                return tradeInstrumentId; // Fallback
            }

            // In pairs mode, map source to instrument ID
            switch (executionInstrumentSource)
            {
                case SignalSource.Numerator:
                    return instrumentOrder[0]; // Numerator ID
                case SignalSource.Denominator:
                    return instrumentOrder[1]; // Denominator ID  
                case SignalSource.Synthetic:
                    return instrumentOrder[2]; // Synthetic ID
                default:
                    Console.WriteLine($"Warning: Unknown execution instrument source {executionInstrumentSource}, using synthetic");
                    return instrumentOrder[2]; // Default to synthetic
            }
        }

        //public dobule[] GetHistoricalAlphas()
        //{
        //    return Alp
        //}

        #endregion

        #region Description Helpers

        /// <summary>
        /// Get description of signal source for logging
        /// </summary>
        /// <returns>Human-readable signal source description</returns>
        protected string GetSignalSourceDescription()
        {
            if (!isPairMode) return "Single Instrument";
            return signalSource.ToString();
        }

        /// <summary>
        /// Get description of execution instrument for logging
        /// </summary>
        /// <returns>Human-readable execution instrument description</returns>
        protected string GetExecutionInstrumentDescription()
        {
            if (!isPairMode) return "Single Instrument";
            return executionInstrumentSource.ToString();
        }

        /// <summary>
        /// Get comprehensive configuration description
        /// </summary>
        /// <returns>Full configuration description</returns>
        protected string GetConfigurationDescription()
        {
            if (!isPairMode)
            {
                return "Single Instrument Mode";
            }

            if (signalSource == executionInstrumentSource)
            {
                return $"Pairs Mode: {signalSource} (signal & execution)";
            }
            else
            {
                return $"Pairs Mode: {signalSource} signals → {executionInstrumentSource} execution";
            }
        }

        #endregion

        #region Validation

        /// <summary>
        /// Validate signal source and execution instrument configuration
        /// </summary>
        protected void ValidateSignalExecutionConfiguration()
        {
            try
            {
                if (!isPairMode)
                {
                    Console.WriteLine("Single instrument mode: Signal and execution instrument are the same.");
                    return;
                }

                if (signalSource == executionInstrumentSource)
                {
                    Console.WriteLine($"Standard configuration: Using {signalSource} for both signals and execution.");
                }
                else
                {
                    Console.WriteLine($"Cross-instrument strategy: Signals from {signalSource}, executing on {executionInstrumentSource}");

                    // Add specific warnings for certain combinations
                    if (signalSource == SignalSource.Synthetic && executionInstrumentSource != SignalSource.Synthetic)
                    {
                        Console.WriteLine("Note: Using synthetic signals to execute on individual instruments. " +
                                        "Ensure proper position sizing and risk management.");
                    }

                    if (signalSource != SignalSource.Synthetic && executionInstrumentSource == SignalSource.Synthetic)
                    {
                        Console.WriteLine("Note: Using individual instrument signals to execute on synthetic. " +
                                        "Consider correlation and liquidity differences.");
                    }

                    if (signalSource == SignalSource.Numerator && executionInstrumentSource == SignalSource.Denominator)
                    {
                        Console.WriteLine("Note: Inverse correlation strategy - numerator signals, denominator execution.");
                    }

                    if (signalSource == SignalSource.Denominator && executionInstrumentSource == SignalSource.Numerator)
                    {
                        Console.WriteLine("Note: Inverse correlation strategy - denominator signals, numerator execution.");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in configuration validation: {ex.Message}");
            }
        }

        #endregion

        #region Time Management

        /// <summary>
        /// Check if current time is within trading hours
        /// </summary>
        /// <param name="currentTime">Current time to check</param>
        /// <returns>True if within trading hours</returns>
        protected bool IsWithinTradingHours(DateTime currentTime)
        {
            if (Parameters == null) return true; // No restrictions if no parameters

            var timeOfDay = currentTime.TimeOfDay;
            return timeOfDay >= Parameters.start_time && timeOfDay <= Parameters.end_time;
        }

        /// <summary>
        /// Check if new positions can be entered at current time
        /// </summary>
        /// <param name="currentTime">Current time to check</param>
        /// <returns>True if new positions can be entered</returns>
        protected bool CanEnterNewPosition(DateTime currentTime)
        {
            if (Parameters == null) return true; // No restrictions if no parameters

            var timeOfDay = currentTime.TimeOfDay;
            return timeOfDay >= Parameters.entry_time && timeOfDay <= Parameters.entry_allowedUntil;
        }

        /// <summary>
        /// Check if all positions should be exited at current time
        /// </summary>
        /// <param name="currentTime">Current time to check</param>
        /// <returns>True if all positions should be exited</returns>
        protected bool ShouldExitAllPositions(DateTime currentTime)
        {
            if (Parameters == null) return false; // No forced exits if no parameters

            var timeOfDay = currentTime.TimeOfDay;
            return timeOfDay >= Parameters.exit_time;
        }

        #endregion

        #region Position Management

        /// <summary>
        /// Execute theoretical entry position
        /// </summary>
        /// <param name="bars">Current bars</param>
        /// <param name="side">Order side</param>
        /// <param name="accountValue">Current account value</param>
        protected void ExecuteTheoreticalEntry(Bar[] bars, OrderSide side)
        {
            try
            {
                Bar signalBar = GetSignalBar(bars);

                Bar bar = GetExecutionInstrumentBar(bars);

                double entryPrice = bar.Close;

                DualPositionManager?.UpdateTheoPosition(signalBar.DateTime, side, positionSize, entryPrice);

                Console.WriteLine($"Theoretical entry: {side} {positionSize} @ {entryPrice:F4} " +
                                $"(Signal: {GetSignalSourceDescription()}, Execution: {GetExecutionInstrumentDescription()})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in ExecuteTheoreticalEntry: {ex.Message}");
            }
        }

        /// <summary>
        /// Execute theoretical exit position
        /// </summary>
        /// <param name="bars">Current bars</param>
        /// <param name="currentPosition">Current position size</param>
        protected void ExecuteTheoreticalExit(Bar[] bars, int currentPosition)
        {
            try
            {
                Bar signalBar = GetSignalBar(bars);
                OrderSide exitSide = currentPosition > 0 ? OrderSide.Sell : OrderSide.Buy;
                int exitSize = Math.Abs(currentPosition);

                Bar bar = GetExecutionInstrumentBar(bars);  
                double exitPrice = bar.Close;

                DualPositionManager?.UpdateTheoPosition(signalBar.DateTime, exitSide, exitSize, exitPrice);

                Console.WriteLine($"Theoretical exit: {exitSide} {exitSize} @ {exitPrice:F4} " +
                                $"(Signal: {GetSignalSourceDescription()}, Execution: {GetExecutionInstrumentDescription()})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in ExecuteTheoreticalExit: {ex.Message}");
            }
        }

        /// <summary>
        /// Get current theoretical position
        /// </summary>
        /// <returns>Current theoretical position size</returns>
        protected int GetCurrentTheoPosition()
        {
            return DualPositionManager?.TheoPositionManager?.CurrentPosition ?? 0;
        }

        /// <summary>
        /// Check if there's a live order
        /// </summary>
        /// <returns>True if there's a live order</returns>
        protected bool HasLiveOrder()
        {
            return TradeManager?.HasLiveOrder ?? false;
        }

        /// <summary>
        /// Cancel current order if exists
        /// </summary>
        protected void CancelCurrentOrder()
        {
            try
            {
                if (TradeManager?.HasLiveOrder == true)
                {
                    TradeManager.CancelOrder(TradeManager.CurrentOrderId);
                    Console.WriteLine("Current order cancelled");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error cancelling order: {ex.Message}");
            }
        }

        #endregion

        #region Event Handlers

        /// <summary>
        /// Handle fill events
        /// </summary>
        /// <param name="fill">Fill event</param>
        public virtual void OnFill(Fill fill)
        {
            try
            {
                DualPositionManager?.UpdateActualPosition(
                    fill.DateTime,
                    fill.Side,
                    (int)fill.Qty,
                    fill.Price
                );

                Console.WriteLine($"Fill processed: {fill.Side} {fill.Qty} @ {fill.Price:F4}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing fill: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle order events
        /// </summary>
        /// <param name="order">Order event</param>
        public virtual void OnOrderEvent(Order order)
        {
            try
            {
                TradeManager?.HandleOrderUpdate(order);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling order event: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle strategy start
        /// </summary>
        public virtual void OnStrategyStart()
        {
            try
            {
                Console.WriteLine($"Strategy {Name} started");
                Console.WriteLine($"Configuration: {GetConfigurationDescription()}");
                ValidateSignalExecutionConfiguration();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in OnStrategyStart: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle strategy stop
        /// </summary>
        public virtual void OnStrategyStop()
        {
            try
            {
                if (Parameters?.is_write_metrics == true)
                {
                    DualPositionManager?.SaveAllMetrics();
                    Console.WriteLine("Metrics saved");
                }
                Console.WriteLine($"Strategy {Name} stopped");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in OnStrategyStop: {ex.Message}");
            }
        }

        // Default implementations for market data - override if needed
        public virtual void OnBar(Bar[] bars) { }
        public virtual void OnTrade(Trade trade) { }
        public virtual void OnAsk(Ask ask) { }
        public virtual void OnBid(Bid bid) { }

        #endregion

        #region Abstract Methods - Must be implemented by concrete strategies

        /// <summary>
        /// Process bar data and make trading decisions
        /// </summary>
        /// <param name="bars">Array of bars</param>
        /// <param name="accountValue">Current account value</param>
        public abstract void ProcessBar(Bar[] bars);

        /// <summary>
        /// Determine if should enter long position
        /// </summary>
        /// <param name="bars">Array of bars</param>
        /// <returns>True if should enter long</returns>
        public abstract bool ShouldEnterLongPosition(Bar[] bars);

        /// <summary>
        /// Determine if should enter short position
        /// </summary>
        /// <param name="bars">Array of bars</param>
        /// <returns>True if should enter short</returns>
        public abstract bool ShouldEnterShortPosition(Bar[] bars);

        /// <summary>
        /// Determine if should exit long position
        /// </summary>
        /// <param name="bars">Array of bars</param>
        /// <returns>True if should exit long</returns>
        public abstract bool ShouldExitLongPosition(Bar[] bars);

        /// <summary>
        /// Determine if should exit short position
        /// </summary>
        /// <param name="bars">Array of bars</param>
        /// <returns>True if should exit short</returns>
        public abstract bool ShouldExitShortPosition(Bar[] bars);

        #endregion
    }
}

