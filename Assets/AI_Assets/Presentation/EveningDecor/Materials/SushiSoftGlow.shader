Shader "Sushi/Decor/SoftGlow"
{
    Properties
    {
        _GlowColor ("Warm Scattering Color", Color) = (1, 0.55, 0.18, 1)
        _Intensity ("Scattering Strength", Range(0, 0.5)) = 0.12
        _Falloff ("Soft Edge Falloff", Range(1, 8)) = 3
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Name "SoftLightScattering"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _GlowColor;
                float _Intensity;
                float _Falloff;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; };

            // <summary>구형 광채 메시의 위치와 표면 방향을 화면으로 전달합니다.</summary>
            Varyings Vert(Attributes input)
            {
                // 구체의 입체 위치를 변환합니다.
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                // 시선에 대한 곡면의 각도로 가장자리 투명도를 계산할 수 있게 합니다.
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            // <summary>구체 외곽으로 갈수록 자연스럽게 사라지는 낮은 밝기의 빛을 더합니다.</summary>
            half4 Frag(Varyings input) : SV_Target
            {
                // 정사영 카메라에서도 일관된 시선 방향을 얻습니다.
                float3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float facing = saturate(dot(normalize(input.normalWS), view));
                // 외곽에서 값이 0으로 수렴하여 둥근 테두리가 생기지 않게 합니다.
                float softness = pow(facing, _Falloff);
                // 낮은 선형 밝기를 가산하여 배경이 비치는 넓은 광채를 만듭니다.
                return half4(_GlowColor.rgb * (_Intensity * softness), 0);
            }
            ENDHLSL
        }
    }
}
