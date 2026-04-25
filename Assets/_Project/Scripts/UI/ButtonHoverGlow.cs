using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using DG.Tweening;

[RequireComponent(typeof(Image))]
public class ButtonHoverGlow : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerDownHandler,
    IPointerUpHandler
{
    [SerializeField] private Color normalColor  = new Color(0.05f, 0.12f, 0.14f, 1f);
    [SerializeField] private Color glowColor    = new Color(0.12f, 0.75f, 0.68f, 1f);
    [SerializeField] private Color pressedColor = new Color(0.04f, 0.45f, 0.42f, 1f);
    [SerializeField] private float duration     = 0.15f;

    private Image  _image;
    private Tween  _tween;
    private bool   _hovered;

    private void Awake()
    {
        _image = GetComponent<Image>();
        _image.color = normalColor;
    }

    public void OnPointerEnter(PointerEventData _)
    {
        _hovered = true;
        Animate(glowColor);
    }

    public void OnPointerExit(PointerEventData _)
    {
        _hovered = false;
        Animate(normalColor);
    }

    public void OnPointerDown(PointerEventData _) => Animate(pressedColor);

    public void OnPointerUp(PointerEventData _) => Animate(_hovered ? glowColor : normalColor);

    private void Animate(Color target)
    {
        _tween?.Kill();
        _tween = DOTween.To(() => _image.color, c => _image.color = c, target, duration)
                        .SetEase(Ease.OutQuad)
                        .SetUpdate(true);
    }

    private void OnDestroy() => _tween?.Kill();
}
