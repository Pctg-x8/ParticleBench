// Upgrade NOTE: replaced 'UNITY_INSTANCE_ID' with 'UNITY_VERTEX_INPUT_INSTANCE_ID'
// Upgrade NOTE: upgraded instancing buffer 'InstanceData' to new syntax.

Shader "Instancing/Line Particle[CPU]"
{
    Properties
    {
        _ColorGradient ("Gradient Textures", 2D) = "white" {}
        _LumFactor ("Lumination Factor", Float) = 1
        [PerRendererData] _pos1("Position1", Vector) = (0, 0, 0, 0)
        [PerRendererData] _pos2("Position2", Vector) = (0, 1, 0, 0)
        [PerRendererData] _colorSampleCoord("Color Sampling Vcoord", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "Queue" = "Transparent"
            "PreviewType" = "Plane"
            "LightMode" = "Always"
        }
        Cull Off
        Lighting Off
        ZTest Always
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vmain
            #pragma fragment fmain

            #include "UnityCG.cginc"

            sampler2D _ColorGradient;
            float _LumFactor;

#if 0
            UNITY_INSTANCING_BUFFER_START(InstanceData)
                UNITY_DEFINE_INSTANCED_PROP(float4, _pos1)
#define _pos1_arr InstanceData
                UNITY_DEFINE_INSTANCED_PROP(float4, _pos2)
#define _pos2_arr InstanceData
                UNITY_DEFINE_INSTANCED_PROP(float4, _colorSampleCoord)
#define _colorSampleCoord_arr InstanceData
            UNITY_INSTANCING_BUFFER_END(InstanceData)
#else
            float4 _pos1, _pos2, _colorSampleCoord;
#endif

            struct VertexIn
            {
                uint vindex: SV_VertexID;
                // UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct VertexData
            {
                float4 pos : SV_Position;
                float4 color : COLOR0;
            };

            VertexData vmain(VertexIn vin)
            {
                // UNITY_SETUP_INSTANCE_ID(vin);
                VertexData vd;
                /*vd.pos = mul(UNITY_MATRIX_VP, lerp(UNITY_ACCESS_INSTANCED_PROP(_pos1_arr, _pos1), UNITY_ACCESS_INSTANCED_PROP(_pos2_arr, _pos2), vin.vindex));
                vd.color = tex2Dlod(_ColorGradient, float4(UNITY_ACCESS_INSTANCED_PROP(_colorSampleCoord_arr, _colorSampleCoord).xy, 0.0f, 0.0f));
                vd.color.a = UNITY_ACCESS_INSTANCED_PROP(_colorSampleCoord_arr, _colorSampleCoord).x;*/
                vd.pos = mul(UNITY_MATRIX_VP, lerp(_pos1, _pos2, vin.vindex));
                vd.color = tex2Dlod(_ColorGradient, float4(_colorSampleCoord.xy, 0.0f, 0.0f));
                vd.color.a = _colorSampleCoord.x;
                return vd;
            }
            float4 fmain(VertexData vd) : SV_Target { return vd.color * _LumFactor; }
            ENDCG
        }
    }
}