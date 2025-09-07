using System;
using SmartQuant;


namespace PriceExecutionHandler
{
    /// <summary>
    /// Interface for integrating SpreadHandler with TemplatePair_OPUS OrderFeed system
    /// </summary>
    public interface IOrderFeedAdapter
    {
        #region Order Submission
        
        /// <summary>
        /// Submit an order through the OrderFeed system
        /// </summary>
        /// <param name="order">Order to submit</param>
        /// <param name="spreadOrderId">Associated spread order ID for tracking</param>
        /// <returns>True if order was successfully submitted</returns>
        bool SubmitOrder(Order order, int spreadOrderId);
        
        /// <summary>
        /// Cancel an order through the OrderFeed system
        /// </summary>
        /// <param name="order">Order to cancel</param>
        /// <returns>True if cancel request was successfully submitted</returns>
        bool CancelOrder(Order order);
        
        /// <summary>
        /// Replace/modify an order through the OrderFeed system
        /// </summary>
        /// <param name="order">Order to replace</param>
        /// <param name="newPrice">New price</param>
        /// <param name="newQuantity">New quantity</param>
        /// <returns>True if replace request was successfully submitted</returns>
        bool ReplaceOrder(Order order, double newPrice, double newQuantity);
        
        #endregion
        
        #region Order Tracking
        
        /// <summary>
        /// Register a spread order for execution report routing
        /// </summary>
        /// <param name="spreadOrderId">Spread order ID</param>
        /// <param name="componentOrderId">Component order ID</param>
        /// <param name="orderType">Type of component order (primary/hedge)</param>
        void RegisterSpreadOrder(int spreadOrderId, int componentOrderId, SpreadOrderType orderType);
        
        /// <summary>
        /// Unregister a spread order when execution is complete
        /// </summary>
        /// <param name="spreadOrderId">Spread order ID to unregister</param>
        void UnregisterSpreadOrder(int spreadOrderId);
        
        /// <summary>
        /// Check if an order is associated with a spread
        /// </summary>
        /// <param name="orderId">Order ID to check</param>
        /// <returns>True if order is part of a spread execution</returns>
        bool IsSpreadOrder(int orderId);
        
        /// <summary>
        /// Get the spread order ID associated with a component order
        /// </summary>
        /// <param name="componentOrderId">Component order ID</param>
        /// <returns>Spread order ID, or 0 if not found</returns>
        int GetSpreadOrderId(int componentOrderId);
        
        #endregion
        
        #region Position Integration
        
        /// <summary>
        /// Update position tracking for spread execution
        /// </summary>
        /// <param name="instrumentId">Instrument ID</param>
        /// <param name="quantity">Quantity change (signed)</param>
        /// <param name="price">Execution price</param>
        void UpdateSpreadPosition(int instrumentId, int quantity, double price);
        
        /// <summary>
        /// Get current position for an instrument
        /// </summary>
        /// <param name="instrumentId">Instrument ID</param>
        /// <returns>Current position quantity</returns>
        int GetCurrentPosition(int instrumentId);
        
        #endregion
        
        #region Events
        
        /// <summary>
        /// Event fired when an execution report is received for a spread order
        /// </summary>
        event EventHandler<SpreadExecutionReportEventArgs> SpreadExecutionReport;
        
        /// <summary>
        /// Event fired when an order submission fails
        /// </summary>
        event EventHandler<OrderSubmissionErrorEventArgs> OrderSubmissionError;
        
        #endregion
        
        #region Utility Methods
        
        /// <summary>
        /// Generate a unique order ID for spread orders
        /// </summary>
        /// <returns>Unique order ID</returns>
        int GenerateOrderId();
        
        /// <summary>
        /// Get current market data for an instrument
        /// </summary>
        /// <param name="instrumentId">Instrument ID</param>
        /// <returns>Current market data or null if not available</returns>
        MarketData GetMarketData(int instrumentId);
        
        #endregion
    }
    
    /// <summary>
    /// Type of spread component order
    /// </summary>
    public enum SpreadOrderType
    {
        Primary,    // First leg to be executed
        Hedge      // Second leg to hedge the first
    }
    
    /// <summary>
    /// Event arguments for spread execution reports
    /// </summary>
    public class SpreadExecutionReportEventArgs : EventArgs
    {
        public int SpreadOrderId { get; set; }
        public int ComponentOrderId { get; set; }
        public SpreadOrderType OrderType { get; set; }
        public ExecutionReport ExecutionReport { get; set; }
        public DateTime Timestamp { get; set; }
        
        public SpreadExecutionReportEventArgs(int spreadOrderId, int componentOrderId, 
            SpreadOrderType orderType, ExecutionReport executionReport)
        {
            SpreadOrderId = spreadOrderId;
            ComponentOrderId = componentOrderId;
            OrderType = orderType;
            ExecutionReport = executionReport;
            Timestamp = DateTime.UtcNow;
        }
    }
    
    /// <summary>
    /// Event arguments for order submission errors
    /// </summary>
    public class OrderSubmissionErrorEventArgs : EventArgs
    {
        public int SpreadOrderId { get; set; }
        public Order Order { get; set; }
        public string ErrorMessage { get; set; }
        public Exception Exception { get; set; }
        public DateTime Timestamp { get; set; }
        
        public OrderSubmissionErrorEventArgs(int spreadOrderId, Order order, string errorMessage, Exception exception = null)
        {
            SpreadOrderId = spreadOrderId;
            Order = order;
            ErrorMessage = errorMessage;
            Exception = exception;
            Timestamp = DateTime.UtcNow;
        }
    }
    
    /// <summary>
    /// Current market data for an instrument
    /// </summary>
    public class MarketData
    {
        public int InstrumentId { get; set; }
        public double? BidPrice { get; set; }
        public double? AskPrice { get; set; }
        public double? LastPrice { get; set; }
        public int? BidSize { get; set; }
        public int? AskSize { get; set; }
        public DateTime LastUpdate { get; set; }
        
        public double? MidPrice => (BidPrice.HasValue && AskPrice.HasValue) ? (BidPrice + AskPrice) / 2.0 : null;
        public double? Spread => (BidPrice.HasValue && AskPrice.HasValue) ? AskPrice - BidPrice : null;
    }
}

