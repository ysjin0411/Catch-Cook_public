Shader "Sushi2/PierTransparentWater"
{
    Properties
    {
        _BaseColor("Deep Water", Color) = (0.025,0.12,0.17,1)
        _CrestColor("Wave Crest", Color) = (0.14,0.32,0.36,1)
        _ShallowOpacity("Shallow water opacity", Range(0,1)) = 0.24
        _DepthDensity("Underwater haze", Range(0.1,3)) = 0.8
        _ClearEndZ("Clear foreground end", Float) = 0
        _OpaqueStartZ("Opaque harbor start", Float) = 8
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _CrestColor;
            float _ShallowOpacity, _DepthDensity, _ClearEndZ, _OpaqueStartZ;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half fog : TEXCOORD1; };

            // 수면의 월드 위치와 화면 좌표를 전달합니다.
            Varyings Vert(Attributes input)
            {
                // 기존 바다 메시의 형태와 원근을 그대로 사용합니다.
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.fog = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            // 가까운 수면 아래의 물체는 드러내고 깊어질수록 청록색으로 흐리게 합니다.
            half4 Frag(Varyings input) : SV_Target
            {
                // 기존 항구 수면과 같은 잔물결 및 반사 계산을 유지합니다.
                float2 p = input.positionWS.xz;
                float t = _Time.y;
                float a = sin(p.x*1.7+p.y*2.4+t*.9+sin(p.y*.72)*1.8);
                float b = sin(p.x*3.3-p.y*1.1-t*.65+sin(p.x*.45+p.y*.39));
                float3 normal = normalize(float3(a*.16,1,b*.12));
                Light sun = GetMainLight();
                float3 view = normalize(GetWorldSpaceViewDir(input.positionWS));
                float spec = pow(saturate(dot(normal,normalize(view+sun.direction))),100);
                float fresnel = pow(1-saturate(dot(normal,view)),3);
                float ripple = smoothstep(.91,1,(a+b)*.25+.5);
                half3 col = lerp(_BaseColor.rgb,_CrestColor.rgb,fresnel*.38+ripple*.012);
                col += sun.color*spec*.008;
                // 불투명 물고기와 기둥이 기록한 깊이로 실제 수면 아래 깊이를 구합니다.
                float2 uv = GetNormalizedScreenSpaceUV(input.positionCS);
                float rawDepth = SampleSceneDepth(uv);
                #if !UNITY_REVERSED_Z
                    rawDepth = lerp(UNITY_NEAR_CLIP_VALUE,1,rawDepth);
                #endif
                float3 submerged = ComputeWorldSpacePosition(uv,rawDepth,UNITY_MATRIX_I_VP);
                float depth = max(0,input.positionWS.y-submerged.y);
                // 얕은 곳은 투명하게, 깊은 곳은 수중 안개로 자연스럽게 감춥니다.
                float alpha = lerp(_ShallowOpacity,0.96,1-exp(-depth*_DepthDensity));
                // 건물 뒤쪽의 먼 항구는 원래처럼 불투명한 바다로 남깁니다.
                float distant = smoothstep(_ClearEndZ,max(_ClearEndZ+0.01,_OpaqueStartZ),input.positionWS.z);
                alpha = lerp(alpha,1,distant);
                return half4(MixFog(col,input.fog),alpha);
            }
            ENDHLSL
        }
    }
}
