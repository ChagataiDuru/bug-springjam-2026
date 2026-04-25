using System;
using System.Threading.Tasks;

namespace Taiyun.SuckTheWater.Service.LinkTestService
{
    /// <summary>
    /// Service for checking internet connectivity.
    /// Required for online multiplayer functionality.
    /// </summary>
    public interface ILinkTestService : IService
    {
        /// <summary>
        /// Checks if device has internet connectivity.
        /// </summary>
        /// <returns>True if internet is available, false otherwise</returns>
        Task<bool> CheckInternet();
        
        /// <summary>
        /// Event fired when internet connectivity status changes.
        /// </summary>
        event Action<bool> OnLinkStatusChanged;
    }
}
