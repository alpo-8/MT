﻿using Lykke.SettingsReader.Attributes;

namespace MarginTrading.Backend.Core.Settings
{
    public class Db
    {
        public StorageMode StorageMode { get; set; }
        
        //[AzureTableCheck]
        public string LogsConnString { get; set; }
        //[AzureTableCheck]
        public string MarginTradingConnString { get; set; }
//        [AzureTableCheck]
//        public string HistoryConnString { get; set; }
        //[AzureBlobCheck]
        public string StateConnString { get; set; }
        
        [SqlCheck]
        public string SqlConnectionString { get; set; }
    }
}