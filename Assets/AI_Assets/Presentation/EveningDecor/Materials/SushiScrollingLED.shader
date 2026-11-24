Shader "Sushi/ScrollingLED"
{
    Properties
    {
        [HDR] _EmissionColor ("LED Color", Color) = (5, 0.035, 0.85, 1)
        _ScrollOffset ("Vertical Offset", Float) = 0
        _WindowHalfHeight ("Display Half Height", Float) = 0.30
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" "DisableBatching"="True" }
        Pass
        {
            Name "ScrollingLED"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Back
            ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _EmissionColor;
                float _ScrollOffset;
                float _WindowHalfHeight;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float localHeight : TEXCOORD0; half facing : TEXCOORD1; };

            // <summary>LED 메시의 입체 형태를 유지하면서 로컬 위쪽으로 이동합니다.</summary>
            Varyings Vert(Attributes input)
            {
                // 전광판 자체가 회전하거나 이동해도 표시창과 같은 좌표계에서 스크롤합니다.
                Varyings output;
                float3 position = input.positionOS.xyz;
                position.y += _ScrollOffset;
                output.positionCS = TransformObjectToHClip(position);
                output.localHeight = position.y;
                // 둥근 LED의 가장자리에 약한 음영을 주어 입체감을 남깁니다.
                output.facing = saturate(-input.normalOS.z);
                return output;
            }

            // <summary>표시창을 벗어난 픽셀을 제거하고 남은 LED에 HDR 발광색을 출력합니다.</summary>
            half4 Frag(Varyings input) : SV_Target
            {
                // 프레임 위나 아래에 글자가 떠다니지 않도록 이동 후 높이로 자릅니다.
                clip(_WindowHalfHeight - abs(input.localHeight));
                // 기존 Bloom이 처리할 수 있는 밝기를 유지합니다.
                return half4(_EmissionColor.rgb * lerp(0.72h, 1.0h, input.facing), 1);
            }
            ENDHLSL
        }
    }
}
