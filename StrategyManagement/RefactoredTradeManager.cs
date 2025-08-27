using System;
using System.Collections.Generic;
using SmartQuant;
using SmartQuant.Strategy_;

namespace StrategyManagement
{
    /// <summary>
    /// Refactored trade manager implementing ITradeManager
    /// </summary>
    public class TradeManager : ITradeManager
    {
        private readonly Strategy_ strategy;
        private readonly Dictionary<int, Order> orders;
        private readonly object lockObject = new object();

        public bool HasLiveOrder { get; private set; }
        public int CurrentOrderId { get; private set; }
        public IReadOnlyDictionary<int, Order> ActiveOrders => orders;

        public TradeManager(Strategy_ strategy)
        {
            this.strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
            this.orders = new Dictionary<int, Order>();
            Reset();
        }

        public void CreateOrder(OrderSide side, double quantity, double price, Instrument instrument)
        {
            if (instrument == null)
                throw new ArgumentNullException(nameof(instrument));

            if (quantity <= 0)
                throw new ArgumentException("Quantity must be positive", nameof(quantity));

            lock (lockObject)
            {
                var order = strategy.Order(
                    instrument,
                    OrderType.Limit,
                    side,
                    Math.Abs(quantity),
                    double.NaN,
                    price
                );

                // Add any custom fields
                order[0] = "RJFU";
                order[1] = "MVJT8";

                strategy.Send(order);

                orders[order.Id] = order;
                CurrentOrderId = order.Id;
                HasLiveOrder = true;

                Console.WriteLine($"Created order {order.Id}: {side} {quantity} @ {price}");
            }
        }

        public void CancelOrder(int orderId)
        {
            lock (lockObject)
            {
                if (orders.TryGetValue(orderId, out var order))
                {
                    strategy.Cancel(order);
                    Console.WriteLine($"Cancelling order {orderId}");
                }
            }
        }

        public void CancelAllOrders()
        {
            lock (lockObject)
            {
                foreach (var order in orders.Values)
                {
                    strategy.Cancel(order);
                }
                Console.WriteLine($"Cancelling all {orders.Count} orders");
            }
        }

        public void ReplaceOrder(int orderId, double newPrice, double newQuantity)
        {
            lock (lockObject)
            {
                if (orders.TryGetValue(orderId, out var order))
                {
                    strategy.Replace(order, newPrice);
                    Console.WriteLine($"Replacing order {orderId}: new price={newPrice}, new qty={newQuantity}");
                }
            }
        }

        public void HandleOrderUpdate(Order order)
        {
            if (order == null) return;

            lock (lockObject)
            {
                if (!orders.ContainsKey(order.Id)) return;

                orders[order.Id] = order;

                switch (order.Status)
                {
                    case OrderStatus.New:
                    case OrderStatus.Replaced:
                        HasLiveOrder = true;
                        break;

                    case OrderStatus.Filled:
                    case OrderStatus.Cancelled:
                        orders.Remove(order.Id);
                        UpdateLiveOrderStatus();
                        break;

                    case OrderStatus.Rejected:
                        orders.Remove(order.Id);
                        UpdateLiveOrderStatus();
                        Console.WriteLine($"Order rejected: {order.Id} - {order.Text}");
                        break;
                }

                Console.WriteLine($"Order {order.Id} status: {order.Status}");
            }
        }

        private void UpdateLiveOrderStatus()
        {
            HasLiveOrder = orders.Count > 0;
            if (!HasLiveOrder)
            {
                CurrentOrderId = 0;
            }
        }

        public void Reset()
        {
            lock (lockObject)
            {
                CancelAllOrders();
                orders.Clear();
                HasLiveOrder = false;
                CurrentOrderId = 0;
            }
        }
    }
}