﻿using System;
using MarginTrading.Backend.Core;

namespace MarginTrading.Backend.Services.Events
{
    public class OrderCancelledEventArgs
    {
        public OrderCancelledEventArgs(Order order)
        {
            if (order == null) throw new ArgumentNullException(nameof(order));
            Order = order;
        }

        public Order Order { get; }
    }
}