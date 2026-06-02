using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace  VoxelEngine
{
    public class ChunkStreamingManager : MonoBehaviour
    {
        public static readonly int _minLoadDistance = 3;

        [SerializeField] ChunkRendererManager _crm;
        [SerializeField] Transform _target;
        [SerializeField] int _loadDistance; // chunk unit
        [SerializeField] int _visibleDistance; // chunk unit

        public int LoadDistance
        {
            get => _loadDistance;
            set
            {
                if (value < _minLoadDistance)
                {
                    _loadDistance = _minLoadDistance;
                }
                else
                {
                    _loadDistance = value;
                }
            }
        }

        public int VisibleDistance
        {
            get => _visibleDistance;
            set
            {
                if (value < _loadDistance)
                {
                    _visibleDistance = _loadDistance;
                }
                else
                {
                    _visibleDistance = value;
                }
            }
        }

        HashSet<Vector3Int> _loadedChunks = new HashSet<Vector3Int>();
        HashSet<Vector3Int> _desiredChunks = new HashSet<Vector3Int>();
        HashSet<Vector3Int> _newLoaded = new HashSet<Vector3Int>();
        HashSet<Vector3Int> _newUnloaded = new HashSet<Vector3Int>();
        HashSet<Vector3Int> _fading = new HashSet<Vector3Int>();

        Vector3Int _playerChunkPos;

        static readonly ProfilerMarker CheckRangeMarker = new ProfilerMarker("ChunkStreamingManager.CheckRange");
        static readonly ProfilerMarker UpdateFadeMarker = new ProfilerMarker("ChunkStreamingManager.UpdateFade");

        void Start()
        {
            Shader.SetGlobalFloat("_FadeStartDistance", _visibleDistance * VoxelStatics.ChunkSize);
            Shader.SetGlobalFloat("_FadeEndDistance", _loadDistance * VoxelStatics.ChunkSize);
        }

        void Update()
        {
            if (_target == null || _crm == null)
            {
                return;
            }

            Shader.SetGlobalVector("_PlayerPosition", _target.position);

            Vector3Int currentPlayerChunkPos = VoxelWorld.WorldToChunkCoord(_target.position);

            using (CheckRangeMarker.Auto())
            {
                if (currentPlayerChunkPos != _playerChunkPos)
                {
                    _playerChunkPos = currentPlayerChunkPos;

                    UpdateChunksWithRange(_loadDistance);

                    if (_crm)
                    {
                        _crm.OnChunkLoaded(_newLoaded);
                        _crm.OnChunkUnloaded(_newUnloaded);
                    }

                    _newLoaded.Clear();
                    _newUnloaded.Clear();
                }
            }
        }

        void UpdateChunksWithRange(int range)
        {
            _desiredChunks.Clear();

            for (int x = -range; x <= range; x++)
            {
                for (int y = -range; y <= range; y++)
                {
                    for (int z = -range; z <= range; z++)
                    {
                        Vector3Int chunkCoord = _playerChunkPos + new Vector3Int(x, y, z);
                        _desiredChunks.Add(chunkCoord);
                    }
                }
            }
            
            foreach (var chunkCood in _loadedChunks)
            {
                if (!_desiredChunks.Contains(chunkCood))
                {
                    _newUnloaded.Add(chunkCood);
                    _fading.Remove(chunkCood);
                }
            }

            foreach (var chunkCood in _desiredChunks)
            {
                if (!_loadedChunks.Contains(chunkCood))
                {
                    _newLoaded.Add(chunkCood);
                    _fading.Add(chunkCood);
                }
            }

            foreach (var chunkCoord in _newLoaded)
            {
                if (_loadedChunks.Contains(chunkCoord) == false)
                {
                    _loadedChunks.Add(chunkCoord);
                }
            }

            foreach (var chunkCoord in _newUnloaded)
            {
                _loadedChunks.Remove(chunkCoord);
            }
        }

        void UpdateFade()
        {
            if (_crm == null)
            {
                return;
            }

            Vector3 playerPos = _target.position;
            foreach (var chunkCood in _loadedChunks)
            {
                float dist = Vector3.Magnitude(playerPos - VoxelWorld.ChunkToWorldCenter(chunkCood));
                float fade = 1.0f - (dist - VoxelStatics.ChunkSize * _visibleDistance) / (VoxelStatics.ChunkSize * (_loadDistance - _visibleDistance));
                fade = Mathf.Clamp01(fade);
                _crm.SetChunkFade(chunkCood, fade);
            }
        }
    }
}