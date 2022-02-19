using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace CSharpPU
{
    public class RWTexture2D<T> : System.IDisposable where T : struct
    {
        RenderTexture _gpu_texture = null;

        Texture2D _cpu_texture = null;

        bool m_gpu_dirty, m_cpu_dirty;

        readonly int width, height;

        readonly DefaultFormat format;

        public RenderTexture Texture => GPU_Texture;

        Color[] m_pixels;

        RenderTexture GPU_Texture
        {
            get
            {
                if (_gpu_texture == null)
                {
                    _gpu_texture = new RenderTexture(width, height, 1, format) {
                        enableRandomWrite = true
                    };
                    _gpu_texture.Create();
                }

                if (m_gpu_dirty)
                {
                    Graphics.Blit(_cpu_texture, _gpu_texture);
                    m_gpu_dirty = false;
                }

                return _gpu_texture;
            }
        }

        Texture2D CPU_Texture
        {
            get
            {
                if (_cpu_texture == null)
                {
                    _cpu_texture = new Texture2D(width, height, format, TextureCreationFlags.None);
                    m_pixels = new Color[width * height];
                }
                
                if (m_cpu_dirty)
                {
                    var a = RenderTexture.active;

                    RenderTexture.active = _gpu_texture;
                    _cpu_texture.ReadPixels(new Rect(0, 0, _cpu_texture.width, _cpu_texture.height), 0, 0);
                    _cpu_texture.Apply();

                    m_pixels = _cpu_texture.GetPixels();

                    RenderTexture.active = a;
                    m_cpu_dirty = false;
                }

                return _cpu_texture;
            }
        }

        public RWTexture2D(uint width, uint height, DefaultFormat format = DefaultFormat.HDR)
        {
            this.width = (int)width;
            this.height = (int)height;
            this.format = format;
        }

        public float4 this[uint2 uv]
        {
            get
            {
                int index = (int)(uv.y * width + uv.x);
                var color = m_pixels[index];
                return new float4(color.r, color.g, color.b, color.a);
            }
            set
            {
                int index = (int)(uv.y * width + uv.x);
                m_pixels[index] = new Color(value.x, value.y, value.z, value.w);
            }
        }

        public void Dispose()
        {
            _gpu_texture?.Release();
        }

        internal void ApplyCPUChanges()
        {
            CPU_Texture.SetPixels(m_pixels);
            CPU_Texture.Apply();
        }

        internal RenderTexture HintGPUChanged()
        {
            m_cpu_dirty = true;
            return GPU_Texture;
        }

        internal Texture2D HintCPUChanged()
        {
            m_gpu_dirty = true;
            return CPU_Texture;
        }
    }
}