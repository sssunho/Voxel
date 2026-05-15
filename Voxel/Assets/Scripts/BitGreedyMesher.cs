using System;
using UnityEngine;
using Unity.Mathematics;
using Unity.Profiling;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace VoxelEngine
{
    public class BitGreedyMesher : IDisposable
    {
        [BurstCompile]
        struct CreateAxisColJob : IJobFor
        {
            [ReadOnly] public NativeArray<BlockType> Blocks;
            public NativeArray<ulong> AxisCol;
            
            public int PaddedSize;
            public int StrideU;
            public int StrideV;
            public int StrideN;

            public void Execute(int index)
            {
                int u = index / PaddedSize;
                int v = index - u * PaddedSize;
                ulong col = 0;

                for (int n = 0; n < PaddedSize; n++)
                {
                    if (Blocks[StrideU * u + StrideV * v + StrideN * n] != BlockType.Air)
                    {
                        col |= 1UL << n;
                    }
                }

                AxisCol[index] = col;
            }
        }

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

        NativeArray<ulong> _axisColX = new NativeArray<ulong>((VoxelStatics.ChunkSize + 2) * (VoxelStatics.ChunkSize + 2), Allocator.Persistent);
        NativeArray<ulong> _axisColY = new NativeArray<ulong>((VoxelStatics.ChunkSize + 2) * (VoxelStatics.ChunkSize + 2), Allocator.Persistent);
        NativeArray<ulong> _axisColZ = new NativeArray<ulong>((VoxelStatics.ChunkSize + 2) * (VoxelStatics.ChunkSize + 2), Allocator.Persistent);

        ulong[] _visible = new ulong[6 * (VoxelStatics.ChunkSize + 2) * (VoxelStatics.ChunkSize + 2)];
        int[] _usedTypes = new int[(int)BlockType.Max];
        bool[] _typeUsed = new bool[(int)BlockType.Max];
        ulong[] _planeMask = new ulong[(int)BlockType.Max * (VoxelStatics.ChunkSize + 2)];

        static ProfilerMarker AxisColMarker = new ProfilerMarker("BitGreedyMesher.AxisCol");
        static ProfilerMarker MaskMarker = new ProfilerMarker("BitGreedyMesher.BuildMask");
        static ProfilerMarker GreedyMarker = new ProfilerMarker("BitGreedyMesher.Greedy");
        static ProfilerMarker AllocMarker = new ProfilerMarker("BitGreedyMesher.Alloc");

        public void BuildMesh(ChunkMeshInput meshInput, MeshBuildData buildData)
        {
            buildData.Clear();

            if (meshInput.Blocks.IsCreated == false)
            {
                return;
            }

            using (AxisColMarker.Auto())
            {
                CreateAxisCol(meshInput);
            }

            using (MaskMarker.Auto())
            {
                CreateVisibleMask(meshInput);
            }

            using (GreedyMarker.Auto())
            {
                for (int i = 0; i < PlaneDesc.BitPlaneDescs.Length; i++)
                {
                    GreedyMeshPlane(meshInput, PlaneDesc.BitPlaneDescs[i], buildData);
                }
            }
        }

        void CreateAxisCol(ChunkMeshInput meshInput)
        {
            int length = meshInput.PaddedSize * meshInput.PaddedSize;

            CreateAxisColJob xjob = new CreateAxisColJob()
            {
                AxisCol = _axisColX,
                Blocks = meshInput.Blocks,
                PaddedSize = meshInput.PaddedSize,
                StrideU = meshInput.PaddedSize,
                StrideV = length,
                StrideN = 1,
            };

            CreateAxisColJob yjob = new CreateAxisColJob()
            {
                AxisCol = _axisColY,
                Blocks = meshInput.Blocks,
                PaddedSize = meshInput.PaddedSize,
                StrideU = meshInput.PaddedSize * meshInput.PaddedSize,
                StrideV = 1,
                StrideN = meshInput.PaddedSize
            };

            CreateAxisColJob zjob = new CreateAxisColJob()
            {
                AxisCol = _axisColZ,
                Blocks = meshInput.Blocks,
                PaddedSize = meshInput.PaddedSize,
                StrideU = 1,
                StrideV = meshInput.PaddedSize,
                StrideN = length,
            };

            int batchSize = 32;
            JobHandle xhandle = xjob.ScheduleParallel(length, batchSize, default);
            JobHandle yhandle = yjob.ScheduleParallel(length, batchSize, default);
            JobHandle zhandle = zjob.ScheduleParallel(length, batchSize, default);

            JobHandle.CombineDependencies(xhandle, yhandle, zhandle).Complete();
        }

        void CreateVisibleMask(ChunkMeshInput meshInput)
        {
            Array.Clear(_visible, 0, _visible.Length);

            int axisStride = meshInput.PaddedSize * meshInput.PaddedSize;
            int stride = meshInput.PaddedSize;

            for (int axis = 0; axis < 3; axis++)
            {
                NativeArray<ulong> axisCol = _axisColX;

                if (axis == 1)
                {
                    axisCol = _axisColY;
                }
                else if (axis == 2)
                {
                    axisCol = _axisColZ;
                }

                int negativeBase = (2 * axis + 0) * axisStride;
                int positiveBase = (2 * axis + 1) * axisStride;

                for (int u = 0; u < meshInput.PaddedSize; u++)
                {
                    for (int v = 0; v < meshInput.PaddedSize; v++)
                    {
                        ulong col = axisCol[u * stride + v];
                        ulong pVisible = col & ~(col >> 1);
                        ulong nVisible = col & ~(col << 1);

                        while (pVisible != 0)
                        {
                            int n = math.tzcnt(pVisible);
                            pVisible &= pVisible - 1;
                            _visible[positiveBase + n * stride + u] |= 1UL << v;
                        }

                        while (nVisible != 0)
                        {
                            int n = math.tzcnt(nVisible);
                            nVisible &= nVisible - 1;
                            _visible[negativeBase + n * stride + u] |= 1UL << v;
                        }
                    }
                }
            }
        }

        void GreedyMeshPlane(ChunkMeshInput meshInput, PlaneDesc desc, MeshBuildData meshBuildData)
        {
            int typeStride = meshInput.PaddedSize;
            int np = desc.IsNegative ? 0 : 1;
            int visibleFaceBase = (2 * (int)desc.Normal + np) * meshInput.PaddedSize * meshInput.PaddedSize;
            ulong validFaceBits = ((1UL << meshInput.Size) - 1UL) << 1;

            Array.Clear(_planeMask, 0, _planeMask.Length);

            for (int n = 1; n <= meshInput.Size; n++)
            {
                int usedTypeCount = 0;

                for (int u = 1; u <= meshInput.Size; u++)
                {
                    int visibleIndex = visibleFaceBase + n * meshInput.PaddedSize + u;
                    ulong visibleRow = _visible[visibleIndex] & validFaceBits;

                    while (visibleRow != 0)
                    {
                        int v = math.tzcnt(visibleRow);
                        visibleRow &= visibleRow - 1;

                        int blockIndex = u * desc.PaddedUStride + v * desc.PaddedVStride + n * desc.PaddedNStride;
                        BlockType type = meshInput.Blocks[blockIndex];

                        if (type == BlockType.Air)
                        {
                            continue;
                        }

                        if (!_typeUsed[(int)type])
                        {
                            _typeUsed[(int)type] = true;
                            _usedTypes[usedTypeCount++] = (int)type;
                        }

                        _planeMask[(int)type * typeStride + u] |= 1UL << v;
                    }
                }

                for (int i = 0; i < usedTypeCount; i++)
                {
                    int typeID = _usedTypes[i];
                    int typeBase = typeID * typeStride;

                    for (int startU = 1; startU <= meshInput.Size; startU++)
                    {
                        ulong row = _planeMask[typeBase + startU];

                        while (row != 0)
                        {
                            int startV = math.tzcnt(row);
                            int sizeV = math.tzcnt(~(row >> startV));

                            ulong quadMask = ((1UL << sizeV) - 1UL) << startV;

                            int sizeU = 1;
                            while (startU + sizeU <= meshInput.Size) // padded 좌표계 이므로 size 도 포함해야함
                            {
                                if ((quadMask & _planeMask[typeBase + (startU + sizeU)]) != quadMask)
                                {
                                    break;
                                }

                                sizeU++;
                            }

                            AddQuad(desc, startU, startV, n, sizeU, sizeV, meshBuildData, (BlockType)_usedTypes[i]);
                            for (int k = 0; k < sizeU; k++)
                            {
                                _planeMask[typeBase + (startU + k)] &= ~quadMask;
                            }

                            row = _planeMask[typeBase + startU];

                        }
                    }
                }

                for (int i = 0; i < usedTypeCount; i++)
                {
                    int typeID = _usedTypes[i];
                    Array.Clear(_planeMask, typeID * typeStride, typeStride);
                    _typeUsed[typeID] = false;
                }
            }
        }

        void AddQuad(PlaneDesc desc, int paddedU, int paddedV, int paddedN, int sizeU, int sizeV, MeshBuildData meshBuildData, BlockType type)
        {
            Vector3 local = Vector3.zero;

            switch (desc.U)
            {
                case Axis.X:
                    local.x = paddedU - 1;
                    break;

                case Axis.Y:
                    local.y = paddedU - 1;
                    break;

                case Axis.Z:
                    local.z = paddedU - 1;
                    break;
            }

            switch (desc.V)
            {
                case Axis.X:
                    local.x = paddedV - 1;
                    break;

                case Axis.Y:
                    local.y = paddedV - 1;
                    break;

                case Axis.Z:
                    local.z = paddedV - 1;
                    break;
            }

            switch (desc.Normal)
            {
                case Axis.X:
                    local.x = paddedN - 1;
                    break;

                case Axis.Y:
                    local.y = paddedN - 1;
                    break;

                case Axis.Z:
                    local.z = paddedN - 1;
                    break;
            }

            AddQuad(desc.Normal, desc.U, desc.V, desc.IsNegative, local, sizeU, sizeV, meshBuildData, type);
        }

        public void Dispose()
        {
            if (_axisColX.IsCreated)
            {
                _axisColX.Dispose();
            }

            if (_axisColY.IsCreated)
            {
                _axisColY.Dispose();
            }

            if (_axisColZ.IsCreated)
            {
                _axisColZ.Dispose();
            }
        }

        public static void AddQuad(Axis normal, Axis u, Axis v, bool isNegativeNormal, Vector3 position, int width, int height, MeshBuildData meshBuildData, BlockType type)
        {
            int startIndex = meshBuildData.Vertices.Length;

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
            switch (type)
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
}
