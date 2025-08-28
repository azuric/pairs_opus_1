namespace StrategyManagement
{
    /// <summary>
    /// Defines the source instrument for signal generation in pairs trading
    /// </summary>
    public enum SignalSource
    {
        /// <summary>
        /// Use numerator instrument for signals (first instrument in pairs)
        /// In single instrument mode, this is the only instrument
        /// </summary>
        Numerator = 0,

        /// <summary>
        /// Use denominator instrument for signals (second instrument in pairs)
        /// Only valid in pairs trading mode
        /// </summary>
        Denominator = 1,

        /// <summary>
        /// Use synthetic instrument for signals (calculated ratio in pairs)
        /// This is the default for backward compatibility
        /// </summary>
        Synthetic = 2
    }
}