using SmartQuant;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenQuant
{
    public class TradeManager
    {
        MyStrategy BuySide { get; set; }
        public Instrument Instrument { get; set; }

        public TradeManager(MyStrategy buySide)
        {
            BuySide = buySide;
            Instrument = BuySide.Instrument;
            Orders = new Dictionary<int, Order>();
        }

        public Dictionary<int, Order> Orders { get; protected set; }

        public bool LiveOrder { get; set; }
        public int OrderId { get; protected set; }

        public void HandleOrders(Order order)
        {
            int UID = order.Id;

            if (Orders.ContainsKey(UID))
            {
                Orders[UID] = order;

                if (order.Status == SmartQuant.OrderStatus.New || order.Status == SmartQuant.OrderStatus.Replaced)
                {
                    LiveOrder = true;
                }
                if (order.Status == SmartQuant.OrderStatus.Filled || order.Status == SmartQuant.OrderStatus.Cancelled)
                {
                    LiveOrder = false;

                    Orders.Remove(UID);
                }
                if (order.Status == SmartQuant.OrderStatus.Rejected)
                {
                    LiveOrder = false;
                    Orders.Remove(UID);
                    Console.WriteLine("Rejected: " + UID);
                }
            }
        }

        public void CreateOrder(int side, double size, double prc)
        {
            SmartQuant.Order order = BuySide.Order(Instrument, OrderType.Limit, (SmartQuant.OrderSide)side, Math.Abs(size), double.NaN, prc);
            LiveOrder = true;
            order[0] = "RJFU";
            order[1] = "MVJT8";
            BuySide.Send(order);
            Orders.Add(order.Id, order);
            OrderId = order.Id;
        }

        public void CancelOrder(int orderId)
        {
            if (Orders.Count > 0)
            {
                if (Orders.ContainsKey(orderId))
                {
                    var order = Orders[orderId];
                    BuySide.Cancel(order);
                }
            }
        }
    }
}
