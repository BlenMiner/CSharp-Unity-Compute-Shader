# CSharp-Unity-Compute-Shader (Unity 2020.3.26f1)
Create compute shaders for Unity with C#.

This is extremely incomplete, started it this weekend (2/19/2022).

Why?

- Learning
- Easier debugging (simulate shader on CPU)

Shader example:

```c#
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
```

Becomes:

```hlsl
#pragma kernel Main

RWTexture2D<float4> m_texture;

[numthreads(32, 32, 1)]
void Main (uint3 id : SV_DispatchThreadID)
{
    m_texture[id.xy] = float4(id.x % 10.0 / 10.0, id.y % 15.0 / 15.0, 1.0, 1.0);
}
```
