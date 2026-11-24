Shader "Sushi2/Weather/Cloud Raymarch"
{
    Properties
    {
        _ShadowColor("Cloud Interior", Color) = (0.10,0.15,0.23,1)
        _LightColor("Moonlit Vapor", Color) = (0.30,0.36,0.44,1)
        _HazeColor("Distance Haze", Color) = (0.12,0.18,0.25,1)
        _Haze("Distance Blend", Range(0,1)) = 0.1
        _Density("Optical Thickness", Range(0,4)) = 1.2
        _Coverage("Cloud Coverage", Range(0.15,0.7)) = 0.4
        _NoiseScale("Billow Frequency", Vector) = (6,2.5,3,0)
        _Seed("Shape Variation", Float) = 0
        _WindVelocity("Wind Velocity (World Units Per Second)", Vector) = (0,0,0,0)
        _LowerFadeStart("Lower Fade Start (World Height)", Float) = -1000
        _LowerFadeEnd("Lower Fade End (World Height)", Float) = -999
        _MoonPosition("Moon World Position", Vector) = (0,0,0,0)
        _MoonScatter("Moon Scattering", Range(0,1)) = 0.2
        _FlashColor("Lightning Color", Color) = (0.6,0.7,1,1)
        _FlashStrength("Lightning Strength", Range(0,3)) = 0
        _FlashPosition("Lightning World Position", Vector) = (0,0,0,0)
        _FlashRadius("Lightning Spread", Float) = 35
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+8" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Back
            ZWrite Off
            Blend One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _ShadowColor, _LightColor, _HazeColor, _FlashColor;
            float4 _NoiseScale, _MoonPosition, _FlashPosition, _WindVelocity;
            float _LowerFadeStart, _LowerFadeEnd;
            float _Haze, _Density, _Coverage, _Seed, _MoonScatter, _FlashStrength, _FlashRadius;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 positionOS:TEXCOORD0; };

            /// <summary>볼륨의 경계 상자를 투영하며 실제 구름은 내부에서 계산합니다.</summary>
            Varyings Vert(Attributes input)
            {
                // 상자의 표면색이나 법선은 구름 명암에 사용하지 않습니다.
                Varyings output;
                output.positionCS=TransformObjectToHClip(input.positionOS.xyz);
                output.positionOS=input.positionOS.xyz;
                return output;
            }

            /// <summary>삼각함수 없이 고정된 3차원 난수를 계산합니다.</summary>
            float Hash(float3 p)
            {
                // 공간에 고정된 노이즈가 프레임마다 흔들리는 것을 방지합니다.
                p=frac(p*0.1031);
                p+=dot(p,p.yzx+33.33);
                return frac((p.x+p.y)*p.z);
            }

            /// <summary>3차원 격자를 부드럽게 보간합니다.</summary>
            float Noise(float3 p)
            {
                // 여덟 꼭짓점 사이를 보간해 볼륨 내부에 연속된 밀도를 만듭니다.
                float3 i=floor(p), f=frac(p);
                f=f*f*(3-2*f);
                return lerp(lerp(lerp(Hash(i),Hash(i+float3(1,0,0)),f.x),lerp(Hash(i+float3(0,1,0)),Hash(i+float3(1,1,0)),f.x),f.y),lerp(lerp(Hash(i+float3(0,0,1)),Hash(i+float3(1,0,1)),f.x),lerp(Hash(i+float3(0,1,1)),Hash(i+float3(1,1,1)),f.x),f.y),f.z);
            }

            /// <summary>큰 구름 덩어리와 작은 수증기를 결합한 밀도장을 만듭니다.</summary>
            float Density(float3 p)
            {
                // 경계 상자가 보이지 않도록 모든 축의 끝에서 밀도를 0으로 만듭니다.
                float3 q=p*2;
                float boundary=saturate((1-abs(q.x))*5)*saturate((1-abs(q.z))*5)*saturate((1-abs(q.y))*5);
                // 바람을 오브젝트 공간으로 바꿔 크기가 다른 구름도 일정한 월드 속도로 이동합니다.
                float3 drift=mul((float3x3)unity_WorldToObject,_WindVelocity.xyz*_Time.y)*2;
                float3 flow=q-drift;
                float3 v=flow*_NoiseScale.xyz+float3(_Seed,0,_Seed*.73);
                // 서로 다른 크기의 밀도 무늬로 윤곽을 침식합니다.
                float billow=Noise(v)*.65+Noise(v*2.03+7.1)*.25+Noise(v*4.11+13.7)*.10;
                // 큰 윤곽과 세부 무늬가 함께 이동하며 경계 상자는 그대로 유지합니다.
                float height=q.y+.18*sin(flow.x*5+_Seed);
                float cloud=saturate((billow-_Coverage-abs(height)*.30)*4.5);
                // 수평선 가까이에서는 부드럽게 사라져 수면과 구름이 붙어 보이지 않게 합니다.
                float worldHeight=TransformObjectToWorld(p).y;
                float lowerFade=smoothstep(_LowerFadeStart,max(_LowerFadeStart+.001,_LowerFadeEnd),worldHeight);
                return cloud*boundary*lowerFade;
            }

            /// <summary>시선이 통과하는 수증기의 두께와 빛을 누적하여 구름을 렌더링합니다.</summary>
            half4 Frag(Varyings input):SV_Target
            {
                // 카메라에서 경계 상자 내부로 들어가는 구간을 구합니다.
                float3 origin=TransformWorldToObject(_WorldSpaceCameraPos);
                float3 direction=normalize(input.positionOS-origin);
                float3 inverseDirection=rcp(direction+1e-7);
                float3 t0=(-.5-origin)*inverseDirection, t1=(.5-origin)*inverseDirection;
                float3 nearT=min(t0,t1),farT=max(t0,t1);
                float start=max(max(nearT.x,nearT.y),nearT.z);
                float end=min(min(farT.x,farT.y),farT.z);
                float stepLength=max(0,end-max(0,start))/40;
                float3 p=origin+direction*(max(0,start)+stepLength*.5);
                float3 accumulated=0;
                float transmission=1;
                // 윗부분의 광학 두께가 작을수록 달빛이 밝게 퍼집니다.
                [loop] for(int step=0;step<40;step++)
                {
                    float density=Density(p);
                    if(density>.002)
                    {
                        float above=Density(p+float3(-.025,.10,-.02))+Density(p+float3(-.06,.23,-.04))*.7;
                        float illumination=exp(-above*2.5)*.65+saturate(p.y+.5)*.25;
                        float3 color=lerp(_ShadowColor.rgb,_LightColor.rgb,saturate(illumination));
                        float3 world=TransformObjectToWorld(p);
                        float moonGlow=exp(-dot(world.xy-_MoonPosition.xy,world.xy-_MoonPosition.xy)/150);
                        color+=float3(.30,.25,.16)*moonGlow*_MoonScatter;
                        // 번개 주변의 구름만 밝게 하여 가게 전체가 번쩍이는 것을 피합니다.
                        float2 flashOffset=world.xy-_FlashPosition.xy;
                        float flashFalloff=exp(-dot(flashOffset,flashOffset)/max(1,_FlashRadius*_FlashRadius));
                        color=lerp(color,_HazeColor.rgb,_Haze)+_FlashColor.rgb*_FlashStrength*flashFalloff;
                        // 앞쪽부터 투과율을 누적해 얇은 가장자리가 배경에 자연스럽게 섞이게 합니다.
                        float opacity=1-exp(-density*_Density*stepLength*18);
                        accumulated+=transmission*opacity*color;
                        transmission*=1-opacity;
                        if(transmission<.015) break;
                    }
                    p+=direction*stepLength;
                }
                // 미리 곱한 알파로 겹치는 구름의 경계를 합성합니다.
                return half4(accumulated,1-transmission);
            }
            ENDHLSL
        }
    }
}
