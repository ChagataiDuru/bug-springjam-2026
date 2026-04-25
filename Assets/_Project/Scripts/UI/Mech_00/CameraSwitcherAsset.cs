using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

public class CameraSwitcherAsset : MonoBehaviour
{
    private List<Camera> _cameras = new List<Camera>();
    private int _currentIndex = 0;

    void Start()
    {
        _cameras = GetComponentsInChildren<Camera>(includeInactive: true)
            .OrderBy(c => c.gameObject.name)
            .ToList();

        if (_cameras.Count == 0) return;

        for (int i = 0; i < _cameras.Count; i++)
            _cameras[i].gameObject.SetActive(i == 0);
    }

    void Update()
    {
        if (_cameras.Count == 0) return;

        bool spacePressed = Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
        bool clickPressed = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;

        if (spacePressed || clickPressed)
        {
            SwitchToNext();
        }
    }

    void SwitchToNext()
    {
        _cameras[_currentIndex].gameObject.SetActive(false);
        _currentIndex = (_currentIndex + 1) % _cameras.Count;
        _cameras[_currentIndex].gameObject.SetActive(true);

        Debug.Log($"Switched to: {_cameras[_currentIndex].name}");
    }
}