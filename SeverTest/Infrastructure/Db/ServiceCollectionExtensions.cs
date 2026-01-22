using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ServerTest.Infrastructure.Db
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddDbInfrastructure(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<DbOptions>(configuration.GetSection("Db"));
            services.AddSingleton<IDbManager, DbManager>();
            services.AddScoped<IUnitOfWorkFactory, UnitOfWorkFactory>();

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            return services;
        }
    }

    public interface IUnitOfWorkFactory
    {
        Task<IUnitOfWork> BeginAsync(CancellationToken ct = default);
    }

    public sealed class UnitOfWorkFactory : IUnitOfWorkFactory
    {
        private readonly IDbManager _dbManager;

        public UnitOfWorkFactory(IDbManager dbManager)
        {
            _dbManager = dbManager;
        }

        public Task<IUnitOfWork> BeginAsync(CancellationToken ct = default) => _dbManager.BeginUnitOfWorkAsync(ct);
    }
}
