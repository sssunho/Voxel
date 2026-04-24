using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VoxelEngine
{
    public struct ChunkMeshInput
    {
        public BlockType[] Blocks;
        public int Size;

        public bool IsInRange(int x, int y, int z)
        {
            return x >= -1 && y >= -1 && z >= -1 &&
                x <= Size && y <= Size && z <= Size;
        }

        public bool IsInRange(Vector3Int pos)
        {
            return IsInRange(pos.x, pos.y, pos.z);
        }

        public int ToFlatIndex(int x, int y, int z)
        {
            if (IsInRange(x, y, z) == false)
            {
                return -1;
            }

            int Padded = Size + 2;

            return (x + 1) + (y + 1) * Padded + (z + 1) * Padded * Padded;
        }

        public int ToFlatIndex(Vector3Int pos)
        {
            return ToFlatIndex(pos.x, pos.y, pos.z);
        }
        public BlockType GetBlock(int x, int y, int z)
        {
            int idx = ToFlatIndex(x, y, z);

            if (idx < 0)
                return BlockType.Air;

            return Blocks[idx];
        }

        public BlockType GetBlock(Vector3Int pos)
        {
            return GetBlock(pos.x, pos.y, pos.z);
        }

        public bool IsSolid(int x, int y, int z)
        {
            return GetBlock(x, y, z) != BlockType.Air;
        }

        public bool IsSolid(Vector3Int pos)
        {
            return IsSolid(pos.x, pos.y, pos.z);
        }
    }

    public class VoxelWorld
    {
        class Chunk
        {
            readonly Voxel[,,] _blocks = new Voxel[VoxelStatics.ChunkSize, VoxelStatics.ChunkSize, VoxelStatics.ChunkSize];

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

                return _blocks[x, y, z].IsSolid;
            }

            public bool SetBlock(int x, int y, int z, BlockType type)
            {
                if (x < 0 || y < 0 || z < 0 ||
                    x >= VoxelStatics.ChunkSize || y >= VoxelStatics.ChunkSize || z >= VoxelStatics.ChunkSize)
                {
                    return false;
                }

                if (_blocks[x, y, z].Type != type)
                {
                    _blocks[x, y, z].Type = type;
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

                return _blocks[x, y, z];
            }

            public Voxel GetBlock(Vector3Int pos)
            {
                return GetBlock(pos.x, pos.y, pos.z);
            }

            public BlockType GetBlockType(int x, int y, int z)
            {
                return GetBlock(x, y, z).Type;
            }

        }

        readonly Dictionary<Vector3Int, Chunk> _chunks = new();
        readonly HashSet<Vector3Int> _dirtyChunks = new();

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

        public void ConsumeDirtyChunks(List<Vector3Int> outList)
        {
            if (outList == null)
            {
                return;
            }

            outList.Clear();
            outList.AddRange(_dirtyChunks);
            _dirtyChunks.Clear();
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
                for (int i = 0; i < VoxelStatics.ChunkSize; i++)
                {
                    for (int j = 0; j < VoxelStatics.ChunkSize; j++)
                    {
                        for (int k = 0; k < VoxelStatics.ChunkSize; k++)
                        {
                            kv.Value.SetBlock(i, j, k, BlockType.Air);
                        }
                    }
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

        public static Vector3 ChunkToWorldOrigin(Vector3Int pos)
        {
            return new Vector3(pos.x * VoxelStatics.ChunkSize, pos.y * VoxelStatics.ChunkSize, pos.z * VoxelStatics.ChunkSize);
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

        public ChunkMeshInput CreateMeshInput(Vector3Int chunkCoord)
        {
            ChunkMeshInput input = new();
            int chunkSize = VoxelStatics.ChunkSize;
            int blockCount = (chunkSize + 2) * (chunkSize + 2) * (chunkSize + 2);

            input.Blocks = new BlockType[blockCount];
            input.Size = chunkSize;

            if (TryGetChunk(chunkCoord, out Chunk chunk))
            {
                for (int x = -1; x <= chunkSize; x++)
                {
                    for (int y = -1; y <= chunkSize; y++)
                    {
                        for (int z = -1; z <= chunkSize; z++)
                        {
                            int index = input.ToFlatIndex(x, y, z);

                            if (x >= 0 && y >= 0 && z >= 0 && x < chunkSize && y < chunkSize && z < chunkSize)
                            {
                                Vector3Int local = new Vector3Int(x, y, z);
                                input.Blocks[index] = chunk.GetBlock(local).Type;
                            }
                            else
                            {
                                Vector3Int neighborCoord = chunkCoord;
                                Vector3Int neighborLocal = new Vector3Int(x, y, z);

                                if (x == -1)
                                {
                                    neighborCoord.x -= 1;
                                    neighborLocal.x = chunkSize - 1;
                                }
                                else if (x == chunkSize)
                                {
                                    neighborCoord.x += 1;
                                    neighborLocal.x = 0;
                                }

                                if (y == -1)
                                {
                                    neighborCoord.y -= 1;
                                    neighborLocal.y = chunkSize - 1;
                                } 
                                else if (y == chunkSize)
                                {
                                    neighborCoord.y += 1;
                                    neighborLocal.y = 0;
                                }

                                if (z == -1)
                                {
                                    neighborCoord.z -= 1;
                                    neighborLocal.z = chunkSize - 1;
                                }
                                else if (z == chunkSize)
                                {
                                    neighborCoord.z += 1;
                                    neighborLocal.z = 0;
                                }

                                if (TryGetChunk(neighborCoord, out Chunk neighbor))
                                {
                                    input.Blocks[index] = neighbor.GetBlock(neighborLocal).Type;
                                }
                                else
                                {
                                    input.Blocks[index] = BlockType.Air;
                                }
                            }
                        }
                    }
                }

            }

            return input;
        }

    }
}