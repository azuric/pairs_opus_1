using System;
using System.Collections.Generic;
using SmartQuant;

namespace StrategyManagement
{
    /// <summary>
    /// Interface for trade/order management
    /// </summary>
    public interface ITradeManager
    {
        /// <summary>
        /// Indicates if there's a live order
        /// </summary>
        bool HasLiveOrder { get; }

        /// <summary>
        /// Current live order ID
        /// </summary>
        int CurrentOrderId { get; }

        /// <summary>
        /// Dictionary of active orders
        /// </summary>
        IReadOnlyDictionary<int, Order> ActiveOrders { get; }

        /// <summary>
        /// Create and send an order
        /// </summary>
        int CreateOrder(OrderSide side, double quantity, double price, Instrument instrument);

        /// <summary>
        /// Cancel an order
        /// </summary>
        void CancelOrder(int orderId);

        /// <summary>
        /// Cancel all orders
        /// </summary>
        void CancelAllOrders();

        /// <summary>
        /// Replace an order
        /// </summary>
        void ReplaceOrder(int orderId, double newPrice, double newQuantity);

        /// <summary>
        /// Handle order status updates
        /// </summary>
        void HandleOrderUpdate(Order order);

        /// <summary>
        /// Reset the trade manager
        /// </summary>
        void Reset();
    }
}