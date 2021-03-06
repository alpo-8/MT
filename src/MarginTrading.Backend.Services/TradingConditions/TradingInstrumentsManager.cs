﻿using System.Linq;
using System.Threading.Tasks;
using Common.Log;
using JetBrains.Annotations;
using MarginTrading.Backend.Core.TradingConditions;
using MarginTrading.Common.Services;
using MarginTrading.SettingsService.Contracts;
using MarginTrading.SettingsService.Contracts.TradingConditions;
using Newtonsoft.Json;

namespace MarginTrading.Backend.Services.TradingConditions
{
    [UsedImplicitly]
    public class TradingInstrumentsManager : ITradingInstrumentsManager
    {
        private readonly ITradingInstrumentsCacheService _tradingInstrumentsCacheService;
        private readonly ITradingInstrumentsApi _tradingInstrumentsApi;
        private readonly IConvertService _convertService;
        private readonly IConsole _console;

        public TradingInstrumentsManager(
            ITradingInstrumentsCacheService tradingInstrumentsCacheService,
            ITradingInstrumentsApi tradingInstrumentsApi,
            IConvertService convertService,
            IConsole console)
        {
            _tradingInstrumentsCacheService = tradingInstrumentsCacheService;
            _tradingInstrumentsApi = tradingInstrumentsApi;
            _convertService = convertService;
            _console = console;
        }

        public void Start()
        {
            UpdateTradingInstrumentsCacheAsync().Wait();
        }

        public async Task UpdateTradingInstrumentsCacheAsync(string id = null)
        {
            _console.WriteLine($"Started {nameof(UpdateTradingInstrumentsCacheAsync)}");

            var count = 0;
            if (string.IsNullOrEmpty(id))
            {
                var instruments = (await _tradingInstrumentsApi.List(string.Empty))?
                    .Select(i =>_convertService.Convert<TradingInstrumentContract, TradingInstrument>(i))
                    .ToDictionary(x => x.GetKey());

                if (instruments != null)
                {
                    _tradingInstrumentsCacheService.InitCache(instruments.Values.Select(i => (ITradingInstrument) i)
                        .ToList());
   
                    count = instruments.Count;
                }
            }
            else
            {
                var ids = JsonConvert.DeserializeObject<TradingInstrumentContract>(id);
                
                var instrumentContract = await _tradingInstrumentsApi.Get(ids.TradingConditionId, ids.Instrument);
                
                if (instrumentContract != null)
                {
                    var newInstrument = _convertService.Convert<TradingInstrumentContract, TradingInstrument>(instrumentContract);
                    
                    _tradingInstrumentsCacheService.UpdateCache(newInstrument);
                    
                    count = 1;
                }
                else
                {
                    _tradingInstrumentsCacheService.RemoveFromCache(ids.TradingConditionId, ids.Instrument);
                }
            }

            _console.WriteLine($"Finished {nameof(UpdateTradingInstrumentsCacheAsync)} with count: {count}.");
        }
    }
}
