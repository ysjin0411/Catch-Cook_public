Shader "Sushi2/Weather/Cloud Volume"
{
    Properties
    {
        _ShadowColor("Cloud Underside", Color) = (0.07,0.10,0.16,1)
        _LightColor("Cloud Upper Surface", Color) = (0.24,0.30,0.39,1)
        _HazeColor("Distance Haze", Color) = (0.14,0.23,0.30,1)
        _Haze("Haze Amount", Range(0,1)) = 0.2
        _RimStrength("Silver Edge", Range(0,0.3)) = 0.035
        _Density("Cloud Density", Range(0,1)) = 0.88
        _LightDirection("Moonlight Direction", Vector) = (-0.4,0.8,-0.3,0)
        _FlashColor("Lightning Color", Color) = (0.6,0.7,1,1)
        _FlashStrength("Lightning Strength", Range(0,3)) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+8" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Back
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _ShadowColor, _LightColor, _HazeColor, _FlashColor;
            float4 _LightDirection;
            float _Haze, _RimStrength, _FlashStrength, _Density;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 normalWS:TEXCOORD0; float3 positionWS:TEXCOORD1; float3 positionOS:TEXCOORD2; };

            /// <summary>실제 구름 메시의 위치와 표면 방향을 전달합니다.</summary>
            Varyings Vert(Attributes input)
            {
                // 메시를 월드 공간과 화면 공간으로 변환합니다.
                Varyings output;
                output.positionWS=TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS=TransformWorldToHClip(output.positionWS);
                output.normalWS=TransformObjectToWorldNormal(input.normalOS);
                // 표면의 미세한 밀도 차이는 이미지 대신 3차원 좌표에서 계산합니다.
                output.positionOS=input.positionOS.xyz;
                return output;
            }

            /// <summary>고정된 공간 좌표를 구름 밀도용 난수로 변환합니다.</summary>
            float Hash(float3 p)
            {
                // 카메라가 움직여도 무늬가 표면에 고정되도록 합니다.
                return frac(sin(dot(p,float3(127.1,311.7,74.7)))*43758.5453);
            }

            /// <summary>연속적인 3차원 밀도 무늬를 계산합니다.</summary>
            float Noise(float3 p)
            {
                // 격자 모서리의 값을 부드럽게 보간합니다.
                float3 i=floor(p),f=frac(p);
                f=f*f*(3-2*f);
                return lerp(lerp(lerp(Hash(i),Hash(i+float3(1,0,0)),f.x),lerp(Hash(i+float3(0,1,0)),Hash(i+float3(1,1,0)),f.x),f.y),lerp(lerp(Hash(i+float3(0,0,1)),Hash(i+float3(1,0,1)),f.x),lerp(Hash(i+float3(0,1,1)),Hash(i+float3(1,1,1)),f.x),f.y),f.z);
            }

            /// <summary>구름의 입체 표면 방향에 따라 저녁 명암과 은은한 가장자리 빛을 계산합니다.</summary>
            half4 Frag(Varyings input):SV_Target
            {
                // 실제 메시 법선을 기준으로 윗면과 아랫면의 명암을 구분합니다.
                float3 normal=normalize(input.normalWS);
                float diffuse=saturate(dot(normal,normalize(_LightDirection.xyz))*.6+.4);
                float upper=saturate(normal.y*.5+.5);
                half3 color=lerp(_ShadowColor.rgb,_LightColor.rgb,saturate(diffuse*.7+upper*.3));
                // 시선에 비스듬한 구름 가장자리에 약한 달빛을 더합니다.
                float3 view=normalize(GetWorldSpaceViewDir(input.positionWS));
                float rim=pow(1-saturate(dot(normal,view)),3)*upper;
                color+=_LightColor.rgb*rim*_RimStrength;
                // 입체 표면에 서로 다른 크기의 밀도 차이를 더합니다.
                float density=Noise(input.positionOS*1.2)*.65+Noise(input.positionOS*3.1)*.35;
                color*=.84+density*.22;
                // 먼 구름이 기존 전역 Fog에 완전히 지워지지 않도록 별도 거리 색을 적용합니다.
                color=lerp(color,_HazeColor.rgb,_Haze);
                color+=_FlashColor.rgb*_FlashStrength;
                // 옆면으로 갈수록 두께가 얇아 보이게 해 딱딱한 조각 같은 윤곽을 없앱니다.
                float thickness=smoothstep(0,.42,saturate(dot(normal,view)));
                float alpha=thickness*_Density*(.72+.28*density);
                return half4(color,alpha);
            }
            ENDHLSL
        }
    }
}
