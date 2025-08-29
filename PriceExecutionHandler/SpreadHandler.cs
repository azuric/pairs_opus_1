using System;
using System.Collections.Generic;
using SmartQuant;

namespace PriceExecutionHandler
{
    /// <summary>
    /// Simplified SpreadHandler implementation for executing synthetic instrument orders
    /// by trading component instruments (numerator and denominator)
    /// </summary>
    public class SpreadHandler : ISpreadHandler
    {
        #region Private Fields
        
        private readonly SpreadOrder _spreadOrder;
        private readonly SpreadExecutionParameters _parameters;
        private readonly Instrument _numeratorInstrument;
        private readonly Instrument _denominatorInstrument;
        private readonly object _lockObject = new object();
        
        // Execution state
        private SpreadExecutionStatus _status;
        private SpreadPosition _currentPosition;
        private DateTime _executionStartTime;
        
        // Order tracking
        private Order _primaryOrder;
        private Order _hedgeOrder;
        private bool _primaryOrderCompleted;
        private bool _hedgeOrderCompleted;
        
        // Quantities tracking
        private int _totalQuantityToExecute;
        private int _remainingQuantity;
        private int _currentClipSize;
        
        #endregion
        
        #region Properties
        
        public int SpreadOrderId => _spreadOrder.SpreadOrderId;
        public string InstrumentSymbol => _spreadOrder.InstrumentSymbol;
        public SpreadExecutionStatus Status => _status;
        public SpreadPosition CurrentPosition => _currentPosition;
        
        #endregion
        
        #region Events
        
        public event EventHandler<SpreadExecutionEventArgs> ExecutionEvent;
        
        #endregion
        
        #region Constructor
        
        public SpreadHandler(
            SpreadOrder spreadOrder,
            SpreadExecutionParameters parameters,
            Instrument numeratorInstrument,
            Instrument denominatorInstrument)
        {
            _spreadOrder = spreadOrder ?? throw new ArgumentNullException(nameof(spreadOrder));
            _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
            _numeratorInstrument = numeratorInstrument ?? throw new ArgumentNullException(nameof(numeratorInstrument));
            _denominatorInstrument = denominatorInstrument ?? throw new ArgumentNullException(nameof(denominatorInstrument));
            
            // Validate parameters
            _parameters.Validate();
            
            // Initialize state
            _status = SpreadExecutionStatus.Initialized;
            _currentPosition = new SpreadPosition();
            _totalQuantityToExecute = _spreadOrder.Quantity;
            _remainingQuantity = _totalQuantityToExecute;
            
            LogMessage($"SpreadHandler initialized for {InstrumentSymbol}, Quantity: {_totalQuantityToExecute}, Side: {_spreadOrder.Side}");
        }
        
        #endregion
        
        #region Public Methods
        
        public void StartExecution()
        {
            lock (_lockObject)
            {
                if (_status != SpreadExecutionStatus.Initialized)
                {
                    LogMessage($"Cannot start execution - current status: {_status}");
                    return;
                }
                
                try
                {
                    _status = SpreadExecutionStatus.InProgress;
                    _executionStartTime = DateTime.UtcNow;
                    
                    LogMessage("Starting spread execution");
                    RaiseExecutionEvent(SpreadExecutionStatus.InProgress, "Execution started");
                    
                    // Determine which leg to execute first based on liquid contract parameter
                    ExecuteNextClip();
                }
                catch (Exception ex)
                {
                    _status = SpreadExecutionStatus.Failed;
                    LogMessage($"Error starting execution: {ex.Message}");
                    RaiseExecutionEvent(SpreadExecutionStatus.Failed, $"Execution failed: {ex.Message}");
                }
            }
        }
        
        public void CancelExecution()
        {
            lock (_lockObject)
            {
                if (_status == SpreadExecutionStatus.Completed || _status == SpreadExecutionStatus.Cancelled)
                {
                    return;
                }
                
                LogMessage("Cancelling spread execution");
                
                // Cancel any active orders
                if (_primaryOrder != null && IsOrderActive(_primaryOrder))
                {
                    CancelOrder(_primaryOrder);
                }
                
                if (_hedgeOrder != null && IsOrderActive(_hedgeOrder))
                {
                    CancelOrder(_hedgeOrder);
                }
                
                _status = SpreadExecutionStatus.Cancelled;
                RaiseExecutionEvent(SpreadExecutionStatus.Cancelled, "Execution cancelled");
            }
        }
        
        public void OnExecutionReport(ExecutionReport report)
        {
            lock (_lockObject)
            {
                try
                {
                    LogMessage($"Received execution report: Order {report.Order.Id}, Status: {report.OrdStatus}, ExecType: {report.ExecType}");
                    
                    if (report.Order.Id == _primaryOrder?.Id)
                    {
                        HandlePrimaryOrderReport(report);
                    }
                    else if (report.Order.Id == _hedgeOrder?.Id)
                    {
                        HandleHedgeOrderReport(report);
                    }
                    else
                    {
                        LogMessage($"Received execution report for unknown order: {report.Order.Id}");
                    }
                }
                catch (Exception ex)
                {
                    LogMessage($"Error processing execution report: {ex.Message}");
                }
            }
        }
        
        public void OnMarketDataUpdate(Instrument instrument)
        {
            // For now, just log market data updates
            // In a more sophisticated implementation, this could trigger price updates
            if (_parameters.EnableLogging)
            {
                LogMessage($"Market data update for {instrument.Symbol}: Bid={instrument.Bid?.Price}, Ask={instrument.Ask?.Price}");
            }
        }
        
        #endregion
        
        #region Private Methods - Execution Logic
        
        private void ExecuteNextClip()
        {
            if (_remainingQuantity <= 0)
            {
                LogMessage("No remaining quantity to execute");
                return;
            }
            
            // Calculate current clip size
            _currentClipSize = Math.Min(_parameters.ClipSize, _remainingQuantity);
            
            // Determine which instrument to execute first (liquid contract)
            var (primaryInstrument, isPrimaryNumerator) = GetPrimaryInstrument();
            
            // Create and submit primary order
            CreateAndSubmitPrimaryOrder(primaryInstrument, isPrimaryNumerator);
        }
        
        private (Instrument instrument, bool isNumerator) GetPrimaryInstrument()
        {
            if (_parameters.LiquidContract.ToLower() == "denominator")
            {
                LogMessage("Using denominator as primary (liquid) contract");
                return (_denominatorInstrument, false);
            }
            else
            {
                LogMessage("Using numerator as primary (liquid) contract");
                return (_numeratorInstrument, true);
            }
        }
        
        private void CreateAndSubmitPrimaryOrder(Instrument instrument, bool isNumerator)
        {
            try
            {
                var orderSide = _spreadOrder.Side;
                var price = CalculateExecutionPrice(instrument, orderSide, isNumerator);
                
                _primaryOrder = new Order
                {
                    Instrument = instrument,
                    Type = OrderType.Limit,
                    Side = orderSide,
                    Qty = _currentClipSize,
                    Price = price,
                    Text = $"SpreadPrimary_{SpreadOrderId}"
                };
                
                LogMessage($"Submitting primary order: {instrument.Symbol} {orderSide} {_currentClipSize} @ {price:F4}");
                
                // Submit order (this would integrate with TemplatePair_OPUS OrderFeed)
                SubmitOrder(_primaryOrder);
            }
            catch (Exception ex)
            {
                LogMessage($"Error creating primary order: {ex.Message}");
                _status = SpreadExecutionStatus.Failed;
                RaiseExecutionEvent(SpreadExecutionStatus.Failed, $"Failed to create primary order: {ex.Message}");
            }
        }
        
        private double CalculateExecutionPrice(Instrument instrument, OrderSide side, bool isNumerator)
        {
            double price;
            
            if (_parameters.UseMidPrice)
            {
                // Use mid-price
                var bid = instrument.Bid?.Price ?? 0;
                var ask = instrument.Ask?.Price ?? 0;
                price = (bid + ask) / 2.0;
            }
            else
            {
                // Use market price based on side
                if (side == OrderSide.Buy)
                {
                    price = instrument.Ask?.Price ?? instrument.Trade?.Price ?? 0;
                }
                else
                {
                    price = instrument.Bid?.Price ?? instrument.Trade?.Price ?? 0;
                }
            }
            
            // Apply simplified price calculation based on user requirements
            if (isNumerator)
            {
                // For numerator orders: numPrice = spreadprice * denprice
                var denPrice = GetCurrentPrice(_denominatorInstrument);
                price = _spreadOrder.SpreadPrice * denPrice;
                LogMessage($"Numerator price calculation: {_spreadOrder.SpreadPrice:F4} * {denPrice:F4} = {price:F4}");
            }
            else
            {
                // For denominator orders: denPrice = numprice / spreadprice
                var numPrice = GetCurrentPrice(_numeratorInstrument);
                price = numPrice / _spreadOrder.SpreadPrice;
                LogMessage($"Denominator price calculation: {numPrice:F4} / {_spreadOrder.SpreadPrice:F4} = {price:F4}");
            }
            
            // Round to tick size
            price = RoundToTickSize(price, instrument);
            
            return price;
        }
        
        private double GetCurrentPrice(Instrument instrument)
        {
            if (_parameters.UseMidPrice)
            {
                var bid = instrument.Bid?.Price ?? 0;
                var ask = instrument.Ask?.Price ?? 0;
                return (bid + ask) / 2.0;
            }
            else
            {
                return instrument.Trade?.Price ?? instrument.Bid?.Price ?? instrument.Ask?.Price ?? 0;
            }
        }
        
        private double RoundToTickSize(double price, Instrument instrument)
        {
            var tickSize = instrument.TickSize;
            if (tickSize > 0)
            {
                return Math.Round(price / tickSize) * tickSize;
            }
            return Math.Round(price, 4); // Default to 4 decimal places
        }
        
        #endregion
        
        #region Private Methods - Order Handling
        
        private void HandlePrimaryOrderReport(ExecutionReport report)
        {
            LogMessage($"Processing primary order report: {report.ExecType}, Status: {report.OrdStatus}");
            
            if (report.ExecType == ExecType.ExecTrade)
            {
                // Update position for primary leg
                UpdatePosition(report, true);
                
                // Check if primary order is completely filled
                if (report.OrdStatus == OrderStatus.Filled)
                {
                    _primaryOrderCompleted = true;
                    LogMessage($"Primary order completed. Filled quantity: {report.CumQty}");
                    
                    // Immediately place hedge order
                    PlaceHedgeOrder(report);
                }
                else if (report.OrdStatus == OrderStatus.PartiallyFilled)
                {
                    LogMessage($"Primary order partially filled: {report.CumQty}/{report.Order.Qty}");
                    // For simplicity, wait for complete fill before hedging
                }
            }
            else if (report.OrdStatus == OrderStatus.Cancelled || report.OrdStatus == OrderStatus.Rejected)
            {
                LogMessage($"Primary order {report.OrdStatus.ToString().ToLower()}: {report.Text}");
                HandleOrderFailure($"Primary order {report.OrdStatus.ToString().ToLower()}");
            }
        }
        
        private void HandleHedgeOrderReport(ExecutionReport report)
        {
            LogMessage($"Processing hedge order report: {report.ExecType}, Status: {report.OrdStatus}");
            
            if (report.ExecType == ExecType.ExecTrade)
            {
                // Update position for hedge leg
                UpdatePosition(report, false);
                
                // Check if hedge order is completely filled
                if (report.OrdStatus == OrderStatus.Filled)
                {
                    _hedgeOrderCompleted = true;
                    LogMessage($"Hedge order completed. Filled quantity: {report.CumQty}");
                    
                    // Update remaining quantity
                    _remainingQuantity -= (int)report.CumQty;
                    
                    // Check if entire spread order is complete
                    CheckExecutionCompletion();
                }
                else if (report.OrdStatus == OrderStatus.PartiallyFilled)
                {
                    LogMessage($"Hedge order partially filled: {report.CumQty}/{report.Order.Qty}");
                }
            }
            else if (report.OrdStatus == OrderStatus.Cancelled || report.OrdStatus == OrderStatus.Rejected)
            {
                LogMessage($"Hedge order {report.OrdStatus.ToString().ToLower()}: {report.Text}");
                HandleOrderFailure($"Hedge order {report.OrdStatus.ToString().ToLower()}");
            }
        }
        
        private void PlaceHedgeOrder(ExecutionReport primaryFillReport)
        {
            try
            {
                // Determine hedge instrument (opposite of primary)
                var isPrimaryNumerator = _primaryOrder.Instrument.Id == _numeratorInstrument.Id;
                var hedgeInstrument = isPrimaryNumerator ? _denominatorInstrument : _numeratorInstrument;
                
                // Hedge side is opposite of spread side
                var hedgeSide = _spreadOrder.Side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy;
                
                // Use same quantity as primary fill
                var hedgeQuantity = (int)primaryFillReport.LastQty;
                
                // Calculate hedge price
                var hedgePrice = CalculateExecutionPrice(hedgeInstrument, hedgeSide, !isPrimaryNumerator);
                
                _hedgeOrder = new Order
                {
                    Instrument = hedgeInstrument,
                    Type = OrderType.Limit,
                    Side = hedgeSide,
                    Qty = hedgeQuantity,
                    Price = hedgePrice,
                    Text = $"SpreadHedge_{SpreadOrderId}"
                };
                
                LogMessage($"Submitting hedge order: {hedgeInstrument.Symbol} {hedgeSide} {hedgeQuantity} @ {hedgePrice:F4}");
                
                // Submit hedge order immediately
                SubmitOrder(_hedgeOrder);
            }
            catch (Exception ex)
            {
                LogMessage($"Error placing hedge order: {ex.Message}");
                HandleOrderFailure($"Failed to place hedge order: {ex.Message}");
            }
        }
        
        private void UpdatePosition(ExecutionReport report, bool isPrimaryLeg)
        {
            var fillQuantity = (int)report.LastQty;
            var fillPrice = report.LastPx;
            var side = report.Order.Side;
            
            // Determine signed quantity
            var signedQuantity = side == OrderSide.Buy ? fillQuantity : -fillQuantity;
            
            if (isPrimaryLeg)
            {
                // Update primary leg position
                if (_primaryOrder.Instrument.Id == _numeratorInstrument.Id)
                {
                    UpdateNumeratorPosition(signedQuantity, fillPrice);
                }
                else
                {
                    UpdateDenominatorPosition(signedQuantity, fillPrice);
                }
            }
            else
            {
                // Update hedge leg position
                if (_hedgeOrder.Instrument.Id == _numeratorInstrument.Id)
                {
                    UpdateNumeratorPosition(signedQuantity, fillPrice);
                }
                else
                {
                    UpdateDenominatorPosition(signedQuantity, fillPrice);
                }
            }
            
            // Update synthetic position
            UpdateSyntheticPosition();
            
            LogMessage($"Position updated - Num: {_currentPosition.NumeratorPosition}, Den: {_currentPosition.DenominatorPosition}, Synthetic: {_currentPosition.NetSyntheticPosition}");
        }
        
        private void UpdateNumeratorPosition(int signedQuantity, double price)
        {
            var oldPosition = _currentPosition.NumeratorPosition;
            var oldAvgPrice = _currentPosition.NumeratorAvgPrice;
            
            _currentPosition.NumeratorPosition += signedQuantity;
            
            // Update average price
            if (_currentPosition.NumeratorPosition != 0)
            {
                var totalValue = (oldPosition * oldAvgPrice) + (signedQuantity * price);
                _currentPosition.NumeratorAvgPrice = totalValue / _currentPosition.NumeratorPosition;
            }
            else
            {
                _currentPosition.NumeratorAvgPrice = 0;
            }
        }
        
        private void UpdateDenominatorPosition(int signedQuantity, double price)
        {
            var oldPosition = _currentPosition.DenominatorPosition;
            var oldAvgPrice = _currentPosition.DenominatorAvgPrice;
            
            _currentPosition.DenominatorPosition += signedQuantity;
            
            // Update average price
            if (_currentPosition.DenominatorPosition != 0)
            {
                var totalValue = (oldPosition * oldAvgPrice) + (signedQuantity * price);
                _currentPosition.DenominatorAvgPrice = totalValue / _currentPosition.DenominatorPosition;
            }
            else
            {
                _currentPosition.DenominatorAvgPrice = 0;
            }
        }
        
        private void UpdateSyntheticPosition()
        {
            // For simplicity, assume 1:1 ratio between numerator and denominator
            // In a more sophisticated implementation, this would consider actual instrument ratios
            _currentPosition.NetSyntheticPosition = _currentPosition.NumeratorPosition - _currentPosition.DenominatorPosition;
            
            // Calculate synthetic average price
            if (_currentPosition.NetSyntheticPosition != 0 && _currentPosition.DenominatorAvgPrice != 0)
            {
                _currentPosition.SyntheticAvgPrice = _currentPosition.NumeratorAvgPrice / _currentPosition.DenominatorAvgPrice;
            }
            else
            {
                _currentPosition.SyntheticAvgPrice = 0;
            }
        }
        
        private void CheckExecutionCompletion()
        {
            if (_remainingQuantity <= 0)
            {
                // Entire order is complete
                _status = SpreadExecutionStatus.Completed;
                LogMessage($"Spread execution completed. Total executed: {_totalQuantityToExecute}");
                RaiseExecutionEvent(SpreadExecutionStatus.Completed, "Execution completed successfully");
            }
            else if (_primaryOrderCompleted && _hedgeOrderCompleted)
            {
                // Current clip is complete, but more quantity remains
                _status = SpreadExecutionStatus.PartiallyFilled;
                LogMessage($"Clip completed. Remaining quantity: {_remainingQuantity}");
                
                // Reset for next clip
                _primaryOrder = null;
                _hedgeOrder = null;
                _primaryOrderCompleted = false;
                _hedgeOrderCompleted = false;
                
                // Execute next clip
                ExecuteNextClip();
            }
        }
        
        private void HandleOrderFailure(string reason)
        {
            _status = SpreadExecutionStatus.Failed;
            LogMessage($"Execution failed: {reason}");
            RaiseExecutionEvent(SpreadExecutionStatus.Failed, reason);
        }
        
        #endregion
        
        #region Private Methods - Utilities
        
        private bool IsOrderActive(Order order)
        {
            return order != null && 
                   (order.Status == OrderStatus.New || 
                    order.Status == OrderStatus.PartiallyFilled || 
                    order.Status == OrderStatus.Replaced);
        }
        
        private void SubmitOrder(Order order)
        {
            // This is a placeholder - in the actual implementation, this would integrate
            // with the TemplatePair_OPUS OrderFeed component
            LogMessage($"[PLACEHOLDER] Submitting order: {order.Instrument.Symbol} {order.Side} {order.Qty} @ {order.Price}");
            
            // For now, just set the order status to indicate it was submitted
            // In the real implementation, this would be handled by the order management system
        }
        
        private void CancelOrder(Order order)
        {
            // This is a placeholder - in the actual implementation, this would integrate
            // with the TemplatePair_OPUS OrderFeed component
            LogMessage($"[PLACEHOLDER] Cancelling order: {order.Id}");
        }
        
        private void LogMessage(string message)
        {
            if (_parameters.EnableLogging)
            {
                Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] SpreadHandler[{SpreadOrderId}]: {message}");
            }
        }
        
        private void RaiseExecutionEvent(SpreadExecutionStatus status, string message)
        {
            ExecutionEvent?.Invoke(this, new SpreadExecutionEventArgs(SpreadOrderId, status, message));
        }
        
        #endregion
    }
}

