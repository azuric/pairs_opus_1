using System;
using System.Collections.Generic;
using SmartQuant;

namespace PriceExecutionHandler
{
    /// <summary>
    /// Execution status for spread orders
    /// </summary>
    public enum SpreadExecutionStatus
    {
        Initialized,
        InProgress,
        PartiallyFilled,
        Completed,
        Cancelled,
        Failed
    }
    
    /// <summary>
    /// Represents a spread order to be executed
    /// </summary>
    public class SpreadOrder
    {
        public int SpreadOrderId { get; set; }
        public string InstrumentSymbol { get; set; }
        public OrderSide Side { get; set; }
        public int Quantity { get; set; }
        public double SpreadPrice { get; set; }
        public DateTime CreatedTime { get; set; }
        
        public SpreadOrder()
        {
            CreatedTime = DateTime.UtcNow;
        }
    }
    
    /// <summary>
    /// Current position information for a spread
    /// </summary>
    public class SpreadPosition
    {
        public int NumeratorPosition { get; set; }
        public int DenominatorPosition { get; set; }
        public double NumeratorAvgPrice { get; set; }
        public double DenominatorAvgPrice { get; set; }
        public int NetSyntheticPosition { get; set; }
        public double SyntheticAvgPrice { get; set; }
        
        /// <summary>
        /// Calculate position imbalance between legs
        /// </summary>
        public int PositionImbalance => Math.Abs(NumeratorPosition) - Math.Abs(DenominatorPosition);
        
        /// <summary>
        /// Check if position is balanced (both legs filled)
        /// </summary>
        public bool IsBalanced => PositionImbalance == 0;
    }
    
    /// <summary>
    /// Configuration parameters for spread execution
    /// </summary>
    public class SpreadExecutionParameters
    {
        /// <summary>
        /// Which contract to execute first ("numerator" or "denominator")
        /// </summary>
        public string LiquidContract { get; set; } = "numerator";
        
        /// <summary>
        /// Maximum clip size for individual orders
        /// </summary>
        public int ClipSize { get; set; } = 100;
        
        /// <summary>
        /// Maximum execution time before timeout
        /// </summary>
        public TimeSpan MaxExecutionTime { get; set; } = TimeSpan.FromMinutes(5);
        
        /// <summary>
        /// Use mid-price for execution calculations
        /// </summary>
        public bool UseMidPrice { get; set; } = false;
        
        /// <summary>
        /// Enable detailed logging
        /// </summary>
        public bool EnableLogging { get; set; } = true;
        
        /// <summary>
        /// Validate parameters
        /// </summary>
        public void Validate()
        {
            if (ClipSize <= 0)
                throw new ArgumentException("ClipSize must be positive");
            if (MaxExecutionTime <= TimeSpan.Zero)
                throw new ArgumentException("MaxExecutionTime must be positive");
            if (string.IsNullOrEmpty(LiquidContract))
                throw new ArgumentException("LiquidContract must be specified");
        }
    }
    
    /// <summary>
    /// Event arguments for spread execution events
    /// </summary>
    public class SpreadExecutionEventArgs : EventArgs
    {
        public int SpreadOrderId { get; set; }
        public SpreadExecutionStatus Status { get; set; }
        public string Message { get; set; }
        public DateTime Timestamp { get; set; }
        public Exception Error { get; set; }
        
        public SpreadExecutionEventArgs(int spreadOrderId, SpreadExecutionStatus status, string message)
        {
            SpreadOrderId = spreadOrderId;
            Status = status;
            Message = message;
            Timestamp = DateTime.UtcNow;
        }
    }
    
    /// <summary>
    /// Result of spread execution operation
    /// </summary>
    public class SpreadExecutionResult
    {
        public int SpreadOrderId { get; set; }
        public SpreadExecutionStatus Status { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public double ExecutedQuantity { get; set; }
        public double AverageSpreadPrice { get; set; }
        public string ErrorMessage { get; set; }

        public TimeSpan? ExecutionDuration => EndTime.HasValue ? (TimeSpan?)(EndTime.Value - StartTime) : null;
    }
}

