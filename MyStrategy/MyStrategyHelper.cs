using OpenQuant;
using Parameters;
using SmartQuant.Strategy_;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenQuant
{
    //public partial class MyStrategy : InstrumentStrategy_
    //{
    //    protected override void OnFill(SmartQuant.Fill fill)
    //    {
    //        Log(fill, fillGroup, fill.DateTime);

    //        int UID = fill.Order.Id;
    //        double price = fill.Price;
    //        Int32 size = Convert.ToInt32(fill.Qty);
    //        DateTime time = fill.DateTime;

    //        PositionManager.UpdatePosition(fill.DateTime, (int)fill.Side, (int)fill.Qty, fill.Price);

    //        if (logLevel <= 7)
    //            Console.WriteLine("SmartQuant Fill: date" + fill.DateTime.ToString("yyyy-MM-dd HH:mm:ss.fffffff") + ", " + "uid " + fill.Order.Id + ", side " + fill.Side + ", qty " + fill.Qty + ", price " + fill.Price);

    //    }

    //    protected override void OnOrderReplaced(SmartQuant.Order order)
    //    {
    //        TradeManager.HandleOrders(order);
    //        Console.WriteLine(Clock.DateTime +", " + order.Status);
    //    }

    //    protected override void OnNewOrder(SmartQuant.Order order)
    //    {
    //        TradeManager.HandleOrders(order);
    //        Console.WriteLine(Clock.DateTime + ", " + order.Status);
    //    }

    //    protected override void OnOrderCancelled(SmartQuant.Order order)
    //    {
    //        TradeManager.HandleOrders(order);
    //        Console.WriteLine(Clock.DateTime + ", " + order.Status);
    //    }

    //    protected override void OnOrderFilled(SmartQuant.Order order)
    //    {
    //        TradeManager.HandleOrders(order);
    //        Console.WriteLine(Clock.DateTime + ", " + order.Status);
    //    }

    //    protected override void OnStrategyStop()
    //    {
    //        if (StrategyParameters.is_write_metrics)
    //            PositionManager.print_cycle_metrics();
    //    }
    //}
}
