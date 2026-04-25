using UnityEngine;

/// <summary>
/// Displays the name of the currently playing animation on a TextMesh.
/// Attach this script to any GameObject in your scene.
/// </summary>
public class AnimationNameDisplay : MonoBehaviour
{
    [Header("Target Animator")]
    [Tooltip("Drag the GameObject that has the Animator component here")]
    public Animator targetAnimator;

    [Header("Text Mesh to Update")]
    [Tooltip("Drag your TextMesh GameObject here")]
    public TextMesh displayTextMesh;

    [Header("Settings")]
    [Tooltip("Which layer in the Animator to read from (usually 0)")]
    public int animatorLayer = 0;

    [Tooltip("How often (in seconds) to refresh the text. 0 = every frame")]
    public float refreshInterval = 0.1f;

    private float _timer = 0f;

    void Update()
    {
        // Throttle updates based on refreshInterval
        _timer += Time.deltaTime;
        if (refreshInterval > 0f && _timer < refreshInterval)
            return;
        _timer = 0f;

        // Safety checks
        if (targetAnimator == null || displayTextMesh == null)
            return;

        // Get the current animation state info
        AnimatorStateInfo stateInfo = targetAnimator.GetCurrentAnimatorStateInfo(animatorLayer);

        // Try to get the clip name from the current playing clips
        AnimatorClipInfo[] clipInfos = targetAnimator.GetCurrentAnimatorClipInfo(animatorLayer);

        if (clipInfos != null && clipInfos.Length > 0)
        {
            // Get the clip with the highest weight (most dominant)
            AnimationClip dominantClip = clipInfos[0].clip;
            float highestWeight = clipInfos[0].weight;

            for (int i = 1; i < clipInfos.Length; i++)
            {
                if (clipInfos[i].weight > highestWeight)
                {
                    highestWeight = clipInfos[i].weight;
                    dominantClip = clipInfos[i].clip;
                }
            }

            if (dominantClip != null)
            {
                displayTextMesh.text = dominantClip.name;
            }
        }
        else
        {
            // Fallback: show the state name if no clip info is available
            displayTextMesh.text = "(no clip playing)";
        }
    }
}
