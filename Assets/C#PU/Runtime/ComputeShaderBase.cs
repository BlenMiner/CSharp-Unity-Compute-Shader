using System;
using UnityEngine;
using Unity.Mathematics;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CSharpPU.Buffers;
using System.Threading.Tasks;
using UnityEngine.Rendering;

namespace CSharpPU
{
    public class ComputeShaderBase : IDisposable
    {
        private class ComputeShaderData
        {
            public Dictionary<string, CSBufferInfo> Buffers = new Dictionary<string, CSBufferInfo>();

            public Dictionary<string, RWTexture2D<float4>> Textures = new Dictionary<string, RWTexture2D<float4>>();
        }

        public static string s_LastPath = null;

        readonly numthreadsAttribute[] m_numthreads;

        readonly MethodInfo[] m_kernels;

        readonly Dictionary<string, int> m_kernelIds;

        readonly Dictionary<int, ComputeShaderData> m_shaderData;

        readonly Dictionary<string, FieldInfo> m_fields;

        readonly List<Task> m_taskList;

        readonly ComputeShader m_shader;

        public int FindKernel(string name)
        {
            if (m_kernelIds.TryGetValue(name, out int id))
            {
                return id;
            }

            return -1;
        }

        public ComputeShaderBase(ComputeShader shader = null)
        {
            m_shader = shader;

            s_LastPath = new System.Diagnostics.StackTrace(true).GetFrame(1).GetFileName();

            m_taskList = new List<Task>(10);

            m_kernelIds = new Dictionary<string, int>();
            m_shaderData = new Dictionary<int, ComputeShaderData>();
            m_fields = new Dictionary<string, FieldInfo>();

            m_kernels = GetType().GetMethods()
                .Where(m => m.GetCustomAttributes(typeof(numthreadsAttribute), false).Length > 0)
                .ToArray();

            m_numthreads = new numthreadsAttribute[m_kernels.Length];

            var bufferFields = GetType().GetFields(
                BindingFlags.Instance | 
                BindingFlags.Static |
                BindingFlags.NonPublic |
                BindingFlags.Public
            );

            foreach (var bfield in bufferFields)
            {
                m_fields.Add(bfield.Name, bfield);
            }
            
            for (int i = 0; i < m_kernels.Length; ++i)
            {
                m_kernelIds.Add(m_kernels[i].Name, i);
                m_shaderData.Add(i, new ComputeShaderData());

                m_numthreads[i] = (numthreadsAttribute)m_kernels[i].GetCustomAttribute(typeof(numthreadsAttribute), false);
            }
        }

        private void ValidateKernel(int kernelIndex)
        {
            if (kernelIndex < 0 || kernelIndex >= m_kernels.Length)
                throw new Exception("Kernel ID isn't valid");
        }

        public void SetTexture(int kernelIndex, string name, RWTexture2D<float4> m_texture)
        {
            ValidateKernel(kernelIndex);

            var shaderData = m_shaderData[kernelIndex];

            shaderData.Textures.Remove(name);
            shaderData.Textures.Add(name, m_texture);
        }

        public void SetBuffer(int kernelIndex, string name, ICSBuffer buffer)
        {
            ValidateKernel(kernelIndex);
            
            var shaderData = m_shaderData[kernelIndex];

            if (!shaderData.Buffers.TryGetValue(name, out var bufferData))
            {
                bufferData = new CSBufferInfo(buffer);
                shaderData.Buffers.Add(name, bufferData);
            }
            else
            {
                bufferData.UpdateBuffer(buffer);
            }
        }

        public void DownloadBuffers(int kernelIndex)
        {
            var shaderData = m_shaderData[kernelIndex];
            
            // Update our buffers
            foreach(var buffer in shaderData.Buffers)
            {
                var GPU = buffer.Value.GPUBuffer;
                var CPU = buffer.Value.Buffer;

                CPU.DownloadBuffer(GPU);
                buffer.Value.Buffer = CPU;
            }
        }

        public void DispatchGPU(int kernelIndex, uint threadGroupsX, uint threadGroupsY, uint threadGroupsZ)
        {
            if (m_shader == null)
                throw new Exception("You need to provide shader in constructor to use this.");
            
            #if UNITY_EDITOR
            Debug.Assert(
                m_shader.FindKernel(m_kernels[kernelIndex].Name) == kernelIndex,
                "C#PU: Kernel indexes don't match. This is a bug."
            );
            #endif

            var shaderData = m_shaderData[kernelIndex];
            
            // Update our buffers
            foreach(var buffer in shaderData.Buffers)
                m_shader.SetBuffer(kernelIndex, buffer.Key, buffer.Value.GPUBuffer);

            // Update our textures
            foreach(var texture in shaderData.Textures)
            {
                m_shader.SetTexture(kernelIndex, texture.Key, texture.Value.Texture);
                texture.Value.HintGPUChanged();
            }

            m_shader.Dispatch(kernelIndex, (int)threadGroupsX, (int)threadGroupsY, (int)threadGroupsZ);
        }


        public void DispatchCPU(int kernelIndex, uint threadGroupsX, uint threadGroupsY, uint threadGroupsZ)
        {
            m_taskList.Clear();

            ValidateKernel(kernelIndex);

            var shaderData = m_shaderData[kernelIndex];
            var numthreads = m_numthreads[kernelIndex];
            var method = m_kernels[kernelIndex];

            // Update our buffers
            foreach(var buffer in shaderData.Buffers)
            {
                var field = m_fields[buffer.Key];

                if (typeof(ICSBuffer).IsAssignableFrom(field.FieldType))
                {
                    field.SetValue(this, buffer.Value.Buffer);
                }
                else throw new Exception($"invalid target type ({field.FieldType}, expected Buffer) for field '{field.Name}'");
            }

            // Update our textures
            foreach(var texture in shaderData.Textures)
            {
                var field = m_fields[texture.Key];

                if (field.FieldType == typeof(RWTexture2D<float4>))
                {
                    field.SetValue(this, texture.Value);
                    texture.Value.HintCPUChanged();
                }
                else throw new Exception($"invalid target type ({field.FieldType}, expected RWTexture2D<float4>) for field '{field.Name}'");
            }

            for (uint x = 0; x < numthreads.numThreadsX; ++x)
            {
                for (uint y = 0; y < numthreads.numThreadsY; ++y)
                {
                    for (uint z = 0; z < numthreads.numThreadsZ; ++z)
                    {
                        uint3 startIndex = new uint3(x * threadGroupsX, y * threadGroupsY, z * threadGroupsZ);

                        m_taskList.Add(Task.Run(() => {
                            
                            for (uint x = 0; x < threadGroupsX; ++x)
                            for (uint y = 0; y < threadGroupsY; ++y)
                            for (uint z = 0; z < threadGroupsZ; ++z)
                            {
                                uint3 idx = new uint3(x, y, z) + startIndex;
                                method.Invoke(this, new object[] { idx });
                            }

                        }));
                    }
                }
            }

            Task.WaitAll(m_taskList.ToArray());

            // Update our texture pixels
            foreach(var texture in shaderData.Textures)
                texture.Value.ApplyCPUChanges();
        }

        public void Dispose()
        {
            foreach(var data in m_shaderData)
            {
                foreach(var buff in data.Value.Buffers)
                {
                    buff.Value.DisposeGPUBuffer();
                }
            }
        }
    }
}