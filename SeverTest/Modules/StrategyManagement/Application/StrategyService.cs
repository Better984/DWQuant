using Microsoft.AspNetCore.Mvc;
using ServerTest.Modules.StrategyManagement.Infrastructure;
using ServerTest.Models;
using ServerTest.Models.Strategy;

namespace ServerTest.Modules.StrategyManagement.Application
{
    public sealed class StrategyService
    {
        private readonly StrategyRepository _repository;

        public StrategyService(StrategyRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        public Task<IActionResult> Create(long uid, StrategyCreateRequest request)
        {
            return _repository.Create(uid, request);
        }

        public Task<IActionResult> List(long uid)
        {
            return _repository.List(uid);
        }

        public Task<IActionResult> ListOfficial(long uid)
        {
            return _repository.ListOfficial(uid);
        }

        public Task<IActionResult> OfficialVersions(long uid, long defId)
        {
            return _repository.OfficialVersions(uid, defId);
        }

        public Task<IActionResult> ListTemplate(long uid)
        {
            return _repository.ListTemplate(uid);
        }

        public Task<IActionResult> ListMarket(long uid)
        {
            return _repository.ListMarket(uid);
        }

        public Task<IActionResult> PublishMarket(long uid, StrategyMarketPublishRequest request)
        {
            return _repository.PublishMarket(uid, request);
        }

        public Task<IActionResult> Update(long uid, StrategyUpdateRequest request)
        {
            return _repository.Update(uid, request);
        }

        public Task<IActionResult> Publish(long uid, StrategyPublishRequest request)
        {
            return _repository.Publish(uid, request);
        }

        public Task<IActionResult> PublishOfficial(long uid, StrategyCatalogPublishRequest request)
        {
            return _repository.PublishOfficial(uid, request);
        }

        public Task<IActionResult> PublishTemplate(long uid, StrategyCatalogPublishRequest request)
        {
            return _repository.PublishTemplate(uid, request);
        }

        public Task<IActionResult> SyncOfficial(long uid, StrategyCatalogPublishRequest request)
        {
            return _repository.SyncOfficial(uid, request);
        }

        public Task<IActionResult> SyncTemplate(long uid, StrategyCatalogPublishRequest request)
        {
            return _repository.SyncTemplate(uid, request);
        }

        public Task<IActionResult> RemoveOfficial(long uid, StrategyCatalogPublishRequest request)
        {
            return _repository.RemoveOfficial(uid, request);
        }

        public Task<IActionResult> RemoveTemplate(long uid, StrategyCatalogPublishRequest request)
        {
            return _repository.RemoveTemplate(uid, request);
        }

        public Task<IActionResult> SyncMarket(long uid, StrategyMarketPublishRequest request)
        {
            return _repository.SyncMarket(uid, request);
        }

        public Task<IActionResult> CreateShareCode(long uid, StrategyShareCreateRequest request)
        {
            return _repository.CreateShareCode(uid, request);
        }

        public Task<IActionResult> ImportShareCode(long uid, StrategyImportShareCodeRequest request)
        {
            return _repository.ImportShareCode(uid, request);
        }

        public Task<IActionResult> Versions(long uid, long usId)
        {
            return _repository.Versions(uid, usId);
        }

        public Task<IActionResult> Delete(long uid, StrategyDeleteRequest request)
        {
            return _repository.Delete(uid, request);
        }

        public Task<IActionResult> UpdateInstanceState(long uid, long id, StrategyInstanceStateRequest request, CancellationToken ct)
        {
            return _repository.UpdateInstanceState(uid, id, request, ct);
        }

    }
}
