using System;
using System.Collections.Generic;
using System.Linq;
using Parameters;
using SmartQuant;
using SmartQuant.Strategy_;
using StrategyManagement;

namespace OpenQuant
{
    /// <summary>
    /// Refactored container strategy that manages multiple strategy instances
    /// </summary>
    public class ContainerStrategy : Strategy_
    {
        private readonly Dictionary<string, StrategyInfo> strategyInfoDict;
        private System.Timers.Timer updateTimer;

        public IReadOnlyDictionary<string, StrategyInfo> StrategyInfos => strategyInfoDict;

        public ContainerStrategy(Framework framework, string name) : base(framework, name)
        {
            strategyInfoDict = new Dictionary<string, StrategyInfo>();
        }

        protected override void OnStrategyStart()
        {
            Console.WriteLine($"Container strategy started with {Strategies.Count - 1} strategies");

            // Initialize strategy info for each child strategy (skip first which is SellSide)
            for (int i = 1; i < Strategies.Count; i++)
            {
                if (Strategies[i] is MyStrategy strategy)
                {
                    var info = new StrategyInfo
                    {
                        Name = strategy.Name,
                        DisplayParameters = strategy.DisplayParameters,
                        StrategyManager = strategy.StrategyParameters.strategy_type ?? "Unknown",
                        Instrument = strategy.StrategyParameters.trade_instrument,
                        Status = "Running"
                    };

                    strategyInfoDict[strategy.Name] = info;
                }
            }

            // Start update timer for monitoring
            updateTimer = new System.Timers.Timer(1000);
            updateTimer.Elapsed += UpdateTimer_Elapsed;
            updateTimer.Start();
        }

        protected override void OnStrategyStop()
        {
            updateTimer?.Stop();
            updateTimer?.Dispose();

            // Generate summary report
            GenerateSummaryReport();

            Console.WriteLine("Container strategy stopped");
        }

        protected override void OnBar(Bar bar)
        {
            // Container doesn't process bars directly
        }

        private void UpdateTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            // Update strategy info
            foreach (var kvp in strategyInfoDict)
            {
                var info = kvp.Value;
                if (info.DisplayParameters != null)
                {
                    info.LastUpdate = DateTime.Now;
                    // Additional updates can be added here
                }
            }
        }

        private void GenerateSummaryReport()
        {
            Console.WriteLine("\n=== Strategy Performance Summary ===");
            Console.WriteLine($"{"Strategy",-20} {"Type",-15} {"Instrument",-10} {"Position",-10} {"PnL",-15}");
            Console.WriteLine(new string('-', 70));

            foreach (var strategy in Strategies.Skip(1).OfType<MyStrategy>())
            {
                var positionManager = strategy.PositionManager;
                var totalPnL = positionManager.RealizedPnL + positionManager.UnrealizedPnL;

                Console.WriteLine($"{strategy.Name,-20} " +
                                $"{strategy.StrategyParameters.strategy_type ?? "Unknown",-15} " +
                                $"{strategy.StrategyParameters.trade_instrument,-10} " +
                                $"{positionManager.CurrentPosition,-10} " +
                                $"{totalPnL,-15:C}");
            }

            Console.WriteLine(new string('-', 70));
        }

        /// <summary>
        /// Get strategy by name
        /// </summary>
        public MyStrategy GetStrategy(string name)
        {
            return Strategies.OfType<MyStrategy>().FirstOrDefault(s => s.Name == name);
        }

        /// <summary>
        /// Get all strategies of a specific type
        /// </summary>
        public IEnumerable<MyStrategy> GetStrategiesByType(string strategyType)
        {
            return Strategies.OfType<MyStrategy>()
                .Where(s => string.Equals(s.StrategyParameters.strategy_type, strategyType, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Information about a strategy for monitoring
    /// </summary>
    public class StrategyInfo
    {
        public string Name { get; set; }
        public string StrategyManager { get; set; }
        public string Instrument { get; set; }
        public string Status { get; set; }
        public DisplayParameters DisplayParameters { get; set; }
        public DateTime LastUpdate { get; set; }
    }
}


//using Parameters;
//using System.Collections.Generic;
//using System.Timers;
//using SmartQuant;
//using SmartQuant.Strategy_;

//namespace OpenQuant
//{
//    public class ContainerStrategy : Strategy_
//    {
//        //public MainForm theForm;
//        //private bool isForm = false;
//        //private System.Timers.Timer timer;
//        public Dictionary<string, DisplayParameters> StrategyExecutorDict = new Dictionary<string, DisplayParameters>();

//        public ContainerStrategy(Framework framework, string name)
//            : base(framework, name)
//        {
//            //statCont = this;
//        }

//        protected override void OnStrategyStart()
//        {
//            for (int i = 1; i < Strategies.Count; i++)
//            {
//                MyStrategy strategy = (MyStrategy)Strategies[i];

//                if (strategy != null)
//                {
//                    StrategyExecutorDict.Add(strategy.Name, strategy.DisplayParameters);
//                }
//            }

//            //if (isForm)
//            //{
//            //    theForm = new MainForm(this, StrategyExecutorDict);

//            //    System.Threading.ThreadPool.QueueUserWorkItem(delegate (object state)
//            //    {
//            //        Application.Run(theForm);
//            //    });
//            //}

//            //timer = new System.Timers.Timer(1000);
//            //timer.Elapsed += Timer_Elapsed;
//            //timer.Start();
//        }

//        private void Timer_Elapsed(object sender, ElapsedEventArgs e)
//        {
//            //if (isForm)
//            //    //theForm.treeListView1.Refresh();
//            //    theForm.treeListView1.RefreshObjects(StrategyExecutorDict.ToList());
//        }

//        protected override void OnBar(Bar bar)
//        {
//        }

//    }
//}

