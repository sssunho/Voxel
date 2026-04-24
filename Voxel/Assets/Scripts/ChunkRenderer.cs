using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor.Experimental.GraphView;

#if UNITY_EDITOR
using Unity.Profiling;
#endif

namespace VoxelEngine
{
    public static class MeshBuilder
    {
#if UNITY_EDITOR

        static readonly ProfilerMarker BuildMeshMarker = new("MeshBuilder.BuildMesh");
        static readonly ProfilerMarker GreedyMeshPlaneMarker = new("MeshBuilder.GreedyMeshPlane");
        static readonly ProfilerMarker MeshUploadMarker = new("MeshBuilder.MeshUpload");
        static readonly ProfilerMarker RecalculateMarker = new("MeshBuilder.Recalculate");
        static readonly ProfilerMarker AddQuadMarkder = new("MeshBuilder.AddQuad");

#endif


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

        static bool[] _greedyMeshingMaskBuffer = new bool[VoxelStatics.ChunkSize * VoxelStatics.ChunkSize];

        public static void AddQuad(Axis normal, Axis u, Axis v, bool isNegativeNormal, Vector3 position, int width, int height, List<Vector3> vertices, List<int> triangles, List<Vector2> uvs, List<Vector2> uv2s, BlockType type)
        {
            int startIndex = vertices.Count;

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

                vertices.Add(position + vertex);

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

                uvs.Add(new Vector2(uvU, uvV));

                uv2s.Add(tile);
            }

            for (int i = 0; i < 6; i++)
            {
                triangles.Add(startIndex + FaceTriangles[i]);
            }
        }

        

        public static Mesh BuildMesh(ChunkMeshInput meshInput)
        {
            using (BuildMeshMarker.Auto())
            using (PerformanceMeasure.Measure($"MeshBuilder.BuildMesh.Total"))
            {
                List<Vector3> vertices = new List<Vector3>();
                List<int> triangles = new List<int>();
                List<Vector2> uvs = new List<Vector2>();
                List<Vector2> uv2s = new List<Vector2>();

                foreach (PlaneDesc desc in PlaneDescs)
                {
                    using (GreedyMeshPlaneMarker.Auto())
                    using (PerformanceMeasure.Measure($"MeshBuilder.BuildMesh.GreedyMeshPlane"))
                    {
                        GreedyMeshPlane(meshInput, desc, vertices, triangles, uvs, uv2s);
                    }
                }

                Mesh mesh = new Mesh();

                using (MeshUploadMarker.Auto())
                using (PerformanceMeasure.Measure("MeshBuilder.MeshDataAssign"))
                {
                    mesh.SetVertices(vertices);
                    mesh.SetTriangles(triangles, 0);
                    mesh.uv = uvs.ToArray();
                    mesh.uv2 = uv2s.ToArray();
                }

                using (RecalculateMarker.Auto())
                using (PerformanceMeasure.Measure("MeshBuilder.Recalculate"))
                {
                    mesh.RecalculateNormals();
                    mesh.RecalculateBounds();
                }

                return mesh;
            }
        }

        static void GreedyMeshPlane(ChunkMeshInput meshInput, PlaneDesc desc, List<Vector3> vertices, List<int> triangles, List<Vector2> uvs, List<Vector2> uv2s)
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
                            AddQuad(desc.Normal, desc.U, desc.V, desc.IsNegative, localPos, width, height, vertices, triangles, uvs, uv2s, type);
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
        static readonly ProfilerMarker BuildMeshMarker = new("ChunkRenderer.BuildMesh");
        static readonly ProfilerMarker ApplyMeshMarker = new("ChunkRenderer.ApplyMesh");

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

                if (_world == null)
                {
                    return;
                }

                using (BuildMeshMarker.Auto())
                using (PerformanceMeasure.Measure("Chunk.RebuildMesh.BuildMesh"))
                {
                    _mesh = MeshBuilder.BuildMesh(_world.CreateMeshInput(_chunkCoord));
                }

                using (ApplyMeshMarker.Auto())
                using (PerformanceMeasure.Measure("Chunk.RebuildMesh.ApplyMesh"))
                {
                    _mesh.name = $"ChunkMesh_{gameObject.name}";
                    _meshFilter.sharedMesh = _mesh;
                    _meshCollider.sharedMesh = _mesh;
                }

                // 아래 로그는 가비지가 꽤 많이 생긴다. 필요할 때만 풀자
                //Debug.Log($"Rebuild chunk mesh : {gameObject.name}\n vertices : {_mesh.vertices.Length} \n triangles : {_mesh.triangles.Length}");
            }
        }

    }
}