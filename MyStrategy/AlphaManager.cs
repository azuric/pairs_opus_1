using SmartQuant;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenQuant
{
    public class AlphaManager
    {
        protected readonly bool IsBar;
        protected DateTime CurrentDateTime;
        public List<string> AlphaList;
        public double[] Data;
        protected Dictionary<string, double> EMAs;
        public Dictionary<int, BarSeries> bars;

        protected bool isReady;
        private double run;

        // Common metrics
        protected readonly Dictionary<string, double> alphaValues;

        protected readonly Dictionary<int, int> instrumentIndexMap;

        public AlphaManager()
        {
            AlphaList = new List<string>();
            EMAs = new Dictionary<string, double>();
            bars = new Dictionary<int, BarSeries>();

            Data = new double[64];
            instrumentIndexMap = new Dictionary<int, int>();
            run = 0;

            alphaValues = new Dictionary<string, double>
                {
                    { "3", 2.0 / 4.0 },
                    { "5", 2.0 / 6.0 },
                    { "15", 2.0 / 16.0 },
                    { "30", 2.0 / 31.0 },
                    { "60", 2.0 / 61.0 },
                    { "120", 2.0 / 121.0 },
                    { "240", 2.0 / 241.0 },
                    { "480", 2.0 / 481.0 },
                    { "720", 2.0 / 721.0 },
                    { "1440", 2.0 / 1441.0 }
                };
        }

        public int GetPeriodOffset(string period)
        {
            // Maps period strings to array offsets
            Dictionary<string, int> periodOffsets = new Dictionary<string, int>
        {
            { "5", 0 },
            { "15", 1 },
            { "30", 2 },
            { "60", 3 },
            { "120", 4 },
            { "240", 5 },
            { "480", 6 },
            { "720", 7 }
        };

            return periodOffsets.TryGetValue(period, out int offset) ? offset : 0;
        }

        protected int GetDataIndex(string period, string metricType)
        {
            // Define base indices for different metric types
            Dictionary<string, int> metricBaseIndices = new Dictionary<string, int>
                {
                    { "diff", 0 },
                    { "run", 8 },
                    { "range", 16 },
                    { "std", 24 },
                    { "snr", 32 },
                    { "direction", 40 },
                    { "ticks", 48 },
                    { "tickvolume", 56 }
                };

            int periodOffset = GetPeriodOffset(period);
            int baseIndex = metricBaseIndices.TryGetValue(metricType, out int index) ? index : 0;

            return baseIndex + periodOffset;
        }

        public double[] GetData()
        {
            return Data;
        }


        protected double EMA(double alpha, double value, double prevEma)
        {
            if (double.IsNaN(prevEma))
                return value;

            return prevEma + alpha * (value - prevEma);
        }

        protected double EMV(double alpha, double value, double ema, double prevEmv)
        {
            if (double.IsNaN(prevEmv) || double.IsNaN(ema))
            {
                double variance = Math.Pow(value - ema, 2);
                return variance;
            }

            double xy = Math.Pow(value - ema, 2);
            return prevEmv + alpha * (xy - prevEmv);
        }

        protected double CalculateRun(double diff, double previousRun)
        {
            if (diff > 0.0)
                return previousRun > 0.0 ? previousRun + diff : diff;
            else if (diff < 0.0)
                return previousRun < 0.0 ? previousRun + diff : diff;

            return previousRun;
        }

        protected double CalculateRange(BarSeries bars, string period)
        {
            int periodValue = Convert.ToInt32(period);
            if (bars.Count < periodValue)
                return Double.NaN;

            double high = bars.HighestHigh(periodValue);
            double low = bars.LowestLow(periodValue);

            return (high - low)/low;
        }

        protected virtual void ProcessBar(Bar bar, Bar previousBar)
        {
            if (bar == null || previousBar == null)
                return;

            // Get bar data
            double price = bar.Close;
            double prevPrice = previousBar.Close;
            int direction = Convert.ToInt32(bar[0]);
            int ticks = Convert.ToInt32(bar[1]);
            int tickVolume = Convert.ToInt32(bar.Volume);

            // Calculate metrics
            double diff = (price - prevPrice) / prevPrice;
            //double range = CalculateRange(bar, previousBar);

            isReady = true;
        }

        protected void UpdateRangeMetrics(BarSeries bars)
        {
            foreach (var alpha in alphaValues)
            {
                string period = alpha.Key;
                double alphaValue = alpha.Value;

                // Range metrics stored starting at index 16
                int baseIndex = 16;
                int periodOffset = GetPeriodOffset(period);
                int dataIndex = baseIndex + periodOffset;

                // Get previous EMA value from Data array and update
                Data[dataIndex] = CalculateRange(bars, period);
            }
        }

        protected void UpdateDirectionalMetrics(BarSeries bars)
        {
            // Get directional data from bar
            Bar currentBar = bars.Last;
            double price = currentBar.Close;
            double prevPrice = bars.Ago(1).Close;
            double prevPrice2 = bars.Ago(2).Close;
            int direction = Convert.ToInt32(currentBar[0]);
            int ticks = Convert.ToInt32(currentBar[1]);
            int tickVolume = Convert.ToInt32(currentBar.Volume);           

            double diff = (price - prevPrice) / prevPrice;
            double diff2 = (prevPrice - prevPrice2) / prevPrice2;
            double snr = diff * diff2;

            run = CalculateRun(diff, run);

            //if (direction != 0)
            {
                foreach (var alpha in alphaValues)
                {
                    string period = alpha.Key;
                    double alphaValue = alpha.Value;
                    int periodOffset = GetPeriodOffset(period);

                    // Price metrics (index 0)
                    int priceIndex = 0 + periodOffset;
                    Data[priceIndex] = EMA(alphaValue, diff, Data[priceIndex]);

                    int runIndex = 8 + periodOffset;
                    Data[runIndex] = EMA(alphaValue, run, Data[runIndex]);

                    // Std metrics (index 24)
                    int stdIndex = 24 + periodOffset;
                    Data[stdIndex] = EMV(alphaValue, diff, Data[stdIndex - 24], Data[stdIndex]);

                    // Std metrics (index 24)
                    int snrIndex = 32 + periodOffset;
                    Data[snrIndex] = EMA(alphaValue, snr, Data[snrIndex]);

                    // Direction metrics (index 40)
                    int directionIndex = 40 + periodOffset;
                    Data[directionIndex] = EMA(alphaValue, direction, Data[directionIndex]);

                    // Ticks metrics (index 48)
                    int ticksIndex = 48 + periodOffset;
                    Data[ticksIndex] = EMA(alphaValue, ticks, Data[ticksIndex]);

                    // Tick volume metrics (index 56)
                    int volumeIndex = 56 + periodOffset;
                    Data[volumeIndex] = EMA(alphaValue, tickVolume, Data[volumeIndex]);
                }
            }
        }

        public void Update(BarSeries bars) 
        {
            if (bars == null) return;

            if (bars.Count <= 2) return;
                
            int instrumentIndex = GetInstrumentIndex(bars);
            Bar currentBar = bars.Last;

            Bar previousBar = bars.Ago(1);
            ProcessBar(currentBar, previousBar);
            UpdateDirectionalMetrics(bars);
            UpdateRangeMetrics(bars);
            GenerateAlphaData(bars);
        }

        protected void GenerateAlphaData(BarSeries bars)
        {
            if (bars.Count == 0) return;

            var currentBar = bars.Last;
            StringBuilder sb = new StringBuilder();

            // Add timestamp and price
            sb.Append(currentBar.DateTime.ToString("yyyy-MM-dd HH:mm:ss.fffffff"));
            sb.Append(",").Append(currentBar.Close);

            // Add all calculated metrics
            for (int i = 0; i < Data.Length; i++)
            {
                sb.Append(",").Append(Data[i]);
            }

            AlphaList.Add(sb.ToString());
        }



        protected virtual int GetInstrumentIndex(BarSeries series)
        {
            // Override in derived classes to provide specific instrument index mapping
            return 0;
        }

        public Dictionary<int, BarSeries> GetBars()
        {
            return bars;
        }
    }
}