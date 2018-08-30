using System.Threading.Tasks;
using Lykke.Common;
using MarginTrading.Backend.Contracts.Workflow.SpecialLiquidation.Events;
using MarginTrading.Backend.Core.Services;
using MarginTrading.Backend.Core.Settings;
using MarginTrading.Backend.Services.Infrastructure;
using MarginTrading.Common.Services;

namespace MarginTrading.Backend.Services.Services
{
    public class FakeGavelService : IFakeGavelService
    {
        private readonly ICqrsSender _cqrsSender;
        private readonly IDateService _dateService;
        private readonly IThreadSwitcher _threadSwitcher;
        private readonly SpecialLiquidationSettings _specialLiquidationSettings;

        public FakeGavelService(
            ICqrsSender cqrsSender,
            IDateService dateService,
            IThreadSwitcher threadSwitcher,
            SpecialLiquidationSettings specialLiquidationSettings)
        {
            _cqrsSender = cqrsSender;
            _dateService = dateService;
            _threadSwitcher = threadSwitcher;
            _specialLiquidationSettings = specialLiquidationSettings;
        }
        
        public void GetPriceForSpecialLiquidation(string operationId, string instrument, decimal volume)
        {
            _threadSwitcher.SwitchThread(async () =>
            {
                await Task.Delay(1000);//waiting for the state to be saved into the repo
                _cqrsSender.PublishEvent(new PriceForSpecialLiquidationCalculatedEvent
                {
                    OperationId = operationId,
                    CreationTime = _dateService.Now(),
                    Instrument = instrument,
                    Volume = volume,
                    Price = _specialLiquidationSettings.FakePrice,
                }, "Gavel");
            });
        }

        public void ExecuteSpecialLiquidationOrder(string operationId, string instrument, decimal volume,
            decimal price)
        {
            _threadSwitcher.SwitchThread(async () =>
            {
                await Task.Delay(1000);//waiting for the state to be saved into the repo
                _cqrsSender.PublishEvent(new SpecialLiquidationOrderExecutedEvent
                {
                    OperationId = operationId,
                    CreationTime = _dateService.Now(),
                }, "Gavel");
            });
        }
    }
}