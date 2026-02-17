using Microsoft.AspNetCore.Mvc;
using ServerTest.Modules.Planet.Domain;
using ServerTest.Modules.Planet.Infrastructure;

namespace ServerTest.Modules.Planet.Application
{
    public sealed class PlanetService
    {
        private readonly PlanetRepository _repository;

        public PlanetService(PlanetRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        public Task<IActionResult> CreatePostAsync(long uid, PlanetPostCreateRequest request, CancellationToken ct)
        {
            return _repository.CreatePostAsync(uid, request, ct);
        }

        public Task<IActionResult> UpdatePostAsync(long uid, PlanetPostUpdateRequest request, CancellationToken ct)
        {
            return _repository.UpdatePostAsync(uid, request, ct);
        }

        public Task<IActionResult> DeletePostAsync(long uid, PlanetPostDeleteRequest request, CancellationToken ct)
        {
            return _repository.DeletePostAsync(uid, request, ct);
        }

        public Task<IActionResult> SetVisibilityAsync(long uid, PlanetPostVisibilityRequest request, CancellationToken ct)
        {
            return _repository.SetVisibilityAsync(uid, request, ct);
        }

        public Task<IActionResult> ListPostsAsync(long uid, PlanetPostListRequest request, CancellationToken ct)
        {
            return _repository.ListPostsAsync(uid, request, ct);
        }

        public Task<IActionResult> GetPostDetailAsync(long uid, PlanetPostDetailRequest request, CancellationToken ct)
        {
            return _repository.GetPostDetailAsync(uid, request, ct);
        }

        public Task<IActionResult> ReactPostAsync(long uid, PlanetPostReactionRequest request, CancellationToken ct)
        {
            return _repository.ReactPostAsync(uid, request, ct);
        }

        public Task<IActionResult> ToggleFavoriteAsync(long uid, PlanetPostFavoriteRequest request, CancellationToken ct)
        {
            return _repository.ToggleFavoriteAsync(uid, request, ct);
        }

        public Task<IActionResult> AddCommentAsync(long uid, PlanetPostCommentCreateRequest request, CancellationToken ct)
        {
            return _repository.AddCommentAsync(uid, request, ct);
        }

        public Task<IActionResult> GetOwnerStatsAsync(long uid, PlanetOwnerStatsRequest request, CancellationToken ct)
        {
            return _repository.GetOwnerStatsAsync(uid, request, ct);
        }
    }
}
