using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine
{
    public class BlockInteractionController : MonoBehaviour
    {
        [SerializeField]
        VoxelWorldBehaviour _worldBehaviour;

        [SerializeField]
        float _removeDelay = 0.1f;

        Camera _mainCamera;

        float _removeTime = 0.0f;

        VoxelWorld _world;

        BlockType _blockType = BlockType.Dirt;

        bool _isPlacingBlock = false;
        Vector3Int _initialPlaceNormal;
        Vector3Int _lastPlacePosition;

        void Start()
        {
            if (_worldBehaviour == null)
            {
                _worldBehaviour = FindObjectOfType<VoxelWorldBehaviour>();
            }

            if (_worldBehaviour)
            {
                _world = _worldBehaviour.World;
            }

            _mainCamera = Camera.main;
        }

        void Update()
        {
            if (_removeTime > 0.0f)
            {
                _removeTime -= Time.unscaledDeltaTime;
                if (_removeTime < 0.0f)
                {
                    _removeTime = 0.0f;
                }
            }

            if (Input.GetMouseButtonDown(0))
            {
                OnLeftMouseButtonDown();
            }

            if (Input.GetMouseButton(0))
            {
                OnLeftMouseButton();
            }

            if (Input.GetMouseButtonUp(0))
            {
                OnLeftMouseButtonUp();
            }

            if (Input.GetMouseButtonDown(1))
            {
                OnRightMouseButtonDown();
            }

            if (Input.GetMouseButton(1))
            {
                OnRightMouseButton();
            }

            if (Input.GetMouseButtonUp(1))
            {
                OnRightMouseButtonUp();
            }

            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                ChangeBlockType(BlockType.Dirt);
            }

            if (Input.GetKeyDown(KeyCode.Alpha2))
            {
                ChangeBlockType(BlockType.Wood);
            }

            if (Input.GetKeyDown(KeyCode.Alpha3))
            {
                ChangeBlockType(BlockType.Stone);
            }
        }

        void OnLeftMouseButtonDown()
        {
            if (_world == null)
            {
                return;
            }

            if (TryGetRayHitOnMousePosition(Input.mousePosition, out RaycastHit hit))
            {
                Vector3 modifiedPos = hit.point + 0.001f * hit.normal;
                Vector3Int voxelPos = VoxelWorld.WorldToVoxelCoord(modifiedPos);
                _world.SetBlock(voxelPos, _blockType);

                _isPlacingBlock = true;
                _initialPlaceNormal = Vector3Int.RoundToInt(hit.normal);
                _lastPlacePosition = voxelPos;
            }
        }


        void OnLeftMouseButton()
        {
            if (_world == null)
            {
                return;
            }

            if (_isPlacingBlock == false)
            {
                return;
            }

            if (TryGetRayHitOnMousePosition(Input.mousePosition, out RaycastHit hit))
            {
                if (Vector3Int.RoundToInt(hit.normal) != _initialPlaceNormal)
                {
                    return;
                }

                Vector3 modifiedPos = hit.point + 0.001f * hit.normal;
                Vector3Int voxelPos = VoxelWorld.WorldToVoxelCoord(modifiedPos);

                if (voxelPos == _lastPlacePosition)
                {
                    return;
                }

                Vector3Int next = _lastPlacePosition + _initialPlaceNormal;

                if (voxelPos == next)
                {
                    return;
                }

                _world.SetBlock(voxelPos, _blockType);
                _lastPlacePosition = voxelPos;
            }

        }

        void OnLeftMouseButtonUp()
        {
            _isPlacingBlock = false;
        }

        void OnRightMouseButtonDown()
        {
            RemoveBlockOnMouse(Input.mousePosition);
        }

        void OnRightMouseButton()
        {
            RemoveBlockOnMouse(Input.mousePosition);
        }

        void OnRightMouseButtonUp()
        {

        }

        void ChangeBlockType(BlockType type)
        {
            _blockType = type;
        }


        bool CreateBlockOnMouse(Vector3 mousePos)
        {
            if (_world == null)
            {
                return false;
            }

            if (TryGetRayHitOnMousePosition(mousePos, out RaycastHit hit))
            {
                Vector3 modifiedPos = hit.point + 0.001f * hit.normal;
                _world.SetBlock(VoxelWorld.WorldToVoxelCoord(modifiedPos), _blockType);
                return true;
            }

            return false;
        }

        void RemoveBlockOnMouse(Vector3 mousePos)
        {
            if (_world == null)
            {
                return;
            }

            if (_removeTime > 0)
            {
                return;
            }

            if (TryGetRayHitOnMousePosition(mousePos, out RaycastHit hit))
            {
                Vector3 modifiedPos = hit.point - 0.001f * hit.normal;
                Vector3Int voxelPos = VoxelWorld.WorldToVoxelCoord(modifiedPos);
                if (voxelPos.y == 0)
                {
                    return; // 지면은 못 파괴하도록 막자
                }
                _world.SetBlock(voxelPos, BlockType.Air);
            }

            _removeTime = _removeDelay;
        }

        bool TryGetRayHitOnMousePosition(Vector3 screenPos, out RaycastHit hit)
        {
            hit = new();

            if (_mainCamera == null)
            {
                return false;
            }
            
            Ray ray = _mainCamera.ScreenPointToRay(screenPos);
            if (Physics.Raycast(ray, out hit, 100.0f))
            {
                return true;
            }

            return false;
        }

    }

}