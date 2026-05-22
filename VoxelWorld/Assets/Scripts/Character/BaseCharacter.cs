using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class BaseCharacter : MonoBehaviour
{
    public readonly float Gravity = -9.81f;

    CharacterController _characterController;

    void Awake()
    {
        _characterController = GetComponent<CharacterController>();
    }

    void Start()
    {
        
    }

    void Update()
    {
        
    }

    public void Move(Vector3 move)
    {
        _characterController.Move(move);
    }

    public void Jump(float power)
    {

    }
}
