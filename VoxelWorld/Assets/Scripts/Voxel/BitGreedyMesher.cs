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

        [BurstCompile]
        struct CreateVisibleMaskJob : IJob
        {
            public NativeArray<ulong> Visible;
            [ReadOnly] public NativeArray<ulong> AxisColX;
            [ReadOnly] public NativeArray<ulong> AxisColY;
            [ReadOnly] public NativeArray<ulong> AxisColZ;

            public int AxisStride;
            public int Stride;
            public int PaddedSize;

            public void Execute()
            {
                Clear();

                for (int axis = 0; axis < 3; axis++)
                {
                    NativeArray<ulong> axisCol = AxisColX;

                    if (axis == 1)
                    {
                        axisCol = AxisColY;
                    }
                    else if (axis == 2)
                    {
                        axisCol = AxisColZ;
                    }

                    int negativeBase = (2 * axis + 0) * AxisStride;
                    int positiveBase = (2 * axis + 1) * AxisStride;

                    for (int u = 0; u < PaddedSize; u++)
                    {
                        for (int v = 0; v < PaddedSize; v++)
                        {
                            ulong col = axisCol[u * Stride + v];
                            ulong pVisible = col & ~(col >> 1);
                            ulong nVisible = col & ~(col << 1);

                            while (pVisible != 0)
                            {
                                int n = math.tzcnt(pVisible);
                                pVisible &= pVisible - 1;
                                Visible[positiveBase + n * Stride + u] |= 1UL << v;
                            }

                            while (nVisible != 0)
                            {
                                int n = math.tzcnt(nVisible);
                                nVisible &= nVisible - 1;
                                Visible[negativeBase + n * Stride + u] |= 1UL << v;
                            }
                        }
                    }
                }
            }

            void Clear()
            {
                for (int i = 0; i < Visible.Length; i++)
                {
                    Visible[i] = 0;
                }
            }
        }

        [BurstCompile]
        struct GreedyMeshJob : IJob
        {
            [ReadOnly] public NativeArray<BlockType> Blocks;
            [ReadOnly] public NativeArray<ulong> Visible;

            public NativeArray<ulong> PlaneMask;
            public NativeArray<byte> TypeUsed;
            public NativeArray<int> UsedTypes;

            public MeshBuildData MeshBuildData;
            public PlaneDesc Desc;

            public int Size;
            public int PaddedSize;

            public void Execute()
            {
                int typeStride = PaddedSize;
                int np = Desc.IsNegative ? 0 : 1;
                int visibleFaceBase = (2 * (int)Desc.Normal + np) * PaddedSize * PaddedSize;
                ulong validFaceBits = ((1UL << Size) - 1UL) << 1;

                for (int i = 0; i < PlaneMask.Length; i++)
                {
                    PlaneMask[i] = 0;
                }

                for (int n = 1; n <= Size; n++)
                {
                    int usedTypeCount = 0;

                    for (int u = 1; u <= Size; u++)
                    {
                        int visibleIndex = visibleFaceBase + n * PaddedSize + u;
                        ulong visibleRow = Visible[visibleIndex] & validFaceBits;

                        while (visibleRow != 0)
                        {
                            int v = math.tzcnt(visibleRow);
                            visibleRow &= visibleRow - 1;

                            int blockIndex = u * Desc.PaddedUStride + v * Desc.PaddedVStride + n * Desc.PaddedNStride;
                            BlockType type = Blocks[blockIndex];

                            if (type == BlockType.Air)
                            {
                                continue;
                            }

                            if (TypeUsed[(int)type] == 0)
                            {
                                TypeUsed[(int)type] = 1;
                                UsedTypes[usedTypeCount++] = (int)type;
                            }

                            PlaneMask[(int)type * typeStride + u] |= 1UL << v;
                        }
                    }

                    for (int i = 0; i < usedTypeCount; i++)
                    {
                        int typeID = UsedTypes[i];
                        int typeBase = typeID * typeStride;

                        for (int startU = 1; startU <= Size; startU++)
                        {
                            ulong row = PlaneMask[typeBase + startU];

                            while (row != 0)
                            {
                                int startV = math.tzcnt(row);
                                int sizeV = math.tzcnt(~(row >> startV));

                                ulong quadMask = ((1UL << sizeV) - 1UL) << startV;

                                int sizeU = 1;
                                while (startU + sizeU <= Size) // padded 좌표계 이므로 size 도 포함해야함
                                {
                                    if ((quadMask & PlaneMask[typeBase + (startU + sizeU)]) != quadMask)
                                    {
                                        break;
                                    }

                                    sizeU++;
                                }

                                AddQuad(Desc.NormalDirection, startU, startV, n, sizeU, sizeV, (BlockType)UsedTypes[i]);
                                for (int k = 0; k < sizeU; k++)
                                {
                                    PlaneMask[typeBase + (startU + k)] &= ~quadMask;
                                }

                                row = PlaneMask[typeBase + startU];

                            }
                        }
                    }

                    for (int i = 0; i < usedTypeCount; i++)
                    {
                        int typeID = UsedTypes[i];
                        int typeBase = typeID * typeStride;
                        TypeUsed[typeID] = 0;
                        for (int u = 0; u < typeStride; u++)
                        {
                            PlaneMask[typeBase + u] = 0;
                        }
                    }
                }
            }

            void AddQuad(Direction normal, int paddedU, int paddedV, int paddedN, int sizeU, int sizeV, BlockType type)
            {
                Vector3 local = Vector3.zero;

                switch (Desc.U)
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

                switch (Desc.V)
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

                switch (Desc.Normal)
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

                int startIndex = MeshBuildData.Vertices.Length;
                Direction normalDirection = GetDirectionFromFaceNormal(Desc.Normal, Desc.IsNegative);
                Vector2 tile = GetBlockTileCoord(type, normal);

                for (int i = 0; i < 4; i++)
                {
                    Vector3 vertex = FaceVertices[(int)normalDirection * 4 + i];

                    if (Desc.U == Axis.X)
                    {
                        vertex.x *= sizeU;
                    }
                    else if (Desc.U == Axis.Y)
                    {
                        vertex.y *= sizeU;
                    }
                    else
                    {
                        vertex.z *= sizeU;
                    }

                    if (Desc.V == Axis.X)
                    {
                        vertex.x *= sizeV;
                    }
                    else if (Desc.V == Axis.Y)
                    {
                        vertex.y *= sizeV;
                    }
                    else
                    {
                        vertex.z *= sizeV;
                    }

                    MeshBuildData.Vertices.Add(local + vertex);

                    float uvU = 0f;
                    float uvV = 0f;

                    if (Desc.U == Axis.X)
                    {
                        uvU = vertex.x;
                    }
                    else if (Desc.U == Axis.Y)
                    {
                        uvU = vertex.y;
                    }
                    else
                    {
                        uvU = vertex.z;
                    }

                    if (Desc.V == Axis.X)
                    {
                        uvV = vertex.x;
                    }
                    else if (Desc.V == Axis.Y)
                    {
                        uvV = vertex.y;
                    }
                    else
                    {
                        uvV = vertex.z;
                    }

                    if (Desc.Normal == Axis.X)
                    {
                        float tmp = uvU;
                        uvU = uvV;
                        uvV = tmp;
                    }


                    MeshBuildData.UVs.Add(new Vector2(uvU, uvV));
                    MeshBuildData.UV2s.Add(tile);
                }

                for (int i = 0; i < 6; i++)
                {
                    MeshBuildData.Triangles.Add(startIndex + FaceTriangles[i]);
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

            static Vector2 GetBlockTileCoord(BlockType type, Direction normal)
            {
                switch (type)
                {
                    case BlockType.Dirt:
                        return new Vector2(7, 4);

                    case BlockType.Grass:
                        if (normal == Direction.Up)
                        {
                            return new Vector2(6, 8);
                        }
                        else if (normal == Direction.Down)
                        {
                            return new Vector2(7, 4);
                        }
                        else
                        {
                            return new Vector2(7, 5);
                        }

                    case BlockType.Sand:
                        return new Vector2(3, 3);

                    case BlockType.Stone:
                        return new Vector2(3, 0);

                    default:
                        return new Vector2(0, 0);
                }
            }

            static readonly float3[] FaceVertices = new float3[6 * 4]
            {                   
                // left               
                new float3(0, 0, 0),
                new float3(0, 0, 1),
                new float3(0, 1, 1),
                new float3(0, 1, 0),
                                  
                // right             
                new float3(1, 0, 1),
                new float3(1, 0, 0),
                new float3(1, 1, 0),
                new float3(1, 1, 1),
                                  
                // down               
                new float3(0, 0, 0),
                new float3(1, 0, 0),
                new float3(1, 0, 1),
                new float3(0, 0, 1),
                               
                // up    
                new float3(0, 1, 1),
                new float3(1, 1, 1),
                new float3(1, 1, 0),
                new float3(0, 1, 0),

                // back
                new float3(1, 0, 0),
                new float3(0, 0, 0),
                new float3(0, 1, 0),
                new float3(1, 1, 0),

                // forward
                new float3(0, 0, 1),
                new float3(1, 0, 1),
                new float3(1, 1, 1),
                new float3(0, 1, 1),
            };

        }

        static readonly int[] FaceTriangles = { 0, 1, 2, 0, 2, 3 };

        NativeArray<ulong> _axisColX = new NativeArray<ulong>((VoxelStatics.ChunkSize + 2) * (VoxelStatics.ChunkSize + 2), Allocator.Persistent);
        NativeArray<ulong> _axisColY = new NativeArray<ulong>((VoxelStatics.ChunkSize + 2) * (VoxelStatics.ChunkSize + 2), Allocator.Persistent);
        NativeArray<ulong> _axisColZ = new NativeArray<ulong>((VoxelStatics.ChunkSize + 2) * (VoxelStatics.ChunkSize + 2), Allocator.Persistent);

        NativeArray<ulong> _visible = new NativeArray<ulong>(6 * (VoxelStatics.ChunkSize + 2) * (VoxelStatics.ChunkSize + 2), Allocator.Persistent);
        NativeArray<int> _usedTypes = new NativeArray<int>((int)BlockType.Max, Allocator.Persistent);
        NativeArray<byte> _typeUsed = new NativeArray<byte>((int)BlockType.Max, Allocator.Persistent);
        NativeArray<ulong> _planeMask = new NativeArray<ulong>((int)BlockType.Max * (VoxelStatics.ChunkSize + 2), Allocator.Persistent);

        static readonly ProfilerMarker AxisColMarker = new ProfilerMarker("BitGreedyMesher.AxisCol");
        static readonly ProfilerMarker MaskMarker = new ProfilerMarker("BitGreedyMesher.BuildMask");
        static readonly ProfilerMarker GreedyMarker = new ProfilerMarker("BitGreedyMesher.Greedy");

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
            CreateVisibleMaskJob job = new CreateVisibleMaskJob()
            {
                Visible = _visible,
                AxisColX = _axisColX,
                AxisColY = _axisColY,
                AxisColZ = _axisColZ,
                Stride = meshInput.PaddedSize,
                AxisStride = meshInput.PaddedSize * meshInput.PaddedSize,
                PaddedSize = meshInput.PaddedSize,
            };

            JobHandle h = job.Schedule();
            h.Complete();
        }

        void GreedyMeshPlane(ChunkMeshInput meshInput, PlaneDesc desc, MeshBuildData meshBuildData)
        {
            GreedyMeshJob job = new GreedyMeshJob()
            {
                Blocks = meshInput.Blocks,
                Desc = desc,
                MeshBuildData = meshBuildData,
                PaddedSize = meshInput.PaddedSize,
                Size = meshInput.Size,
                PlaneMask = _planeMask,
                TypeUsed = _typeUsed,
                UsedTypes = _usedTypes,
                Visible = _visible,
            };

            JobHandle h = job.Schedule();
            h.Complete();
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

            if (_visible.IsCreated)
            {
                _visible.Dispose();
            }

            if (_typeUsed.IsCreated)
            {
                _typeUsed.Dispose();
            }

            if (_usedTypes.IsCreated)
            {
                _usedTypes.Dispose();
            }

            if (_planeMask.IsCreated)
            {
                _planeMask.Dispose();
            }
        }
    }
}
