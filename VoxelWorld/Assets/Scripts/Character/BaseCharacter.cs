using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class BaseCharacter : MonoBehaviour
{
    public readonly float Gravity = -9.81f;

    [SerializeField] float _speed = 5.0f;
    [SerializeField] float _rotationSpeed = 5.0f;
    [SerializeField] float _fallMultiplier = 2.5f;  // 하강 가속
    [SerializeField] float _lowJumpMultiplier = 2.0f;  // 짧은 점프 시 상승 감속

    CharacterController _characterController;
    Vector3 _velocity = Vector3.zero;
    bool _moveInputThisFrame = false;
    bool _jumpButtonHeld;

    void Awake()
    {
        _characterController = GetComponent<CharacterController>();
    }

    void Update()
    {
        if (!_moveInputThisFrame)
        {
            _velocity.x = Mathf.Lerp(_velocity.x, 0, Time.deltaTime * 10f);
            _velocity.z = Mathf.Lerp(_velocity.z, 0, Time.deltaTime * 10f);
        }

        _moveInputThisFrame = false;

        Vector3 vxz = new Vector3(_velocity.x, 0, _velocity.z);

        if (vxz.sqrMagnitude > 0.001f)
        {
            transform.rotation = Quaternion.Lerp(
                transform.rotation,
                Quaternion.LookRotation(vxz),
                _rotationSpeed * Time.deltaTime
            );
        }

        if (_characterController.isGrounded && _velocity.y < 0)
        {
            _velocity.y = -2f;
        }

        _velocity.y += Gravity * Time.deltaTime;

        _characterController.Move(_velocity * Time.deltaTime);
    }
    void ApplyGravity()
    {
        if (_velocity.y < 0)
        {
            // 하강 중 : 중력 추가 적용
            _velocity.y += Gravity * (_fallMultiplier - 1) * Time.deltaTime;
        }
        else if (_velocity.y > 0 && !_jumpButtonHeld)
        {
            // 상승 중 + 버튼을 뗐을 때 : 상승 빠르게 끊음
            _velocity.y += Gravity * (_lowJumpMultiplier - 1) * Time.deltaTime;
        }

        // 기본 중력은 기존 Update에서 그대로 적용
    }

    public void Move(Vector3 move)
    {
        move.y = 0;

        if (move.sqrMagnitude > 1f)
        {
            move.Normalize();
        }

        _moveInputThisFrame = true;

        float lerpSpeed = 10.0f;
        Vector3 targetVelocity = move * _speed;

        _velocity.x = Mathf.Lerp(_velocity.x, targetVelocity.x, Time.deltaTime * lerpSpeed);
        _velocity.z = Mathf.Lerp(_velocity.z, targetVelocity.z, Time.deltaTime * lerpSpeed);
    }

    public void Jump(float power)
    {
        if (_characterController.isGrounded)
        {
            _velocity.y = power;
        }
    }

    public void SetJumpButtonHeld(bool held)
    {
        _jumpButtonHeld = held;
    }
}
