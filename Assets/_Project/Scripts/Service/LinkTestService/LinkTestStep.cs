using System.Threading.Tasks;
using Taiyun.SuckTheWater.Main;
using UnityEngine;

namespace Taiyun.SuckTheWater.InitScene.Steps
{
    /// <summary>
    /// Initialization step that checks for internet connectivity.
    /// Shows popup and waits if no internet is available.
    /// Required for multiplayer functionality.
    /// </summary>
    public class LinkTestStep : IInitialStep
    {
        public string Name => "Checking Internet Connection";
        
        public async Task<bool> Execute()
        {
            await Task.Yield();
            Debug.Log($"[LinkTestStep] Executing...");
            
            bool isLinked = false;
            while (!isLinked)
            {
                isLinked = await SupremeManager.Instance.ServiceManager.LinkTestService.CheckInternet();
                
                if (!isLinked)
                {
                    Debug.LogWarning("[LinkTestStep] No internet connection detected");
                    
                    string message = "Internet connection required.\nPlease check your network and try again.";
                    await SupremeManager.Instance.WaitPopUpOk(message);
                }
                
                await Task.Yield();
            }
            
            Debug.Log("[LinkTestStep] Internet connection verified");
            return true;
        }
    }
}
