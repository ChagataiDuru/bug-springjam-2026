// This class has been deprecated in Chroma 2.1.0.
// The file is still included in the package for compatibility reasons.

namespace Dustyroom
{
#if UNITY_6000_1_OR_NEWER
    public class GenericRendererFeature : UnityEngine.Rendering.Universal.FullScreenPassRendererFeature { }
#else
    public class GenericRendererFeature : FullScreenPassRendererFeature { }
#endif
}
