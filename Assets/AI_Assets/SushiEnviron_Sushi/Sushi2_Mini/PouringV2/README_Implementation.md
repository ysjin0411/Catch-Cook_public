# Sushi2 공용 음료 따르기 — 적용 안내

> 최신 구현은 **FluidV3/START_HERE.md**를 먼저 읽으세요. 입자 간 압력·점성, 컵 내부 입자 유지, 병 기울이기와 발사점 연결, 테스트 프리팹 생성 도구가 추가되었습니다. 아래 내용은 이전 V2 방식의 기록이며 V3 코드와 섞어 연결하지 않습니다.

최신 요청에 따른 디자인은 **Sprites/Bottle_Opaque_Wide.png**입니다. 넓은 몸통의 불투명 공용 병입니다.
아래 탭/레버 배치와 예제 코드는 이전 탭 방식의 설명입니다. 이번에는 병 이미지 디자인만 추가했으며, 병 기울이기와 병 입구 위치를 따라가는 발사점 연결은 아직 예제에 적용하지 않았습니다.
이전 디자인은 Sprites/Tap_Body_Modern.png + Tap_Lever_Modern.png입니다.
도자기형 Dispenser_Body/Lever는 폐기한 초안입니다. 신규 구성에 연결하지 마세요.
V1, 기존 씬, 기존 C# 스크립트, 프로젝트 설정은 수정하지 않았습니다.
CodeSamples는 자동 컴파일되지 않는 .txt 파일입니다. 사용자가 복사·이름 변경 후 직접 연결합니다.

## 1. 참고 영상에서 확인한 것과 이번 구현

첨부 MP4에서 0·2·4·6초 장면을 파일로 추출해 확인했습니다.
4초 장면은 요철이 있는 줄기가 컵의 액체와 연결되고, 6초 장면은 줄기가 끊긴 뒤 액체가 남아 있습니다.
이 파일의 Windows 디코더 표시 길이는 약 9.03초였습니다. 원작 내부 코드를 확인한 것은 아닙니다.

이 예제는 실제 Sphere GameObject + Rigidbody로 물방울을 낙하시킵니다.
구체의 MeshRenderer는 끄고 위치를 UI 셰이더에 전달합니다.
셰이더가 각 구체 주변의 밀도를 합산하여 연결 부위를 그립니다.
컵의 고인 액체도 같은 밀도 계산에 포함되어 낙하 중인 방울과 이어집니다.

이는 **3D 물방울의 움직임 + 정면 2D 화면에서의 메타볼 합성**입니다.
3D 메시를 실제로 용접하거나 SPH 유체의 압력·점도를 계산하는 솔버는 아닙니다.
고인 액체는 모든 물방울을 계속 쌓는 대신, 도착한 양으로 수위를 정하고 파동을 얹습니다.
따라서 고정 정면 미니게임에 적합하며, 카메라를 회전해서 보는 물에는 맞지 않습니다.
물방울 스프라이트 애니메이션이나 직사각형 물줄기 이미지는 새 구성에 사용하지 않습니다.

## 2. 파일과 역할

- Sprites/Tap_Body_Modern.png: 공용 불투명 검정·스테인리스 탭.
- Sprites/Tap_Lever_Modern.png: 회전용 별도 레버.
- Sprites/Cup_Back.png / Cup_Front.png: 액체 뒤/앞에 놓는 컵.
- Sprites/Cup_Mask.png: 다른 방식으로 확장할 때 쓸 내부 마스크. 이번 셰이더에는 불필요.
- Sprites/Gauge_Track.png / Gauge_Triangle.png / Target_Band.png: 입력 게이지와 목표 수위.
- Models/Cup_Physics.obj: 윗면이 열린 컵의 3D 충돌용 메시.
- CodeSamples/PouringModel.cs.txt: 입력 압력, 유량, 미도착량, 수위, 결과 판정.
- CodeSamples/PouringController.cs.txt: Input System 입력, 모델 갱신, 결과 이벤트.
- CodeSamples/PouringView.cs.txt: 게이지, 목표 영역, 레버와 액체 시스템 연결.
- CodeSamples/MetaballFluidRig.cs.txt: 48개 3D 물방울 풀, 실제 도착 집계, 셰이더 데이터 전달.
- CodeSamples/UI_MetaballFluid.shader.txt: 물방울 및 수면의 메타볼 합성.
- Validation.txt: 컴파일과 모델 검사 결과.

FX_Drop, FX_Splash, Stream_Core, Liquid_Fill, Liquid_Surface는 이전 방식의 보조 리소스입니다.
이번 구성에 필수 파일이 아니며, 실제 방울은 Unity Sphere로 런타임 생성됩니다.
BuildAssets.ps1.txt는 첫 UI 리소스를 만든 도구 기록입니다. 현대식 탭이나 메타볼 코드를 만드는 도구는 아닙니다.

## 3. 조작과 판정

압력 p는 0~1입니다.
- 자동 이동: p = Max(0, p - 0.45 × deltaTime)
- 새로 누른 입력: p = Min(1, p + 0.12)
- 입력 게이지 노란 구간: 0.40~0.60
- 왼쪽 구간 유량: 0에서 초당 0.12까지 증가.
- 노란 구간 유량: 초당 0.12 유지.
- 오른쪽 구간 유량: 초당 0.34까지 증가.
- 컵 목표 수위: 0.68~0.78, 제한 시간: 12초.

유량의 단위는 '컵 전체 높이에 대한 초당 정규화 증가량'입니다.
컵의 실제 리터 단위 부피를 적분한 모델은 아닙니다.
게임용 수위 판정이 목적이므로 컵의 테이퍼와 관계없이 같은 높이 기준을 사용합니다.

유량을 곧바로 수위에 더하지 않습니다.
유량 → 발사 대기량 → 방울별 할당량 → 컵 도착 → 실제 수위 순서로 이동합니다.
풀에 빈자리가 없으면 발사 대기량을 보존합니다. 버리지 않습니다.
밖으로 흘린 방울은 수위에 포함하지 않습니다.

### 성공 방식 선택

RequireStoppedForSuccess = true (기본, 권장):
연타를 멈추고 압력이 0이 되어 물이 모두 도착한 뒤, 0.2초 정지 상태에서 수위가 목표 안이면 성공.
목표 상한을 넘으면 즉시 실패. 부족하면 다시 누를 수 있습니다.
입력을 멈춘 뒤에도 감쇠 중 발생한 물과 공중의 물방울이 더 들어오므로 조금 일찍 멈춥니다.

RequireStoppedForSuccess = false:
사용자가 설명한 것처럼 목표 하한까지 채우면 즉시 성공합니다.
이 모드는 과한 연타도 목표를 지나면서 성공하므로 상한 초과를 이용한 난도가 사실상 없어집니다.
예제에서 선택지를 명시적으로 제공하며, 다른 실패 조건을 몰래 추가하지 않습니다.

## 4. 코드 가져오기와 임포트

1. CodeSamples의 .cs.txt 네 개를 직접 Scripts 폴더에 복사하고 마지막 .txt를 제거합니다.
2. UI_MetaballFluid.shader.txt를 Shader 폴더에 복사하고 마지막 .txt를 제거합니다.
3. 클래스 이름과 .cs 파일 이름을 일치시킵니다. namespace는 SushiPouringSample입니다.
4. asmdef를 쓰는 폴더라면 Unity.InputSystem, UnityEngine.UI 참조를 확인합니다.
5. PNG는 Sprite (2D and UI), Single, Alpha Is Transparency On, Bilinear, Compression None으로 시작합니다.
6. 그림 전체의 투명 여백을 유지합니다. 임의로 Trim하면 아래 좌표 기준이 바뀝니다.
7. Cup_Physics.obj는 Scale Factor 1, Mesh Compression Off로 시작합니다.
8. 오브젝트 메시의 실제 크기를 확인합니다: X/Z 바깥 반지름 최대 0.92, Y -2.5~0.4.
9. 모든 새 파일과 Unity가 생성한 .meta를 함께 팀 브랜치에 커밋합니다.

## 5. UI 계층 만들기

Canvas는 Screen Space - Overlay, CanvasScaler는 프로젝트 설정을 따라갑니다.
미니게임 내부 기준 RectTransform은 500×600입니다.
크기를 바꾸려면 내부 좌표는 유지하고 이 패널의 부모를 균일하게 Scale합니다.
모든 장식 Image와 RawImage의 Raycast Target은 끕니다.

Canvas
  PourPanel (RectTransform 500×600, PouringController, PouringView, MetaballFluidRig)
    Background
    TapBody (Image)
      LeverPivot (빈 RectTransform)
        LeverArt (Image)
    CupBack (Image)
    FluidOutput (RawImage)
    CupFront (Image)
    TargetRegion (빈 RectTransform)
      TargetBand (Image)
    PressureTrack (Image)
      IdealBand (Image)
      Triangle (Image)

그림 순서는 위 목록대로입니다. FluidOutput은 컵 뒤판보다 앞, 앞판보다 뒤입니다.
FluidOutput에는 Mask나 RectMask2D를 걸지 않습니다.
이 셰이더는 컵 자체의 경계를 계산하며, 전체 물줄기까지 컵 마스크로 자르면 출구의 물이 사라집니다.
UI 머티리얼은 FluidRig가 런타임에 복제해 소유합니다. Shader 필드에 셰이더 에셋을 직접 연결합니다.
이 예제 셰이더는 외부 UI stencil 마스크 지원을 의도적으로 포함하지 않습니다.

### RectTransform 기준

표에 따로 쓰지 않은 앵커와 피벗은 중앙 (0.5,0.5), Rotation 0, Scale 1입니다.

| 오브젝트 | 크기 | 중앙 기준 위치 | 설정 |
|---|---|---|---|
| FluidOutput | 500×600 | (0,0) | RawImage, UV Rect (0,0,1,1), Color white |
| CupBack / CupFront | 240×320 | (0,-100) | 각각 해당 PNG |
| TargetRegion | 168×280 | (0,-100) | 컵 내부의 아래 -240~위 40 |
| TargetBand | 런타임 앵커 결정 | offset 0 | Target_Band, 흰색 tint, alpha 약 0.25 |
| PressureTrack | 400×24 | (0,-275) | Gauge_Track |
| IdealBand | 런타임 앵커 결정 | offset 0 | Target_Band, alpha 1 |
| Triangle | 24×18 | 런타임 결정 | Gauge_Triangle |
| TapBody | 420×420 | 약 (-122,110) | Tap_Body_Modern, Preserve Aspect |
| LeverPivot | 0×0 | TapBody 기준 약 (-13,135) | 회전 기준점 |
| LeverArt | 140×140 | (0,0) | pivot 약 (0.485,0.286) |

탭 출구는 패널 중앙 기준 약 (0,190)에 맞춥니다.
생성 이미지의 가장자리 오차가 있으므로 실제 노즐 끝에 맞춰 TapBody 위치를 마지막으로 조정합니다.
LeverArt는 투명 여백 안에서 둥근 연결부 중심이 피벗에 오도록 조정합니다.
이 수치는 현재 새 탭 이미지 기준의 시작값입니다.

TargetRegion은 실제 액체 범위와 같습니다.
그 자식 TargetBand의 Y 앵커 0.68~0.78이 모델 판정과 정확히 대응합니다.
입력의 IdealBand와 수위의 TargetBand는 서로 다른 오브젝트입니다.

## 6. 3D 물리 공간 만들기

Canvas와 별개로 빈 GameObject FluidPhysicsRoot를 만듭니다.
기존 매장 물리와 겹치지 않는 위치, 예를 들어 (1000,1000,0)에 놓습니다.
회전 (0,0,0), Scale (1,1,1)을 유지합니다.
이동 중에는 이 루트를 움직이거나 회전하지 않습니다.

그 아래 Cup_Physics.obj를 배치합니다.
- localPosition (0,0,0), localRotation (0,0,0), localScale (1,1,1).
- MeshRenderer 비활성.
- MeshCollider 추가, 해당 메시 연결, Convex Off.
- Rigidbody를 붙이지 않는 정적 컵.
- 3D Physics Simulation Mode는 일반 Fixed Update 기준.
- 물방울 Rigidbody와 충돌할 수 있는 레이어를 사용합니다.
- Physics.gravity가 아래쪽인 일반 중력 설정을 전제로 합니다.

FluidRig는 이 루트 아래에 48개의 Sphere와 Rigidbody를 만듭니다.
Z 위치와 회전을 고정해서 정면 2D 게임처럼 움직이게 합니다.
방울끼리는 충돌을 제외하므로 딱딱한 구슬 더미가 되지 않습니다.
컵의 정적 MeshCollider에는 충돌할 수 있습니다.
컵 내부 수면에 도달하면 방울을 회수하고 그 방울의 양을 모델에 전달합니다.

FluidRig Inspector:
- Output: FluidOutput RawImage
- Fluid Shader: SushiMini/UI Metaball Fluid 셰이더
- Physics Root: FluidPhysicsRoot
- Nozzle: (0,1.9)

월드 1단위는 패널 기준 100픽셀입니다.
노즐 (0,1.9)는 UI (0,190)에 대응합니다.
컵 내부는 y -2.4~0.4, 반너비 아래 0.72~위 0.84입니다.
컵이나 패널 비율을 바꿀 때 모델 수치는 그대로 두고, 메시·셰이더·FluidRig 좌표를 함께 변경해야 합니다.

## 7. 컴포넌트 연결과 입력

PouringView의 Fluid, LeverPivot, PressureMarker, IdealBand, TargetBand를 각각 연결합니다.
PouringController의 View를 연결합니다.
Input Actions에 Button 타입 액션을 준비합니다.
Tap Action에는 상호작용 키, Cancel Action에는 Escape 등 취소 키를 지정합니다.
연타 입력에는 Hold/SlowTap Interaction을 붙이지 않는 구성을 권장합니다.
Input System Update Mode는 Dynamic Update 기준입니다.

컨트롤러는 액션을 복제해서 사용합니다. 공유 플레이어 ActionMap을 끄거나 켜지 않습니다.
UI를 여는 키가 첫 연타로 처리되지 않도록, 시작 후 한 번 키를 놓아야 입력을 받습니다.
Time.timeScale == 0이면 진행도 멈춥니다. 일시정지 상태에서 미니게임만 진행하는 예제는 아닙니다.

독립 테스트:
- 플레이 모드 진입.
- PourPanel과 FluidRig를 활성화.
- PouringController 컴포넌트 메뉴의 Test / Begin Lemon Pour 실행.
- 키 연타 → 노란 구간 유지 → 입력 중지 → 방울 도착 → 수위 확인.
- 결과 확인은 Completed 이벤트에 연결하거나 디버거로 Result 확인.

음료 색 예시:
청포도 #BDD94D, 자몽 #F38A84, 레몬 #F2D65C, 맥주 #DDA23B, 차 #B6C96A.
Controller.Begin(Color)으로 시작할 때 색을 전달합니다.
맥주 거품, 차 김 등은 이번 최소 구현에 들어 있지 않습니다. 색상만으로 공용 동작을 사용합니다.

## 8. 기존 프로젝트와 연결할 위치

읽어본 기존 구조:
- SushiInteractor.Update: 키를 유지하는 동안 ISushiHoldInteraction.TickHoldInteract를 호출.
- GuestNPC.TickHoldInteract: DrinkInteractionTime을 채우면 CompleteServing 호출.
- ServingNPC도 같은 길게 누르기 인터페이스를 이용해 직원의 자동 음료 제공을 처리.

따라서 플레이어의 음료 분기에만 미니게임 진입을 추가해야 합니다.
기존 TickHoldInteract를 미니게임 도중 계속 실행하면 시간이 차서 먼저 제공되는 문제가 생깁니다.
직원의 자동 제공과 청소 상호작용은 별도 흐름으로 남깁니다.

권장 연결 순서:
1. 실제 접촉 중이고 음료를 기다리는 손님인지 확인.
2. 주문 ID 또는 주문 버전, 손님, 제공자 참조를 세션에 저장.
3. 대상의 제공권을 확보하여 직원이나 다른 플레이어가 동시에 제공하지 못하게 잠금.
4. 플레이어 이동과 기존 음료 Hold 처리를 막고 패널 활성화.
5. Completed를 구독한 후 Begin(주문 음료 색) 호출.
6. Success이면 같은 손님과 주문이 여전히 유효한지 확인한 뒤 전용 완료 API로 제공 확정.
7. 실패·취소·손님 이탈은 보상 없이 세션 취소.
8. 모든 종료 경로에서 잠금 반환, 이동 복구, 구독 해제, 패널 닫기.

완료 이벤트에서 TickHoldInteract에 거대한 deltaTime을 넣어 강제로 완료하지 마세요.
GuestNPC 쪽에 세션 소유권과 주문 일치를 검증하는 완료 메서드를 별도로 두는 편이 명확합니다.
기존 CompleteServing의 접근 범위와 보상 처리 구조를 그대로 활용하되 외부에서 무조건 호출되게 열지는 마세요.
이 연결 코드는 팀의 플레이어 상태/주문 잠금 방식과 함께 수정해야 하므로 현재 파일에 자동 적용하지 않았습니다.

손님이 대기를 포기하거나 주문이 바뀌거나 제공자가 멀어졌다면 Controller.Cancel()을 호출합니다.
미니게임 도중 계속 움직일 수 있는 정책이라면 접촉 검사를 별도로 유지해야 합니다.
제공 완료를 알리는 이벤트만으로 실제 보상이 자동 지급되는 것은 아닙니다.

## 9. 튜닝과 한계

- 물줄기가 구슬처럼 끊김: _Balls의 영향 반경 0.21을 조금 증가하거나 Parcel을 감소.
- 너무 끈적하게 붙음: 영향 반경 감소 또는 Threshold 증가.
- 떨어질 때 너무 굵음: 영향 반경 감소. 물방울 그림을 축소하는 방식이 아닙니다.
- 연결은 되지만 표면이 무거움: Ripple 최대치와 감쇠 조정.
- 쉽게 넘침: MaxFlow 감소, DriftPerSecond 증가 또는 TargetMax 증가.
- 연타 피로: TapImpulse를 늘리면 같은 압력을 유지하는 초당 입력 횟수가 줄어듭니다.
- 표시가 느림: RawImage 면적 또는 최대 방울 수를 줄이고 프로파일링.
- PhysicsRoot 근처에 기존 맵 지형이 있으면 충돌할 수 있으므로 전용 공간 확보.
- 낙하 속도와 출구 위치를 크게 바꾸면 연타를 멈춘 후 들어오는 양도 달라집니다.

셰이더는 픽셀마다 최대 48개 영향력을 합산합니다.
현재 방식은 RenderTexture/블러용 추가 카메라가 필요 없는 단일 UI 패스입니다.
방울을 수백~수천 개로 늘릴 경우 밀도 RenderTexture를 누적하는 방식이 더 적합할 수 있습니다.
단순 화면 합성에서는 가까운 깊이의 방울만 다루는 것이 안전하므로 Z를 고정했습니다.

## 10. 확인한 것 / 아직 확인하지 않은 것

C# 네 파일: 프로젝트와 동일한 Unity 6000.0.68f1, UI, InputSystem DLL을 참조한 컴파일 통과.
모델: 도착 전 수위 유지, 물량 이동, 30/60/144 FPS에서 0.6초 도착 지연, 시간 초과, 과다 입력,
취소 후 불변, 즉시 성공 옵션 검사를 통과했습니다.
이 검사는 물리 엔진 자체를 재현한 테스트가 아닙니다.

기존 프로젝트를 실행·수정하지 않았으므로 실제 Unity 셰이더 컴파일, 그래픽 출력,
MeshCollider 충돌, 입력 연결 및 매장 보상 연동은 아직 검증하지 않았습니다.
완성된 씬이나 연결된 프리팹이 아니라 직접 적용할 수 있는 리소스와 컴파일 확인된 예제입니다.

참고 문서:
- https://docs.unity.com/en-us/engine/6000.0/script-reference/unityengine/rigidbody
- https://docs.unity3d.com/Packages/com.unity.inputsystem@1.13/api/UnityEngine.InputSystem.InputAction.html

