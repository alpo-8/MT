﻿using MarginTrading.Backend.Contracts.Orders;

namespace MarginTrading.Backend.Contracts.Positions
{
    public class PositionCloseRequest
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