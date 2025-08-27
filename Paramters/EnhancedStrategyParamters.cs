using System;
using System.Collections.Generic;

namespace Parameters
{
    /// <summary>
    /// Enhanced strategy parameters with strategy type support
    /// </summary>
    public class EnhancedStrategyParameters
    {
        // Basic parameters
        public string name { get; set; }
        public string strategy_type { get; set; } = "simple";
        public string trade_instrument { get; set; }
        public double inst_tick_size { get; set; }
        public double inst_factor { get; set; }
        public int round { get; set; }
        public double round_denominator { get; set; }
        public long bar_size { get; set; }


        public TimeSpan start_calc_time { get; set; }
        public TimeSpan end_calc_time { get; set; }
        public TimeSpan start_time { get; set; }
        public TimeSpan end_time { get; set; }
        public TimeSpan exit_time { get; set; }
        public TimeSpan entry_allowedUntil { get; set; }

        // Output parameters
        public bool is_writing { get; set; }
        public bool is_write_metrics { get; set; }
        public string metrics_file { get; set; }

        // Strategy-specific thresholds
        public double[][] threshold_entry { get; set; }
        public double[][] threshold_exit { get; set; }

        // Additional parameters for enhanced strategies
        public int max_position_size { get; set; } = 1;
        public double risk_per_trade { get; set; } = 0.02; // 2% risk per trade
        public bool use_stop_loss { get; set; } = true;
        public bool use_take_profit { get; set; } = true;
        public Dictionary<string, object> additional_params { get; set; }
    }

    /// <summary>
    /// List of strategy parameters
    /// </summary>
    public class EnhancedStrategyParameterList
    {
        public List<StrategyParameters> strategyParamList { get; set; }

        public EnhancedStrategyParameterList()
        {
            strategyParamList = new List<StrategyParameters>();
        }
    }

}