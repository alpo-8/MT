using System;
using MarginTrading.Backend.Core;
using MessagePack;

namespace MarginTrading.Backend.Services.Workflow.Liquidation.Events
{
    [MessagePackObject]
    public class LiquidationFailedInternalEvent
    {
        [Key(0)]
        public string OperationId { get; set; }
        
        [Key(1)]
        public DateTime CreationTime { get; set; }
        
        [Key(2)]
        public string Reason { get; set; }
        
        [Key(3)]
        public LiquidationType LiquidationType { get; set; }
    }
}