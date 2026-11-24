Shader "Sushi2/PierWaterContact"
{
    Properties
    {
        _BaseColor("Water contact color", Color) = (0.22,0.42,0.45,0.28)
        _ShadowMode("Shadow instead of ripples", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            float _ShadowMode;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            // 수면과 나란한 장식 평면을 화면 좌표로 변환합니다.
            Varyings Vert(Attributes input)
            {
                // 원래 바다 메시를 수정하지 않고 별도 평면만 투영합니다.
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            // 기둥에서 퍼지는 잔물결 또는 데크 아래의 부드러운 접촉 그림자를 표시합니다.
            half4 Frag(Varyings input) : SV_Target
            {
                // UV 중심을 기준으로 가장자리까지의 거리를 계산합니다.
                float2 p = input.uv * 2 - 1;
                float radius = length(p);
                // 그림자는 사각 평면의 경계에서 자연스럽게 사라지게 만듭니다.
                float shadow = (1 - smoothstep(0.65,1,abs(p.x))) * (1 - smoothstep(0.1,1,abs(p.y)));
                // 두 개의 잔물결이 천천히 바깥으로 퍼졌다가 사라집니다.
                float phase = frac(_Time.y * 0.16);
                float r1 = 0.28 + phase * 0.62;
                float r2 = 0.28 + frac(phase + 0.5) * 0.62;
                float ring1 = (1 - smoothstep(0.012,0.04,abs(radius-r1))) * (1 - smoothstep(0.58,0.92,r1));
                float ring2 = (1 - smoothstep(0.012,0.04,abs(radius-r2))) * (1 - smoothstep(0.58,0.92,r2));
                // 물결의 밝기를 낮춰 기존 수면과 UI보다 두드러지지 않게 합니다.
                float alpha = lerp(saturate(ring1+ring2),shadow,saturate(_ShadowMode));
                return half4(_BaseColor.rgb, _BaseColor.a * alpha);
            }
            ENDHLSL
        }
    }
}
