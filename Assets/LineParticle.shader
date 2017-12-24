Shader "Instancing/Line Particle"
{
    Properties
    {
        _ColorGradient ("Gradient Textures", 2D) = "white" {}
        _LumFactor ("Lumination Factor", Float) = 1
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

            struct InstanceData
            {
                float4 pos[2];
                float2 colorSampleCoord, _pad;
            };
            StructuredBuffer<InstanceData> instanceDrawingData;
            sampler2D _ColorGradient;
            float _LumFactor;

            struct VertexData
            {
                float4 pos : SV_Position;
                float4 color : COLOR0;
            };

            VertexData vmain(uint vindex: SV_VertexID, uint index : SV_InstanceID)
            {
                VertexData vd;
                vd.pos = mul(UNITY_MATRIX_VP, instanceDrawingData[index].pos[vindex]);
                vd.color = tex2Dlod(_ColorGradient, float4(instanceDrawingData[index].colorSampleCoord, 0.0f, 0.0f));
                vd.color.a = instanceDrawingData[index].colorSampleCoord;
                return vd;
            }
            float4 fmain(VertexData vd) : SV_Target { return vd.color * _LumFactor; }
            ENDCG
        }
    }
}