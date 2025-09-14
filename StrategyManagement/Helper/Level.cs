using System.Collections.Generic;
using System;
using System.Linq;
using SmartQuant;

namespace StrategyManagement
{
    public class Level
    {
        public string LevelID { get; set; }
        
        public double EntryPrice { get; set; }
        public double ExitPrice { get; set; }

        public LevelOrder ActualEntryOrder { get; set; }
        public LevelOrder ActualExitOrder { get; set; }

        public LevelOrder TheoEntryOrder { get; set; }
        public LevelOrder TheoExitOrder { get; set; }

        public int NetTheoPosition { get; set; }
        public double NetPosition { get; set; }
        public OrderSide Side { get; set; }

        public Level(string levelID, OrderSide side, int size, double entryPrice, double exitPrice)
        {
            NetPosition = 0;
            LevelID = levelID;
            EntryPrice = entryPrice;
            ExitPrice = exitPrice;
            Side = side;
            TheoEntryOrder = new LevelOrder();
            TheoExitOrder = new LevelOrder();
            ActualEntryOrder = new LevelOrder();
            ActualExitOrder = new LevelOrder();
        }
    }

    public class LevelOrder
    {
        public int Position { get; set; }
        public int CurrentPosition { get; set; }
        public int[] LiveTransit { get; set; }
        public double Price { get; set; }
        public int Id { get; set; }
        public bool IsEntry { get; set; }
        public OrderSide Side { get; set; }

        public LevelOrder()
        {
            LiveTransit = new int[2];
            LiveTransit[0] = 0;
            LiveTransit[1] = 0;
        }

        public LevelOrder(int id, int position, double price, OrderSide side, bool isEntry)
        {
            Id = id; 
            Position = position; 
            Price = price;
            IsEntry = isEntry;
            Side = side;
            LiveTransit = new int[2];
            LiveTransit[0] = 0;
            LiveTransit[1] = 1;
            CurrentPosition = 0;
        }
    }
}
