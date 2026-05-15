using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;

namespace VoxelEngine
{
    public enum Axis : byte
    {
        X = 0,
        Y = 1,
        Z = 2,
    }

    public struct MeshBuildData
    {
        public List<Vector3> Vertices;
        public List<int> Triangles;
        public List<Vector2> UVs;
        public List<Vector2> UV2s;

        public MeshBuildData(int vertexCapacity, int triangleCapacity)
        {
            Vertices = new List<Vector3>(vertexCapacity);
            Triangles = new List<int>(triangleCapacity);
            UVs = new List<Vector2>(vertexCapacity);
            UV2s = new List<Vector2>(vertexCapacity);
        }

        public void Clear()
        {
            Vertices.Clear();
            Triangles.Clear();
            UVs.Clear();
            UV2s.Clear();
        }
    }

    public struct PlaneDesc
    {
        public static readonly PlaneDesc[] PlaneDescs =
        {
            new PlaneDesc(Direction.Left, Axis.Z, Axis.Y, Axis.X, true, VoxelStatics.ChunkSize),
            new PlaneDesc(Direction.Right, Axis.Y, Axis.Z, Axis.X, false, VoxelStatics.ChunkSize),
            new PlaneDesc(Direction.Down, Axis.X, Axis.Z, Axis.Y, true, VoxelStatics.ChunkSize),
            new PlaneDesc(Direction.Up, Axis.Z, Axis.X, Axis.Y, false, VoxelStatics.ChunkSize),
            new PlaneDesc(Direction.Back, Axis.Y, Axis.X, Axis.Z, true, VoxelStatics.ChunkSize),
            new PlaneDesc(Direction.Forward, Axis.X, Axis.Y, Axis.Z, false, VoxelStatics.ChunkSize),
        };

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

    public static class MeshBuilder
    {
        static readonly Vector3[,] FaceVertices = new Vector3[6, 4]
        {                   
            // left               
            {
                new Vector3(0, 0, 0),
                new Vector3(0, 0, 1),
                new Vector3(0, 1, 1),
                new Vector3(0, 1, 0),
            },                    
                                  
            // right              
            {
                new Vector3(1, 0, 1),
                new Vector3(1, 0, 0),
                new Vector3(1, 1, 0),
                new Vector3(1, 1, 1),
            },                   
                                  
            // down               
            {
                new Vector3(0, 0, 0),
                new Vector3(1, 0, 0),
                new Vector3(1, 0, 1),
                new Vector3(0, 0, 1),
            },
                               
            // up                 
            {
                new Vector3(0, 1, 1),
                new Vector3(1, 1, 1),
                new Vector3(1, 1, 0),
                new Vector3(0, 1, 0),
            },       

            // back
            {
                new Vector3(1, 0, 0),
                new Vector3(0, 0, 0),
                new Vector3(0, 1, 0),
                new Vector3(1, 1, 0),
            },

            // forward
            {
                new Vector3(0, 0, 1),
                new Vector3(1, 0, 1),
                new Vector3(1, 1, 1),
                new Vector3(0, 1, 1)
            },
        };

        static readonly int[] FaceTriangles = { 0, 1, 2, 0, 2, 3 };

        public static void AddQuad(Axis normal, Axis u, Axis v, bool isNegativeNormal, Vector3 position, int width, int height, MeshBuildData meshBuildData, BlockType type)
        {
            int startIndex = meshBuildData.Vertices.Count;

            Direction normalDirection = GetDirectionFromFaceNormal(normal, isNegativeNormal);
            Vector2 tile = GetBlockTileCoord(type);

            for (int i = 0; i < 4; i++)
            {
                Vector3 vertex = FaceVertices[(int)normalDirection, i];

                if (u == Axis.X)
                {
                    vertex.x *= width;
                }
                else if (u == Axis.Y)
                {
                    vertex.y *= width;
                }
                else
                {
                    vertex.z *= width;
                }

                if (v == Axis.X)
                {
                    vertex.x *= height;
                }
                else if (v == Axis.Y)
                {
                    vertex.y *= height;
                }
                else
                {
                    vertex.z *= height;
                }

                meshBuildData.Vertices.Add(position + vertex);

                float uvU = 0f;
                float uvV = 0f;

                if (u == Axis.X)
                {
                    uvU = vertex.x;
                }
                else if (u == Axis.Y)
                {
                    uvU = vertex.y;
                }
                else
                {
                    uvU = vertex.z;
                }

                if (v == Axis.X)
                {
                    uvV = vertex.x;
                }
                else if (v == Axis.Y)
                {
                    uvV = vertex.y;
                }
                else
                {
                    uvV = vertex.z;
                }

                meshBuildData.UVs.Add(new Vector2(uvU, uvV));

                meshBuildData.UV2s.Add(tile);
            }

            for (int i = 0; i < 6; i++)
            {
                meshBuildData.Triangles.Add(startIndex + FaceTriangles[i]);
            }
        }

        static Direction GetDirectionFromFaceNormal(Axis axis, bool isNegative)
        {
            switch (axis)
            {
                case Axis.X:
                    if (isNegative)
                    {
                        return Direction.Left;
                    }
                    else
                    {
                        return Direction.Right;
                    }

                case Axis.Y:
                    if (isNegative)
                    {
                        return Direction.Down;
                    }
                    else
                    {
                        return Direction.Up;
                    }

                case Axis.Z:
                    if (isNegative)
                    {
                        return Direction.Back;
                    }
                    else
                    {
                        return Direction.Forward;
                    }

                default:
                    throw new ArgumentOutOfRangeException(nameof(axis), axis, null);
            }
        }

        static Vector2 GetBlockTileCoord(BlockType type)
        {
            switch(type)
            {
                case BlockType.Dirt:
                    return new Vector2(0, 0);

                case BlockType.Grass:
                    return new Vector2(1, 0);

                case BlockType.Wood:
                    return new Vector2(2, 0);

                case BlockType.Stone:
                    return new Vector2(3, 0);

                default:
                    return new Vector2(0, 0);
            }
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

        public void RebuildMesh(BitGreedyMesher mesher)
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
                    MeshBuildData meshBuildData = CreateMeshBuildData(input, mesher);
                    ApplyMeshData(meshBuildData);
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

                _mesh.SetVertices(meshBuildData.Vertices);
                _mesh.SetTriangles(meshBuildData.Triangles, 0);
                _mesh.SetUVs(0, meshBuildData.UVs);
                _mesh.SetUVs(1, meshBuildData.UV2s);
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
                        //_meshCollider.sharedMesh = _mesh;
                    }
                }
            }
        }

        MeshBuildData CreateMeshBuildData(ChunkMeshInput input, BitGreedyMesher mesher)
        {
            MeshBuildData meshBuildData = default;
            using (BuildMeshMarker.Auto())
            {
                if (mesher != null)
                {
                    meshBuildData = mesher.BuildMesh(input);
                }
            }
            return meshBuildData;
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