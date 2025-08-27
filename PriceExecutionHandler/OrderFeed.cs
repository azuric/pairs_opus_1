using SmartQuant.Strategy_;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SmartQuant;
using Parameters;

namespace PriceExecutionHandler
{
    public partial class SellSide : SellSideStrategy_
    {

        public override void OnSendCommand(ExecutionCommand command_)
        {
            //RiskManager goes here
            CreateSellSideOrderAndSend(command_);
        }

        private void CreateSellSideOrderAndSend(ExecutionCommand command_)
        {

            Order _buySideOrder = command_.Order;
            Instrument _instrument = _buySideOrder.Instrument;


            double _priceToTrade = _buySideOrder.Price;


            Order _sellSideOrder = _buySideOrder.Side != OrderSide.Buy
                ? SellLimitOrder(_buySideOrder.Instrument, _buySideOrder.Qty, _priceToTrade)
                : BuyLimitOrder(_buySideOrder.Instrument, _buySideOrder.Qty, _priceToTrade);


            lock (this)
            {
                orderBuy2SellDict.Add(command_.OrderId, _sellSideOrder);
                orderSell2BuyDict.Add(_sellSideOrder.Id, _buySideOrder);

                if (ordersPerInstrument.ContainsKey(_sellSideOrder.Instrument.Id))
                    ordersPerInstrument[_sellSideOrder.Instrument.Id] += (int)_sellSideOrder.Qty;

            }

            Send(_sellSideOrder);
        }

        public override void OnCancelCommand(ExecutionCommand command_)
        {
            if (orderBuy2SellDict.TryGetValue(command_.OrderId, out Order _order))
                Cancel(_order);
        }

        public override void OnReplaceCommand(ExecutionCommand command_)
        {
            if (orderBuy2SellDict.TryGetValue(command_.OrderId, out Order _order))
                Replace(_order, command_.Price, command_.StopPx, command_.Qty);
        }

        protected override void OnExecutionReport(ExecutionReport report_)
        {

            if (orderBuy2SellDict.ContainsKey(report_.OrderId))
                return;

            if (orderSell2BuyDict.TryGetValue(report_.Order.Id, out Order _order))
            {
                ExecutionReport _newReport = new ExecutionReport(_order)
                {
                    DateTime = report_.DateTime,
                    ExecType = report_.ExecType,
                    OrdStatus = report_.OrdStatus,
                    LeavesQty = report_.LeavesQty,
                    LastPx = report_.LastPx,
                    AvgPx = report_.AvgPx,
                    CumQty = report_.CumQty,
                    LastQty = report_.LastQty,
                    Side = report_.Side

                };

                _newReport.LeavesQty = report_.LeavesQty;
                _newReport.Commission = report_.Commission;
                _newReport.Text = report_.Text;

                if (report_.Order.IsDone)
                {
                    lock (this)
                    {
                        orderSell2BuyDict.Remove(report_.Order.Id);
                        orderBuy2SellDict.Remove(_order.Id);

                        UpdatePosition(_newReport);
                    }
                }

                if (report_.Order.IsFilled || report_.Order.IsCancelled)
                {
                    if (ordersPerInstrument.ContainsKey(_order.Instrument.Id))
                        ordersPerInstrument[_order.Instrument.Id] -= (int)_order.Qty;
                }

                EmitExecutionReport(_newReport);
            }
            else
            {
                Console.WriteLine("OrderFeed: Order rejected " + report_.ToString());
            }
        }

        public void UpdatePosition(ExecutionReport report_)
        {
            if (report_.ExecType == ExecType.ExecTrade)
            {
                int qty = report_.Side == OrderSide.Buy ? (int)report_.LastQty : (int)-report_.LastQty;

                int instrumentID = report_.InstrumentId;
                if (positionPerInstrument.ContainsKey(instrumentID))
                    positionPerInstrument[instrumentID] += qty;
            }
        }

        protected void Reject(ExecutionCommand command_, string text_)
        {

            ExecutionReport _report = new ExecutionReport(command_)
            {
                DateTime = Clock.DateTime,
                ExecType = ExecType.ExecRejected,
                OrdStatus = OrderStatus.Rejected,
                LeavesQty = command_.Qty,
                Text = text_
            };

            EmitExecutionReport(_report);
        }


    }
}
