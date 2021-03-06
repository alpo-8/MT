using System;
using MessagePack;

namespace MarginTrading.Backend.Contracts.Workflow.SpecialLiquidation.Commands
{
    /// <summary>
    /// Command from Corporate Actions to close all positions by instrument
    /// </summary>
    [MessagePackObject]
    public class StartSpecialLiquidationCommand
    {
        /// <summary>
        /// Operation Id
        /// </summary>
        [Key(0)]
        public string OperationId { get; set; }
        
        /// <summary>
        /// Command creation time
        /// </summary>
        [Key(1)]
        public DateTime CreationTime { get; set; }
        
        /// <summary>
        /// Instrument
        /// </summary>
        [Key(3)]
        public string Instrument { get; set; }
    }
}