using System;
using System.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace VoxelEngine
{
    public enum TestMapType
    {
        Floor,
        Cube,
        PerlinNoise,
    }

    public enum TestMapSize
    {
        Size16 = 16,
        Size32 = 32,
        Size64 = 64,
        Size128 = 128,
        Size256 = 256,
        Size512 = 512,
    }

    public class MapGenerator : MonoBehaviour
    {
        [SerializeField] TestMapType _mapType;

        [SerializeField] TestMapSize _mapSize;

        [SerializeField] VoxelWorldBehaviour _worldBehaviour;

        [SerializeField, Range(1, 128)] int _perlinNoiseHeight = 64;

        [SerializeField] float _perlinNoiseScale = 0.01f;

        VoxelWorld _world;

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

            StartCoroutine(StartDelayCoroutine());
        }

        IEnumerator StartDelayCoroutine()
        {
            // unity profiler에 첫 프레임이 안잡히는 버그가 있다. 그냥 적당히 기다려 줘서 profiler에 잡히게 해주자
            int delayFrameCount = 5;
            while (delayFrameCount > 0)
            {
                yield return null;
                delayFrameCount--;
            }

            GenerateMap();
        }

        void GenerateMap()
        {
            if (_world == null)
            {
                return;
            }

            _world.ClearBlocks();

            switch (_mapType)
            {
                case TestMapType.Floor:
                    GenerateFloor();
                    break;
                case TestMapType.Cube:
                    GenerateCube();
                    break;
                case TestMapType.PerlinNoise:
                    GeneratePerlinNoiseMap();
                    break;
                default:
                    break;
            }
        }

        void GenerateFloor()
        {
            if (_world == null)
            {
                return;
            }

            int size = (int)_mapSize / 2;

            for (int i = -size; i < size; i++)
            {
                for (int j = -size; j < size; j++)
                {
                    _world.SetBlock(new Vector3Int(i, 0, j), BlockType.Dirt);
                }
            }

        }

        void GenerateCube()
        {
            if (_world == null)
            {
                return;
            }

            int size = (int)_mapSize / 2;
            for (int i = -size; i < size; i++)
            {
                for (int j = -size; j < size; j++)
                {
                    for (int k = -size; k < size; k++)
                    {
                        _world.SetBlock(new Vector3Int(i, j, k), BlockType.Dirt);
                    }
                }
            }

        }

        void GeneratePerlinNoiseMap()
        {
            if (_world == null)
            {
                return;
            }

            int size = (int)_mapSize / 2;
            for (int x = -size; x < size; x++)
            {
                for (int z = -size; z < size; z++)
                {
                    int surfaceHeight = (int)(_perlinNoiseHeight *
                        Mathf.PerlinNoise(x * _perlinNoiseScale, z * _perlinNoiseScale));

                    for (int y = 0; y < surfaceHeight; y++)
                    {
                        float cave = noise.snoise(new float3(
                            x * 0.04f,
                            y * 0.04f,
                            z * 0.04f));

                        if (cave > 0.3f) continue; 

                        BlockType type;
                        if (y == surfaceHeight - 1) type = BlockType.Grass;
                        else if (y >= surfaceHeight - 4) type = BlockType.Dirt;
                        else type = BlockType.Stone;

                        _world.SetBlock(x, y, z, type);
                    }
                }
            }
        }
    }

}
