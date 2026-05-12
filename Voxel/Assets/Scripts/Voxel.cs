namespace VoxelEngine
{
    public enum Direction
    {
        Left,
        Right,
        Down,
        Up,
        Back,
        Forward,
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

}