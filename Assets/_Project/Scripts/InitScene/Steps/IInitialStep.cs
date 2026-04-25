using System.Threading.Tasks;

namespace Taiyun.SuckTheWater.InitScene.Steps
{
    /// <summary>
    /// Interface for initialization steps executed in InitScene.
    /// Each step represents a distinct initialization task (internet check, Steam init, etc.)
    /// </summary>
    public interface IInitialStep
    {
        /// <summary>
        /// Name of this initialization step (for display in loading UI)
        /// </summary>
        string Name { get; }
        
        /// <summary>
        /// Executes this initialization step.
        /// </summary>
        /// <returns>True if successful, false if failed</returns>
        Task<bool> Execute();
    }
}
