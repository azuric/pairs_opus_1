using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using SmartQuant;

namespace PriceExecutionHandler
{
    /// <summary>
    /// Basic implementation of spread execution manager
    /// </summary>
    public class SpreadExecutionManager : ISpreadExecutionManager
    {
        #region Private Fields
        
        private readonly ConcurrentDictionary<int, ISpreadHandler> _activeHandlers;
        private readonly object _lockObject = new object();
        private SpreadExecutionParameters _defaultParameters;
        private int _nextOrderId = 1;
        private readonly SpreadExecutionStatistics _statistics;
        
        #endregion
        
        #region Events
        
        public event EventHandler<SpreadExecutionEventArgs> ExecutionEvent;

        InstrumentManager _instrumentManager;
        
        #endregion
        
        #region Constructor
        
        public SpreadExecutionManager(InstrumentManager instrumentManager)
        {
            _instrumentManager = instrumentManager;
            _activeHandlers = new ConcurrentDictionary<int, ISpreadHandler>();
            _defaultParameters = new SpreadExecutionParameters();
            _statistics = new SpreadExecutionStatistics();
            
            LogMessage("SpreadExecutionManager initialized");
        }
        
        #endregion
        
        #region Public Methods - Execution
        
        public SpreadExecutionResult ExecuteSpreadOrder(SpreadOrder spreadOrder, SpreadExecutionParameters parameters)
        {
            lock (_lockObject)
            {
                try
                {
                    // Validate inputs
                    if (spreadOrder == null)
                        throw new ArgumentNullException(nameof(spreadOrder));
                    
                    // Use provided parameters or default
                    var effectiveParameters = parameters ?? _defaultParameters;
                    effectiveParameters.Validate();
                    
                    // Assign unique order ID if not already set
                    if (spreadOrder.SpreadOrderId == 0)
                    {
                        spreadOrder.SpreadOrderId = _nextOrderId++;
                    }
                    
                    LogMessage($"Starting spread execution for order {spreadOrder.SpreadOrderId}: {spreadOrder.InstrumentSymbol} {spreadOrder.Side} {spreadOrder.Quantity} @ {spreadOrder.SpreadPrice}");
                    
                    // Parse instrument symbol to get numerator and denominator
                    var (numeratorInstrument, denominatorInstrument) = ParseInstrumentSymbol(spreadOrder.InstrumentSymbol);
                    
                    // Create spread handler
                    var spreadHandler = new SpreadHandler(
                        spreadOrder,
                        effectiveParameters,
                        numeratorInstrument,
                        denominatorInstrument);
                    
                    // Subscribe to handler events
                    spreadHandler.ExecutionEvent += OnSpreadHandlerEvent;
                    
                    // Store handler
                    _activeHandlers[spreadOrder.SpreadOrderId] = spreadHandler;
                    
                    // Start execution
                    spreadHandler.StartExecution();
                    
                    // Update statistics
                    _statistics.TotalExecutions++;
                    _statistics.LastExecutionTime = DateTime.UtcNow;
                    
                    // Return initial result
                    return new SpreadExecutionResult
                    {
                        SpreadOrderId = spreadOrder.SpreadOrderId,
                        Status = SpreadExecutionStatus.InProgress,
                        StartTime = DateTime.UtcNow
                    };
                }
                catch (Exception ex)
                {
                    LogMessage($"Error starting spread execution: {ex.Message}");
                    _statistics.FailedExecutions++;
                    
                    return new SpreadExecutionResult
                    {
                        SpreadOrderId = spreadOrder?.SpreadOrderId ?? 0,
                        Status = SpreadExecutionStatus.Failed,
                        StartTime = DateTime.UtcNow,
                        EndTime = DateTime.UtcNow,
                        ErrorMessage = ex.Message
                    };
                }
            }
        }
        
        public void CancelSpreadOrder(int spreadOrderId)
        {
            if (_activeHandlers.TryGetValue(spreadOrderId, out var handler))
            {
                LogMessage($"Cancelling spread order {spreadOrderId}");
                handler.CancelExecution();
            }
            else
            {
                LogMessage($"Cannot cancel spread order {spreadOrderId} - not found");
            }
        }
        
        public SpreadExecutionStatus GetExecutionStatus(int spreadOrderId)
        {
            if (_activeHandlers.TryGetValue(spreadOrderId, out var handler))
            {
                return handler.Status;
            }
            return SpreadExecutionStatus.Failed; // Order not found
        }
        
        public SpreadPosition GetCurrentPosition(int spreadOrderId)
        {
            if (_activeHandlers.TryGetValue(spreadOrderId, out var handler))
            {
                return handler.CurrentPosition;
            }
            return new SpreadPosition(); // Empty position if not found
        }
        
        #endregion
        
        #region Public Methods - Configuration
        
        public void UpdateDefaultParameters(SpreadExecutionParameters parameters)
        {
            if (parameters == null)
                throw new ArgumentNullException(nameof(parameters));
            
            parameters.Validate();
            _defaultParameters = parameters;
            LogMessage("Default execution parameters updated");
        }
        
        public SpreadExecutionParameters GetDefaultParameters()
        {
            return _defaultParameters;
        }
        
        #endregion
        
        #region Public Methods - Monitoring
        
        public IEnumerable<int> GetActiveExecutions()
        {
            return _activeHandlers.Keys.ToList();
        }
        
        public SpreadExecutionStatistics GetExecutionStatistics()
        {
            return _statistics;
        }
        
        #endregion
        
        #region Private Methods
        
        private (Instrument numerator, Instrument denominator) ParseInstrumentSymbol(string instrumentSymbol)
        {
            // Simple parsing for instrument symbols like "AAPL/MSFT"
            // In a real implementation, this would be more sophisticated
            
            if (string.IsNullOrEmpty(instrumentSymbol))
                throw new ArgumentException("Instrument symbol cannot be null or empty");
            
            var parts = instrumentSymbol.Split('/');
            if (parts.Length != 2)
                throw new ArgumentException($"Invalid instrument symbol format: {instrumentSymbol}. Expected format: 'NUMERATOR/DENOMINATOR'");
            
            var numeratorSymbol = parts[0].Trim();
            var denominatorSymbol = parts[1].Trim();
            
            // Get instruments from InstrumentManager
            
            var numeratorInstrument = _instrumentManager.Get(numeratorSymbol);
            var denominatorInstrument = _instrumentManager.Get(denominatorSymbol);
            
            if (numeratorInstrument == null)
                throw new ArgumentException($"Numerator instrument not found: {numeratorSymbol}");
            
            if (denominatorInstrument == null)
                throw new ArgumentException($"Denominator instrument not found: {denominatorSymbol}");
            
            return (numeratorInstrument, denominatorInstrument);
        }
        
        private void OnSpreadHandlerEvent(object sender, SpreadExecutionEventArgs e)
        {
            LogMessage($"Spread handler event: Order {e.SpreadOrderId}, Status: {e.Status}, Message: {e.Message}");
            
            // Update statistics based on final status
            if (e.Status == SpreadExecutionStatus.Completed)
            {
                _statistics.CompletedExecutions++;
                CleanupHandler(e.SpreadOrderId);
            }
            else if (e.Status == SpreadExecutionStatus.Failed)
            {
                _statistics.FailedExecutions++;
                CleanupHandler(e.SpreadOrderId);
            }
            else if (e.Status == SpreadExecutionStatus.Cancelled)
            {
                _statistics.CancelledExecutions++;
                CleanupHandler(e.SpreadOrderId);
            }
            
            // Forward event to subscribers
            ExecutionEvent?.Invoke(this, e);
        }
        
        private void CleanupHandler(int spreadOrderId)
        {
            if (_activeHandlers.TryRemove(spreadOrderId, out var handler))
            {
                handler.ExecutionEvent -= OnSpreadHandlerEvent;
                LogMessage($"Cleaned up handler for spread order {spreadOrderId}");
            }
        }
        
        private void LogMessage(string message)
        {
            Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] SpreadExecutionManager: {message}");
        }
        
        #endregion
        
        #region Public Methods - Integration Points (Placeholders)
        
        /// <summary>
        /// Handle execution reports from the order management system
        /// This would be called by the TemplatePair_OPUS OrderFeed component
        /// </summary>
        /// <param name="report">Execution report</param>
        public void OnExecutionReport(ExecutionReport report)
        {
            // Find the handler that owns this order
            foreach (var handler in _activeHandlers.Values)
            {
                // Check if this execution report belongs to this handler
                // In a real implementation, we would have better order tracking
                handler.OnExecutionReport(report);
            }
        }
        
        /// <summary>
        /// Handle market data updates
        /// This would be called by the TemplatePair_OPUS PriceFeed component
        /// </summary>
        /// <param name="instrument">Updated instrument</param>
        public void OnMarketDataUpdate(Instrument instrument)
        {
            // Forward market data updates to all active handlers
            foreach (var handler in _activeHandlers.Values)
            {
                handler.OnMarketDataUpdate(instrument);
            }
        }
        
        #endregion
    }
}

