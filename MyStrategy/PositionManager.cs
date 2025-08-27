using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Parameters;
using SmartQuant;

namespace OpenQuant
{
    public class PositionManager
    {
        public int current_position;
        public double average_price;
        public double realised_pl;
        public double unrealised_pl;
        public double last_price;
        public DateTime last_entry_time;
        public DateTime first_entry_time;
        public DateTime exit_time;
        public double[] positions = new double[2];
        public TradeMetrics trade_metric;
        public List<TradeMetrics> cycle_metrics = new List<TradeMetrics>();
        private FileStream fs;
        private string fileName;
        private StrategyParameters strategyParameters;

        public PositionManager(StrategyParameters strategyParameters, string fileName)
        {
            this.fileName = fileName;
            this.strategyParameters = strategyParameters;
        }

        public void UpdatePosition(DateTime dt, int side, int position, double price)
        {
            int sigside = (side == 0) ? 1 : -1;
            int signedOrderQuantity = position * sigside;

            if (current_position == 0)
            {
                // Starting a new position
                average_price = price;
                first_entry_time = dt;
                last_entry_time = dt;
                trade_metric = new TradeMetrics(dt, price, position, side);
            }
            else if ((double)signedOrderQuantity / (double)current_position < -1)
            {
                // Position reversal - close current position and start new one
                double pnl = RealisePL(Math.Sign(current_position), average_price, price, Math.Abs(current_position));
                realised_pl += pnl;

                // Complete the current trade metric
                trade_metric.last_fill = dt;
                trade_metric.exit_price = price;
                trade_metric.pnl = pnl;
                trade_metric.cycle_time = (double)(dt - trade_metric.first_fill).TotalMinutes;

                print_cycle_metrics_realtime(trade_metric);
                cycle_metrics.Add(trade_metric);

                // Start new position with remaining quantity
                int remainingQty = Math.Abs(signedOrderQuantity) - Math.Abs(current_position);
                average_price = price;
                trade_metric = new TradeMetrics(dt, price, remainingQty, side);
            }
            else if ((double)signedOrderQuantity / (double)current_position > 0)
            {
                // Adding to existing position
                last_entry_time = dt;
                average_price = (average_price * (double)current_position + price * (double)signedOrderQuantity) / ((double)current_position + (double)signedOrderQuantity);
                trade_metric.Update_Fill(Math.Abs(current_position + signedOrderQuantity), average_price, dt);
            }
            else
            {
                // Reducing or closing position
                double pnl = RealisePL(Math.Sign(current_position),  average_price, price, Math.Abs(signedOrderQuantity));
                realised_pl += pnl;

                // If completely closing the position
                if (current_position + signedOrderQuantity == 0)
                {
                    trade_metric.last_fill = dt;
                    trade_metric.exit_price = price;
                    trade_metric.pnl = RealisePL(Math.Sign(current_position), average_price, price, Math.Abs(current_position));
                    trade_metric.cycle_time = (double)(dt - trade_metric.first_fill).TotalMinutes;

                    print_cycle_metrics_realtime(trade_metric);
                    cycle_metrics.Add(trade_metric);

                    // Reset for next trade
                    trade_metric = null;
                }
                else
                {
                    // Partially reducing position - update the trade metric but don't complete it
                    trade_metric.last_fill = dt;
                    trade_metric.Update_Price(price, dt);
                }
            }

            current_position += signedOrderQuantity;
        }

        public void Update_Trade_Metric(Bar bar)
        {
            if (trade_metric != null && current_position != 0)
            {
                if (current_position > 0)
                {
                    unrealised_pl = RealisePL(1, average_price, bar.Close, current_position);
                }
                else if (current_position < 0)
                {
                    unrealised_pl = RealisePL(-1, average_price, bar.Close, Math.Abs(current_position));
                }

                trade_metric.Update_Price(bar.Close, bar.DateTime);
            }
            else
            {
                unrealised_pl = 0.0;
            }
        }

        public double RealisePL(int side, double entryPrice, double exitPrice, int pos)
        {
            // side: 1 for long, -1 for short
            // For long positions: profit = (exit - entry) * quantity * factor
            // For short positions: profit = (entry - exit) * quantity * factor
            return side * pos * (exitPrice - entryPrice) * strategyParameters.inst_factor;
        }

        public void print_cycle_metrics()
        {
            string fd_1 = @"C:\tmp\Template\";

            using (var fs = new FileStream(fd_1 + "\\" + fileName + "120.25.15.1.mad.csv", FileMode.Create, FileAccess.Write, FileShare.Write))
            using (var sw = new StreamWriter(fs))
            {
                // Write header
                sw.WriteLine("first_fill,last_fill,side,avg_price,exit_price,avg_price_delta,cycle_time,maximum_adverse_excursion,maximum_favourable_excursion,max_position,time_since_last_fill,pnl");

                for (int i = 0; i < cycle_metrics.Count; i++)
                {
                    var metric = cycle_metrics[i];
                    sw.WriteLine($"{metric.first_fill:yyyy-MM-dd HH:mm:ss.fffffff},{metric.last_fill:yyyy-MM-dd HH:mm:ss.fffffff},{metric.side},{metric.avg_price},{metric.exit_price},{metric.avg_price_delta},{metric.cycle_time},{metric.maximum_adverse_excursion},{metric.maximum_favourable_excurion},{metric.max_position},{metric.time_since_last_fill},{metric.pnl}");
                }
            }
        }

        public void print_cycle_metrics_realtime(TradeMetrics tradeMetric)
        {
            string fd_1 = @"C:\tmp\Template\";

            using (var fs = new FileStream(fd_1 + "\\trade_metrics_rt.csv", FileMode.Append, FileAccess.Write, FileShare.Write))
            using (var sw = new StreamWriter(fs))
            {
                sw.WriteLine($"{tradeMetric.first_fill:yyyy-MM-dd HH:mm:ss.fffffff},{tradeMetric.last_fill:yyyy-MM-dd HH:mm:ss.fffffff},{tradeMetric.side},{tradeMetric.avg_price},{tradeMetric.exit_price},{tradeMetric.avg_price_delta},{tradeMetric.cycle_time},{tradeMetric.maximum_adverse_excursion},{tradeMetric.maximum_favourable_excurion},{tradeMetric.max_position},{tradeMetric.time_since_last_fill},{tradeMetric.pnl}");
            }
        }
    }

    public class TradeMetrics
    {
        public double cycle_time;
        public DateTime first_fill;
        public DateTime last_fill;
        public double avg_price_delta;
        public double avg_price;
        public double exit_price;
        public double maximum_adverse_excursion;
        public double maximum_favourable_excurion;
        public double time_since_last_fill;
        public double max_position;
        public double pnl;
        public int side;

        public TradeMetrics(DateTime first, double fill_price, double pos, int side)
        {
            first_fill = first;
            cycle_time = 0;
            last_fill = first;
            time_since_last_fill = 0;

            avg_price = fill_price;
            avg_price_delta = 0;
            maximum_adverse_excursion = 0;
            maximum_favourable_excurion = 0;
            exit_price = 0;
            pnl = 0;

            this.side = side;
            max_position = pos;
        }

        public void Update_Fill(double pos, double price, DateTime time)
        {
            if (pos > max_position)
            {
                max_position = pos;
            }

            last_fill = time;
            time_since_last_fill = 0;
            avg_price = price;
        }

        public void Update_Price(double price, DateTime current)
        {
            if (side == 0) // Long position
                avg_price_delta = price - avg_price;
            else // Short position
                avg_price_delta = avg_price - price;

            exit_price = price;

            if (avg_price_delta > maximum_favourable_excurion)
                maximum_favourable_excurion = avg_price_delta;
            else if (avg_price_delta < maximum_adverse_excursion)
                maximum_adverse_excursion = avg_price_delta;

            cycle_time = (double)(current - first_fill).TotalMinutes;
            time_since_last_fill = (double)(current - last_fill).TotalMinutes;
        }
    }
}