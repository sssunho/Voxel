using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine
{
    public class ChunkRendererManager : MonoBehaviour
    {
        readonly Dictionary<Vector3Int, ChunkRenderer> _renderers = new();
        readonly List<ChunkRenderer> _deactivateRenderers = new();
        readonly List<Vector3Int> _dirtyChunks = new();
        readonly Queue<Vector3Int> _rebuildQueue = new();
        readonly HashSet<Vector3Int> _queuedChunks = new();

        [SerializeField] VoxelWorldBehaviour _worldBehaviour;
        [SerializeField] ChunkRenderer _chunkRendererPrefab;
        [SerializeField] int _maxRebuildPerFrame = 4;

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

                EnqueueDirtyChunks();
                ProcessRebuildQueue();
            }
        }

        private void EnqueueDirtyChunks()
        {
            _world.ConsumeDirtyChunks(_dirtyChunks);

            foreach (Vector3Int chunkCoord in _dirtyChunks)
            {
                if (_queuedChunks.Add(chunkCoord))
                {
                    _rebuildQueue.Enqueue(chunkCoord);
                }
            }
        }

        private void ProcessRebuildQueue()
        {
            bool isChange = false;
            int rebuildCount = Mathf.Min(_maxRebuildPerFrame, _rebuildQueue.Count);

            for (int i = 0; i < rebuildCount; i++)
            {
                isChange = true;

                Vector3Int chunkCoord = _rebuildQueue.Dequeue();
                _queuedChunks.Remove(chunkCoord);

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