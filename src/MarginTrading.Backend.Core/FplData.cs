﻿namespace MarginTrading.Backend.Core
{
    public class FplData
    {
        public decimal Fpl { get; set; }
        public decimal MarginRate { get; set; }
        public decimal MarginInit { get; set; }
        public decimal MarginMaintenance { get; set; }
        public int AccountBaseAssetAccuracy { get; set; }
        
        /// <summary>
        /// Margin used for open of position
        /// </summary>
        public decimal InitialMargin { get; set; }
        
        public int CalculatedHash { get; set; }
        public int ActualHash { get; set; }
    }
}
