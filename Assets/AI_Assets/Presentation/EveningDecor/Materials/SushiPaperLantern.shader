Shader "Sushi/Decor/PaperLantern"
{
    Properties
    {
        _BaseColor ("Warm Paper Edge", Color) = (0.85, 0.30, 0.035, 1)
        [HDR] _EmissionColor ("Inner Glow", Color) = (3.2, 1.65, 0.38, 1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "LuminousPaper"
            Tags { "LightMode"="UniversalForward" }
            ZWrite On
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _EmissionColor;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; };

            // <summary>종이등의 입체 위치와 표면 방향을 픽셀 셰이더로 전달합니다.</summary>
            Varyings Vert(Attributes input)
            {
                // 오브젝트 좌표를 월드 및 클립 좌표로 변환합니다.
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                // 각 면의 방향을 보존하여 시점을 바꿔도 입체 음영이 유지되게 합니다.
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            // <summary>등불 가장자리는 주황색, 중심은 밝은 노란색으로 표현합니다.</summary>
            half4 Frag(Varyings input) : SV_Target
            {
                // 정사영 카메라에서도 올바른 시선 방향을 사용합니다.
                float3 normal = normalize(input.normalWS);
                float3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                // 가장자리에서 두꺼운 종이를 통과하는 빛이 줄어드는 효과를 만듭니다.
                float facing = pow(saturate(dot(normal, view)), 1.45);
                float3 glow = lerp(_BaseColor.rgb, _EmissionColor.rgb, facing);
                // 화면에 표시되는 색은 HDR을 유지하여 Bloom에 전달합니다.
                return half4(glow, 1.0);
            }
            ENDHLSL
        }
    }
}
