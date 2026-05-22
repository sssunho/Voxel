using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    [SerializeField] BaseCharacter _character;
    [SerializeField] Transform _cameraTransform;
    [SerializeField] float _jumpPower = 5.0f;

    InputSystem_Actions _inputActions;
    Vector2 _moveInput;

    void Awake()
    {
        _inputActions = new InputSystem_Actions();

        _inputActions.Player.Move.performed += OnMove;
        _inputActions.Player.Move.canceled += OnMove;
        
        _inputActions.Player.Jump.performed += OnJumpPerform;
        _inputActions.Player.Jump.canceled += OnJumpCanceled;
    }

    void OnEnable() => _inputActions.Enable();
    void OnDisable() => _inputActions.Disable();

    void OnDestroy()
    {
        _inputActions.Player.Move.performed -= OnMove;
        _inputActions.Player.Move.canceled -= OnMove;

        _inputActions.Player.Jump.performed -= OnJumpPerform;
        _inputActions.Dispose();
    }

    void Update()
    {
        if (_character == null) return;
        ApplyMovement();
    }

    void ApplyMovement()
    {
        if (_moveInput.sqrMagnitude < 0.001f) return;

        Vector3 forward = _cameraTransform != null
            ? Vector3.ProjectOnPlane(_cameraTransform.forward, Vector3.up).normalized
            : Vector3.forward;

        Vector3 right = _cameraTransform != null
            ? Vector3.ProjectOnPlane(_cameraTransform.right, Vector3.up).normalized
            : Vector3.right;

        Vector3 moveDirection = forward * _moveInput.y + right * _moveInput.x;
        _character.Move(moveDirection);
    }

    void OnMove(InputAction.CallbackContext ctx)
    {
        _moveInput = ctx.ReadValue<Vector2>();
    }

    void OnJumpPerform(InputAction.CallbackContext ctx)
    {
        _character.Jump(_jumpPower);
        _character.SetJumpButtonHeld(true);
    }

    void OnJumpCanceled(InputAction.CallbackContext ctx)
    {
        _character.SetJumpButtonHeld(false);
    }
}