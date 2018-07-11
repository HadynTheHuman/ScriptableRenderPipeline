using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine.Rendering;

namespace UnityEngine.Experimental.Rendering.HDPipeline
{
    // Keep this class first in the file. Otherwise it seems that the script type is not registered properly.
    public abstract class AtmosphericScattering : VolumeComponent
    {
        static readonly int m_TypeParam = Shader.PropertyToID("_AtmosphericScatteringType");
        // Fog Color
        static readonly int m_ColorModeParam = Shader.PropertyToID("_FogColorMode");
        static readonly int m_FogColorDensityParam = Shader.PropertyToID("_FogColorDensity");
        static readonly int m_MipFogParam = Shader.PropertyToID("_MipFogParameters");

        readonly static int m_GradientFogParam = Shader.PropertyToID("_FogGradientTexture");

        // Fog Color
        public FogColorParameter       colorMode = new FogColorParameter(FogColorMode.SkyColor);
        [Tooltip("Constant Fog Color")]
        public ColorParameter          color = new ColorParameter(Color.grey);
        public ClampedFloatParameter   density = new ClampedFloatParameter(1.0f, 0.0f, 1.0f);
        [Tooltip("Maximum mip map used for mip fog (0 being lowest and 1 highest mip).")]
        public ClampedFloatParameter   mipFogMaxMip = new ClampedFloatParameter(0.5f, 0.0f, 1.0f);
        [Tooltip("Distance at which minimum mip of blurred sky texture is used as fog color.")]
        public MinFloatParameter       mipFogNear = new MinFloatParameter(0.0f, 0.0f);
        [Tooltip("Distance at which maximum mip of blurred sky texture is used as fog color.")]
        public MinFloatParameter       mipFogFar = new MinFloatParameter(1000.0f, 0.0f);
        
        public FogGradientParameter gradient = new FogGradientParameter(null);
        [HideInInspector]
        public TextureParameter gradientTexture = new TextureParameter(null, true); // This is here to cache the gradient texture / speed up interpolation, and shouldn't be set by the user.

        public abstract void PushShaderParameters(CommandBuffer cmd, FrameSettings frameSettings);

        public static void PushNeutralShaderParameters(CommandBuffer cmd)
        {
            cmd.SetGlobalFloat(m_TypeParam, (float)FogType.None);
        }

        public void PushShaderParametersCommon(CommandBuffer cmd, FogType type, FrameSettings frameSettings)
        {
            if (frameSettings.enableAtmosphericScattering)
                cmd.SetGlobalFloat(m_TypeParam, (float)type);
            else
                cmd.SetGlobalFloat(m_TypeParam, (float)FogType.None);

            // Fog Color
            cmd.SetGlobalFloat(m_ColorModeParam, (float)colorMode.value);
            cmd.SetGlobalColor(m_FogColorDensityParam, new Color(color.value.r, color.value.g, color.value.b, density));
            cmd.SetGlobalVector(m_MipFogParam, new Vector4(mipFogNear, mipFogFar, mipFogMaxMip, 0.0f));
            cmd.SetGlobalTexture(m_GradientFogParam, gradientTexture.value);
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (gradientTexture.value == null && gradient.value != null)
            {
                gradientTexture.value = MakeTextureFromGradient(gradient.value);
            }
        }

        void OnValidate()
        {
            if(colorMode.value != FogColorMode.Gradient)
            {
                gradientTexture.overrideState = false;
                return;
            }

            gradientTexture.overrideState = true;

            if (gradient.value == null)
            {
                gradientTexture.value = null;
                return;
            }

            // TODO: Could hash the gradient and only regenerate the texture when it's changed.
            gradientTexture.value = MakeTextureFromGradient(gradient.value);
        }

        protected static Texture2D MakeTextureFromGradient(Gradient fogGradient, int resolution = 256)
        {
            Texture2D texture = new Texture2D(resolution, 1, TextureFormat.ARGB32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.alphaIsTransparency = true;

            Color[] colors = new Color[resolution];
            for (int i = 0; i < colors.Length; ++i)
            {
                float t = (float)i / (colors.Length - 1);
                colors[i] = fogGradient.Evaluate(t);
            }

            texture.SetPixels(colors);
            texture.Apply(false);

            return texture;
        }
    }

    [GenerateHLSL]
    public enum FogType
    {
        None,
        Linear,
        Exponential
    }

    [GenerateHLSL]
    public enum FogColorMode
    {
        ConstantColor,
        SkyColor,
        Gradient,
    }

    [Serializable, DebuggerDisplay(k_DebuggerDisplay)]
    public sealed class FogTypeParameter : VolumeParameter<FogType>
    {
        public FogTypeParameter(FogType value, bool overrideState = false)
            : base(value, overrideState) { }
    }

    [Serializable, DebuggerDisplay(k_DebuggerDisplay)]
    public sealed class FogColorParameter : VolumeParameter<FogColorMode>
    {
        public FogColorParameter(FogColorMode value, bool overrideState = false)
            : base(value, overrideState) { }
    }

    [Serializable, DebuggerDisplay(k_DebuggerDisplay)]
    public sealed class FogGradientParameter : VolumeParameter<Gradient>
    {
        public FogGradientParameter(Gradient value, bool overrideState = false)
            : base(value, overrideState) { }

        public override void Interp(Gradient from, Gradient to, float t)
        {
            base.Interp(from, to, t);

            // TODO: this is overkill, and prone to breaking due to gradient key limits. Instead, implement the equivalent method in VolumeParameter.TextureParameter.

            // if(from == null)
            // {
            //     m_Value = to;
            //     return;
            // }
            // else if (to == null)
            // {
            //     m_Value = from;
            //     return;
            // }
            // 
            // // Color
            // 
            // HashSet<float> colorTimes = new HashSet<float>();
            // 
            // foreach (GradientColorKey colorKey in from.colorKeys)
            // {
            //     colorTimes.Add(colorKey.time);
            // }
            // 
            // foreach (GradientColorKey colorKey in to.colorKeys)
            // {
            //     colorTimes.Add(colorKey.time);
            // }
            // 
            // var combinedColorKeys = new GradientColorKey[colorTimes.Count];
            // int combinedColorIndex = 0;
            // 
            // foreach (float colorTime in colorTimes)
            // {
            //     Color combinedColor = Color.Lerp(from.Evaluate(colorTime), to.Evaluate(colorTime), t);
            //     combinedColorKeys[combinedColorIndex++] = (new GradientColorKey(combinedColor, colorTime));
            // }
            // 
            // // Alpha
            // 
            // HashSet<float> alphaTimes = new HashSet<float>();
            // 
            // foreach (GradientAlphaKey alphaKey in from.alphaKeys)
            // {
            //     alphaTimes.Add(alphaKey.time);
            // }
            // 
            // foreach (GradientAlphaKey alphaKey in to.alphaKeys)
            // {
            //     alphaTimes.Add(alphaKey.time);
            // }
            // 
            // var combinedAlphaKeys = new GradientAlphaKey[alphaTimes.Count];
            // int combinedAlphaIndex = 0;
            // 
            // foreach (float alphaTime in alphaTimes)
            // {
            //     float combinedAlpha = Mathf.Lerp(from.Evaluate(alphaTime).a, to.Evaluate(alphaTime).a, t);
            //     combinedAlphaKeys[combinedAlphaIndex++] = (new GradientAlphaKey(combinedAlpha, alphaTime));
            // }
            // 
            // m_Value.SetKeys(combinedColorKeys, combinedAlphaKeys);

        }
    }
}
