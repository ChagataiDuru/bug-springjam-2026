using System.Threading.Tasks;
using Taiyun.SuckTheWater.Main;
using UnityEngine;

namespace Taiyun.SuckTheWater.InitScene.Steps
{
    /// <summary>
    /// Initialization step that starts platform services (Steam + LobbyManager).
    /// This step ensures the ServiceManager is fully initialized.
    /// </summary>
    public class PlatformServiceStep : IInitialStep
    {
        public string Name => "Initializing Steam";

        public async Task<bool> Execute()
        {
            await Task.Yield();
            Debug.Log($"[PlatformServiceStep] Executing...");

            var serviceManager = SupremeManager.Instance?.ServiceManager;
            if (serviceManager == null)
            {
                Debug.LogError("[PlatformServiceStep] ServiceManager is null!");
                return false;
            }

            var status = await serviceManager.Init();

            if (!status)
            {
                Debug.LogError("[PlatformServiceStep] Failed to initialize services");
                return false;
            }

            if (serviceManager.LobbyManager?.Provider == null)
            {
                Debug.LogError("[PlatformServiceStep] LobbyManager or Provider is null after init");
                return false;
            }

            Debug.Log("[PlatformServiceStep] Platform services initialized successfully");
            return true;
        }
    }
}