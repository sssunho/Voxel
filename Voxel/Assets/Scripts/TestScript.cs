using System;
using UnityEngine;

public class TestScript : MonoBehaviour
{
    void Start()
    {
        long before = GC.GetAllocatedBytesForCurrentThread();

        byte[] temp = new byte[1024];
        int[] temp2 = new int[2048];
        string text = new string('a', 100);

        long after = GC.GetAllocatedBytesForCurrentThread();

        Debug.Log($"Alloc Test = {after - before}");
    }

}
