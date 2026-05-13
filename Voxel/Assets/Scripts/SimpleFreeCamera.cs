using UnityEngine;

public class SimpleFreeCamera : MonoBehaviour
{
    [Header("Move")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float sprintSpeed = 10f;

    [Header("Look")]
    [SerializeField] private float mouseSensitivity = 2f;
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
        if (Input.GetMouseButtonDown(1))
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        if (Input.GetMouseButtonUp(1))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        // 우클릭 중일 때만 회전
        if (!Input.GetMouseButton(1))
            return;

        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        _yaw += mouseX;
        _pitch -= mouseY;
        _pitch = Mathf.Clamp(_pitch, -maxLookAngle, maxLookAngle);

        transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
    }

    private void UpdateMove()
    {
        float horizontal = Input.GetAxisRaw("Horizontal"); // A/D
        float vertical = Input.GetAxisRaw("Vertical");     // W/S

        Vector3 moveDir =
            transform.forward * vertical +
            transform.right * horizontal;

        moveDir.Normalize();

        float currentSpeed = Input.GetKey(KeyCode.LeftShift)
            ? sprintSpeed
            : moveSpeed;

        transform.position += moveDir * currentSpeed * Time.deltaTime;
    }
}