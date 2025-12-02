using System;
using System.Threading;
using System.Threading.Tasks;
using Chat.Web.Options;
using Chat.Web.Repositories;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Chat.Web.Services
{
    /// <summary>
    /// Background service that initializes CosmosClients asynchronously during application startup.
    /// This avoids blocking the thread pool with .GetAwaiter().GetResult() in DI registration.
    /// </summary>
    public class CosmosClientsInitializationService : IHostedService
    {
        private readonly CosmosOptions _options;
        private readonly ILogger<CosmosClients> _logger;
        private readonly Action<CosmosClients> _setInstance;

        public CosmosClientsInitializationService(
            CosmosOptions options,
            ILogger<CosmosClients> logger,
            Action<CosmosClients> setInstance)
        {
            _options = options;
            _logger = logger;
            _setInstance = setInstance;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Initializing Cosmos DB clients for database '{Database}' with containers: Users={Users}, Rooms={Rooms}, Messages={Messages}",
                    _options.Database, _options.UsersContainer, _options.RoomsContainer, _options.MessagesContainer);
                
                // Use async factory pattern without blocking
                var clients = await CosmosClients.CreateAsync(_options).ConfigureAwait(false);
                
                _logger.LogInformation("Cosmos DB clients initialized successfully");
                
                // Set the singleton instance
                _setInstance(clients);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize Cosmos DB clients. ConnectionString configured: {HasConnectionString}, Database: {Database}",
                    !string.IsNullOrWhiteSpace(_options.ConnectionString), _options.Database);
                throw;
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            // No cleanup needed
            return Task.CompletedTask;
        }
    }
}
