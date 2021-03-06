﻿namespace MarginTrading.Backend.Contracts.Orders
{
    public class OrderCancelRequest
    {
        public OriginatorTypeContract Originator { get; set; }

        public string Comment { get; set; }
        
        public string AdditionalInfo { get; set; }
        
        /// <summary>
        /// The correlation identifier. Optional: if not passed will be auto-generated.  
        /// </summary>
        public string CorrelationId { get; set; }
    }
}