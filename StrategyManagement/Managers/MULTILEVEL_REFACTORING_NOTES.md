# MomentumMultiLevelStrategyManager Refactoring Notes

## Overview

The `MomentumMultiLevelStrategyManager` has been completely refactored to make it debuggable, testable, and transparent. The refactored version is saved as `MomentumMultiLevelStrategyManager_Refactored.cs`.

## Key Changes

### 1. **All Decision Logic Consolidated in OnBar**

The entire strategy logic now flows through a single `OnBar` method with clear, sequential steps:

```
STEP 1: Update Price Window and Signal
STEP 2: Calculate Statistics
STEP 3: Update MAD (Mean Absolute Deviation)
STEP 4: Display Current State
STEP 5: Check Force Exit Conditions
STEP 6: Check Exit Conditions for Active Levels
STEP 7: Check Entry Conditions for Each Level
STEP 8: Log Level States
```

Each step is clearly labeled and logged, making it easy to follow the execution flow.

### 2. **All Try-Catch Blocks Removed**

- **NO** exception handling - all errors will surface immediately
- Makes debugging easier as you can see exactly where and why failures occur
- Stack traces will point directly to the problematic line
- The original had try-catch in `ExecuteEntryOrder`, `ExecuteExitOrder`, and `ForceExitAllPositions` - all removed

### 3. **Comprehensive Logging System**

Four separate log files are created in `C:\tmp\Template\debug_logs\`:

#### a. **Calculation Log** (`multilevel_calculation_log_TIMESTAMP.txt`)
- Every bar logged with full details
- All intermediate calculations shown step-by-step
- Signal calculations, MAD updates, statistics
- Entry/exit decision logic with all conditions evaluated
- Level creation and management details

Example output:
```
========== BAR 1234 - 2024-01-15 14:30:00 ==========
Signal Bar: O=4523.5000 H=4525.2500 L=4522.7500 C=4524.0000

--- STEP 1: Update Price Window and Signal ---
Price added to window: 4524.0000
Price window: Count=10, Required=10

--- STEP 2: Calculate Statistics ---
Moving Average: 4520.234567
Signal: (4524.0000 / 4520.234567) - 1.0 = 0.000833

--- STEP 3: Update MAD (Mean Absolute Deviation) ---
MAD updated: 0.001234 -> 0.001567

--- STEP 4: Current State ---
Current Position: 6
Average Entry Price: 4518.2500
Active Levels: 2 / 3
Active Level Details:
  Level 1 (Index 0): Buy, Pos=3, Entry=4516.5000
  Level 2 (Index 1): Buy, Pos=3, Entry=4520.0000

--- STEP 7: Check Entry Conditions ---
Entry Pre-checks:
  Within Trading Hours: True
  Position Limit OK: True (2 < 3)
  No Live Order: True

Checking each entry level:
  Entry Level 0: Threshold=0.5
    Level already active (ID 1) - skipping
  Entry Level 1: Threshold=0.75
    Level already active (ID 2) - skipping
  Entry Level 2: Threshold=1.0
    Long Threshold: -1.0 * 0.001567 = -0.001567
    Short Threshold: 1.0 * 0.001567 = 0.001567
    Signal: 0.000833
    Long Check: signal (0.000833) < longThreshold (-0.001567) = False
    Short Check: signal (0.000833) > shortThreshold (0.001567) = False
    No entry triggered
```

#### b. **Order Log** (`multilevel_order_log_TIMESTAMP.csv`)
- CSV format for easy analysis in Excel/Python
- Every order event (entry/exit/force_exit) logged
- Columns: BarNum, Timestamp, Event, LevelId, LevelIndex, Side, Price, Quantity, Signal, MAD, EntryThreshold, ExitThreshold, Position, ActiveLevels

Example:
```csv
BarNum,Timestamp,Event,LevelId,LevelIndex,Side,Price,Quantity,Signal,MAD,EntryThreshold,ExitThreshold,Position,ActiveLevels
1234,2024-01-15 14:30:00,ENTRY,1,0,Buy,4516.5000,3,0.000833,0.001567,0.5|0.75|1.0,0.5|0.25,0,0
1456,2024-01-15 14:45:00,ENTRY,2,1,Buy,4520.0000,3,0.001234,0.001567,0.5|0.75|1.0,0.5|0.25,3,1
1678,2024-01-15 15:30:00,EXIT,1,0,Sell,4522.5000,2,0.000456,0.001567,0.5|0.75|1.0,0.5|0.25,6,2
```

#### c. **Trade Log** (`multilevel_trade_log_TIMESTAMP.csv`)
- CSV format with complete trade details per level
- Columns: LevelId, LevelIndex, EntryTime, ExitTime, Side, EntryPrice, ExitPrice, Quantity, PnL, Signal, EntryLevel, ExitLevel, HoldBars

Example:
```csv
LevelId,LevelIndex,EntryTime,ExitTime,Side,EntryPrice,ExitPrice,Quantity,PnL,Signal,EntryLevel,ExitLevel,HoldBars
1,0,2024-01-15 14:30:00,2024-01-15 15:45:00,Buy,4516.5000,4522.5000,3,18.00,0.000833,0.5,0.5|0.25,75
2,1,2024-01-15 14:45:00,2024-01-15 16:00:00,Buy,4520.0000,4525.0000,3,15.00,0.001234,0.75,0.5|0.25,75
```

#### d. **Level Log** (`multilevel_level_log_TIMESTAMP.csv`)
- CSV format tracking level state at each bar
- Shows unrealized PnL for active levels
- Columns: BarNum, Timestamp, LevelId, LevelIndex, Status, Side, EntryPrice, CurrentPosition, RemainingQty, Signal, PnL

Example:
```csv
BarNum,Timestamp,LevelId,LevelIndex,Status,Side,EntryPrice,CurrentPosition,RemainingQty,Signal,PnL
1234,2024-01-15 14:30:00,1,0,ACTIVE,Buy,4516.5000,3,3,0.000833,22.50
1234,2024-01-15 14:30:00,2,1,ACTIVE,Buy,4520.0000,3,3,0.000833,12.00
```

### 4. **Simplified Code Structure**

#### Step-by-Step Calculations
Every calculation is broken down and logged:

```csharp
// Calculate thresholds
double longThreshold = -entryLevel * mad;
double shortThreshold = entryLevel * mad;

WriteLogLine($"    Long Threshold: -{entryLevel} * {mad:F6} = {longThreshold:F6}");
WriteLogLine($"    Short Threshold: {entryLevel} * {mad:F6} = {shortThreshold:F6}");
WriteLogLine($"    Signal: {signal:F6}");

// Check long entry
bool longTriggered = signal < longThreshold;
WriteLogLine($"    Long Check: signal ({signal:F6}) < longThreshold ({longThreshold:F6}) = {longTriggered}");
```

#### Clear Entry/Exit Flow
Each level is checked independently with full logging:

```csharp
for (int i = 0; i < entryLevels.Count; i++)
{
    double entryLevel = entryLevels[i];
    var level = levelManager.Levels[i];

    WriteLogLine($"  Entry Level {i}: Threshold={entryLevel}");

    if (level != null)
    {
        WriteLogLine($"    Level already active (ID {level.Id}) - skipping");
        continue;
    }

    // Calculate and check thresholds...
}
```

### 5. **Multi-Level Strategy Details**

The refactored code makes the multi-level logic transparent:

#### Entry Levels
- Each entry level has a threshold (e.g., 0.5, 0.75, 1.0)
- Levels are triggered when signal crosses threshold * MAD
- Long entry: signal < -threshold * MAD
- Short entry: signal > threshold * MAD
- Maximum concurrent levels enforced

#### Exit Levels
- Each level can have multiple exit points (e.g., 0.5, 0.25)
- Partial exits at each level
- Exits triggered when signal crosses back
- Full tracking of remaining quantity per level

#### Position Tracking
- Global position tracking (currentPosition, averageEntryPrice)
- Per-level position tracking (via LevelManager)
- Both logged at each step for verification

### 6. **Execution Flow**

The OnBar method follows a strict sequential flow:

1. **Update price window** - Add new price, remove old if needed
2. **Calculate statistics** - Moving average from price window
3. **Update MAD** - Daily reset and intraday maximum
4. **Display current state** - Position, levels, prices
5. **Check force exits** - Time-based or risk-based
6. **Check level exits** - For each active level, check exit conditions
7. **Check level entries** - For each inactive level, check entry conditions
8. **Log level states** - Record current state of all levels

## How to Use

### 1. Replace the Original File

```bash
# Backup original
cp MomentumMultiLevelStrategyManager.cs MomentumMultiLevelStrategyManager_Original.cs

# Use refactored version
cp MomentumMultiLevelStrategyManager_Refactored.cs MomentumMultiLevelStrategyManager.cs
```

### 2. Set Log Directory

The default log directory is `C:\tmp\Template\debug_logs\`. Change this in the code if needed:

```csharp
private string logDirectory = "C:\\tmp\\Template\\debug_logs\\";
```

### 3. Run Your Backtest

The strategy will create four log files with timestamps:
- `multilevel_calculation_log_20240115_143000.txt`
- `multilevel_order_log_20240115_143000.csv`
- `multilevel_trade_log_20240115_143000.csv`
- `multilevel_level_log_20240115_143000.csv`

### 4. Debug with Logs

#### Step Through Logic
Open `multilevel_calculation_log_*.txt` and search for specific bars or conditions:
- Search for "BAR 1234" to see a specific bar
- Search for "ENTRY TRIGGERED" to find entry points
- Search for "Force exit" to find force exit events
- Search for "Exit Quantity" to see exit decisions

#### Analyze Orders
Open `multilevel_order_log_*.csv` in Excel:
- Filter by Event (ENTRY, EXIT, FORCE_EXIT)
- Group by LevelId to see level lifecycle
- Check Signal vs MAD at each order

#### Analyze Trades
Open `multilevel_trade_log_*.csv` in Excel or Python:
- Calculate win rate per level
- Analyze hold time distribution
- Compare PnL by entry level

#### Track Level States
Open `multilevel_level_log_*.csv` in Excel:
- Chart unrealized PnL over time
- See how many levels are active at each bar
- Track position size changes

### 5. Debug in Visual Studio

With all try-catch removed and clear variable names:
1. Set breakpoints in OnBar
2. Step through each section
3. Inspect variables at each step
4. Watch window shows all intermediate calculations

## Testing Checklist

- [ ] Verify signal calculations match expected values
- [ ] Check that MAD updates correctly daily
- [ ] Confirm entry levels trigger at correct thresholds
- [ ] Validate exit levels trigger correctly
- [ ] Ensure position limits are enforced
- [ ] Check that force exit works at end of day
- [ ] Verify position tracking matches level manager
- [ ] Confirm PnL calculations are correct
- [ ] Validate multiple concurrent levels work correctly
- [ ] Check that partial exits work as expected

## Known Differences from Original

1. **No try-catch blocks** - Original had error handling in ExecuteEntryOrder, ExecuteExitOrder, ForceExitAllPositions
2. **Legacy methods return false** - `ShouldEnterLongPosition`, etc. are not used, all logic in OnBar
3. **More verbose logging** - Significantly more output for debugging
4. **Explicit variable naming** - More intermediate variables for clarity
5. **Removed console logging** - Original had Console.WriteLine, refactored uses file logging only

## Performance Notes

The refactored version will be **slower** due to extensive logging:
- File I/O on every bar
- String formatting for logs
- No performance optimizations

This is intentional - the goal is debuggability, not speed. Once the logic is validated, you can:
- Remove or reduce logging
- Add try-catch back for production
- Optimize calculations

## Multi-Level Strategy Specific Questions

The comprehensive logging allows you to answer:

### 1. **Why did level X enter here?**
- Check calculation log for signal and threshold values
- See exact MAD value used
- Verify position limit wasn't exceeded

### 2. **Why didn't level X exit here?**
- Check if signal crossed exit threshold
- See remaining quantity for that exit level
- Verify exit wasn't already executed

### 3. **How are positions distributed across levels?**
- Check level log to see position per level
- Compare with global position tracking
- Verify they sum correctly

### 4. **What's the PnL per level?**
- Trade log has every level's complete trade history
- Level log shows unrealized PnL at each bar
- Can calculate metrics by level index

### 5. **Are force exits working correctly?**
- Check calculation log for force exit triggers
- See time-based and risk-based conditions
- Verify all levels exited

### 6. **Is the level manager state correct?**
- Compare logged level states with expected
- Check active level count vs max
- Verify level IDs and indices match

## Example Debugging Scenarios

### Scenario 1: "Level didn't enter when expected"

**Step 1:** Find the bar in calculation log
```
Search for the timestamp in multilevel_calculation_log_*.txt
```

**Step 2:** Check signal and MAD
```
--- STEP 2: Calculate Statistics ---
Signal: 0.000833
--- STEP 3: Update MAD ---
MAD: 0.001567
```

**Step 3:** Check entry conditions
```
--- STEP 7: Check Entry Conditions ---
  Entry Level 2: Threshold=1.0
    Long Threshold: -0.001567
    Short Threshold: 0.001567
    Signal: 0.000833
    Long Check: False
    Short Check: False
```

**Step 4:** Identify problem
```
Signal (0.000833) didn't cross threshold (±0.001567)
```

### Scenario 2: "Position tracking mismatch"

**Step 1:** Check order log for all orders
```
Open multilevel_order_log_*.csv
Filter by LevelId
Sum quantities by Side
```

**Step 2:** Check level log for level positions
```
Open multilevel_level_log_*.csv
Check CurrentPosition column for each level
Sum across all levels
```

**Step 3:** Compare with calculation log
```
Search for "Position updated" in calculation log
Check currentPosition value
```

**Step 4:** Identify discrepancy
```
If they don't match, trace through UpdatePositionTracking calls
```

### Scenario 3: "Force exit not triggering"

**Step 1:** Check force exit conditions
```
Search for "Check Force Exit Conditions" in calculation log
```

**Step 2:** See evaluated conditions
```
  Time-based exit: 15:30:00 >= 15:45:00 = False
  Risk-based exit: 2 > 3 = False
  Should Force Exit: False
```

**Step 3:** Verify parameters
```
Check Parameters.exit_time value
Check levelManager.MaxConcurrentLevels
```

## Next Steps

1. **Validate Logic** - Run backtest and verify calculations match expectations
2. **Check Levels** - Ensure levels trigger at correct thresholds
3. **Analyze Trades** - Use trade log to understand performance per level
4. **Verify Exits** - Check partial exits work correctly
5. **Optimize** - Once validated, add back optimizations if needed
6. **Production** - Add error handling back for production use

## Advanced Analysis Ideas

With the comprehensive logs, you can:

1. **Level Performance Analysis**
   - Compare win rate by level index
   - See if certain levels are more profitable
   - Analyze optimal number of levels

2. **Threshold Optimization**
   - Test different entry/exit thresholds
   - Analyze threshold vs win rate
   - Find optimal spacing between levels

3. **Hold Time Analysis**
   - Calculate average hold time per level
   - See if longer holds are more profitable
   - Identify optimal exit timing

4. **Risk Analysis**
   - Track maximum concurrent levels
   - Analyze drawdown when multiple levels active
   - Calculate risk-adjusted returns per level

5. **Signal Analysis**
   - Chart signal vs MAD over time
   - Identify signal patterns before profitable trades
   - Analyze MAD stability and resets
