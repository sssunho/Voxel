using UnityEngine;
using UnityEngine.InputSystem;

public class ThirdPersonCamera : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] Transform _target;

    [Header("Distance")]
    [SerializeField] float _distance = 5.0f;
    [SerializeField] float _minDistance = 1.0f;
    [SerializeField] float _maxDistance = 10.0f;

    [Header("Angle")]
    [SerializeField] float _minVerticalAngle = -30.0f;
    [SerializeField] float _maxVerticalAngle = 60.0f;

    [Header("Sensitivity")]
    [SerializeField] float _mouseSensitivity = 3.0f;
    [SerializeField] float _scrollSensitivity = 2.0f;

    [Header("Damping")]
    [SerializeField] float _rotationDamping = 10.0f;
    [SerializeField] float _zoomDamping = 8.0f;

    [Header("Shoulder Offset")]
    [SerializeField] float _shoulderOffsetX = 0.8f;
    [SerializeField] float _shoulderOffsetY = 0.5f;

    // 현재 rotation / distance
    float _currentYaw;
    float _currentPitch;
    float _currentDistance;

    // velocity (degrees/second, units/second)
    float _yawVelocity;
    float _pitchVelocity;
    float _distanceVelocity;

    bool _skipDeltaFrame;

    Mouse _mouse;

    void Awake()
    {
        _currentYaw = transform.eulerAngles.y;
        _currentPitch = transform.eulerAngles.x;
        _currentDistance = _distance;

        _mouse = Mouse.current;
    }

    void LateUpdate()
    {
        if (_target == null) return;
        if (_mouse == null)
        {
            _mouse = Mouse.current;
            return;
        }

        HandleInput();
        ApplyDamping();
        UpdateRig();
    }

    void HandleInput()
    {
        if (_mouse.rightButton.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            _skipDeltaFrame = true;
        }

        if (_mouse.rightButton.wasReleasedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        if (_mouse.rightButton.isPressed)
        {
            if (_skipDeltaFrame)
            {
                _skipDeltaFrame = false;
            }
            else
            {
                // delta(pixels/frame) → velocity(degrees/second)
                Vector2 delta = _mouse.delta.ReadValue() * _mouseSensitivity;
                if (Time.deltaTime > 0f)
                {
                    _yawVelocity = delta.x / Time.deltaTime;
                    _pitchVelocity = -delta.y / Time.deltaTime;
                }
            }
        }

        // 스크롤은 이벤트성이므로 velocity에 누적
        float scroll = _mouse.scroll.ReadValue().y;
        _distanceVelocity -= scroll * _scrollSensitivity;
    }

    void ApplyDamping()
    {
        float t = Time.deltaTime;

        _yawVelocity = Mathf.Lerp(_yawVelocity, 0f, t * _rotationDamping);
        _pitchVelocity = Mathf.Lerp(_pitchVelocity, 0f, t * _rotationDamping);
        _distanceVelocity = Mathf.Lerp(_distanceVelocity, 0f, t * _zoomDamping);
    }

    void UpdateRig()
    {
        float t = Time.deltaTime;

        _currentYaw += _yawVelocity * t;
        _currentPitch += _pitchVelocity * t;
        _currentPitch = Mathf.Clamp(_currentPitch, _minVerticalAngle, _maxVerticalAngle);
        _currentDistance += _distanceVelocity * t;
        _currentDistance = Mathf.Clamp(_currentDistance, _minDistance, _maxDistance);

        Quaternion yawOnly = Quaternion.Euler(0f, _currentYaw, 0f);
        Quaternion fullRotation = Quaternion.Euler(_currentPitch, _currentYaw, 0f);

        Vector3 pivotOffset = yawOnly * new Vector3(_shoulderOffsetX, _shoulderOffsetY, 0f);
        Vector3 pivot = _target.position + pivotOffset;

        transform.position = pivot - fullRotation * Vector3.forward * _currentDistance;
        transform.rotation = fullRotation;
    }
}