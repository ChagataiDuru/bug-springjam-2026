using System;
using UnityEngine;

namespace OrangeWolf.Generic
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public class OrthographicCameraResizer : MonoBehaviour
    {
        [SerializeField] private Vector2 _referenceScreenSize = new Vector2(1242, 2208);

        private Camera _camera;

        private void Awake()
        {
            AdjustOrthographicSize();
        }

        private void OnEnable()
        {
            AdjustOrthographicSize();
        }

        private void OnValidate()
        {
            AdjustOrthographicSize();
        }

        public void AdjustOrthographicSize()
        {
            if(_camera == null)
                _camera = GetComponent<Camera>();
            
            Debug.Log( $"Camera Pixel Sizes: {_camera.pixelWidth}x{_camera.pixelHeight}");
            
            float minimumAspectRatio = 9.0f / 16.0f;
            float aspectRatio = (float)_camera.pixelWidth / (float)_camera.pixelHeight;

            if (aspectRatio > minimumAspectRatio) //if screen is shorter than 16:9
            {
                _camera.orthographicSize = (_referenceScreenSize.y * 0.5f) * 0.01f;
            }
            else //if screen is taller than 16:9
            {
                float reverseAspectRatio = 1.0f / aspectRatio;
                float finalHeight = _referenceScreenSize.x * reverseAspectRatio;
                _camera.orthographicSize = finalHeight * 0.005f;
            }
        }
    }
}