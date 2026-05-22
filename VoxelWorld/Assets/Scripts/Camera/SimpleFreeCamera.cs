using UnityEngine;
using UnityEngine.InputSystem;

public class SimpleFreeCamera : MonoBehaviour
{
    [Header("Move")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float sprintSpeed = 10f;

    [Header("Look")]
    [SerializeField] private float mouseSensitivity = 0.08f;
    [SerializeField] private float maxLookAngle = 80f;

    private float _pitch;
    private float _yaw;

    private void Start()
    {
        Vector3 euler = transform.eulerAngles;
        _pitch = euler.x;
        _yaw = euler.y;
    }

    private void Update()
    {
        UpdateLook();
        UpdateMove();
    }

    private void UpdateLook()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null)
            return;

        if (mouse.rightButton.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        if (mouse.rightButton.wasReleasedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        if (!mouse.rightButton.isPressed)
            return;

        Vector2 delta = mouse.delta.ReadValue();

        _yaw += delta.x * mouseSensitivity;
        _pitch -= delta.y * mouseSensitivity;
        _pitch = Mathf.Clamp(_pitch, -maxLookAngle, maxLookAngle);

        transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
    }

    private void UpdateMove()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        float horizontal = 0f;
        float vertical = 0f;

        if (keyboard.aKey.isPressed) horizontal -= 1f;
        if (keyboard.dKey.isPressed) horizontal += 1f;
        if (keyboard.wKey.isPressed) vertical += 1f;
        if (keyboard.sKey.isPressed) vertical -= 1f;

        Vector3 moveDir =
            transform.forward * vertical +
            transform.right * horizontal;

        moveDir.Normalize();

        float currentSpeed = keyboard.leftShiftKey.isPressed
            ? sprintSpeed
            : moveSpeed;

        transform.position += moveDir * currentSpeed * Time.deltaTime;
    }
}