using System;
using System.Collections.Generic;


namespace PriceExecutionHandler
{
    /// <summary>
    /// Interface for managing spread execution operations
    /// </summary>
    public interface ISpreadExecutionManager
    {
        #region Execution Methods
        
        /// <summary>
        /// Execute a spread order
        /// </summary>
        /// <param name="spreadOrder">The spread order to execute</param>
        /// <param name="parameters">Execution parameters</param>
        /// <returns>Execution result</returns>
        SpreadExecutionResult ExecuteSpreadOrder(SpreadOrder spreadOrder, SpreadExecutionParameters parameters);
        
        /// <summary>
        /// Cancel a spread order
        /// </summary>
        /// <param name="spreadOrderId">ID of the spread order to cancel</param>
        void CancelSpreadOrder(int spreadOrderId);
        
        /// <summary>
        /// Get the current status of a spread order
        /// </summary>
        /// <param name="spreadOrderId">ID of the spread order</param>
        /// <returns>Current execution status</returns>
        SpreadExecutionStatus GetExecutionStatus(int spreadOrderId);
        
        /// <summary>
        /// Get the current position for a spread order
        /// </summary>
        /// <param name="spreadOrderId">ID of the spread order</param>
        /// <returns>Current spread position</returns>
        SpreadPosition GetCurrentPosition(int spreadOrderId);
        
        #endregion
        
        #region Configuration
        
        /// <summary>
        /// Update default execution parameters
        /// </summary>
        /// <param name="parameters">New default parameters</param>
        void UpdateDefaultParameters(SpreadExecutionParameters parameters);
        
        /// <summary>
        /// Get current default execution parameters
        /// </summary>
        /// <returns>Current default parameters</returns>
        SpreadExecutionParameters GetDefaultParameters();
        
        #endregion
        
        #region Monitoring
        
        /// <summary>
        /// Get list of active spread executions
        /// </summary>
        /// <returns>List of active spread order IDs</returns>
        IEnumerable<int> GetActiveExecutions();
        
        /// <summary>
        /// Get execution statistics
        /// </summary>
        /// <returns>Execution statistics</returns>
        SpreadExecutionStatistics GetExecutionStatistics();
        
        #endregion
        
        #region Events
        
        /// <summary>
        /// Event fired when any spread execution status changes
        /// </summary>
        event EventHandler<SpreadExecutionEventArgs> ExecutionEvent;
        
        #endregion
    }
    
    /// <summary>
    /// Statistics for spread execution operations
    /// </summary>
    public class SpreadExecutionStatistics
    {
        public int TotalExecutions { get; set; }
        public int CompletedExecutions { get; set; }
        public int FailedExecutions { get; set; }
        public int CancelledExecutions { get; set; }
        public double SuccessRate => TotalExecutions > 0 ? (double)CompletedExecutions / TotalExecutions : 0;
        public TimeSpan AverageExecutionTime { get; set; }
        public DateTime LastExecutionTime { get; set; }
    }
}

