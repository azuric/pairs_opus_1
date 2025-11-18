# MomStrategyManagerFilter Refactoring Notes

## Overview

The `MomStrategyManagerFilter` has been completely refactored to make it debuggable, testable, and transparent. The refactored version is saved as `MomStrategyManagerFilter_Refactored.cs`.

## Key Changes

### 1. **All Decision Logic Consolidated in OnBar**

The entire strategy logic now flows through a single `OnBar` method with clear, sequential steps:

```
STEP 1: Update Signal and Statistics
STEP 2: Get Features from AlphaManager
STEP 3: Cancel Pending Orders
STEP 4: Get Current Position
STEP 5: Check Exit Conditions
STEP 6: Check Entry Conditions
STEP 7: Update Metrics
```

Each step is clearly labeled and logged, making it easy to follow the execution flow.

### 2. **All Try-Catch Blocks Removed**

- **NO** exception handling - all errors will surface immediately
- Makes debugging easier as you can see exactly where and why failures occur
- Stack traces will point directly to the problematic line

### 3. **Comprehensive Logging System**

Three separate log files are created in `C:\tmp\Template\debug_logs\`:

#### a. **Calculation Log** (`calculation_log_TIMESTAMP.txt`)
- Every bar is logged with full details
- All intermediate calculations shown step-by-step
- Signal calculations, MAD updates, statistics
- Feature extraction from AlphaManager
- Entry/exit decision logic with all conditions evaluated
- Filter score calculations with per-feature breakdown

Example output:
```
========== BAR 1234 - 2024-01-15 14:30:00 ==========
Signal Bar: O=4523.50 H=4525.25 L=4522.75 C=4524.00

--- STEP 1: Update Signal and Statistics ---
Signal MA: 4520.123456 -> 4520.234567 (alpha=0.008299, price=4524.00)
Signal: 10000 * (4524.00 / 4520.234567 - 1.0) = 8.332145
MAD updated: 12.456789 -> 15.678901

--- STEP 2: Get Features from AlphaManager ---
AlphaManager Data: Length=66
Extracted 24 Features:
  [0] diff_ema_120: 0.00123456789012345 (from Data[0])
  [1] diff_ema_240: 0.00234567890123456 (from Data[1])
  ...

--- STEP 6: Check Entry Conditions ---
--- Checking LONG Entry ---
Long signal check: signal (8.332145) > mad*entryThreshold (7.839451) = True
NEW LONG SIGNAL DETECTED - Checking filter
Filter Score: 0.456789
Filter Threshold: 0.500000
Filter Passed: False
```

#### b. **Order Log** (`order_log_TIMESTAMP.csv`)
- CSV format for easy analysis in Excel/Python
- Every order event (entry/exit) logged
- Columns: BarNum, Timestamp, Event, Side, Price, Quantity, Signal, MAD, Threshold, FilterScore, FilterPass, Position

Example:
```csv
BarNum,Timestamp,Event,Side,Price,Quantity,Signal,MAD,Threshold,FilterScore,FilterPass,Position
1234,2024-01-15 14:30:00,ENTRY,Buy,4524.00,1,8.332145,15.678901,0.500000,0.523456,True,0
1456,2024-01-15 16:45:00,SignalExit,Sell,4532.50,1,-2.123456,15.678901,0.500000,0.523456,True,1
```

#### c. **Trade Log** (`trade_log_TIMESTAMP.csv`)
- CSV format with complete trade details
- Columns include:
  - Trade metadata: TradeNum, EntryTime, ExitTime, Side, EntryPrice, ExitPrice, PnL
  - Signal: Signal value, FilterScore
  - All 24 feature values at entry time
  - All 24 bin assignments
  - All 24 weight contributions
  
This allows you to analyze:
- Which features contributed to the filter score
- How features were binned
- What the market conditions were at entry

### 4. **Simplified Code Structure**

#### Variable Naming
- Clear, descriptive variable names
- Intermediate calculations stored in named variables for inspection

#### Step-by-Step Calculations
Every calculation is broken down:

```csharp
// OLD (hard to debug):
signal = 10000 * ((signalBar.Close / signal_ma) - 1.0);

// NEW (easy to inspect):
double old_signal_ma = signal_ma;
signal_ma = EMA(alpha, signalBar.Close, signal_ma);
WriteLogLine($"Signal MA: {old_signal_ma:F6} -> {signal_ma:F6} (alpha={alpha:F6}, price={signalBar.Close:F2})");

signal = 10000 * ((signalBar.Close / signal_ma) - 1.0);
WriteLogLine($"Signal: 10000 * ({signalBar.Close:F2} / {signal_ma:F6} - 1.0) = {signal:F6}");
```

#### Filter Calculation Details
When a signal triggers, the filter calculation is logged in full detail:

```
Filter Calculation Details:
  [0] diff_ema_120:
      Value: 0.00123456789012345
      Bins: [-0.005000, -0.001000, 0.001000, 0.003000, 0.010000]
      Bin: 2
      Weight[8]: 0.045678
      Contribution: 0.045678
  [1] diff_ema_240:
      Value: 0.00234567890123456
      Bins: [-0.008000, -0.002000, 0.002000, 0.005000, 0.015000]
      Bin: 2
      Weight[9]: 0.023456
      Contribution: 0.023456
  ...
  Total Filter Score: 0.523456
```

### 5. **Execution Flow**

The OnBar method follows a strict sequential flow:

1. **Update signal and statistics** - Calculate signal, MAD, moving averages
2. **Get features** - Extract 24 features from AlphaManager
3. **Cancel pending orders** - Clean slate for new decisions
4. **Get current position** - Determine if we're long, short, or flat
5. **Check exits** - If in position, check if we should exit
   - Exit all positions check
   - Signal-based exit check
6. **Check entries** - If flat, check if we should enter
   - Trading hours and position limits
   - Long entry conditions (signal + filter)
   - Short entry conditions (signal + filter)
7. **Update metrics** - Record performance metrics

### 6. **Signal State Tracking**

The refactored code maintains signal state to prevent multiple entries per signal:

```csharp
// When signal breaches threshold for the first time
if (longSignalTriggered && !longSignalActive)
{
    longSignalActive = true;
    // Check filter ONCE
    longSignalFilterPassed = PassesFilter();
}

// If signal drops below threshold, reset for next signal
if (!longSignalTriggered && longSignalActive)
{
    longSignalActive = false;
    longSignalFilterPassed = false;
}

// Only enter if signal is active AND filter passed
return longSignalActive && longSignalFilterPassed;
```

This ensures:
- Filter is checked only once when signal first triggers
- No re-entry while signal stays above threshold
- Clean reset when signal drops below threshold

## How to Use

### 1. Replace the Original File

```bash
# Backup original
cp MomStrategyManagerFilter.cs MomStrategyManagerFilter_Original.cs

# Use refactored version
cp MomStrategyManagerFilter_Refactored.cs MomStrategyManagerFilter.cs
```

### 2. Set Log Directory

The default log directory is `C:\tmp\Template\debug_logs\`. Change this in the code if needed:

```csharp
private string logDirectory = "C:\\tmp\\Template\\debug_logs\\";
```

### 3. Run Your Backtest

The strategy will create three log files with timestamps:
- `calculation_log_20240115_143000.txt`
- `order_log_20240115_143000.csv`
- `trade_log_20240115_143000.csv`

### 4. Debug with Logs

#### Step Through Logic
Open `calculation_log_*.txt` and search for specific bars or conditions:
- Search for "BAR 1234" to see a specific bar
- Search for "ENTERING LONG" to find entry points
- Search for "Filter Calculation Details" to see filter breakdowns

#### Analyze Orders
Open `order_log_*.csv` in Excel:
- Filter by Event (ENTRY, SignalExit, ExitAll)
- Check if FilterPass is True/False
- Compare Signal vs Threshold

#### Analyze Trades
Open `trade_log_*.csv` in Excel or Python:
- Calculate win rate, average PnL
- Analyze which features had highest contributions
- Check if certain bins are more profitable

### 5. Debug in Visual Studio

With all try-catch removed and clear variable names:
1. Set breakpoints in OnBar
2. Step through each section
3. Inspect variables at each step
4. Watch window shows all intermediate calculations

## Testing Checklist

- [ ] Verify signal calculations match expected values
- [ ] Check that MAD updates correctly daily
- [ ] Confirm features are extracted from correct AlphaManager indices
- [ ] Validate filter score calculation matches Python implementation
- [ ] Ensure entries only occur when both signal and filter pass
- [ ] Verify exits occur at correct signal thresholds
- [ ] Check that signal state prevents multiple entries
- [ ] Confirm PnL calculations are correct
- [ ] Validate all 24 features are logged correctly
- [ ] Check bin assignments match expected ranges

## Known Differences from Original

1. **No try-catch blocks** - Original had some error handling, refactored version has none
2. **Legacy methods return false** - `ShouldEnterLongPosition`, etc. are not used, all logic in OnBar
3. **More verbose logging** - Significantly more output for debugging
4. **Explicit variable naming** - More intermediate variables for clarity

## Performance Notes

The refactored version will be **slower** due to extensive logging:
- File I/O on every bar
- String formatting for logs
- No performance optimizations

This is intentional - the goal is debuggability, not speed. Once the logic is validated, you can:
- Remove or reduce logging
- Add try-catch back for production
- Optimize calculations

## Next Steps

1. **Validate Logic** - Run backtest and verify calculations match expectations
2. **Check Filter** - Ensure filter scores match Python implementation
3. **Analyze Trades** - Use trade log to understand performance
4. **Optimize** - Once validated, add back optimizations if needed
5. **Production** - Add error handling back for production use

## Questions to Answer with Logs

The comprehensive logging allows you to answer:

1. **Why did we enter here?**
   - Check calculation log for signal and filter values
   - See exact feature values and contributions

2. **Why didn't we enter here?**
   - Check if signal triggered but filter failed
   - See which features caused filter to fail

3. **Why did we exit here?**
   - Check signal vs exit threshold
   - See if it was signal exit or end-of-day exit

4. **Are features calculated correctly?**
   - Compare logged feature values with AlphaManager data
   - Verify indices are correct

5. **Is the filter working as expected?**
   - Check filter scores vs threshold
   - Analyze feature contributions
   - Verify bin assignments

6. **What's the PnL breakdown?**
   - Trade log has every trade with entry/exit prices
   - Can calculate metrics by feature values, filter scores, etc.
