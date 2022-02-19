using UnityEngine;
using Unity.Mathematics;
using CSharpPU;

public class RainbowExample : ComputeShaderBase
{
    public RainbowExample(ComputeShader shader = null) : base(shader) {}

    RWTexture2D<float4> m_texture;

    [numthreads(32, 32, 1)]
    public void Main([SV_DispatchThreadID]uint3 id)
    {
        m_texture[id.xy] = new float4(id.x % 10f / 10f, id.y % 15f / 15f, 1f, 1f);
    }
}