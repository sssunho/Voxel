using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using VoxelEngine;

[BurstCompile]
public struct CountVisibleFaceJob : IJobFor
{
    [ReadOnly] public NativeArray<BlockType> Blocks;
    public NativeArray<int> FaceCounts;
    public int Size;

    public void Execute(int index)
    {
        int padded = Size + 2;
        int strideY = padded;
        int strideZ = padded * padded;

        int x = index % Size;
        int y = (index / Size) % Size;
        int z = index / (Size * Size);

        int paddedIndex =
            (x + 1) +
            (y + 1) * strideY +
            (z + 1) * strideZ;

        int count = 0;

        if (Blocks[paddedIndex] == BlockType.Air)
        {
            FaceCounts[index] = 0;
            return;
        }

        if (Blocks[paddedIndex + 1] == BlockType.Air)
        {
            count++;
        }

        if (Blocks[paddedIndex - 1] == BlockType.Air)
        {
            count++;
        }

        if (Blocks[paddedIndex + strideY] == BlockType.Air)
        {
            count++;
        }

        if (Blocks[paddedIndex - strideY] == BlockType.Air)
        {
            count++;
        }

        if (Blocks[paddedIndex + strideZ] == BlockType.Air)
        {
            count++;
        }

        if (Blocks[paddedIndex - strideZ] == BlockType.Air)
        {
            count++;
        }

        FaceCounts[index] = count;
    }
}

public class TestScript : MonoBehaviour
{
    void Start()
    {


    }

}
