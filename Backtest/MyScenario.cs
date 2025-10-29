using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using SmartQuant;
using SmartQuant.Strategy_;
using Parameters;
using PriceExecutionHandler;
using StrategyManagement;

namespace OpenQuant
{
    /// <summary>
    /// Updated backtest implementation supporting both single instruments and pairs
    /// </summary>
    public partial class BackTest : Scenario_
    {
        private const string TRADE_ROOT = @"C:\tmp\Template";

        private ContainerStrategy containerStrategy;
        private SellSide sellSide;
        private StrategyParameterList strategyParametersList;

        public bool IsBarMode { get; set; } = true;

        public BackTest(Framework framework) : base(framework)
        {
        }

        public override void Run()
        {
            Console.WriteLine("=== Starting Enhanced Backtest (Single + Pairs) ===");

            // Initialize container strategy
            containerStrategy = new ContainerStrategy(framework, "Container");

            // Load parameters from JSON
            LoadParameters();

            // Initialize sell side handler
            InitializeSellSide();

            // Configure simulation settings
            
            ConfigureSimulation();

            // Configure providers
            containerStrategy.ExecutionProvider = ProviderManager.GetExecutionProvider("ExecutionSimulator");
            containerStrategy.DataProvider = ProviderManager.GetDataProvider("ExecutionSimulator");

            // Add strategies based on parameters
            AddStrategies();

            // Start the backtest
            Console.WriteLine("\nStarting backtest execution...");

            Start(containerStrategy, StrategyMode.Backtest);
        }
        

        private void LoadParameters()
        {
            string[] args = Environment.GetCommandLineArgs();
            string parameterFile = args.Length > 1 ? args[1] : "sample_strategies";

            // Handle comma-separated values if present
            string[] splitString = parameterFile.Split(',');
            parameterFile = splitString[0];
            StrategyManager.Global["tag"] = splitString[0];
            string jsonPath = "";

            if (args.Length == 2)
            {
                jsonPath = Path.Combine(TRADE_ROOT, parameterFile + ".json");
            }
            else
            {
                jsonPath = @"D:\test_tags\" + splitString[0] + ".json";
            }

            Console.WriteLine($"Loading parameters from: {jsonPath}");

            if (!File.Exists(jsonPath))
            {
                throw new FileNotFoundException($"Parameter file not found: {jsonPath}");
            }

            string jsonContent = File.ReadAllText(jsonPath);
            
            strategyParametersList = JsonConvert.DeserializeObject<StrategyParameterList>(jsonContent);

            if (strategyParametersList?.strategyParamList == null || strategyParametersList.strategyParamList.Count == 0)
            {
                throw new InvalidOperationException("No strategies found in parameter file");
            }

            Console.WriteLine($"Successfully loaded {strategyParametersList.strategyParamList.Count} strategy configurations");

            // Display loaded strategies
            Console.WriteLine("\nLoaded strategies:");
            foreach (var param in strategyParametersList.strategyParamList)
            {
                string instrumentType = IsInstrumentPair(param.trade_instrument) ? "PAIR" : "SINGLE";
                Console.WriteLine($"  - {param.name} ({param.strategy_type}) on {param.trade_instrument} [{instrumentType}]");
            }
        }

        private void InitializeSellSide()
        {
            Console.WriteLine("\nInitializing sell side handler...");
            sellSide = new SellSide(framework, "FeedHandler");

            // Process all strategies to set up synthetic instruments BEFORE adding to container
            SetupSyntheticInstruments();

            containerStrategy.Add(sellSide);
        }

        private void SetupSyntheticInstruments()
        {
            Console.WriteLine("\nSetting up synthetic instruments...");

            foreach (var parameters in strategyParametersList.strategyParamList)
            {
                if (IsInstrumentPair(parameters.trade_instrument))
                {
                    var (numerator, denominator) = ParseInstrumentPair(parameters.trade_instrument);
                    Console.WriteLine($"Setting up synthetic: {parameters.trade_instrument} = {numerator} / {denominator}");

                    // Add synthetic instrument mapping to sell side
                    sellSide.AddSyntheticInstrument(parameters.trade_instrument, numerator, denominator);
                }
            }
        }

        private void ConfigureSimulation()
        {
            Console.WriteLine("\nConfiguring simulation settings...");

            // Set simulation date range
            DataSimulator.DateTime1 = new DateTime(2017, 1, 1);
            DataSimulator.DateTime2 = new DateTime(2025, 12, 1);

            Console.WriteLine($"Simulation period: {DataSimulator.DateTime1:yyyy-MM-dd} to {DataSimulator.DateTime2:yyyy-MM-dd}");

            if (!IsBarMode)
            {
                Console.WriteLine("Mode: Tick data");
                ConfigureTickMode();
            }
            else
            {
                Console.WriteLine("Mode: Bar data");
                ConfigureBarMode();
            }

            // Clear bar factory
            BarFactory.Clear();
        }

        private void ConfigureTickMode()
        {
            // Data simulator settings for tick mode
            DataSimulator.SubscribeAll = false;
            DataSimulator.SubscribeBar = false;
            DataSimulator.SubscribeTrade = true;
            DataSimulator.SubscribeQuote = true;
            DataSimulator.SubscribeBid = true;
            DataSimulator.SubscribeAsk = true;

            // Execution simulator settings for tick mode
            ExecutionSimulator.FillAtLimitPrice = true;
            ExecutionSimulator.FillOnTrade = true;
            ExecutionSimulator.FillOnQuote = true;
            ExecutionSimulator.FillOnBar = false;
            ExecutionSimulator.FillOnBarOpen = false;
            ExecutionSimulator.FillLimitOnNext = true;
            ExecutionSimulator.PartialFills = false;
        }

        private void ConfigureBarMode()
        {
            // Data simulator settings for bar mode
            DataSimulator.SubscribeAll = false;
            DataSimulator.SubscribeBar = true;
            DataSimulator.SubscribeTrade = false;
            DataSimulator.SubscribeQuote = false;
            DataSimulator.SubscribeBid = false;
            DataSimulator.SubscribeAsk = false;

            // Execution simulator settings for bar mode
            ExecutionSimulator.FillAtLimitPrice = true;
            ExecutionSimulator.FillOnTrade = false;
            ExecutionSimulator.FillOnQuote = false;
            ExecutionSimulator.FillOnBar = true;
            ExecutionSimulator.FillOnBarOpen = false;
            ExecutionSimulator.FillLimitOnNext = true;
            ExecutionSimulator.PartialFills = false;
        }

        private void AddStrategies()
        {
            Console.WriteLine("\nAdding strategies to container...");

            foreach (var parameters in strategyParametersList.strategyParamList)
            {
                try
                {
                    if (IsInstrumentPair(parameters.trade_instrument))
                    {
                        AddPairStrategy(parameters);
                    }
                    else
                    {
                        AddSingleInstrumentStrategy(parameters);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error adding strategy {parameters.name}: {ex.Message}");
                }
            }
        }

        private void AddSingleInstrumentStrategy(StrategyParameters parameters)
        {
            Console.WriteLine($"\nAdding SINGLE instrument strategy: {parameters.name}");

            // Validate parameters
            ValidateStrategyParameters(parameters);

            // Get instrument
            Instrument instrument = InstrumentManager.Instruments[parameters.trade_instrument];

            if (instrument == null)
            {
                throw new ArgumentException($"Instrument {parameters.trade_instrument} not found in InstrumentManager");
            }

            // Create strategy manager
            IStrategyManager strategyManager = StrategyManagerFactory.CreateAndInitialize(parameters, instrument);

            // Create strategy
            MyStrategy strategy = new MyStrategy(framework, parameters.name, strategyManager);
            strategy.InitializeWithManager(strategyManager);

            // Add single instrument to strategy
            strategy.Add(instrument);

            // Set providers
            strategy.ExecutionProvider = sellSide;
            strategy.DataProvider = sellSide;

            // Configure bar factory if needed
            if (parameters.bar_size > 0 && !IsBarMode)
            {
                BarFactory.Add(instrument, BarType.Time, parameters.bar_size, BarInput.Trade);
            }

            containerStrategy.Add(strategy);

            Console.WriteLine($"  ✓ Added single instrument strategy for {parameters.trade_instrument}");
        }

        private void AddPairStrategy(StrategyParameters parameters)
        {
            Console.WriteLine($"\nAdding PAIR strategy: {parameters.name}");

            // Validate parameters
            ValidateStrategyParameters(parameters);

            // Parse the pair
            var (numerator, denominator) = ParseInstrumentPair(parameters.trade_instrument);

            // Get all instruments (constituents + synthetic)
            Instrument numInstrument = InstrumentManager.Instruments[numerator];
            Instrument denInstrument = InstrumentManager.Instruments[denominator];
            Instrument synthInstrument = InstrumentManager.Instruments[parameters.trade_instrument];

            if (numInstrument == null)
                throw new ArgumentException($"Numerator instrument {numerator} not found in InstrumentManager");
            if (denInstrument == null)
                throw new ArgumentException($"Denominator instrument {denominator} not found in InstrumentManager");
            if (synthInstrument == null)
                throw new ArgumentException($"Synthetic instrument {parameters.trade_instrument} not found in InstrumentManager");

            Instrument inst = synthInstrument;

            switch (parameters.execution_instrument)
            {
                case "num":
                    inst = numInstrument;
                    return;

                case "den":
                    inst = denInstrument;
                    return;

            }

            // Create strategy manager
            IStrategyManager strategyManager = StrategyManagerFactory.CreateAndInitialize(parameters, inst);

            // Create strategy
            MyStrategy strategy = new MyStrategy(framework, parameters.name, strategyManager);
            strategy.InitializeWithManager(strategyManager);

            // Add ALL instruments to strategy (constituents will provide data, synthetic will be traded)
            InstrumentList instruments = new InstrumentList
            {
                numInstrument,
                denInstrument,
                synthInstrument
            };
            strategy.Add(instruments);

            // Set providers
            strategy.ExecutionProvider = sellSide;
            strategy.DataProvider = sellSide;

            // Configure bar factory for all instruments if needed
            if (parameters.bar_size > 0 && !IsBarMode)
            {
                BarFactory.Add(numInstrument, BarType.Time, parameters.bar_size, BarInput.Trade);
                BarFactory.Add(denInstrument, BarType.Time, parameters.bar_size, BarInput.Trade);
                BarFactory.Add(synthInstrument, BarType.Time, parameters.bar_size, BarInput.Trade);
            }

            containerStrategy.Add(strategy);

            Console.WriteLine($"  ✓ Added pair strategy: {parameters.trade_instrument} = {numerator} / {denominator}");
        }

        #region Instrument Pair Utilities

        /// <summary>
        /// Check if instrument string represents a pair (contains dot)
        /// </summary>
        private bool IsInstrumentPair(string instrumentName)
        {
            return !string.IsNullOrEmpty(instrumentName) && instrumentName.Contains(".");
        }

        /// <summary>
        /// Parse instrument pair from "Inst1.Inst2" format
        /// </summary>
        /// <param name="pairName">Instrument pair in format "Inst1.Inst2"</param>
        /// <returns>Tuple of (numerator, denominator)</returns>
        private (string numerator, string denominator) ParseInstrumentPair(string pairName)
        {
            if (string.IsNullOrEmpty(pairName) || !pairName.Contains("."))
            {
                throw new ArgumentException($"Invalid pair format: {pairName}. Expected format: 'Inst1.Inst2'");
            }

            string[] parts = pairName.Split('.');
            if (parts.Length != 2)
            {
                throw new ArgumentException($"Invalid pair format: {pairName}. Expected exactly one dot separator");
            }

            string numerator = parts[0].Trim();
            string denominator = parts[1].Trim();

            if (string.IsNullOrEmpty(numerator) || string.IsNullOrEmpty(denominator))
            {
                throw new ArgumentException($"Invalid pair format: {pairName}. Both parts must be non-empty");
            }

            return (numerator, denominator);
        }

        #endregion

        private void ValidateStrategyParameters(StrategyParameters parameters)
        {
            if (string.IsNullOrWhiteSpace(parameters.name))
                throw new ArgumentException("Strategy name is required");

            if (string.IsNullOrWhiteSpace(parameters.trade_instrument))
                throw new ArgumentException($"Trade instrument not specified for strategy {parameters.name}");

            if (parameters.inst_tick_size <= 0)
                throw new ArgumentException($"Invalid tick size for strategy {parameters.name}");

            if (parameters.inst_factor <= 0)
                throw new ArgumentException($"Invalid instrument factor for strategy {parameters.name}");
        }
    }
}