using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.VisualScripting;
using UnityEngine;

namespace VoxelEngine
{
    [BurstCompile]
    public struct CreateChunkMeshInputJob : IJobFor
    {
        [ReadOnly] public NativeArray<Voxel> Source;
        [ReadOnly] public NativeArray<Voxel> NeighborPX;
        [ReadOnly] public NativeArray<Voxel> NeighborNX;
        [ReadOnly] public NativeArray<Voxel> NeighborPY;
        [ReadOnly] public NativeArray<Voxel> NeighborNY;
        [ReadOnly] public NativeArray<Voxel> NeighborPZ;
        [ReadOnly] public NativeArray<Voxel> NeighborNZ;
        
        public NativeArray<BlockType> Output;

        public bool ExistNeighborNX;
        public bool ExistNeighborPX;
        public bool ExistNeighborNY;
        public bool ExistNeighborPY;
        public bool ExistNeighborNZ;
        public bool ExistNeighborPZ;

        public int Size;
        public int PaddedSize;

        public void Execute(int index)
        {
            int px = index % PaddedSize;
            int py = (index / PaddedSize) % PaddedSize;
            int pz = index / (PaddedSize * PaddedSize);

            int x = px - 1;
            int y = py - 1;
            int z = pz - 1;

            bool neighborNX = x < 0;
            bool neighborPX = x == Size;
            bool neighborNY = y < 0;
            bool neighborPY = y == Size;
            bool neighborNZ = z < 0;
            bool neighborPZ = z == Size;

            int neighborCount = 0;
            if (neighborNX || neighborPX)
            {
                neighborCount++;
            }

            if (neighborNY || neighborPY)
            {
                neighborCount++;
            }

            if (neighborNZ || neighborPZ)
            {
                neighborCount++;
            }

            if (neighborCount == 0)
            {
                Output[index] = Source[x + y * Size + z * Size * Size].Type;
            }
            else if (neighborCount == 1)
            {
                if (neighborNX)
                {
                    if (ExistNeighborNX)
                    {
                        Output[index] = NeighborNX[(Size - 1) + y * Size + z * Size * Size].Type;
                    }
                    else
                    {
                        Output[index] = BlockType.Air;
                    }
                }
                else if (neighborPX)
                {
                    if (ExistNeighborPX)
                    {
                        Output[index] = NeighborPX[0 + y * Size + z * Size * Size].Type;
                    }
                    else
                    {
                        Output[index] = BlockType.Air;
                    }
                }
                else if (neighborNY)
                {
                    if (ExistNeighborNY)
                    {
                        Output[index] = NeighborNY[x + (Size - 1) * Size + z * Size * Size].Type;
                    }
                    else
                    {
                        Output[index] = BlockType.Air;
                    }
                }
                else if (neighborPY)
                {
                    if (ExistNeighborPY)
                    {
                        Output[index] = NeighborPY[x + 0 * Size + z * Size * Size].Type;
                    }
                    else
                    {
                        Output[index] = BlockType.Air;
                    }
                }
                else if (neighborNZ)
                {
                    if (ExistNeighborNZ)
                    {
                        Output[index] = NeighborNZ[x + y * Size + (Size - 1) * Size * Size].Type;
                    }
                    else
                    {
                        Output[index] = BlockType.Air;
                    }
                }
                else if (neighborPZ)
                {
                    if (ExistNeighborPZ)
                    {
                        Output[index] = NeighborPZ[x + y * Size + 0 * Size * Size].Type;
                    }
                    else
                    {
                        Output[index] = BlockType.Air;
                    }
                }
            }
            else
            {
                Output[index] = BlockType.Air;
            }
        }
    }

    public struct ChunkMeshInput : IDisposable
    {
        public NativeArray<BlockType> Blocks;
        public readonly int Size;
        public readonly int PaddedSize;
        public readonly int StrideX;
        public readonly int StrideY;
        public readonly int StrideZ;
        public readonly int PaddedStrideX;
        public readonly int PaddedStrideY;
        public readonly int PaddedStrideZ;

        public ChunkMeshInput(int size)
        {
            Size = size;
            PaddedSize = size + 2;

            StrideX = 1;
            StrideY = size;
            StrideZ = size * size;

            PaddedStrideX = 1;
            PaddedStrideY = PaddedSize;
            PaddedStrideZ = PaddedSize * PaddedSize;

            Blocks = new NativeArray<BlockType>(PaddedSize * PaddedSize * PaddedSize, Allocator.TempJob);
        }

        public void Dispose()
        {
            if (Blocks.IsCreated)
            {
                Blocks.Dispose();
            }
        }
    }

    public class VoxelWorld : IDisposable
    {
        class Chunk : IDisposable
        {
            NativeArray<Voxel> _blocks;

            readonly int _strideY = VoxelStatics.ChunkSize;
            readonly int _strideZ = VoxelStatics.ChunkSize * VoxelStatics.ChunkSize;

            public NativeArray<Voxel> Blocks => _blocks;

            public Chunk()
            {
                _blocks = new NativeArray<Voxel>(VoxelStatics.ChunkSize * VoxelStatics.ChunkSize * VoxelStatics.ChunkSize, Allocator.Persistent);
            }

            public bool IsSolid(Vector3Int pos)
            {
                return IsSolid(pos.x, pos.y, pos.z);
            }

            public bool IsSolid(int x, int y, int z)
            {
                if (x < 0 || y < 0 || z < 0 ||
                    x >= VoxelStatics.ChunkSize || y >= VoxelStatics.ChunkSize || z >= VoxelStatics.ChunkSize)
                {
                    return false;
                }

                return _blocks[x + y * _strideY + z * _strideZ].IsSolid;
            }

            public bool SetBlock(int x, int y, int z, BlockType type)
            {
                if (x < 0 || y < 0 || z < 0 ||
                    x >= VoxelStatics.ChunkSize || y >= VoxelStatics.ChunkSize || z >= VoxelStatics.ChunkSize)
                {
                    return false;
                }
                
                int index = ToFlatIndex(x, y, z);

                if (_blocks[index].Type != type)
                {
                    Voxel block = _blocks[index];
                    block.Type = type;
                    _blocks[index] = block;
                    return true;
                }

                return false;
            }

            public Voxel GetBlock(int x, int y, int z)
            {
                if (x < 0 || y < 0 || z < 0 ||
                    x >= VoxelStatics.ChunkSize || y >= VoxelStatics.ChunkSize || z >= VoxelStatics.ChunkSize)
                {
                    return default;
                }

                int index = ToFlatIndex(x, y, z);
                return _blocks[index];
            }

            public Voxel GetBlockRaw(int x, int y, int z)
            {
                return _blocks[x + y * _strideY + z * _strideZ];
            }

            public Voxel GetBlock(Vector3Int pos)
            {
                return GetBlock(pos.x, pos.y, pos.z);
            }

            public BlockType GetBlockType(int x, int y, int z)
            {
                return GetBlock(x, y, z).Type;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public int ToFlatIndex(int x, int y, int z)
            {
                return x + y * _strideY + z * _strideZ;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public int ToFlatIndex(Vector3Int pos)
            {
                return ToFlatIndex(pos.x, pos.y, pos.z);
            }

            public void ClearBlocks()
            {
                for (int i = 0; i < _blocks.Length; i++)
                {
                    Voxel block = _blocks[i];
                    block.Type = BlockType.Air;
                    _blocks[i] = block;
                }
            }

            public void Dispose()
            {
                if (_blocks.IsCreated)
                {
                    _blocks.Dispose();
                }
            }
        }

        readonly Dictionary<Vector3Int, Chunk> _chunks = new();
        readonly HashSet<Vector3Int> _dirtyChunks = new();

        NativeArray<Voxel> _emptyBlockArray;

        public VoxelWorld()
        {
            _emptyBlockArray = new NativeArray<Voxel>(0, Allocator.Persistent);
        }

        bool TryGetChunk(Vector3Int pos, out Chunk chunk)
        {
            return _chunks.TryGetValue(pos, out chunk);
        }

        Chunk GetOrCreateChunk(Vector3Int pos)
        {
            if (TryGetChunk(pos, out Chunk chunk))
            {
                return chunk;
            }

            chunk = new Chunk();
            _chunks.Add(pos, chunk);
            _dirtyChunks.Add(pos);
            return chunk;
        }

        public void GetDirtyChunks(HashSet<Vector3Int> outChunks)
        {
            if (outChunks != null)
            {
                outChunks.UnionWith(_dirtyChunks);
            }
        }

        public void UnsetChunkDirty(Vector3Int chunkCoord)
        {
            _dirtyChunks.Remove(chunkCoord);
        }

        public bool IsSolid(Vector3Int worldPos)
        {
            return IsSolid(worldPos.x, worldPos.y, worldPos.z);
        }

        public bool IsSolid(int x, int y, int z)
        {
            Vector3Int chunkCoord = WorldToChunkCoord(x, y, z);

            if (TryGetChunk(chunkCoord, out Chunk chunk))
            {
                Vector3Int localPos = WorldToLocalCoord(x, y, z);
                return chunk.IsSolid(localPos);
            }

            return false;
        }

        public void SetBlock(int x, int y, int z, BlockType type)
        {
            Vector3Int chunkPos = WorldToChunkCoord(x, y, z);
            Vector3Int localPos = WorldToLocalCoord(x, y, z);

            Chunk chunk = GetOrCreateChunk(chunkPos);
            bool changed = chunk.SetBlock(localPos.x, localPos.y, localPos.z, type);

            if (changed)
            {
                _dirtyChunks.Add(chunkPos);

                if (localPos.x == 0)
                {
                    SetChunkDirtyIfExist(new Vector3Int(chunkPos.x - 1, chunkPos.y, chunkPos.z));
                }

                if (localPos.x == VoxelStatics.ChunkSize - 1)
                {
                    SetChunkDirtyIfExist(new Vector3Int(chunkPos.x + 1, chunkPos.y, chunkPos.z));
                }

                if (localPos.y == 0)
                {
                    SetChunkDirtyIfExist(new Vector3Int(chunkPos.x, chunkPos.y - 1, chunkPos.z));
                }

                if (localPos.y == VoxelStatics.ChunkSize - 1)
                {
                    SetChunkDirtyIfExist(new Vector3Int(chunkPos.x, chunkPos.y + 1, chunkPos.z));
                }

                if (localPos.z == 0)
                {
                    SetChunkDirtyIfExist(new Vector3Int(chunkPos.x, chunkPos.y, chunkPos.z - 1));
                }

                if (localPos.z == VoxelStatics.ChunkSize - 1)
                {
                    SetChunkDirtyIfExist(new Vector3Int(chunkPos.x, chunkPos.y, chunkPos.z + 1));
                }
            }
        }

        public Voxel GetBlock(int x, int y, int z)
        {
            Vector3Int chunkPos = WorldToChunkCoord(x, y, z);

            if (TryGetChunk(chunkPos, out Chunk chunk))
            {
                Vector3Int localPos = WorldToLocalCoord(x, y, z);
                return chunk.GetBlock(localPos.x, localPos.y, localPos.z);
            }

            return default;
        }

        public Voxel GetBlock(Vector3Int pos)
        {
            return GetBlock(pos.x, pos.y, pos.z);
        }

        public BlockType GetBlockType(Vector3Int pos)
        {
            return GetBlockType(pos.x, pos.y, pos.z);
        }

        public BlockType GetBlockType(int x, int y, int z)
        {
            return GetBlock(x, y, z).Type;
        }

        void SetChunkDirtyIfExist(Vector3Int chunkPos)
        {
            if (TryGetChunk(chunkPos, out Chunk chunk))
            {
                _dirtyChunks.Add(chunkPos);
            }
        }

        public void SetBlock(Vector3Int pos, BlockType type)
        {
            SetBlock(pos.x, pos.y , pos.z, type);
        }

        public void ClearBlocks()
        {
            foreach (var kv in _chunks)
            {
                if (kv.Value != null)
                {
                    kv.Value.ClearBlocks();
                }
                _dirtyChunks.Add(kv.Key);
            }
        }

        public static Vector3Int WorldToChunkCoord(Vector3 pos)
        {
            Vector3Int voxelPos = WorldToVoxelCoord(pos);
            return WorldToChunkCoord(voxelPos.x, voxelPos.y, voxelPos.z);
        }

        public static Vector3Int WorldToChunkCoord(int x, int y, int z)
        {
            return new Vector3Int(Mathf.FloorToInt((float)x / VoxelStatics.ChunkSize), Mathf.FloorToInt((float)y / VoxelStatics.ChunkSize),  Mathf.FloorToInt((float)z / VoxelStatics.ChunkSize));
        }

        public static Vector3Int WorldToLocalCoord(Vector3 pos)
        {
            Vector3Int voxelPos = WorldToVoxelCoord(pos);
            return WorldToLocalCoord(voxelPos.x, voxelPos.y, voxelPos.z);
        }

        public static Vector3Int WorldToLocalCoord(int x, int y, int z)
        {
            return new Vector3Int(Mod(x , VoxelStatics.ChunkSize), Mod(y, VoxelStatics.ChunkSize), Mod(z, VoxelStatics.ChunkSize));
        }

        public static Vector3 ChunkToWorldOrigin(Vector3Int chunkCoord)
        {
            return new Vector3(chunkCoord.x * VoxelStatics.ChunkSize, chunkCoord.y * VoxelStatics.ChunkSize, chunkCoord.z * VoxelStatics.ChunkSize);
        }

        public static Vector3 ChunkToWorldCenter(Vector3Int chunkCoord)
        {
            return ChunkToWorldOrigin(chunkCoord) + (VoxelStatics.ChunkSize / 2) * Vector3.one;
        }

        public static Vector3Int WorldToVoxelCoord(Vector3 pos)
        {
            return new Vector3Int(Mathf.FloorToInt(pos.x), Mathf.FloorToInt(pos.y), Mathf.FloorToInt(pos.z));
        }

        static int Mod(int value, int size)
        {
            int res = value % size;
            if (res < 0)
            {
                res += size;
            }
            return res;
        }

        public ChunkMeshInput CreateChunkMeshInput(Vector3Int chunkCoord)
        {
            if (TryGetChunk(chunkCoord, out Chunk chunk))
            {
                int chunkSize = VoxelStatics.ChunkSize; 
                ChunkMeshInput input = new(chunkSize);

                bool existNX = TryGetChunk(chunkCoord + Vector3Int.left, out Chunk neighborNX);
                bool existPX = TryGetChunk(chunkCoord + Vector3Int.right, out Chunk neighborPX);
                bool existNY = TryGetChunk(chunkCoord + Vector3Int.down, out Chunk neighborNY);
                bool existPY = TryGetChunk(chunkCoord + Vector3Int.up, out Chunk neighborPY);
                bool existNZ = TryGetChunk(chunkCoord + Vector3Int.back, out Chunk neighborNZ);
                bool existPZ = TryGetChunk(chunkCoord + Vector3Int.forward, out Chunk neighborPZ);

                CreateChunkMeshInputJob job = new CreateChunkMeshInputJob()
                {
                    Output = input.Blocks,
                    Size = input.Size,
                    PaddedSize = input.PaddedSize,

                    Source = chunk.Blocks,

                    NeighborNX = existNX ? neighborNX.Blocks : _emptyBlockArray,
                    NeighborPX = existPX ? neighborPX.Blocks : _emptyBlockArray,
                    NeighborNY = existNY ? neighborNY.Blocks : _emptyBlockArray,
                    NeighborPY = existPY ? neighborPY.Blocks : _emptyBlockArray,
                    NeighborNZ = existNZ ? neighborNZ.Blocks : _emptyBlockArray,
                    NeighborPZ = existPZ ? neighborPZ.Blocks : _emptyBlockArray,

                    ExistNeighborNX = existNX,
                    ExistNeighborPX = existPX,
                    ExistNeighborNY = existNY,
                    ExistNeighborPY = existPY,
                    ExistNeighborNZ = existNZ,
                    ExistNeighborPZ = existPZ,
                };

                int jobBatchSize = 64;
                JobHandle handle = job.ScheduleParallel(input.Blocks.Length, jobBatchSize, default);
                handle.Complete();

                return input;
            }

            return new ChunkMeshInput();
        }

        public void Dispose()
        {
            if (_emptyBlockArray.IsCreated)
            {
                _emptyBlockArray.Dispose();
            }

            foreach (var pv in _chunks)
            {
                pv.Value.Dispose();
            }

            _chunks.Clear();
        }
    }
}