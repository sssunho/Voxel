using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;

namespace VoxelEngine
{
    public class ChunkRendererManager : MonoBehaviour
    {
        readonly Dictionary<Vector3Int, ChunkRenderer> _renderers = new();
        readonly List<ChunkRenderer> _deactivateRenderers = new();

        [SerializeField] VoxelWorldBehaviour _worldBehaviour;

        [SerializeField] ChunkRenderer _chunkRendererPrefab;

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
        }

        void LateUpdate()
        {
            if (_world != null)
            {
                PerformanceMeasure.Clear();

                List<Vector3Int> dirtyChunks = new();
                _world.ConsumeDirtyChunks(dirtyChunks);

                bool isChange = false;

                foreach (Vector3Int chunkCoord in dirtyChunks)
                {
                    isChange = true;

                    if (_renderers.TryGetValue(chunkCoord, out ChunkRenderer renderer))
                    {
                        renderer.RebuildMesh();
                        continue;
                    }

                    ChunkRenderer newRenderer = GetNewRenderer();
                    newRenderer.transform.position = VoxelWorld.ChunkToWorldOrigin(chunkCoord);
                    newRenderer.name = $"Chunk({chunkCoord})";
                    newRenderer.Initialize(_world, chunkCoord);

                    _renderers.Add(chunkCoord, newRenderer);
                }

                if (isChange)
                {
                    PerformanceMeasure.LogSummary();
                }
            }
        }

        ChunkRenderer GetNewRenderer()
        {
            Debug.Assert(_chunkRendererPrefab != null, $"chunk renderer manager don't have renderer prefab");

            ChunkRenderer newRenderer = Instantiate(_chunkRendererPrefab);
            if (newRenderer)
            {
                newRenderer.transform.parent = transform;
            }
            return newRenderer;

            // 풀링을 할거라면 아래 해제
            //if (_deactivateRenderers.Count == 0)
            //{
            //    ChunkRenderer newRenderer = Instantiate(_chunkRendererPrefab);
            //    if (newRenderer)
            //    {
            //        newRenderer.transform.parent = transform;
            //    }
            //    return newRenderer;
            //}

            //ChunkRenderer renderer = _deactivateRenderers[_deactivateRenderers.Count - 1];
            //_deactivateRenderers.RemoveAt(_deactivateRenderers.Count - 1);
            //return renderer;
        }
    }

}