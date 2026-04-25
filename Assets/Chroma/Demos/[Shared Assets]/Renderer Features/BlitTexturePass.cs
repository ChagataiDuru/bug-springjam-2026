// This class has been deprecated in Chroma 2.1.0.
// The file is still included in the package for compatibility reasons.

using UnityEngine.Rendering.Universal;

namespace Dustyroom {
#if !UNITY_2022_1_OR_NEWER
internal class BlitTexturePass : ScriptableRenderPass { }
#endif
}