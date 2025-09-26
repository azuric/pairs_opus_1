using System;
using Parameters;
using SmartQuant;

namespace StrategyManagement
{
    /// <summary>
    /// Factory for creating strategy managers based on configuration
    /// </summary>
    public static class StrategyManagerFactory
    {
        public static IStrategyManager CreateStrategyManager(string strategyType, Instrument tradeInstrument)
        {
            if (string.IsNullOrWhiteSpace(strategyType))
                throw new ArgumentException("Strategy type cannot be null or empty", nameof(strategyType));

            switch (strategyType.ToLowerInvariant())
            {
                case "mean_reversion":
                    return new MeanReversionStrategyManager(tradeInstrument);

                case "mom_pair":
                    return new MomPairStrategyManager(tradeInstrument);

                case "mom_pair_us":
                    return new MomPairUSStrategyManager(tradeInstrument);

                case "momentum":
                    return new MomentumStrategyManager(tradeInstrument);

                case "multilevel":
                   return new MomentumMultiLevelStrategyManager(tradeInstrument);

                case "simple":
                case "default":
                    return new SimpleStrategyManager(tradeInstrument);

                default:
                    throw new NotSupportedException($"Strategy type '{strategyType}' is not supported");
            }
        }

        public static IStrategyManager CreateAndInitialize(StrategyParameters parameters, Instrument tradeInstrument)
        {
            if (parameters == null)
                throw new ArgumentNullException(nameof(parameters));

            // Determine strategy type from parameters
            // This could come from a specific field in the JSON or be inferred from the name
            string strategyType = DetermineStrategyType(parameters);

            var manager = CreateStrategyManager(strategyType, tradeInstrument);
            manager.Initialize(parameters);

            return manager;
        }

        private static string DetermineStrategyType(StrategyParameters parameters)
        {
            // Try to determine from the name
            if (parameters.name != null)
            {
                var nameLower = parameters.strategy_type.ToLowerInvariant();

                if (nameLower.Contains("mean") || nameLower.Contains("reversion"))
                    return "mean_reversion";

                if (nameLower.Contains("momentum") || nameLower.Contains("trend"))
                    return "momentum";

                if (nameLower.Contains("mom_pair_us"))
                    return "mom_pair_us";

                if (nameLower.Contains("mom_pair"))
                    return "mom_pair";

                if (nameLower.Contains("multilevel") || nameLower.Contains("multi_level"))
                    return "multilevel";


            }

            // Check if there's a strategy_type field in the parameters
            // For now, default to simple
            return "simple";
        }
    }
}