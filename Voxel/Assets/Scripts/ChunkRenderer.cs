using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;

namespace VoxelEngine
{
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
    }

    public static class MeshBuilder
    {
        static readonly ProfilerMarker BuildMeshMarker = new("MeshBuilder.BuildMesh");
        static readonly ProfilerMarker GreedyMeshPlaneMarker = new("MeshBuilder.GreedyMeshPlane");
        static readonly ProfilerMarker AddQuadMarkder = new("MeshBuilder.AddQuad");

        static readonly Vector3Int[] Offsets = new Vector3Int[]
        {
            new Vector3Int(0, 0, 1), // foward
            new Vector3Int(0, 0, -1), // back
            new Vector3Int(-1, 0, 0), // left
            new Vector3Int(1, 0, 0), // right
            new Vector3Int(0, 1, 0), // up
            new Vector3Int(0, -1, 0), // down
        };

        static readonly Vector3[,] FaceVertices = new Vector3[6, 4]
        {
            // forward
            {
                new Vector3(0, 0, 1),
                new Vector3(1, 0, 1),
                new Vector3(1, 1, 1),
                new Vector3(0, 1, 1)
            },

            // back
            {
                new Vector3(1, 0, 0),
                new Vector3(0, 0, 0),
                new Vector3(0, 1, 0),
                new Vector3(1, 1, 0),
            },                    
                                  
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
                                  
            // up                 
            {
                new Vector3(0, 1, 1),
                new Vector3(1, 1, 1),
                new Vector3(1, 1, 0),
                new Vector3(0, 1, 0),
            },                    
                                  
            // down               
            {
                new Vector3(0, 0, 0),
                new Vector3(1, 0, 0),
                new Vector3(1, 0, 1),
                new Vector3(0, 0, 1),
            },

        };

        struct PlaneDesc
        {
            public Axis Normal;
            public Axis U;
            public Axis V;
            public bool IsNegative;

            public PlaneDesc(Axis u, Axis v, Axis n, bool isNegative)
            {
                U = u;
                V = v;
                Normal = n;
                IsNegative = isNegative;
            }
        }

        static readonly PlaneDesc[] PlaneDescs =
        {
            new PlaneDesc(Axis.X, Axis.Y, Axis.Z, false),
            new PlaneDesc(Axis.X, Axis.Z, Axis.Y, true),
            new PlaneDesc(Axis.Y, Axis.Z, Axis.X, false),
            new PlaneDesc(Axis.Y, Axis.X, Axis.Z, true),
            new PlaneDesc(Axis.Z, Axis.X, Axis.Y, false),
            new PlaneDesc(Axis.Z, Axis.Y, Axis.X, true),
        };

        public enum Axis
        {
            X = 0,
            Y = 1,
            Z = 2,
        }

        static readonly int[] FaceTriangles = { 0, 1, 2, 0, 2, 3 };

        static readonly int DefaultQuadCapacity = 2048;

        static bool[] _greedyMeshingMaskBuffer = new bool[VoxelStatics.ChunkSize * VoxelStatics.ChunkSize];

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

        

        public static MeshBuildData BuildMeshData(ChunkMeshInput meshInput)
        {
            using (BuildMeshMarker.Auto())
            using (PerformanceMeasure.Measure($"MeshBuilder.BuildMesh.Total"))
            {
                MeshBuildData meshBuildData = new MeshBuildData(DefaultQuadCapacity * 4, DefaultQuadCapacity * 6);

                foreach (PlaneDesc desc in PlaneDescs)
                {
                    using (GreedyMeshPlaneMarker.Auto())
                    using (PerformanceMeasure.Measure($"MeshBuilder.BuildMesh.GreedyMeshPlane"))
                    {
                        GreedyMeshPlane(meshInput, desc, meshBuildData);
                    }
                }

                return meshBuildData;
            }
        }

        static void GreedyMeshPlane(ChunkMeshInput meshInput, PlaneDesc desc, MeshBuildData meshBuildData)
        {
            Direction normalDirection = GetDirectionFromFaceNormal(desc.Normal, desc.IsNegative);
            int size = meshInput.Size;

            for (int i = 0; i < _greedyMeshingMaskBuffer.Length; i++)
            {
                _greedyMeshingMaskBuffer[i] = false;
            }

            for (int n = 0; n < size; n++)
            {
                for (int u = 0; u < size; u++)
                {
                    for (int v = 0; v < size; v++)
                    {
                        Vector3Int localPos = UVNtoXYZ(new Vector3Int(u, v, n), desc.U, desc.V, desc.Normal);

                        if (!meshInput.IsSolid(localPos))
                        {
                            continue;
                        }

                        if (!meshInput.IsSolid(localPos + Offsets[(int)normalDirection]))
                        {
                            _greedyMeshingMaskBuffer[u + v * size] = true;
                        }
                    }
                }

                for (int u = 0; u < size; u++)
                {
                    for (int v = 0; v < size; v++)
                    {
                        if (!_greedyMeshingMaskBuffer[u + v * size])
                        {
                            continue;
                        }

                        Vector3Int uvn = new Vector3Int(u, v, n);
                        Vector3Int localPos = UVNtoXYZ(uvn, desc.U, desc.V, desc.Normal);
                        BlockType type = meshInput.GetBlock(localPos);

                        int width = 1;
                        while (u + width < VoxelStatics.ChunkSize && 
                            _greedyMeshingMaskBuffer[u + width + v * size])
                        {
                            Vector3Int nextuvn = new Vector3Int(u + width, v, n);
                            Vector3Int nextLocalPos = UVNtoXYZ(nextuvn, desc.U, desc.V, desc.Normal);
                            
                            if (meshInput.GetBlock(nextLocalPos) != type)
                            {
                                break;
                            }

                            width++;
                        }

                        int height = 1;
                        while (v + height < VoxelStatics.ChunkSize)
                        {
                            bool canExpand = true;
                            for (int i = u; i < u + width; i++)
                            {
                                Vector3Int nextuvn = new Vector3Int(i, v + height, n);
                                Vector3Int nextLocalPos = UVNtoXYZ(nextuvn, desc.U, desc.V, desc.Normal);
                                if (!_greedyMeshingMaskBuffer[i + (v + height) * VoxelStatics.ChunkSize] || meshInput.GetBlock(nextLocalPos) != type)
                                {
                                    canExpand = false;
                                    break;
                                }
                            }

                            if (canExpand)
                            {
                                height++;
                            }
                            else
                            {
                                break;
                            }
                        }

                        using (AddQuadMarkder.Auto())
                        {
                            AddQuad(desc.Normal, desc.U, desc.V, desc.IsNegative, localPos, width, height, meshBuildData, type);
                        }

                        for (int du = u; du < u + width; du++)
                        {
                            for (int dv = v; dv < v + height; dv++)
                            {
                                _greedyMeshingMaskBuffer[du + dv * VoxelStatics.ChunkSize] = false;
                            }
                        }

                    }
                }
            }
        }

        static Vector3Int UVNtoXYZ(Vector3Int uvn, Axis u, Axis v, Axis n)
        {
            Vector3Int res = new Vector3Int();

            if (u == Axis.X)
            {
                res.x = uvn.x;
            }
            else if (u == Axis.Y)
            {
                res.y = uvn.x;
            }
            else
            {
                res.z = uvn.x;
            }

            if (v == Axis.X)
            {
                res.x = uvn.y;
            }
            else if (v == Axis.Y)
            {
                 res.y = uvn.y;
            }
            else
            {
                res.z = uvn.y;
            }

            if (n == Axis.X)
            {
                res.x = uvn.z;
            }
            else if (n == Axis.Y)
            {
                res.y = uvn.z;
            }
            else
            {
                res.z = uvn.z;
            }

            return res;
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

        static readonly ProfilerMarker RebuildMeshMarker = new("ChunkRenderer.RebuildMesh");
        static readonly ProfilerMarker DestroyOldMeshMarker = new("ChunkRenderer.DestroyOldMesh");
        static readonly ProfilerMarker CreateMeshInputData = new("ChunkRenderer.CreateMeshInput");
        static readonly ProfilerMarker BuildMeshDataMarker = new("ChunkRenderer.BuildMesh");
        static readonly ProfilerMarker CreateUnityMeshMarker = new("ChunkRenderer.CreateUnityMesh");
        static readonly ProfilerMarker ApplyMeshMarker = new("ChunkRenderer.ApplyMesh");
        static readonly ProfilerMarker MeshUploadMarker = new("MeshBuilder.MeshUpload");
        static readonly ProfilerMarker RecalculateMarker = new("MeshBuilder.Recalculate");

        void Awake()
        {
            _meshFilter = GetComponent<MeshFilter>();
            _meshRenderer = GetComponent<MeshRenderer>();
            _meshCollider = GetComponent<MeshCollider>();

            if (_meshRenderer)
            {
                _meshRenderer.sharedMaterial = _material;
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

        public void Initialize(VoxelWorld world, Vector3Int chunkCoord)
        {
            _chunkCoord = chunkCoord;
            _world = world;

            RebuildMesh();
        }

        public void RebuildMesh()
        {
            using (RebuildMeshMarker.Auto())
            using (PerformanceMeasure.Measure("Chunk.RebuildMesh.Total"))
            {
                Debug.Assert(_meshFilter != null);

                if (_world == null)
                {
                    return;
                }

                ChunkMeshInput input = CreateMeshInput();
                MeshBuildData meshBuildData = CreateMeshBuildData(input);
                ApplyMeshData(meshBuildData);

                // 아래 로그는 가비지가 꽤 많이 생긴다. 필요할 때만 풀자
                //Debug.Log($"Rebuild chunk mesh : {gameObject.name}\n vertices : {_mesh.vertices.Length} \n triangles : {_mesh.triangles.Length}");
            }
        }

        private void ApplyMeshData(MeshBuildData meshBuildData)
        {
            DestroyOldMesh();

            using (CreateUnityMeshMarker.Auto())
            using (PerformanceMeasure.Measure("Chunk.RebuildMesh.CreateUnityMesh"))
            {
                _mesh = CreateMesh(meshBuildData);
            }

            using (ApplyMeshMarker.Auto())
            using (PerformanceMeasure.Measure("Chunk.RebuildMesh.ApplyMesh"))
            {
                _mesh.name = $"ChunkMesh_{gameObject.name}";
                _meshFilter.sharedMesh = _mesh;
                _meshCollider.sharedMesh = _mesh;
            }
        }

        private static MeshBuildData CreateMeshBuildData(ChunkMeshInput input)
        {
            MeshBuildData meshBuildData;
            using (BuildMeshDataMarker.Auto())
            using (PerformanceMeasure.Measure("Chunk.RebuildMesh.BuildMeshData"))
            {
                meshBuildData = MeshBuilder.BuildMeshData(input);
            }

            return meshBuildData;
        }

        private ChunkMeshInput CreateMeshInput()
        {
            ChunkMeshInput input;
            using (CreateMeshInputData.Auto())
            using (PerformanceMeasure.Measure("Chunk.RebuildMesh.CreateMeshInput"))
            {
                input = _world.CreateMeshInput(_chunkCoord);
            }

            return input;
        }

        Mesh CreateMesh(MeshBuildData meshBuildData)
        {
            Mesh mesh = new Mesh();

            using (MeshUploadMarker.Auto())
            using (PerformanceMeasure.Measure("MeshBuilder.MeshDataAssign"))
            {
                mesh.SetVertices(meshBuildData.Vertices);
                mesh.SetTriangles(meshBuildData.Triangles, 0);
                mesh.SetUVs(0, meshBuildData.UVs);
                mesh.SetUVs(1, meshBuildData.UV2s);
            }

            using (RecalculateMarker.Auto())
            using (PerformanceMeasure.Measure("MeshBuilder.Recalculate"))
            {
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
            }

            return mesh;
        }

        void DestroyOldMesh()
        {
            using (DestroyOldMeshMarker.Auto())
            using (PerformanceMeasure.Measure("Chunk.RebuildMesh.DestroyOld"))
            {
                if (_mesh)
                {
                    if (_meshCollider)
                    {
                        _meshCollider.sharedMesh = null;
                    }

                    _meshFilter.sharedMesh = null;
                    Destroy(_mesh);
                    _mesh = null;
                }
            }
        }

    }
}