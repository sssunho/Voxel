using System;
using UnityEngine;
using Unity.Profiling;
using Unity.Collections;

namespace VoxelEngine
{
    public enum Axis : byte
    {
        X = 0,
        Y = 1,
        Z = 2,
    }

    public struct MeshBuildData : IDisposable
    {
        public NativeList<Vector3> Vertices;
        public NativeList<int> Triangles;
        public NativeList<Vector2> UVs;
        public NativeList<Vector2> UV2s;

        public MeshBuildData(int vertexCapacity, int triangleCapacity)
        {
            Vertices = new NativeList<Vector3>(vertexCapacity, Allocator.Persistent);
            Triangles = new NativeList<int>(triangleCapacity, Allocator.Persistent);
            UVs = new NativeList<Vector2>(vertexCapacity, Allocator.Persistent);
            UV2s = new NativeList<Vector2>(vertexCapacity, Allocator.Persistent);
        }

        public void Clear()
        {
            if (Vertices.IsCreated)
            {
                Vertices.Clear();
            }
            
            if (Triangles.IsCreated)
            {
                Triangles.Clear();
            }

            if (UVs.IsCreated)
            {
                UVs.Clear();
            }

            if (UV2s.IsCreated)
            {
                UV2s.Clear();
            }
        }

        public void Dispose()
        {
            if (Vertices.IsCreated)
            {
                Vertices.Dispose();
            }

            if (Triangles.IsCreated)
            {
                Triangles.Dispose();
            }

            if (UVs.IsCreated)
            {
                UVs.Dispose();
            }

            if (UV2s.IsCreated)
            {
                UV2s.Dispose();
            }
        }
    }

    public struct PlaneDesc
    {
        public static readonly PlaneDesc[] BitPlaneDescs =
        {
            new PlaneDesc(Direction.Left,  Axis.Y, Axis.Z, Axis.X, true,  VoxelStatics.ChunkSize),
            new PlaneDesc(Direction.Right, Axis.Y, Axis.Z, Axis.X, false, VoxelStatics.ChunkSize),
            new PlaneDesc(Direction.Down, Axis.Z, Axis.X, Axis.Y, true,  VoxelStatics.ChunkSize),
            new PlaneDesc(Direction.Up,   Axis.Z, Axis.X, Axis.Y, false, VoxelStatics.ChunkSize),
            new PlaneDesc(Direction.Back,    Axis.X, Axis.Y, Axis.Z, true,  VoxelStatics.ChunkSize),
            new PlaneDesc(Direction.Forward, Axis.X, Axis.Y, Axis.Z, false, VoxelStatics.ChunkSize),
        };

        public Direction NormalDirection;
        public Axis Normal;
        public Axis U;
        public Axis V;
        public bool IsNegative;

        public int UStride;
        public int VStride;
        public int NStride;

        public int PaddedUStride;
        public int PaddedVStride;
        public int PaddedNStride;

        public PlaneDesc(Direction normal, Axis u, Axis v, Axis n, bool isNegative, int size)
        {
            NormalDirection = normal;
            U = u;
            V = v;
            Normal = n;
            IsNegative = isNegative;

            UStride = VStride = NStride = default;

            UStride = AxisToStride(u, size);
            VStride = AxisToStride(v, size);
            NStride = AxisToStride(n, size);

            PaddedUStride = AxisToStride(u, size + 2);
            PaddedVStride = AxisToStride(v, size + 2);
            PaddedNStride = AxisToStride(n, size + 2);
        }

        static int AxisToStride(Axis axis, int size)
        {
            switch (axis)
            {
                case Axis.X:
                    return 1;
                case Axis.Y:
                    return size;
                case Axis.Z:
                    return size * size;

                default:
                    throw new NotImplementedException();
            }
        }

        public int ToBlockIndex(Vector3Int uvn)
        {
            return uvn.x * UStride + uvn.y * VStride + uvn.z * NStride;
        }

        public int ToPaddedBlockIndex(Vector3Int uvn)
        {
            return (uvn.x + 1) * PaddedUStride + (uvn.y + 1) * PaddedVStride + (uvn.z + 1) * PaddedNStride;
        }
    }

    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    [RequireComponent(typeof(MeshCollider))]
    public class ChunkRenderer : MonoBehaviour
    {
        [SerializeField] Material _material;

        VoxelWorld _world;
        Vector3Int _chunkCoord;
        MeshFilter _meshFilter;
        MeshRenderer _meshRenderer;
        MeshCollider _meshCollider;
        Mesh _mesh;
        MaterialPropertyBlock _mpb;
        float _fade = 1.0f;

        static readonly int FadePropertyID = Shader.PropertyToID("_Fade");

        static readonly ProfilerMarker RebuildMeshMarker = new("ChunkRenderer.RebuildMesh");
        static readonly ProfilerMarker ApplyMeshMarker = new ProfilerMarker("ChunkRenderer.ApplyMesh");
        static readonly ProfilerMarker BuildMeshMarker = new ProfilerMarker("ChunkRenderer.BuildMesh");
        static readonly ProfilerMarker CreateMeshInputMarker = new ProfilerMarker("ChunkRenderer.CreateMeshInput");

        public void Initialize(VoxelWorld world, Vector3Int chunkCoord)
        {
            _chunkCoord = chunkCoord;
            _world = world;
        }

        public void RebuildMesh(BitGreedyMesher mesher, MeshBuildData buildData)
        {
            using (RebuildMeshMarker.Auto())
            {
                Debug.Assert(_meshFilter != null);

                if (_world == null)
                {
                    return;
                }

                using (ChunkMeshInput input = CreateChunkMeshInput())
                {
                    CreateMeshBuildData(input, mesher, buildData);
                    ApplyMeshData(buildData);
                }

                // 아래 로그는 가비지가 꽤 많이 생긴다. 필요할 때만 풀자
                //Debug.Log($"Rebuild chunk mesh : {gameObject.name}\n vertices : {_mesh.vertices.Length} \n triangles : {_mesh.triangles.Length}");
            }
        }

        public void ClearMesh()
        {
            if (_mesh)
            {
                if (_meshCollider)
                {
                    _meshCollider.sharedMesh = null;
                }

                _meshFilter.sharedMesh = null;
                _mesh.Clear();
            }
        }

        public void SetFade(float fade)
        {
            if (fade == _fade)
            {
                return;
            }

            _fade = fade;
            _mpb ??= new MaterialPropertyBlock();

            if (_meshRenderer)
            {
                _meshRenderer.GetPropertyBlock(_mpb);
                _mpb.SetFloat(FadePropertyID, fade);
                _meshRenderer.SetPropertyBlock(_mpb);
            }
        }

        public void SetVisible(bool visible)
        {
            if (_meshRenderer)
            {
                _meshRenderer.enabled = visible;
            }

            if (_meshCollider)
            {
                _meshCollider.enabled = visible;
            }
        }

        void Awake()
        {
            _meshFilter = GetComponent<MeshFilter>();
            _meshRenderer = GetComponent<MeshRenderer>();
            _meshCollider = GetComponent<MeshCollider>();

            if (_meshRenderer)
            {
                _meshRenderer.sharedMaterial = _material;
            }

            if (_meshCollider)
            {
                _meshCollider.cookingOptions = MeshColliderCookingOptions.None;
            }
        }

        void OnDestroy()
        {
            if (_mesh)
            {
                Destroy(_mesh);
                _mesh = null;
            }
        }

        void ApplyMeshData(MeshBuildData meshBuildData)
        {
            using (ApplyMeshMarker.Auto())
            {
                ClearMesh();

                if (_mesh == null)
                {
                    _mesh = new Mesh();
                }

                _mesh.SetVertices(meshBuildData.Vertices.AsArray());
                _mesh.SetIndices(meshBuildData.Triangles.AsArray(), MeshTopology.Triangles, 0);
                _mesh.SetUVs(0, meshBuildData.UVs.AsArray());
                _mesh.SetUVs(1, meshBuildData.UV2s.AsArray());
                _mesh.RecalculateNormals();

                if (_mesh)
                {
                    _mesh.name = $"ChunkMesh_{gameObject.name}";
                }

                if (_mesh.vertexCount > 0 && _mesh.GetIndexCount(0) > 0)
                {
                    if (_meshFilter)
                    {
                        _meshFilter.sharedMesh = _mesh;
                    }

                    if (_meshCollider)
                    {
                        _meshCollider.sharedMesh = _mesh;
                    }
                }
            }
        }

        void CreateMeshBuildData(ChunkMeshInput input, BitGreedyMesher mesher, MeshBuildData meshBuildData)
        {
            using (BuildMeshMarker.Auto())
            {
                if (mesher != null)
                {
                    mesher.BuildMesh(input, meshBuildData);
                }
            }
        }

        ChunkMeshInput CreateChunkMeshInput()
        {
            using (CreateMeshInputMarker.Auto())
            {
                ChunkMeshInput input;
                input = _world.CreateChunkMeshInput(_chunkCoord);
                return input;
            }
        }
    }
}