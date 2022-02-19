using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CSharpPU.Buffers
{
    public interface ICSBuffer
    {
        uint Length { get; }

        void UploadBuffer(ref ComputeBuffer buffer);

        void DownloadBuffer(ComputeBuffer GPU);
    }

    public class CSBufferInfo
    {
        public ICSBuffer Buffer;

        ComputeBuffer _GPUBuffer = null;

        bool _GPUBufferDirty = true;

        public void DisposeGPUBuffer()
        {
            if (_GPUBuffer != null && _GPUBuffer.IsValid())
                _GPUBuffer?.Release();
            _GPUBuffer = null;
        }

        public ComputeBuffer GPUBuffer {
            get {
                if (_GPUBufferDirty || _GPUBuffer == null)
                {
                    if (_GPUBuffer != null && _GPUBuffer.count != Buffer.Length)
                    {
                        _GPUBuffer?.Release();
                        _GPUBuffer = null;
                    }

                    Buffer.UploadBuffer(ref _GPUBuffer);
                    _GPUBufferDirty = false;
                }
                return _GPUBuffer;
            }
        }

        public CSBufferInfo(ICSBuffer data)
        {
            Buffer = data;
        }

        public void UpdateBuffer(ICSBuffer data)
        {
            Buffer = data;
            _GPUBufferDirty = true;
        }
    }
}