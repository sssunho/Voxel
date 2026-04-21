using System.Collections.Generic;
using UnityEngine;
using VoxelEngine;

[System.Serializable]
public struct TestBlockProperty
{
    public Vector3Int pos;
    public BlockType blockType;
}

public class TestScript : MonoBehaviour
{
    [SerializeField]
    VoxelWorldBehaviour _world;

    [SerializeField]
    ChunkRenderer _chunkRendererPrefab;

    [SerializeField]
    TestBlockProperty[] _properties;

    [SerializeField]
    Vector3Int[] _qwBlocks;

    Dictionary<Vector2Int, ChunkRenderer> _renderers = new();
     
    void Start()
    {
        if (_world == null)
        {
            return;
        }

        if (_chunkRendererPrefab == null)
        {
            return;
        }

        VoxelWorld world = _world.World;

        foreach (var p in _properties)
        {
            world.SetBlock(p.pos, p.blockType);
        }

        for (int i = -10; i <= 10; i++)
        {
            for (int j = -10; j <= 10; j++)
            {
                world.SetBlock(new Vector3Int(i, 0, j), BlockType.Stone);
            }
        }

        EnsureChunkRenderers(world);
    }

    void Update()
    {
        if (_world == null)
        {
            return;
        }

        VoxelWorld world = _world.World;

        if (Input.GetKeyDown(KeyCode.Q))
        {
            foreach(Vector3Int pos in _qwBlocks)
            {
                world.SetBlock(pos, BlockType.Dirt);
            }

            EnsureChunkRenderers(world);
        }

        if (Input.GetKeyDown(KeyCode.W))
        {
            foreach (Vector3Int pos in _qwBlocks)
            {
                world.SetBlock(pos, BlockType.Dirt);
            }

            EnsureChunkRenderers(world);
        }
    }

    void EnsureChunkRenderers(VoxelWorld world)
    {
        if (world == null)
        {
            return;
        }

        foreach (var kv in world.Chunks)
        {
            if (_renderers.ContainsKey(kv.Key))
            {
                continue;
            }

            ChunkRenderer renderer = Instantiate(_chunkRendererPrefab, VoxelWorld.ChunkToWorldOrigin(kv.Key), Quaternion.identity);
            if (renderer)
            {
                renderer.gameObject.name = $"Chunk({kv.Key})";
                renderer.Initialize(world, kv.Key, kv.Value);
                _renderers.Add(kv.Key, renderer);
            }
        }
    }
}
