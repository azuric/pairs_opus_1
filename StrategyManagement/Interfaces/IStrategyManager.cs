using Parameters;
using SmartQuant;
using StrategyManagement;

public interface IStrategyManager
{
    // Core properties
    string Name { get; }
    StrategyParameters Parameters { get; }
    IPositionManager PositionManager { get; }
    IDualPositionManager DualPositionManager { get; }
    ITradeManager TradeManager { get; }

    // Initialization
    void Initialize(StrategyParameters parameters);
    void SetTradeManager(ITradeManager tradeManager);
    void SetTradingMode(bool isPairMode, int tradeInstrumentId);

    // Main processing - now takes array of bars
    void ProcessBar(Bar[] bars, double accountValue);

    // Market data events
    void OnBar(Bar[] bars);
    void OnTrade(Trade trade);
    void OnAsk(Ask ask);
    void OnBid(Bid bid);

    // Order/Fill events
    void OnFill(Fill fill);
    void OnOrderEvent(Order order);

    // Lifecycle
    void OnStrategyStart();
    void OnStrategyStop();

    // Decision methods
    bool ShouldEnterLongPosition(Bar[] bars);
    bool ShouldEnterShortPosition(Bar[] bars);
    bool ShouldExitLongPosition(Bar[] bars);
    bool ShouldExitShortPosition(Bar[] bars);

    // Position and pricing
    int CalculatePositionSize(Bar[] bars, double accountValue);
    double GetEntryPrice(Bar[] bars, OrderSide side);
    double GetExitPrice(Bar[] bars, OrderSide side);
}
