using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using SmartQuant;


namespace PriceExecutionHandler
{
    /// <summary>
    /// Adapter for integrating SpreadHandler with TemplatePair_OPUS OrderFeed system
    /// </summary>
    public class OrderFeedAdapter : IOrderFeedAdapter
    {
        #region Private Fields
        
        private readonly SellSide _sellSideStrategy;
        private readonly object _lockObject = new object();
        
        // Spread order tracking
        private readonly ConcurrentDictionary<int, SpreadOrderInfo> _spreadOrders;
        private readonly ConcurrentDictionary<int, int> _componentToSpreadMap;
        
        // Order ID generation
        private int _nextOrderId = 10000; // Start from 10000 to avoid conflicts
        
        #endregion
        
        #region Events
        
        public event EventHandler<SpreadExecutionReportEventArgs> SpreadExecutionReport;
        public event EventHandler<OrderSubmissionErrorEventArgs> OrderSubmissionError;
        
        #endregion
        
        #region Constructor
        
        public OrderFeedAdapter(SellSide sellSideStrategy)
        {
            _sellSideStrategy = sellSideStrategy ?? throw new ArgumentNullException(nameof(sellSideStrategy));
            _spreadOrders = new ConcurrentDictionary<int, SpreadOrderInfo>();
            _componentToSpreadMap = new ConcurrentDictionary<int, int>();
            
            LogMessage("OrderFeedAdapter initialized");
        }
        
        #endregion
        
        #region Order Submission
        
        public bool SubmitOrder(Order order, int spreadOrderId)
        {
            try
            {
                lock (_lockObject)
                {
                    LogMessage($"Submitting order for spread {spreadOrderId}: {order.Instrument.Symbol} {order.Side} {order.Qty} @ {order.Price}");
                    
                    // Set order text to identify as spread order
                    order.Text = $"Spread_{spreadOrderId}_{order.Text}";
                    
                    // Submit order through SellSide strategy
                    // Note: This integrates with the existing OrderFeed system
                    _sellSideStrategy.Send(order);
                    
                    // Track the order
                    _componentToSpreadMap[order.Id] = spreadOrderId;
                    
                    // Update spread order info
                    if (_spreadOrders.TryGetValue(spreadOrderId, out var spreadInfo))
                    {
                        spreadInfo.ComponentOrders.Add(order.Id);
                        spreadInfo.LastActivity = DateTime.UtcNow;
                    }
                    
                    LogMessage($"Order submitted successfully: OrderId={order.Id}, SpreadId={spreadOrderId}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Error submitting order for spread {spreadOrderId}: {ex.Message}");
                RaiseOrderSubmissionError(spreadOrderId, order, ex.Message, ex);
                return false;
            }
        }
        
        public bool CancelOrder(Order order)
        {
            try
            {
                lock (_lockObject)
                {
                    LogMessage($"Cancelling order: {order.Id}");
                    
                    // Cancel through SellSide strategy
                    _sellSideStrategy.Cancel(order);
                    
                    LogMessage($"Order cancel request submitted: {order.Id}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Error cancelling order {order.Id}: {ex.Message}");
                
                // Try to get spread order ID for error reporting
                var spreadOrderId = GetSpreadOrderId(order.Id);
                RaiseOrderSubmissionError(spreadOrderId, order, $"Cancel failed: {ex.Message}", ex);
                return false;
            }
        }
        
        public bool ReplaceOrder(Order order, double newPrice, double newQuantity)
        {
            try
            {
                lock (_lockObject)
                {
                    LogMessage($"Replacing order {order.Id}: Price {order.Price}->{newPrice}, Qty {order.Qty}->{newQuantity}");
                    
                    // Replace through SellSide strategy
                    _sellSideStrategy.Replace(order, newPrice, 0, newQuantity);
                    
                    LogMessage($"Order replace request submitted: {order.Id}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Error replacing order {order.Id}: {ex.Message}");
                
                // Try to get spread order ID for error reporting
                var spreadOrderId = GetSpreadOrderId(order.Id);
                RaiseOrderSubmissionError(spreadOrderId, order, $"Replace failed: {ex.Message}", ex);
                return false;
            }
        }
        
        #endregion
        
        #region Order Tracking
        
        public void RegisterSpreadOrder(int spreadOrderId, int componentOrderId, SpreadOrderType orderType)
        {
            lock (_lockObject)
            {
                LogMessage($"Registering spread order: SpreadId={spreadOrderId}, ComponentId={componentOrderId}, Type={orderType}");
                
                // Create or update spread order info
                var spreadInfo = _spreadOrders.GetOrAdd(spreadOrderId, id => new SpreadOrderInfo
                {
                    SpreadOrderId = id,
                    CreatedTime = DateTime.UtcNow,
                    ComponentOrders = new List<int>(),
                    OrderTypes = new Dictionary<int, SpreadOrderType>()
                });
                
                // Track component order
                spreadInfo.ComponentOrders.Add(componentOrderId);
                spreadInfo.OrderTypes[componentOrderId] = orderType;
                spreadInfo.LastActivity = DateTime.UtcNow;
                
                // Map component to spread
                _componentToSpreadMap[componentOrderId] = spreadOrderId;
                
                LogMessage($"Spread order registered successfully");
            }
        }
        
        public void UnregisterSpreadOrder(int spreadOrderId)
        {
            lock (_lockObject)
            {
                LogMessage($"Unregistering spread order: {spreadOrderId}");
                
                if (_spreadOrders.TryRemove(spreadOrderId, out var spreadInfo))
                {
                    // Remove component order mappings
                    foreach (var componentOrderId in spreadInfo.ComponentOrders)
                    {
                        _componentToSpreadMap.TryRemove(componentOrderId, out _);
                    }
                    
                    LogMessage($"Spread order unregistered: {spreadOrderId}, removed {spreadInfo.ComponentOrders.Count} component orders");
                }
                else
                {
                    LogMessage($"Spread order not found for unregistration: {spreadOrderId}");
                }
            }
        }
        
        public bool IsSpreadOrder(int orderId)
        {
            return _componentToSpreadMap.ContainsKey(orderId);
        }
        
        public int GetSpreadOrderId(int componentOrderId)
        {
            return _componentToSpreadMap.TryGetValue(componentOrderId, out var spreadOrderId) ? spreadOrderId : 0;
        }
        
        #endregion
        
        #region Position Integration
        
        public void UpdateSpreadPosition(int instrumentId, int quantity, double price)
        {
            try
            {
                // This integrates with the existing position tracking in SellSide
                // The SellSide.UpdatePosition method will be called automatically through execution reports
                LogMessage($"Spread position update: Instrument={instrumentId}, Qty={quantity}, Price={price:F4}");
                
                // Note: Actual position updates happen through the execution report flow
                // This method is mainly for logging and potential future enhancements
            }
            catch (Exception ex)
            {
                LogMessage($"Error updating spread position: {ex.Message}");
            }
        }
        
        public int GetCurrentPosition(int instrumentId)
        {
            try
            {
                // Access the existing position tracking from SellSide
                // Note: This requires access to the positionPerInstrument dictionary
                // For now, return 0 as placeholder - will be enhanced in Phase 4
                LogMessage($"Getting current position for instrument: {instrumentId}");
                return 0; // Placeholder
            }
            catch (Exception ex)
            {
                LogMessage($"Error getting current position for instrument {instrumentId}: {ex.Message}");
                return 0;
            }
        }
        
        #endregion
        
        #region Utility Methods
        
        public int GenerateOrderId()
        {
            lock (_lockObject)
            {
                return ++_nextOrderId;
            }
        }
        
        public MarketData GetMarketData(int instrumentId)
        {
            try
            {
                var instrument = _sellSideStrategy.InstrumentManager.GetById(instrumentId);
                if (instrument == null)
                    return null;
                
                return new MarketData
                {
                    InstrumentId = instrumentId,
                    BidPrice = instrument.Bid?.Price,
                    AskPrice = instrument.Ask?.Price,
                    LastPrice = instrument.Trade?.Price,
                    BidSize = instrument.Bid?.Size,
                    AskSize = instrument.Ask?.Size,
                    LastUpdate = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                LogMessage($"Error getting market data for instrument {instrumentId}: {ex.Message}");
                return null;
            }
        }
        
        #endregion
        
        #region Public Methods for SellSide Integration
        
        /// <summary>
        /// Called by SellSide.OnExecutionReport to route spread-related execution reports
        /// </summary>
        /// <param name="report">Execution report</param>
        /// <returns>True if report was handled as spread order</returns>
        public bool ProcessExecutionReport(ExecutionReport report)
        {
            try
            {
                var orderId = report.Order.Id;
                
                // Check if this is a spread order
                if (!IsSpreadOrder(orderId))
                    return false;
                
                var spreadOrderId = GetSpreadOrderId(orderId);
                if (spreadOrderId == 0)
                    return false;
                
                // Get order type
                var orderType = SpreadOrderType.Primary; // Default
                if (_spreadOrders.TryGetValue(spreadOrderId, out var spreadInfo))
                {
                    spreadInfo.OrderTypes.TryGetValue(orderId, out orderType);
                    spreadInfo.LastActivity = DateTime.UtcNow;
                }
                
                LogMessage($"Processing spread execution report: SpreadId={spreadOrderId}, OrderId={orderId}, Type={orderType}, Status={report.OrdStatus}");
                
                // Raise event for SpreadHandler
                RaiseSpreadExecutionReport(spreadOrderId, orderId, orderType, report);
                
                return true;
            }
            catch (Exception ex)
            {
                LogMessage($"Error processing execution report: {ex.Message}");
                return false;
            }
        }
        
        #endregion
        
        #region Private Methods
        
        private void RaiseSpreadExecutionReport(int spreadOrderId, int componentOrderId, SpreadOrderType orderType, ExecutionReport report)
        {
            try
            {
                var eventArgs = new SpreadExecutionReportEventArgs(spreadOrderId, componentOrderId, orderType, report);
                SpreadExecutionReport?.Invoke(this, eventArgs);
            }
            catch (Exception ex)
            {
                LogMessage($"Error raising spread execution report event: {ex.Message}");
            }
        }
        
        private void RaiseOrderSubmissionError(int spreadOrderId, Order order, string errorMessage, Exception exception = null)
        {
            try
            {
                var eventArgs = new OrderSubmissionErrorEventArgs(spreadOrderId, order, errorMessage, exception);
                OrderSubmissionError?.Invoke(this, eventArgs);
            }
            catch (Exception ex)
            {
                LogMessage($"Error raising order submission error event: {ex.Message}");
            }
        }
        
        private void LogMessage(string message)
        {
            Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] OrderFeedAdapter: {message}");
        }
        
        #endregion
        
        #region Helper Classes
        
        private class SpreadOrderInfo
        {
            public int SpreadOrderId { get; set; }
            public DateTime CreatedTime { get; set; }
            public DateTime LastActivity { get; set; }
            public List<int> ComponentOrders { get; set; }
            public Dictionary<int, SpreadOrderType> OrderTypes { get; set; }
        }
        
        #endregion
    }
}

