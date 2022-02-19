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
