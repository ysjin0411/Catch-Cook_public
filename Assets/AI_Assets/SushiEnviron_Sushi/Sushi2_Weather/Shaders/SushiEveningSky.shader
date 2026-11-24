Shader "Sushi2/Weather/Evening Sky"
{
    Properties
    {
        _TopColor("Upper Sky", Color) = (0.035,0.065,0.16,1)
        _HorizonColor("Horizon", Color) = (0.2,0.23,0.34,1)
        _GradientPower("Gradient Power", Range(0.2,4)) = 1
        _GradientStart("Horizon Blend Start", Range(0,1)) = 0
        _GradientEnd("Upper Sky Blend End", Range(0,1)) = 1
        _HorizonHazeColor("Horizon Haze Color", Color) = (0.16,0.22,0.30,1)
        _HorizonHazeStrength("Horizon Haze Strength", Range(0,1)) = 0
        _HorizonHazeRange("Horizon Haze Range (Start, Peak, End)", Vector) = (0.29,0.36,0.56,0)
        _StarStrength("Stars", Range(0,1)) = 0.2
        _FlashColor("Lightning Flash Color", Color) = (0.55,0.65,1,1)
        _FlashStrength("Lightning Flash Strength", Range(0,2)) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Off
            ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _TopColor, _HorizonColor, _FlashColor, _HorizonHazeColor;
            float4 _HorizonHazeRange;
            float _HorizonHazeStrength;
            float _GradientPower, _StarStrength, _FlashStrength, _GradientStart, _GradientEnd;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };

            /// <summary>하늘 Quad의 위치와 UV를 화면에 전달합니다.</summary>
            Varyings Vert(Attributes input)
            {
                // 위치를 투영하고 원본 UV를 보존합니다.
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            /// <summary>저녁 그라데이션, 고정된 별, 선택적인 번개 섬광을 표시합니다.</summary>
            half4 Frag(Varyings input):SV_Target
            {
                // 아래쪽 수평선에서 위쪽 하늘까지 색을 보간합니다.
                // 먼 수면의 끝까지 수평선 색을 유지해 직선 경계가 생기지 않도록 합니다.
                float height = pow(saturate((input.uv.y-_GradientStart)/max(.001,_GradientEnd-_GradientStart)), _GradientPower);
                half3 sky = lerp(_HorizonColor.rgb, _TopColor.rgb, height);
                // 수면 경계에서는 기본색을 유지하고 그 위쪽 하늘에만 부드러운 밝기 차이를 만듭니다.
                float haze=smoothstep(_HorizonHazeRange.x,_HorizonHazeRange.y,input.uv.y)*(1-smoothstep(_HorizonHazeRange.y,_HorizonHazeRange.z,input.uv.y));
                sky=lerp(sky,_HorizonHazeColor.rgb,haze*_HorizonHazeStrength);
                // 규칙적인 격자마다 위치가 다른 작은 별을 드물게 선택합니다.
                float2 grid = input.uv * float2(160,90);
                float2 cell = floor(grid);
                float seed = frac(sin(dot(cell,float2(127.1,311.7))) * 43758.5453);
                float2 center = float2(frac(seed*17.1),frac(seed*31.7))*.6+.2;
                float star = (1-smoothstep(.025,.13,length(frac(grid)-center))) * step(.993,seed);
                // 별은 수평선 가까이에서 사라지고 섬광은 기본적으로 꺼져 있습니다.
                sky += star * _StarStrength * smoothstep(.25,.65,input.uv.y);
                // 수면 끝에서 섬광이 갑자기 끊어지지 않도록 상공으로 갈수록 밝게 합니다.
                sky += _FlashColor.rgb * _FlashStrength * smoothstep(.34,.70,input.uv.y);
                return half4(sky,1);
            }
            ENDHLSL
        }
    }
}
