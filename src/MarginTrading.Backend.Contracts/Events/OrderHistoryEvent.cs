﻿using System;
using MarginTrading.Backend.Contracts.Orders;

namespace MarginTrading.Backend.Contracts.Events
{
    public class OrderHistoryEvent
    {
        public OrderContract OrderSnapshot { get; set; }
        
        public OrderHistoryTypeContract Type { get; set; }
        
        public DateTime Timestamp { get; set; }
        
        /// <summary>
        /// Serialised object with additional information for activities in any format
        /// </summary>
        public string ActivitiesMetadata { get; set; }
    }
}