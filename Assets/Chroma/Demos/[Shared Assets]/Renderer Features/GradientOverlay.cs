using UnityEngine.Rendering.Universal;

namespace Dustyroom {
public class GradientOverlay : GenericRendererFeature {
    public GradientOverlay() {
        requirements = ScriptableRenderPassInput.Color;
        injectionPoint = InjectionPoint.BeforeRenderingPostProcessing;
#if UNITY_6000_0_OR_NEWER
        fetchColorBuffer = true;
#endif
    }
}
}