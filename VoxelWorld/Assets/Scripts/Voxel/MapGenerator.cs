using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.LightTransport;

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

    [Serializable]
    public struct PerlinNoiseLayer
    {
        public float Height;     // 진폭 (높이에 미치는 영향)
        public float Scale;      // 주파수 (작을수록 큰 지형)
        [Range(0f, 1f)]
        public float Weight;     // 레이어 가중치
        public Vector2 Offset;   // 노이즈 위치 오프셋 (다양한 시드 효과)
    }
    public class MapGenerator : MonoBehaviour
    {
        [SerializeField] TestMapType _mapType;

        [SerializeField] TestMapSize _mapSize;

        [SerializeField] VoxelWorldBehaviour _worldBehaviour;

        [SerializeField] PerlinNoiseLayer[] _perlinNoises;

        // Inspector에서 각 구간 경계값 조절 가능
        [Header("Radial Falloff - 구간 경계 높이")]
        [SerializeField, Range(0f, 1f)] float _h0 = 1.0f;   // dist=0.0  시작
        [SerializeField, Range(0f, 1f)] float _h1 = 0.5f;   // dist=0.25 (급→완 전환)
        [SerializeField, Range(0f, 1f)] float _h2 = 0.15f;  // dist=0.75 (완→급 전환, 해안선 상단)
        [SerializeField, Range(0f, 1f)] float _h3 = 0.04f;  // dist=0.8  (해안선 하단)
        [SerializeField, Range(0f, 1f)] float _h4 = 0.03f;  // dist=1.0  외곽 평탄

        [Header("Block type")]
        [SerializeField, Range(0, 255)] int _minCoastHeight = 5;

        [Header("Tree Generation")]
        [SerializeField] GameObject _treePrefab;
        [SerializeField] int _treeCount = 150;
        [SerializeField] float _treeMinDist = 0.6f;
        [SerializeField] float _treeMaxDist = 0.8f;

        VoxelWorld _world;

        void Start()
        {
            if (_worldBehaviour == null)
            {
                _worldBehaviour = FindFirstObjectByType<VoxelWorldBehaviour>();
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

            PlaceTrees();
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
            if (_world == null) return;
            int size = (int)_mapSize / 2;

            for (int x = -size; x < size; x++)
            {
                for (int z = -size; z < size; z++)
                {
                    float normalizedDist = Mathf.Sqrt(x * x + z * z) / size;
                    float falloff = GetRadialFalloff(normalizedDist);

                    // FBM: 여러 레이어 합산
                    float totalHeight = SampleFBM(x, z);

                    int surfaceHeight = Mathf.Max(1, (int)(totalHeight * falloff));

                    for (int y = 0; y < surfaceHeight; y++)
                    {
                        float cave = noise.snoise(new float3(
                            x * 0.08f,
                            y * 0.04f,
                            z * 0.08f));

                        if (cave > 0.3f && y > 28) continue;

                        BlockType type;
                        if (surfaceHeight > _minCoastHeight)
                        {
                            if (y == surfaceHeight - 1) type = BlockType.Grass;
                            else if (y >= surfaceHeight - 4) type = BlockType.Dirt;
                            else type = BlockType.Stone;
                        }
                        else
                        {
                            type = BlockType.Sand;
                        }

                        _world.SetBlock(x, y, z, type);
                    }
                }
            }
        }

        float SampleFBM(int x, int z)
        {
            float totalHeight = 0f;
            float totalWeight = 0f;

            foreach (var layer in _perlinNoises)
            {
                float nx = (x + layer.Offset.x) * layer.Scale;
                float nz = (z + layer.Offset.y) * layer.Scale;

                // Perlin noise는 0~1 범위 → -1~1로 변환해 합산
                float sample = Mathf.PerlinNoise(nx, nz);

                totalHeight += sample * layer.Height * layer.Weight;
                totalWeight += layer.Weight;
            }

            // 가중치 정규화
            return totalWeight > 0f ? totalHeight / totalWeight : 0f;
        }

        float GetRadialFalloff(float d)
        {
            if (d <= 0.5f)
            {
                float t = d / 0.5f;
                return Mathf.Lerp(_h0, _h1, Mathf.SmoothStep(0f, 1f, t));
            }
            else if (d <= 0.75f)
            {
                float t = (d - 0.4f) / 0.25f;
                return Mathf.Lerp(_h1, _h2, Mathf.SmoothStep(0f, 1f, t));
            }
            else if (d <= 0.82f)
            {
                float t = (d - 0.75f) / 0.07f;
                return Mathf.Lerp(_h2, _h3, Mathf.SmoothStep(0f, 1f, t));
            }
            else
            {
                float t = (d - 0.82f) / 0.18f;
                return Mathf.Lerp(_h3, _h4, Mathf.SmoothStep(0f, 1f, t));
            }
        }
        void PlaceTrees()
        {
            if (_treePrefab == null || _world == null) return;

            int size = (int)_mapSize / 2;
            int placed = 0;
            int maxAttempts = _treeCount * 10;
            int attempts = 0;
            HashSet<Vector2Int> placedPositions = new HashSet<Vector2Int>();

            while (placed < _treeCount && attempts < maxAttempts)
            {
                attempts++;

                // falloff 범위 내 랜덤 각도 + 거리 샘플링
                float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                float dist = UnityEngine.Random.Range(_treeMinDist, _treeMaxDist);

                int x = Mathf.RoundToInt(Mathf.Cos(angle) * dist * size);
                int z = Mathf.RoundToInt(Mathf.Sin(angle) * dist * size);
                
                if (placedPositions.Contains(new Vector2Int(x, z)))
                {
                    continue;
                }

                placedPositions.Add(new Vector2Int(x, z));

                // 해당 위치 표면 높이 탐색
                int surfaceY = -1;
                for (int y = (int)_mapSize; y >= 0; y--)
                {
                    if (_world.GetBlockType(x, y, z) == BlockType.Grass)
                    {
                        surfaceY = y;
                        break;
                    }
                }

                if (surfaceY < 0) continue;

                // Grass 블록 위에 배치
                Vector3 pos = new Vector3(x + 0.5f, surfaceY + 1, z + 0.5f);
                Instantiate(_treePrefab, pos,
                    Quaternion.Euler(0, UnityEngine.Random.Range(0f, 360f), 0));
                placed++;
            }
        }

    }
}
