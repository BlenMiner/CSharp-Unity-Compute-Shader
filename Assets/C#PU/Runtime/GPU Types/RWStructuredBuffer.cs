using System;
using Unity.Collections;
using Unity.Mathematics;
using CSharpPU.Buffers;
using UnityEngine;
using UnityEngine.Rendering;

namespace CSharpPU
{
    public struct RWStructuredBuffer<T> : ICSBuffer, IDisposable where T : struct
    {
        readonly T[] m_array;

        public RWStructuredBuffer(int count)
        {
            m_array = new T[count];
        }

        public uint Length => (uint)m_array.Length;

        public T this [uint index]
        {
            get
            {
                return m_array[(int)index];
            }
            set
            {
                m_array[(int)index] = value;
            }
        }

        public T this [int index]
        {
            get
            {
                return m_array[index];
            }
            set
            {
                m_array[index] = value;
            }
        }

        public void Dispose() { }

        public void UploadBuffer(ref ComputeBuffer buffer)
        {
            int size = System.Runtime.InteropServices.Marshal.SizeOf(typeof(T));

            if (buffer == null)
                buffer = new ComputeBuffer((int)Length, size);

            buffer.SetData(m_array);
        }

        public void DownloadBuffer(ComputeBuffer GPU)
        {
            GPU.GetData(m_array);
        }
    }
}