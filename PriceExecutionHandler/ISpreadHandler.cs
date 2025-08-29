using System;
using SmartQuant;

namespace PriceExecutionHandler
{
    /// <summary>
    /// Interface for handling individual spread order execution
    /// </summary>
    public interface ISpreadHandler
    {
        #region Properties
        
        /// <summary>
        /// Unique identifier for the spread order
        /// </summary>
        int SpreadOrderId { get; }
        
        /// <summary>
        /// Symbol of the synthetic instrument being traded
        /// </summary>
        string InstrumentSymbol { get; }
        
        /// <summary>
        /// Current execution status
        /// </summary>
        SpreadExecutionStatus Status { get; }
        
       
        /// <summary>
        /// Current spread position information
        /// </summary>
        SpreadPosition CurrentPosition { get; }
        
        #endregion
        
        #region Execution Methods
        
        /// <summary>
        /// Start the spread execution process
        /// </summary>
        void StartExecution();
        
        /// <summary>
        /// Cancel the spread execution
        /// </summary>
        void CancelExecution();
        
        #endregion
        
        #region Event Handlers
        
        /// <summary>
        /// Handle execution reports from component orders
        /// </summary>
        /// <param name="report">Execution report</param>
        void OnExecutionReport(ExecutionReport report);
        
        /// <summary>
        /// Handle market data updates
        /// </summary>
        /// <param name="instrument">Updated instrument</param>
        void OnMarketDataUpdate(Instrument instrument);
        
        #endregion
        
        #region Events
        
        /// <summary>
        /// Event fired when spread execution status changes
        /// </summary>
        event EventHandler<SpreadExecutionEventArgs> ExecutionEvent;
        
        #endregion
    }
}

