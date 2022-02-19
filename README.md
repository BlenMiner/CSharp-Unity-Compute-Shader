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

And this is how you could use it in your game:

```c#
using UnityEngine;
using Unity.Mathematics;
using CSharpPU;
using UnityEngine.UI;

public class CSPURuntimeExample : MonoBehaviour
{
    [SerializeField] ComputeShader m_exampleGeneratedShader;

    [SerializeField] RawImage m_uiImage;

    [SerializeField] bool m_runOnGPU = false;

    private RainbowExample m_exampleShader;

    private RWTexture2D<float4> m_texture;

    private int m_mainKernel;

    uint m_width;

    uint m_height;

    private void Awake()
    {
        // m_exampleGeneratedShader only needs to be set if you want to dispatch on the GPU
        m_exampleShader = new RainbowExample(m_exampleGeneratedShader);

        m_mainKernel = m_exampleShader.FindKernel("Main");

        var rect = m_uiImage.rectTransform.rect;

        m_width = (uint)Mathf.CeilToInt(rect.width);
        m_height = (uint)Mathf.CeilToInt(rect.height);

        m_texture = new RWTexture2D<float4>(m_width, m_height);

        m_exampleShader.SetTexture(m_mainKernel, "m_texture", m_texture);
    }

    private void Update()
    {
        if (m_runOnGPU)
        {
            m_exampleShader.DispatchGPU(m_mainKernel, m_width / 32, m_height / 32, 1);
        }
        else
        {
            m_exampleShader.DispatchCPU(m_mainKernel, m_width / 32, m_height / 32, 1);
        }
        
        m_uiImage.texture = m_texture.Texture;
    }

    private void OnDestroy()
    {
        m_texture.Dispose();
    }
}

```
