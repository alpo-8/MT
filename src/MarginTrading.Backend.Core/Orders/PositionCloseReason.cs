﻿namespace MarginTrading.Backend.Core.Orders
{
    public enum PositionCloseReason
    {
        None,
        Close,
        StopLoss,
        TakeProfit,
        StopOut
    }
}