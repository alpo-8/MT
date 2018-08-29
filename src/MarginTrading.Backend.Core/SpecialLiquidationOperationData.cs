namespace MarginTrading.Backend.Core
{
    public class SpecialLiquidationOperationData
    {
        public SpecialLiquidationOperationState State { get; set; }
        
        public string Instrument { get; set; }
        public string[] PositionIds { get; set; }
        public decimal Volume { get; set; }
        public decimal Price { get; set; }
    }
}