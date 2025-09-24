using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Parameters
{
    public class StrategyParameters
    {
        // Existing parameters
        public string name { get; set; }
        public string strategy_type { get; set; } = "simple";
        public string trade_instrument { get; set; }
        public string execution_instrument { get; set; }
        public double inst_tick_size { get; set; }
        public double inst_factor { get; set; }
        public int round { get; set; }
        public double round_denominator { get; set; }
        public long bar_size { get; set; }

        public TimeSpan start_calc_time { get; set; }
        public TimeSpan end_calc_time { get; set; }
        public TimeSpan start_time { get; set; }
        public TimeSpan end_time { get; set; }
        public TimeSpan entry_time { get; set; }
        public TimeSpan exit_time { get; set; }
        public TimeSpan entry_allowedUntil { get; set; }

        // Output parameters
        public bool is_writing { get; set; }
        public bool is_write_metrics { get; set; }
        public string metrics_file { get; set; }
        public string data_file { get; set; }

        // Strategy-specific thresholds
        public double[][] threshold_entry { get; set; }
        public double[][] threshold_exit { get; set; }

        // Additional parameters for enhanced strategies
        public int max_position_size { get; set; } = 1;
        public int position_size { get; set; } = 1;
        public double risk_per_trade { get; set; } = 0.02; // 2% risk per trade
        public bool use_stop_loss { get; set; } = true;
        public bool use_take_profit { get; set; } = true;
        public Dictionary<string, object> additional_params { get; set; }

        // NEW: Add signal source selection for pairs trading
        /// <summary>
        /// Specifies which instrument to use for signal generation in pairs trading.
        /// Valid values: "num"/"numerator", "den"/"denominator", "synth"/"synthetic"
        /// Default: "synth" for backward compatibility
        /// </summary>
        public string signal_source { get; set; } = "synth";
    }

    public class StrategyParameterList
    {
        public List<StrategyParameters> strategyParamList { get; set; }
    }
}