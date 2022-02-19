using System;
using System.IO;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace CSharpPU
{
    public class CSharpPUSimulation
    {
        public class SimpleCounterKernel : ComputeShaderBase
        {
            public SimpleCounterKernel(ComputeShader shader = null) : base(shader) {}

            RWStructuredBuffer<uint> strutureBuffer;

            [numthreads(1, 1, 1)]
            public void CSMain([SV_DispatchThreadID]uint3 id)
            {
                strutureBuffer[id.x] = id.x * 2;
            }
        }

        public class SimpleCounterBigKernel : ComputeShaderBase
        {
            public SimpleCounterBigKernel(ComputeShader shader = null) : base(shader) {}

            RWStructuredBuffer<uint> strutureBuffer;

            [numthreads(64, 1, 1)]
            public void CSMain([SV_DispatchThreadID]uint3 id)
            {
                if (id.x < strutureBuffer.Length)
                    strutureBuffer[id.x] = id.x * 2;
            }
        }

        private ComputeShader GetShader(string name)
        {
            var path = new System.Diagnostics.StackTrace(true).GetFrame(0).GetFileName();

            FileInfo file = new FileInfo(path);

            var directory = file.Directory.FullName;
            var localDir = "Assets" + directory.Substring(Application.dataPath.Length);

            string shaderPath = $"{localDir}/C#PU-{name}.compute";

            return AssetDatabase.LoadAssetAtPath<ComputeShader>(shaderPath);
        }

        [Test]
        public void FindKernelTest()
        {
            using SimpleCounterKernel simpleShader = new SimpleCounterKernel();

            Assert.AreEqual(0, simpleShader.FindKernel("CSMain"));
            Assert.AreEqual(-1, simpleShader.FindKernel("SomeOtherKernel"));
        }

        [Test]
        public void SimpleCounterDouble_CPU_GPU()
        {
            ComputeShader shader = GetShader(nameof(SimpleCounterKernel));

            using RWStructuredBuffer<uint> buffer = new RWStructuredBuffer<uint>(10);
            using SimpleCounterKernel simpleShader = new SimpleCounterKernel(shader);

            int kernel = simpleShader.FindKernel("CSMain"); Assert.AreEqual(0, kernel);

            simpleShader.SetBuffer(kernel, "strutureBuffer", buffer);
            simpleShader.DispatchCPU(kernel, buffer.Length, 1, 1);

            void Check()
            {
                Assert.AreEqual(0 * 2, buffer[0]);
                Assert.AreEqual(1 * 2, buffer[1]);
                Assert.AreEqual(2 * 2, buffer[2]);
                Assert.AreEqual(3 * 2, buffer[3]);
                Assert.AreEqual(4 * 2, buffer[4]);
                Assert.AreEqual(5 * 2, buffer[5]);
                Assert.AreEqual(6 * 2, buffer[6]);
                Assert.AreEqual(7 * 2, buffer[7]);
                Assert.AreEqual(8 * 2, buffer[8]);
                Assert.AreEqual(9 * 2, buffer[9]);
            }

            Check();

            simpleShader.DispatchGPU(kernel, buffer.Length, 1, 1);
            simpleShader.DownloadBuffers(kernel);
            
            Check();
        }

        [Test]
        public void SimpleCounterDoubleBigCount_CPU()
        {
            const uint count = 512 * 512;

            ComputeShader shader = GetShader(nameof(SimpleCounterBigKernel));

            using RWStructuredBuffer<uint> buffer = new RWStructuredBuffer<uint>((int)count);
            using SimpleCounterBigKernel bigShader = new SimpleCounterBigKernel(shader);

            int kernel = bigShader.FindKernel("CSMain"); Assert.AreEqual(0, kernel);

            bigShader.SetBuffer(kernel, "strutureBuffer", buffer);
            bigShader.DispatchCPU(kernel, count / 64, 1, 1);

            for (int i = 0; i < count / 2; ++i)
                Assert.AreEqual(i * 2 * 2, buffer[i * 2], $"Wrong value at index {i}");
        }

        [Test]
        public void SimpleCounterDoubleBigCount_GPU()
        {
            const uint count = 512 * 512;

            ComputeShader shader = GetShader(nameof(SimpleCounterBigKernel));

            using RWStructuredBuffer<uint> buffer = new RWStructuredBuffer<uint>((int)count);
            using SimpleCounterBigKernel bigShader = new SimpleCounterBigKernel(shader);

            int kernel = bigShader.FindKernel("CSMain");
            
            Assert.AreEqual(0, kernel);

            bigShader.SetBuffer(kernel, "strutureBuffer", buffer);

            bigShader.DispatchGPU(kernel, count / 64, 1, 1);
            bigShader.DownloadBuffers(kernel);

            for (int i = 0; i < count / 2; ++i)
                Assert.AreEqual(i * 2 * 2, buffer[i * 2], $"Wrong value at index {i}");
        }
    }
}
