Sushi2 저녁 날씨 재료
===================

2026-11-11: Assets/01.Scenes/Sushi 2.unity에 Cloudy 저녁 화면을 배치했습니다.
Hierarchy의 -----Weather----- / Sushi_CloudyEvening 아래에서 편집할 수 있습니다.
Sunny/Rainy 전환과 번개 자동 재생은 아직 연결하지 않았습니다.
가게 구조, 카메라 위치·화각, 기존 조명은 유지했습니다.

현재 Cloudy 배치 (3D 프리팹으로 교체)
- Sky_Backdrop: 섬과 바다 뒤의 저녁 그라데이션 Quad.
- Moon3D_Cloudy: 발광 구체와 별도 달무리로 구성한 PF_Moon3D_Cloudy 프리팹.
- Clouds3D: 실제 두께가 있는 구름 메시 프리팹 5개를 서로 다른 크기·깊이에 배치.
- 이전 Moon_Cloudy / Moon_Halo / Clouds_Far / Clouds_Near는 복구용으로 남겨두고 비활성화했습니다.
- Moonlight_OnWater: 달 아래 수평면에 투영된 약한 수면 빛 메시.
- Materials/CloudyScene: 이 씬의 하늘·구름 조정용 별도 머티리얼.
- Materials/Cloud3D: 3D 구름 근경·원경 재료, 밝은 달 표면·달무리 재료.
- Meshes/Cloud3D: 여러 타원체의 밀도 경계를 추출해 만든 연결된 입체 메시 3종.
- Prefabs/PF_CloudBank_A, B, C: 이미지 텍스처를 사용하지 않는 3D 구름 프리팹.
- Prefabs/PF_Moon3D_Cloudy: LuminousSphere(실제 구체)와 SoftHalo(빛 효과 Quad).
- 바다: M_Water_Cloudy_Evening, Water Color Multiplier 0.19.
- Sushi_OutdoorRain은 비활성화했고 초기 비 설정도 false로 바꿨습니다.
- 전용 런타임 스크립트를 추가하지 않은 정적 배치입니다.
- 플레이 모드 프레임 진행, 배경 유지, 비 비활성 상태를 확인했습니다.

3D 구름·달 편집
- 구름은 MeshFilter/MeshRenderer를 가진 프리팹입니다. Scene View에서 회전해 두께를 확인할 수 있습니다.
- 구름 표면의 법선으로 명암을 계산하며 3차원 밀도 무늬와 가장자리 투명도로 윤곽을 부드럽게 합니다.
  볼류메트릭 레이마칭 구름이나 기상 시뮬레이션은 아닙니다.
- 각 구름 인스턴스의 Transform으로 위치·길이·두께를 조정할 수 있습니다.
- M_Cloud3D_Near / Far: Cloud Density는 불투명도, Haze Amount는 원경 색 혼합입니다.
- M_Moon3D_Cloudy: Luminous Surface 1.3, Lunar Markings 0.08.
  달 표면은 불투명 구체이며 밝기는 표면 재료에서 조절합니다.
- M_Moon3D_Halo: Intensity로 달 주변 번짐을 별도로 조절합니다.
- M_Cloudy_SceneSky: Horizon Blend Start 0.36, Upper Sky Blend End 0.90.
  먼 수면의 끝까지 수평선 색을 유지해 하늘과 바다 사이의 직선 색 경계를 줄였습니다.
- 메시 생성 코드는 에디터에서 한 번만 실행했습니다. 게임 실행 중 메시를 다시 만들지 않습니다.
- 달을 다른 이미지로 바꾸고 싶다면 비활성화해 둔 기존 달 Quad를 선택적으로 재사용할 수 있습니다.

구성
- Textures: 투명 PNG 4개 (공통 달, 얇은 구름, 먹구름, 번개)
- Materials/Sky: Sunny / Cloudy / Rainy 하늘 Quad용 그라데이션
- Materials/Moon: 같은 달 텍스처를 사용하는 날씨별 밝기·투명도 변형
- Materials/Clouds: 맑음의 옅은 구름 / 흐림 / 먹구름
- Materials/Effects: 달무리 3종, 달빛 수면 효과 3종, 구름 섬광, 번개, 번개 수면 효과
- Materials/Water: 기존 MV_Lake_Sushi_* 머티리얼의 독립 복사본
- Shaders: URP용 하늘 / 투명 레이어 / 가산 빛 셰이더

달은 구름과 분리되어 있습니다. 날씨에 맞는 M_Moon_*를 교체하거나
Texture (RGBA)에 다른 달 PNG를 지정할 수 있습니다.
월령과 날씨를 독립적으로 구성할 수 있도록 원본 달 이미지에 구름을 합성하지 않았습니다.

기존 이미지 재료를 별도로 배치할 때 (현재 Cloudy의 구름·달은 위 3D 프리팹 사용)
1. Sky는 카메라를 향하는 큰 Quad에 적용합니다. 아래쪽 UV가 수평선 색입니다.
   Skybox 전용 머티리얼이 아니므로 Lighting의 Skybox 슬롯에 넣지 않습니다.
   하늘 Quad는 섬과 바다 뒤, 카메라 Far Clip 안에 놓고 화면보다 넓게 맞춥니다.
   하늘 아래 경계는 수평선 뒤로 살짝 숨깁니다. 모든 Quad의 Collider는 제거합니다.
2. Moon은 작은 별도 Quad에 적용합니다. 왼쪽 기둥과 가운데 간판 사이가 권장 위치입니다.
   달과 달무리의 중심을 일치시키고 달무리를 조금 더 크게 만듭니다.
3. Clouds는 달 앞, 섬 뒤에 놓습니다. 구름 그림의 가로세로 비율은 3:1입니다.
   Cloudy의 달을 일부 가리려면 구름의 위치를 조절합니다.
   Rainy는 먹구름을 2장 정도 높이·크기를 달리해서 배치하면 깊이감이 생깁니다.
   Sunny의 구름은 생략해도 됩니다. 모든 하늘 계열 재료는 조명에 영향받지 않습니다.
4. 바다의 기존 Surface Renderer에는 Materials/Water 중 해당 날씨를 할당합니다.
   기존 셰이더와 텍스처 참조를 재사용하므로 이 폴더만 단독 배포하면 물 재료 의존성이 빠집니다.
   Cloudy는 씬 배치 과정에서 밝기 배수를 0.19로 낮췄습니다.
   Sunny와 Rainy 복사본에는 저녁용 색·잔물결 시작값을 적용했습니다.
5. MoonReflection / LightningReflection은 실제 실시간 반사가 아닌 별도 수면 연출입니다.
   수면 바로 위에 Quad를 눕혀 낙뢰 또는 달 아래 먼바다에 놓고 길이·폭을 조절합니다.
   실제 반사와 중복되면 강도를 줄이거나 생략합니다.

카메라 Solid Color와 Fog
- 계속 매 프레임 바꿀 필요가 없습니다. 날씨를 적용할 때 한 번 설정합니다.
- 하늘 Quad가 보이는 영역은 하늘 머티리얼이 색을 결정합니다.
- 카메라 배경색은 하늘 바깥 빈 영역의 보조 색으로 사용합니다.
- 권장 시작값 RGB (0~1):
  Sunny:  (0.19, 0.22, 0.33)
  Cloudy: (0.14, 0.23, 0.30) - 현재 씬의 배경색과 Fog 색
  Rainy:  (0.09, 0.14, 0.19)
- Fog 색도 수평선 색과 함께 조정하되 최종 색은 Game View에서 확인합니다.
- 하늘 셰이더에는 Fog를 다시 적용하지 않아 먼 거리에서도 구름·달을 구분할 수 있습니다.
- 위 수치는 자동 적용되지 않습니다. WeatherPresets.json은 연결 작업용 참고 데이터입니다.

Rainy 번개 재료 사용
- M_Lightning_Bolt: 번개 PNG. 원본 비율 2:3. 구름 밑에서 먼바다까지 배치합니다.
- M_Clouds_Rainy의 Cloud Flash Strength: 구름 일부가 밝아지는 효과.
  Flash Center는 구름 Quad 내부 UV(0~1), Radius는 밝아지는 범위입니다.
- M_Sky_Rainy의 Lightning Flash Strength: 하늘 전체의 약한 섬광.
- M_CloudFlash: 선택적으로 더하는 부드러운 청백색 빛 Quad.
- M_LightningReflection: 수면에서 잠깐 밝아지는 별도 빛 효과.
- 평소에는 번개·섬광·번개 수면 효과 오브젝트를 꺼둡니다.
  섬광 머티리얼 값은 나중에 런타임 인스턴스 또는 MaterialPropertyBlock으로 조절합니다.
  공유 머티리얼 에셋의 값을 매번 덮어쓰지 않도록 합니다.
- 추천 순서: 약한 구름 섬광 -> 강한 섬광과 번개 -> 잔광 소멸 -> 지연된 천둥.
- Rainy를 벗어날 때 번개 예약과 진행 중인 섬광을 취소하고 밝기를 원상복구해야 합니다.
- 비는 기존 SushiRainEffect를 재사용해 Rainy에서만 켭니다.
  현재 컴포넌트의 초기 비 설정도 날씨에 맞춰 별도로 연결해야 합니다.
- 자동 비/번개 재생 스크립트와 천둥 음원은 이번 재료 묶음에 포함하지 않았습니다.

팀 작업
- 모든 새 파일과 .meta를 함께 커밋합니다.
- 바다의 원본 머티리얼을 이동하거나 수정하지 않았습니다.
- Cloudy 씬 연결까지 완료했습니다. Sunny/Rainy 연결은 후속 작업입니다.

이미지 출처 및 생성 프롬프트 요약
- OpenAI 내장 image_gen 도구로 생성한 프로젝트용 투명 텍스처입니다.
- Moon: low-poly seaside evening game, single ivory nearly-full moon,
  subtle faceted lunar shading and restrained halo, transparent background, no sky/clouds.
- Thin clouds: isolated wide 3:1 bank of wispy slate-gray evening clouds,
  softly faceted low-poly forms, alpha gaps and feathered edges, no moon/sea.
- Storm clouds: isolated wide 3:1 layered slate-blue thundercloud bank,
  heavy faceted volumes, transparent perimeter, no lightning/rain baked in.
- Lightning: one thin branching white-blue cloud-to-sea bolt,
  white core and subtle blue-violet glow, portrait transparent texture, no scene.
- 하늘 그라데이션·별·빛·수면 반짝임은 이미지 합성이 아닌 제공된 셰이더가 계산합니다.
