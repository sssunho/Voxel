using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VoxelEngine;

public enum TestMapType
{
    Floor,
    Cube,
    PerlinNoise,
}

public enum TestMapSize
{
    Size64 = 64,
    Size128 = 128,
    Size256 = 256,
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
        yield return null;
        yield return null;
        yield return null;
        yield return null;
        yield return null;

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
                float height = _perlinNoiseHeight * Mathf.PerlinNoise(x * _perlinNoiseScale, z * _perlinNoiseScale);
                for (int y = 0; y < height; y++)
                {
                    _world.SetBlock(x, y, z, BlockType.Dirt);
                }
            }
        }

    }
}
