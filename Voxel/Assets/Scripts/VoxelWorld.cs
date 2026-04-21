using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine
{
    public class VoxelWorld
    {
        readonly Dictionary<Vector2Int, Chunk> _chunks = new();

        public IReadOnlyDictionary<Vector2Int, Chunk> Chunks => _chunks;

        public bool TryGetChunk(Vector2Int pos, out Chunk chunk)
        {
            return _chunks.TryGetValue(pos, out chunk);
        }

        public Chunk GetOrCreateChunk(Vector2Int pos)
        {
            if (TryGetChunk(pos, out Chunk chunk))
            {
                return chunk;
            }

            chunk = new Chunk();
            _chunks.Add(pos, chunk);
            return chunk;
        }

        public bool IsSolid(Vector3Int worldPos)
        {
            return IsSolid(worldPos.x, worldPos.y, worldPos.z);
        }

        public bool IsSolid(int x, int y, int z)
        {
            Vector2Int chunkCoord = WorldToChunkCoord(x, z);

            if (TryGetChunk(chunkCoord, out Chunk chunk))
            {
                Vector3Int localPos = WorldToLocalCoord(x, y, z);
                return chunk.IsSolid(localPos);
            }

            return false;
        }

        public void SetBlock(int x, int y, int z, BlockType type)
        {
            Vector2Int chunkPos = WorldToChunkCoord(x, z);
            Vector3Int localPos = WorldToLocalCoord(x, y, z);

            Chunk chunk = GetOrCreateChunk(chunkPos);
            bool changed = chunk.SetBlock(localPos.x, localPos.y, localPos.z, type);

            if (changed)
            {
                if (localPos.x == 0)
                {
                    SetChunkDirtyIfExist(new Vector2Int(chunkPos.x - 1, chunkPos.y));
                }

                if (localPos.x == VoxelStatics.ChunkSize - 1)
                {
                    SetChunkDirtyIfExist(new Vector2Int(chunkPos.x + 1, chunkPos.y));
                }

                if (localPos.z == 0)
                {
                    SetChunkDirtyIfExist(new Vector2Int(chunkPos.x, chunkPos.y - 1));
                }

                if (localPos.z == VoxelStatics.ChunkSize - 1)
                {
                    SetChunkDirtyIfExist(new Vector2Int(chunkPos.x, chunkPos.y + 1));
                }
            }
        }

        public Voxel GetBlock(int x, int y, int z)
        {
            Vector2Int chunkPos = WorldToChunkCoord(x, z);

            if (TryGetChunk(chunkPos, out Chunk chunk))
            {
                Vector3Int localPos = WorldToLocalCoord(x, y, z);
                return chunk.GetBlock(localPos.x, localPos.y, localPos.z);
            }

            return default;
        }

        public BlockType GetBlockType(Vector3Int pos)
        {
            return GetBlockType(pos.x, pos.y, pos.z);
        }

        public BlockType GetBlockType(int x, int y, int z)
        {
            return GetBlock(x, y, z).Type;
        }

        void SetChunkDirtyIfExist(Vector2Int chunkPos)
        {
            if (TryGetChunk(chunkPos, out Chunk chunk))
            {
                chunk.SetDirty();
            }
        }

        public void SetBlock(Vector3Int pos, BlockType type)
        {
            SetBlock(pos.x, pos.y , pos.z, type);
        }

        public static Vector2Int WorldToChunkCoord(Vector3 pos)
        {
            Vector3Int voxelPos = WorldToVoxelCoord(pos);
            return WorldToChunkCoord(voxelPos.x, voxelPos.z);
        }

        public static Vector2Int WorldToChunkCoord(int x, int z)
        {
            return new Vector2Int(Mathf.FloorToInt((float)x / VoxelStatics.ChunkSize), Mathf.FloorToInt((float)z / VoxelStatics.ChunkSize));
        }

        public static Vector3Int WorldToLocalCoord(Vector3 pos)
        {
            Vector3Int voxelPos = WorldToVoxelCoord(pos);
            return WorldToLocalCoord(voxelPos.x, voxelPos.y, voxelPos.z);
        }

        public static Vector3Int WorldToLocalCoord(int x, int y, int z)
        {
            return new Vector3Int(Mod(x , VoxelStatics.ChunkSize), y, Mod(z, VoxelStatics.ChunkSize));
        }

        public static Vector3 ChunkToWorldOrigin(Vector2Int pos)
        {
            return new Vector3(pos.x * VoxelStatics.ChunkSize, 0, pos.y *VoxelStatics.ChunkSize);
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

        public ChunkMeshInput CreateMeshInput()
        {
            ChunkMeshInput input = new();

            int chunkSize = VoxelStatics.ChunkSize;
            int blockCount = chunkSize * chunkSize * chunkSize;

            input.Blocks = new BlockType[blockCount];
            input.Size = chunkSize;

            for (int x = 0; x < chunkSize; x++)
            {
                for (int y = 0; y < chunkSize; y++)
                {
                    for (int z = 0; z < chunkSize; z++)
                    {
                        int index = ToFlatIndex(x, y, z, chunkSize);
                        //input.Blocks[index] = _blocks[x, y, z].Type;
                    }
                }
            }

            return input;
        }


        static int ToFlatIndex(int x, int y, int z, int size)
        {
            return x + size * y + size * size * z;
        }

    }
}