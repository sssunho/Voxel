using System;
using UnityEngine;
using Unity.Mathematics;
using Unity.Profiling;

namespace VoxelEngine
{
    public class BitGreedyMesher
    {
        ulong[] _axisCol;
        ulong[] _visible;

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
            _axisCol = new ulong[3 * meshInput.PaddedSize * meshInput.PaddedSize];

            int axisStride = meshInput.PaddedSize * meshInput.PaddedSize;
            int stride = meshInput.PaddedSize;

            for (int x = -1; x <= meshInput.Size; x++)
            {
                for (int y = -1; y <= meshInput.Size; y++)
                {
                    for (int z = -1; z <= meshInput.Size; z++)
                    {
                        if (meshInput.GetBlockRaw(x, y, z) != BlockType.Air)
                        {
                            int px = x + 1;
                            int py = y + 1;
                            int pz = z + 1;

                            int xIndex = axisStride * (int)Axis.X + stride * py + pz;
                            int yIndex = axisStride * (int)Axis.Y + stride * pz + px;
                            int zIndex = axisStride * (int)Axis.Z + stride * px + py;

                            _axisCol[xIndex] |= (1UL << px);
                            _axisCol[yIndex] |= (1UL << py);
                            _axisCol[zIndex] |= (1UL << pz);
                        }
                    }
                }
            }
        }

        void CreateVisibleMask(ChunkMeshInput meshInput)
        {
            _visible = new ulong[6 * meshInput.PaddedSize * meshInput.PaddedSize];

            int axisStride = meshInput.PaddedSize * meshInput.PaddedSize;
            int stride = meshInput.PaddedSize;

            for (int axis = 0; axis < 3; axis++)
            {
                for (int u = 0; u < meshInput.PaddedSize; u++)
                {
                    for (int v = 0; v < meshInput.PaddedSize; v++)
                    {
                        int ni = (2 * axis + 0) * axisStride + u * stride + v;
                        int pi = (2 * axis + 1) * axisStride + u * stride + v;
                        ulong col = _axisCol[axis * axisStride + u * stride + v];

                        _visible[pi] = col & ~(col >> 1);
                        _visible[ni] = col & ~(col << 1);
                    }
                }
            }
        }

        void GreedyMeshPlane(ChunkMeshInput meshInput, PlaneDesc desc, MeshBuildData meshBuildData)
        {
            int typeCount = (int)BlockType.Max;
            int[] usedTypes = new int[typeCount];
            bool[] typeUsed = new bool[typeCount];
            ulong[] planeMask = new ulong[typeCount * meshInput.PaddedSize];
            int typeStride = meshInput.PaddedSize;

            int np = desc.IsNegative ? 0 : 1;
            int visibleFaceBase = (2 * (int)desc.Normal + np) * meshInput.PaddedSize * meshInput.PaddedSize;

            for (int n = 1; n <= meshInput.Size; n++)
            {
                int usedTypeCount = 0;

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

                        if (!typeUsed[(int)type])
                        {
                            typeUsed[(int)type] = true;
                            usedTypes[usedTypeCount++] = (int)type;
                        }

                        planeMask[(int)type * typeStride + u] |= 1UL << v;
                    }
                }

                for (int i = 0; i < usedTypeCount; i++)
                {
                    int typeID = usedTypes[i];
                    int typeBase = typeID * typeStride;

                    for (int startU = 1; startU <= meshInput.Size; startU++)
                    {
                        ulong row = planeMask[typeBase + startU];

                        while (row != 0)
                        {
                            int startV = math.tzcnt(row);
                            int sizeV = math.tzcnt(~(row >> startV));

                            ulong quadMask = ((1UL << sizeV) - 1UL) << startV;

                            int sizeU = 1;
                            while (startU + sizeU <= meshInput.Size) // padded 좌표계 이므로 size 도 포함해야함
                            {
                                if ((quadMask & planeMask[typeBase + (startU + sizeU)]) != quadMask)
                                {
                                    break;
                                }

                                sizeU++;
                            }

                            AddQuad(desc, startU, startV, n, sizeU, sizeV, meshBuildData, (BlockType)usedTypes[i]);

                            for (int k = 0; k < sizeU; k++)
                            {
                                planeMask[typeBase + (startU + k)] &= ~quadMask;
                            }

                            row = planeMask[typeBase + startU];
                        }
                    }
                }

                for (int i = 0; i < usedTypeCount; i++)
                {
                    int typeID = usedTypes[i];
                    Array.Clear(planeMask, typeID * typeStride, typeStride);
                    typeUsed[typeID] = false;
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
    }
}
