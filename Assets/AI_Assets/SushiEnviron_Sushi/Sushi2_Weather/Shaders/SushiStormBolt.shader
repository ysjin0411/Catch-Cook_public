Shader "Sushi2/Weather/Storm Bolt"
{
    Properties
    {
        [HDR] _Color("Lightning Color",Color)=(1.4,1.8,2.5,1)
        _Intensity("Brightness",Range(0,3))=1
    }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+9"}
        Pass
        {
            Tags {"LightMode"="SRPDefaultUnlit"}
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            float _Intensity;
            CBUFFER_END
            struct Attributes {float4 positionOS:POSITION;float2 uv:TEXCOORD0;};
            struct Varyings {float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;};

            /// <summary>실제 3차원 번개 선분을 카메라에 투영합니다.</summary>
            Varyings Vert(Attributes input)
            {
                // 선분의 가로 UV로 중심과 가장자리의 밝기를 구분합니다.
                Varyings output;
                output.positionCS=TransformObjectToHClip(input.positionOS.xyz);
                output.uv=input.uv;
                return output;
            }

            /// <summary>밝은 번개 중심과 부드러운 가장자리를 합성합니다.</summary>
            half4 Frag(Varyings input):SV_Target
            {
                // HDR 중심이 Bloom에 반응하고 경계는 투명하게 사라지도록 합니다.
                float edge=1-smoothstep(.15,1,abs(input.uv.y*2-1));
                return half4(_Color.rgb,edge*_Intensity*_Color.a);
            }
            ENDHLSL
        }
    }
}
