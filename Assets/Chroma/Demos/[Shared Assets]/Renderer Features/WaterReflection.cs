using UnityEngine.Rendering.Universal;

#pragma warning disable CS0618 // Type or member is obsolete
#pragma warning disable CS0672 // Member is obsolete

namespace Dustyroom {
public class WaterReflection : GenericRendererFeature {
    public WaterReflection() {
        requirements = ScriptableRenderPassInput.Color | ScriptableRenderPassInput.Depth;
        injectionPoint = InjectionPoint.BeforeRenderingPostProcessing;
#if UNITY_6000_0_OR_NEWER
        fetchColorBuffer = true;
#endif
    }

#if !UNITY_6000_0_OR_NEWER || UNITY_6000_1_OR_NEWER
    public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData) {
        base.SetupRenderPasses(renderer, in renderingData);

        // Feed the camera color image to the shader as "_MainTex".
        var material = base.passMaterial;
        if (material != null) {
            material.SetTexture("_MainTex", renderingData.cameraData.renderer.cameraColorTargetHandle);
        }
    }
#endif
}
}
