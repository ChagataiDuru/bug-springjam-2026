namespace OrangeWolf.Generic
{
    public class ShaderCacheController
    {
        public void CacheShader()
        {
            UnityEngine.Shader.WarmupAllShaders();
        }
    }
}