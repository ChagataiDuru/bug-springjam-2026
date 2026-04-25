using System.Threading.Tasks;

namespace Taiyun.SuckTheWater.Service
{
    /// <summary>
    /// Base interface for all service implementations.
    /// Services follow a lifecycle: Init -> Start -> Stop
    /// </summary>
    public interface IService
    {
        /// <summary>
        /// Initializes the service with optional parameters.
        /// Called once during application startup.
        /// </summary>
        Task<bool> InitService(params System.Object[] args);
        
        /// <summary>
        /// Starts the service (registers callbacks, starts background tasks, etc.).
        /// Called after initialization.
        /// </summary>
        Task<bool> StartService();
        
        /// <summary>
        /// Stops the service and cleans up resources.
        /// Called on application quit or service shutdown.
        /// </summary>
        Task<bool> StopService();
    }
}
