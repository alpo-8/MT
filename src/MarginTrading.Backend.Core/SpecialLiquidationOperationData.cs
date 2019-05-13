using System.Collections.Generic;
using JetBrains.Annotations;
using MarginTrading.Backend.Core.Orders;

namespace MarginTrading.Backend.Core
{
    public class SpecialLiquidationOperationData : OperationDataBase<SpecialLiquidationOperationState>
    {
        public string Instrument { get; set; }
        public List<string> PositionIds { get; set; }
        public decimal Volume { get; set; }
        public decimal Price { get; set; }
        public string ExternalProviderId { get; set; }
        [CanBeNull]
        public string AccountId { get; set; }
        [CanBeNull]
        public string CausationOperationId { get; set; }
        public string AdditionalInfo { get; set; }
        public OriginatorType OriginatorType { get; set; }
    }
}