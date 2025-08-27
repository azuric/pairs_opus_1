using System;
using SmartQuant.Strategy_;
using SmartQuant;
using Parameters;
using System.Collections.Generic;
using OpenQuant;
using Newtonsoft.Json;
using System.IO;


namespace OpenQuant
{
    public partial class Live : Scenario_
    {
        //Scenarios parameters;
        public bool isBar = false;
        private const string TRADE_ROOT = @"C:\tmp\Template";

        public string value;

        
        public ContainerStrategy containerStrategy;

        InstrumentList instruments = new InstrumentList();

        IDataProvider dataProvider;
        IExecutionProvider executionProvider;

        public StrategyParameterList StrategyParametersList { get; set; }

        public Live(Framework framework) : base(framework)
        {

        }

        public override void Run()
        {
            //EventDispatcher dispatcher = framework.EventManager.Dispatcher;

            //dispatcher.ProviderConnected += Dispatcher_ProviderConnected;
            //dispatcher.ProviderDisconnected += Dispatcher_ProviderDisconnected;
            //dispatcher.ProviderStatusChanged += Dispatcher_ProviderStatusChanged;
            //dispatcher.ProviderError += Dispatcher_ProviderError;
            //dispatcher.InstrumentAdded += Dispatcher_InstrumentAdded;

            //StatisticsManager.Add(new TrueSharpe());

            containerStrategy = new ContainerStrategy(framework, "Container");

            string[] args = Environment.GetCommandLineArgs();

            string argum;
            if (args.Length > 1)
            {
                argum = args[1];
            }
            else
            {
                argum = "abcd_temp_4";
            }
            string[] splitString = argum.Split(',');

            StrategyParametersList = JsonConvert.DeserializeObject<StrategyParameterList>(File.ReadAllText(TRADE_ROOT + @"\" + splitString[0] + ".json"));

            List<StrategyParameters> strategyParameterList = StrategyParametersList.strategyParamList;

            if (!isBar)
            {
                DataSimulator.SubscribeAll = false;
                DataSimulator.SubscribeBar = false;
                DataSimulator.SubscribeTrade = true;
                DataSimulator.SubscribeQuote = true;
                DataSimulator.SubscribeBid = true;
                DataSimulator.SubscribeAsk = true;

                ExecutionSimulator.FillAtLimitPrice = true;
                ExecutionSimulator.FillOnTrade = true;
                ExecutionSimulator.FillOnQuote = true;
                ExecutionSimulator.FillOnBar = false;
                ExecutionSimulator.FillOnBarOpen = false;
                ExecutionSimulator.FillLimitOnNext = true;
                ExecutionSimulator.PartialFills = false;

                BarFactory.Clear();

            }
            else
            {
                DataSimulator.SubscribeAll = false;
                DataSimulator.SubscribeBar = true;
                DataSimulator.SubscribeTrade = false;
                DataSimulator.SubscribeQuote = false;
                DataSimulator.SubscribeBid = false;
                DataSimulator.SubscribeAsk = false;

                ExecutionSimulator.FillAtLimitPrice = true;
                ExecutionSimulator.FillOnTrade = false;
                ExecutionSimulator.FillOnQuote = false;
                ExecutionSimulator.FillOnBar = true;
                ExecutionSimulator.FillOnBarOpen = false;
                ExecutionSimulator.FillLimitOnNext = true;
                ExecutionSimulator.PartialFills = false;

                BarFactory.Clear();
            }

            //for (int i = 0; i < strategyParameterList.Count; i++)
            //{
            //    AddStrategy(strategyParameterList[i]);
            //}

            containerStrategy.DataProvider = ProviderManager.GetDataProvider("ExecutionSimulator");
            containerStrategy.ExecutionProvider = ProviderManager.GetExecutionProvider("ExecutionSimulator");

            EventManager.Filter = null;

            Start(containerStrategy, StrategyMode.Live);
        }

        //public void AddStrategy(StrategyParameters param, )
        //{
        //    List<Instrument> instrumentsList = new List<Instrument>();

        //    Instrument newInstrument = InstrumentManager.Instruments[param.trade_instrument];

        //    instrumentsList.Add(newInstrument);

        //    MyStrategy strategy = new MyStrategy(framework, param.name, param);

        //    foreach (Instrument instr in instrumentsList)
        //    {
        //        if (instr.Type != InstrumentType.Synthetic)
        //        {
        //            strategy.Add(instr);
        //            //BarFactory.Add(instr, BarType.Time, 60, BarInput.Trade);
        //        }
        //    }

        //    strategy.ExecutionProvider = containerStrategy.ExecutionProvider;
        //    strategy.DataProvider = containerStrategy.DataProvider;

        //    containerStrategy.Add(strategy);
        //}

    }
}
