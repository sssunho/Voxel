using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine
{
    public class VoxelWorldBehaviour : MonoBehaviour
    {
        VoxelWorld _world;

        public VoxelWorld World
        {
            get => _world;
        }

        void Awake()
        {
            _world = new();
        }
    }
}
