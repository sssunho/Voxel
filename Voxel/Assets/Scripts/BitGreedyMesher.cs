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

        NativeArray<ulong> _axisColX = new NativeArray<ulong>((VoxelStatics.ChunkSize + 2) * (VoxelStatics.ChunkSize + 2), Allocator.Persistent);
        NativeArray<ulong> _axisColY = new NativeArray<ulong>((VoxelStatics.ChunkSize + 2) * (VoxelStatics.ChunkSize + 2), Allocator.Persistent);
        NativeArray<ulong> _axisColZ = new NativeArray<ulong>((VoxelStatics.ChunkSize + 2) * (VoxelStatics.ChunkSize + 2), Allocator.Persistent);

        ulong[] _visible = new ulong[6 * (VoxelStatics.ChunkSize + 2) * (VoxelStatics.ChunkSize + 2)];
        int[] _usedTypes = new int[(int)BlockType.Max];
        bool[] _typeUsed = new bool[(int)BlockType.Max];
        ulong[] _planeMask = new ulong[(int)BlockType.Max * (VoxelStatics.ChunkSize + 2)];

        ProfilerMarker AxisColMarker = new ProfilerMarker("BitGreedyMesher.AxisCol");
        ProfilerMarker MaskMarker = new ProfilerMarker("BitGreedyMesher.BuildMask");
        ProfilerMarker GreedyMarker = new ProfilerMarker("BitGreedyMesher.Greedy");

        public MeshBuildData BuildMesh(ChunkMeshInput meshInput)
        {
            MeshBuildData meshBuildData = new MeshBuildData(2048 * 4, 2048 * 6);

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
                    GreedyMeshPlane(meshInput, PlaneDesc.BitPlaneDescs[i], meshBuildData);
                }
            }

            return meshBuildData;
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

            JobHandle xhandle = xjob.ScheduleParallel(length, 32, default);
            JobHandle yhandle = yjob.ScheduleParallel(length, 32, default);
            JobHandle zhandle = zjob.ScheduleParallel(length, 32, default);

            JobHandle.CombineDependencies(xhandle, yhandle, zhandle).Complete();
        }

        void CreateVisibleMask(ChunkMeshInput meshInput)
        {
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

                for (int u = 0; u < meshInput.PaddedSize; u++)
                {
                    for (int v = 0; v < meshInput.PaddedSize; v++)
                    {
                        int ni = (2 * axis + 0) * axisStride + u * stride + v;
                        int pi = (2 * axis + 1) * axisStride + u * stride + v;
                        ulong col = axisCol[u * stride + v];

                        _visible[pi] = col & ~(col >> 1);
                        _visible[ni] = col & ~(col << 1);
                    }
                }
            }
        }

        ProfilerMarker A = new ProfilerMarker("BitGreedyMesher.A");
        ProfilerMarker B = new ProfilerMarker("BitGreedyMesher.B");

        void GreedyMeshPlane(ChunkMeshInput meshInput, PlaneDesc desc, MeshBuildData meshBuildData)
        {
            int typeStride = meshInput.PaddedSize;
            int np = desc.IsNegative ? 0 : 1;
            int visibleFaceBase = (2 * (int)desc.Normal + np) * meshInput.PaddedSize * meshInput.PaddedSize;

            Array.Clear(_planeMask, 0, _planeMask.Length);

            for (int n = 1; n <= meshInput.Size; n++)
            {
                int usedTypeCount = 0;

                using (A.Auto())
                {
                    for (int u = 1; u <= meshInput.Size; u++)
                    {
                        int visibleIndex = visibleFaceBase + u * meshInput.PaddedSize;

                        for (int v = 1; v <= meshInput.Size; v++)
                        {
                            ulong visibleCol = _visible[visibleIndex + v];

                            if ((visibleCol & (1UL << n)) == 0)
                            {
                                continue;
                            }

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
                }

                using (B.Auto())
                {
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

            MeshBuilder.AddQuad(desc.Normal, desc.U, desc.V, desc.IsNegative, local, sizeU, sizeV, meshBuildData, type);
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
    }
}
