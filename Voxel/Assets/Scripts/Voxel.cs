using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace VoxelEngine
{
    public enum Direction
    {
        Forward,
        Back,
        Left,
        Right,
        Up,
        Down,
    }

    public enum BlockType : byte
    {
        Air = 0,
        Dirt,
        Stone,
        Wood,

        Max,
    }

    public struct Voxel
    {
        public BlockType Type;

        public bool IsSolid => Type != BlockType.Air;

        public Voxel(BlockType type = BlockType.Air)
        {
            Type = type;
        }
    }

    public static class VoxelStatics
    {
        public const int ChunkSize = 1 << 4;
    }

    public struct ChunkMeshInput
    {
        public BlockType[] Blocks;
        public int Size;
    }

    public class Chunk
    {
        readonly Voxel[,,] _blocks = new Voxel[VoxelStatics.ChunkSize, VoxelStatics.ChunkSize, VoxelStatics.ChunkSize];

        bool _isDirty;
        public bool IsDirty { get => _isDirty; }

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
                _isDirty = true;
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

        public void SetDirty()
        {
            _isDirty = true;
        }

        public void ClearDirty()
        {
            _isDirty = false;
        }

    }

}