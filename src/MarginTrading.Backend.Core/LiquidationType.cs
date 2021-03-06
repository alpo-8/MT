namespace MarginTrading.Backend.Core
{
    public enum LiquidationType
    {
        /// <summary>
        /// Stop out caused liquidation
        /// </summary>
        Normal = 0,
        /// <summary>
        /// MCO caused liquidation
        /// </summary>
        Mco = 1,
        /// <summary>
        /// Liquidation is started by "Close All" API call
        /// </summary>
        Forced = 2,
    }
}