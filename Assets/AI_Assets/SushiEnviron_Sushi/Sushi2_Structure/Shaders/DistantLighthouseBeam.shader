Shader "Sushi2/DistantLighthouseBeam"
{
    Properties
    {
        _BaseColor("Beam Color", Color) = (0.85,0.76,0.55,1)
        _Opacity("Beam Strength", Range(0,2)) = 0.65
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half _Opacity;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half fog:TEXCOORD1; };
            // 메시를 투영하고 안개에 따른 빛의 감쇠를 전달합니다.
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS=TransformObjectToHClip(input.positionOS.xyz);
                output.uv=input.uv;
                output.fog=ComputeFogFactor(output.positionCS.z);
                return output;
            }
            // 광원에서 왼쪽으로 퍼지며 가장자리와 끝부분이 부드럽게 사라집니다.
            half4 Frag(Varyings input):SV_Target
            {
                float distanceAlong=saturate(input.uv.x);
                float halfWidth=lerp(0.035,0.5,distanceAlong);
                float across=abs(input.uv.y-0.5)/halfWidth;
                float edge=1.0-smoothstep(0.12,1.0,across);
                float fade=1.0-smoothstep(0.25,1.0,distanceAlong);
                float haze=ComputeFogIntensity(input.fog);
                return half4(_BaseColor.rgb,edge*fade*haze*_Opacity*_BaseColor.a);
            }
            ENDHLSL
        }
    }
}
