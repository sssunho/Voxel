using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;
using Unity.Jobs;
using Unity.Collections;
using Unity.Burst;
using static VoxelEngine.MeshBuilder;

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

    public struct PlaneDesc
    {
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

        public PlaneDesc(Axis u, Axis v, Axis n, bool isNegative, int size)
        {
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
        [BurstCompile]
        public struct BuildMaskJob : IJobFor
        {
            [ReadOnly] public NativeArray<BlockType> Blocks;
            public NativeArray<byte> Mask;
            public int Size;

            public const byte Forward = 1 << 0;
            public const byte Backward = 1 << 1;
            public const byte Left = 1 << 2;
            public const byte Right = 1 << 3;
            public const byte Up = 1 << 4;
            public const byte Down = 1 << 5;

            public void Execute(int index)
            {
                int padded = Size + 2;
                int strideY = padded;
                int strideZ = padded * padded;

                int x = index % Size;
                int y = (index / Size) % Size;
                int z = index / (Size * Size);

                int paddedIndex = (x + 1) + (y + 1) * strideY + (z + 1) * strideZ;

                byte mask = 0;

                if (Blocks[paddedIndex] != BlockType.Air)
                {
                    if (Blocks[paddedIndex + 1] == BlockType.Air)
                    {
                        mask |= Right;
                    }

                    if (Blocks[paddedIndex - 1] == BlockType.Air)
                    {
                        mask |= Left;
                    }

                    if (Blocks[paddedIndex + strideY] == BlockType.Air)
                    {
                        mask |= Up;
                    }

                    if (Blocks[paddedIndex - strideY] == BlockType.Air)
                    {
                        mask |= Down;
                    }

                    if (Blocks[paddedIndex + strideZ] == BlockType.Air)
                    {
                        mask |= Forward;
                    }

                    if (Blocks[paddedIndex - strideZ] == BlockType.Air)
                    {
                        mask |= Backward;
                    }
                }

                Mask[index] = mask;
            }
        }

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

        static readonly PlaneDesc[] PlaneDescs =
        {
            new PlaneDesc(Axis.X, Axis.Y, Axis.Z, false, VoxelStatics.ChunkSize),
            new PlaneDesc(Axis.X, Axis.Z, Axis.Y, true, VoxelStatics.ChunkSize),
            new PlaneDesc(Axis.Y, Axis.Z, Axis.X, false, VoxelStatics.ChunkSize),
            new PlaneDesc(Axis.Y, Axis.X, Axis.Z, true, VoxelStatics.ChunkSize),
            new PlaneDesc(Axis.Z, Axis.X, Axis.Y, false, VoxelStatics.ChunkSize),
            new PlaneDesc(Axis.Z, Axis.Y, Axis.X, true, VoxelStatics.ChunkSize),
        };

        public enum Axis : byte
        {
            X = 0,
            Y = 1,
            Z = 2,
        }

        static readonly int[] FaceTriangles = { 0, 1, 2, 0, 2, 3 };

        static readonly int DefaultQuadCapacity = 2048;

        static bool[] _greedyMaskBuffer = new bool[VoxelStatics.ChunkSize * VoxelStatics.ChunkSize];

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
            MeshBuildData meshBuildData = new MeshBuildData(DefaultQuadCapacity * 4, DefaultQuadCapacity * 6);

            int blockCount = meshInput.Size * meshInput.Size * meshInput.Size;
            using NativeArray<byte> mask = new NativeArray<byte>(blockCount, Allocator.TempJob);

            BuildMaskJob job = new BuildMaskJob()
            {
                Blocks = meshInput.Blocks,
                Mask = mask,
                Size = meshInput.Size,
            };
            JobHandle handle = job.ScheduleParallel(blockCount, 64, default);
            handle.Complete();

            foreach (PlaneDesc desc in PlaneDescs)
            {
                GreedyMeshPlane(meshInput, desc, meshBuildData, mask);
            }

            return meshBuildData;
        }

        static void GreedyMeshPlane(ChunkMeshInput meshInput, PlaneDesc desc, MeshBuildData meshBuildData, NativeArray<byte> workingFaceMask)
        {
            Direction normalDirection = GetDirectionFromFaceNormal(desc.Normal, desc.IsNegative);
            int size = meshInput.Size;

            byte maskBit = 0;
            switch (normalDirection)
            {
                case Direction.Forward:
                    maskBit = BuildMaskJob.Forward;
                    break;
                case Direction.Back:
                    maskBit = BuildMaskJob.Backward;
                    break;
                case Direction.Left:
                    maskBit = BuildMaskJob.Left;
                    break;
                case Direction.Right:
                    maskBit = BuildMaskJob.Right;
                    break;
                case Direction.Up:
                    maskBit = BuildMaskJob.Up;
                    break;
                case Direction.Down:
                    maskBit = BuildMaskJob.Down;
                    break;
            }

            for (int n = 0; n < size; n++)
            {
                for (int u = 0; u < size; u++)
                {
                    for (int v = 0; v < size; v++)
                    {
                        Vector3Int uvn = new Vector3Int(u, v, n);
                        int index = desc.ToBlockIndex(uvn);

                        if (!IsVisibleFace(index, workingFaceMask, size, maskBit))
                        {
                            continue;
                        }

                        int paddedIndex = desc.ToPaddedBlockIndex(uvn);
                        BlockType type = meshInput.GetBlock(paddedIndex);
                        int width = 1;
                        while (u + width < size)
                        {
                            Vector3Int nextuvn = new Vector3Int(u + width, v, n);
                            int nextIndex = desc.ToBlockIndex(nextuvn);
                            if (!IsVisibleFace(nextIndex, workingFaceMask, size, maskBit))
                            {
                                break;
                            }

                            int paddedNextIndex = desc.ToPaddedBlockIndex(nextuvn);
                            if (meshInput.GetBlock(paddedNextIndex) != type)
                            {
                                break;
                            }

                            width++;
                        }

                        int height = 1;
                        while (v + height < size)
                        {
                            bool canExpand = true;
                            for (int i = u; i < u + width; i++)
                            {
                                Vector3Int nextuvn = new Vector3Int(i, v + height, n);
                                int nextIndex = desc.ToBlockIndex(nextuvn);
                                int paddedNextIndex = desc.ToPaddedBlockIndex(nextuvn);
                                if (!IsVisibleFace(nextIndex, workingFaceMask, size, maskBit) || meshInput.GetBlock(paddedNextIndex) != type)
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

                        Vector3Int localPos = UVNtoXYZ(uvn, desc.U, desc.V, desc.Normal);
                        AddQuad(desc.Normal, desc.U, desc.V, desc.IsNegative, localPos, width, height, meshBuildData, type);

                        for (int du = u; du < u + width; du++)
                        {
                            for (int dv = v; dv < v + height; dv++)
                            {
                                int dIndex = desc.ToBlockIndex(new Vector3Int(du, dv, n));
                                ClearFaceMaskBit(dIndex, workingFaceMask, size, maskBit);
                            }
                        }

                    }
                }
            }
        }

        static bool IsVisibleFace(int blockIndex, NativeArray<byte> workingFaceMask, int size, int maskBit)
        {
            return (workingFaceMask[blockIndex] & maskBit) != 0;
        }

        static void ClearFaceMaskBit(int blockIndex, NativeArray<byte> workingFaceMask, int size, int maskBit)
        {
            workingFaceMask[blockIndex] &= (byte)~maskBit;
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
            {
                Debug.Assert(_meshFilter != null);

                if (_world == null)
                {
                    return;
                }

                using (ChunkMeshInput input = CreateMeshInput())
                {
                    MeshBuildData meshBuildData = CreateMeshBuildData(input);
                    ApplyMeshData(meshBuildData);
                }

                // 아래 로그는 가비지가 꽤 많이 생긴다. 필요할 때만 풀자
                //Debug.Log($"Rebuild chunk mesh : {gameObject.name}\n vertices : {_mesh.vertices.Length} \n triangles : {_mesh.triangles.Length}");
            }
        }

        private void ApplyMeshData(MeshBuildData meshBuildData)
        {
            DestroyOldMesh();

            _mesh = CreateMesh(meshBuildData);

            if (_mesh)
            {
                _mesh.name = $"ChunkMesh_{gameObject.name}";
            }

            if (_meshFilter)
            {
                _meshFilter.sharedMesh = _mesh;
            }

            if (_meshCollider)
            {
                _meshCollider.sharedMesh = _mesh;
            }
        }

        private static MeshBuildData CreateMeshBuildData(ChunkMeshInput input)
        {
            MeshBuildData meshBuildData;
            meshBuildData = MeshBuilder.BuildMeshData(input);
            return meshBuildData;
        }

        private ChunkMeshInput CreateMeshInput()
        {
            ChunkMeshInput input;
            input = _world.CreateMeshInput(_chunkCoord);

            return input;
        }

        Mesh CreateMesh(MeshBuildData meshBuildData)
        {
            Mesh mesh = new Mesh();

            mesh.SetVertices(meshBuildData.Vertices);
            mesh.SetTriangles(meshBuildData.Triangles, 0);
            mesh.SetUVs(0, meshBuildData.UVs);
            mesh.SetUVs(1, meshBuildData.UV2s);

            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }

        void DestroyOldMesh()
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