using System.Collections.Generic;
using System.Linq;
using Unity.Profiling;
using UnityEngine;

namespace VoxelEngine
{
    public class ChunkRendererManager : MonoBehaviour
    {
        readonly Dictionary<Vector3Int, ChunkRenderer> _renderers = new();
        readonly List<ChunkRenderer> _deactivateRenderers = new();
        readonly HashSet<Vector3Int> _dirtyChunks = new();

        readonly List<Vector3Int> _rebuildQueue = new();
        readonly HashSet<Vector3Int> _queuedChunks = new();

        readonly HashSet<Vector3Int> _loadedChunks = new();

        [SerializeField] VoxelWorldBehaviour _worldBehaviour;
        [SerializeField] ChunkRenderer _chunkRendererPrefab;
        [SerializeField] int _maxRebuildPerFrame = 4;

        VoxelWorld _world;
        BitGreedyMesher _mesher;

        public void OnChunkLoaded(HashSet<Vector3Int> chunks)
        {
            foreach (Vector3Int chunkCoord in chunks)
            {
                _loadedChunks.Add(chunkCoord);
                if (_queuedChunks.Add(chunkCoord))
                {
                    _rebuildQueue.Add(chunkCoord);
                }
            }
        }

        public void OnChunkUnloaded(HashSet<Vector3Int> chunks)
        {
            foreach (Vector3Int chunkCoord in chunks)
            {
                _loadedChunks.Remove(chunkCoord);
                _queuedChunks.Remove(chunkCoord);
                _rebuildQueue.Remove(chunkCoord);

                if (_renderers.TryGetValue(chunkCoord, out ChunkRenderer renderer))
                {
                    PushRenderer(renderer);
                    _renderers.Remove(chunkCoord);
                }
            }
        }

        public void SetChunkFade(Vector3Int chunkCoord, float fade)
        {
            if (_renderers.TryGetValue(chunkCoord, out ChunkRenderer renderer))
            {
                renderer.SetFade(fade);
            }
        }

        void Awake()
        {
            _mesher = new();
        }

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

        ProfilerMarker a = new ProfilerMarker("crm.a");
        ProfilerMarker b = new ProfilerMarker("crm.b");

        void LateUpdate()
        {
            if (_world != null)
            {
                using (a.Auto())
                {
                    EnqueueDirtyChunks();
                }
                using (b.Auto())
                {
                    ProcessRebuildQueue();
                }
            }
        }

        void EnqueueDirtyChunks()
        {
            _dirtyChunks.Clear();
            _world.GetDirtyChunks(_dirtyChunks);

            foreach (Vector3Int chunkCoord in _loadedChunks)
            {
                if (_dirtyChunks.Contains(chunkCoord))
                {
                    if (_queuedChunks.Add(chunkCoord))
                    {
                        _rebuildQueue.Add(chunkCoord);
                    }

                    _world.UnsetChunkDirty(chunkCoord);
                }
            }
        }

        void ProcessRebuildQueue()
        {
            int rebuildCount = Mathf.Min(_maxRebuildPerFrame, _rebuildQueue.Count);

            for (int i = 0; i < rebuildCount; i++)
            {
                Vector3Int chunkCoord = _rebuildQueue[0];
                _rebuildQueue.RemoveAt(0);
                _queuedChunks.Remove(chunkCoord);

                if (_renderers.TryGetValue(chunkCoord, out ChunkRenderer renderer))
                {
                    renderer.RebuildMesh(_mesher);
                    continue;
                }

                ChunkRenderer newRenderer = GetNewRenderer();
                newRenderer.transform.position = VoxelWorld.ChunkToWorldOrigin(chunkCoord);
                newRenderer.name = $"Chunk({chunkCoord})";
                newRenderer.Initialize(_world, chunkCoord);
                newRenderer.RebuildMesh(_mesher);

                _renderers.Add(chunkCoord, newRenderer);
            }
        }

        ChunkRenderer GetNewRenderer()
        {
            Debug.Assert(_chunkRendererPrefab != null, $"chunk renderer manager don't have renderer prefab");

            if (_deactivateRenderers.Count == 0)
            {
                ChunkRenderer newRenderer = Instantiate(_chunkRendererPrefab, transform);
                return newRenderer;
            }

            ChunkRenderer renderer = _deactivateRenderers[_deactivateRenderers.Count - 1];
            _deactivateRenderers.RemoveAt(_deactivateRenderers.Count - 1);
            renderer.SetVisible(true);
            return renderer;
        }

        void PushRenderer(ChunkRenderer renderer)
        {
            if (renderer)
            {
                _deactivateRenderers.Add(renderer);
                renderer.SetVisible(false);
                renderer.gameObject.name = "deactivated";
                renderer.ClearMesh();
            }
        }

        void OnDestroy()
        {
            if (_mesher != null)
            {
                _mesher.Dispose();
                _mesher = null;
            }
        }
    }

}