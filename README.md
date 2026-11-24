# Catch & Cook

바다에서 물고기를 포획하고, 확보한 재료로 초밥집을 운영하는 Unity 팀 프로젝트입니다.

오전·오후에는 잠수와 사냥을 진행하고, 저녁에는 메뉴 준비부터 주문·조리·서빙·정산까지 매장을 운영합니다.

이 저장소는 팀 프로젝트 중 제가 담당한 **물고기 시스템과 초밥집 운영 시스템의 코드 및 설정 데이터**를 공개한 포트폴리오 저장소입니다.

[프로젝트 상세 소개 · 시연 영상 · 문제 해결 과정]

## 프로젝트 개요

| 항목 | 내용 |
| --- | --- |
| 개발 기간 | 2026.10.07 ~ 2026.11.22 |
| 팀 구성 | 클라이언트 개발자 3명, UI 디자이너 2명 |
| 개발 환경 | Unity 6000.0.68f1 · C# · URP |
| 담당 영역 | 물고기 행동·생성, 초밥집 운영 로직, 음료 미니게임, 초밥집 씬 구성 |
| 협업 방식 | Git·GitHub 기능별 브랜치, 공용 인터페이스·이벤트 기반 연동 |

## 담당 기능과 주요 코드

### 1. 물고기 시스템

물고기의 설정 데이터, 실행 중 상태, 이동·추적, 외형 표현을 분리했습니다.

- 체력·피해·포획 조건과 행동 상태 관리
- 이동 패턴과 생성 영역에 따른 어종 배치
- 패턴 갱신 및 재생성 스케줄 관리
- 팀 공용 풀과 공격·작살 시스템 연동
- 포획 대상과 배경 물고기의 처리 구조 분리

| 주요 코드 | 역할 |
| --- | --- |
| [Fish.cs](Assets/02.Scripts/Fish/Fish.cs) | 상태와 이벤트를 이동·표현에 연결 |
| [FishModel.cs](Assets/02.Scripts/Fish/FishModel.cs) | 체력, 행동 상태, 피해·포획 규칙 관리 |
| [FishMover.cs](Assets/02.Scripts/Fish/FishMover.cs) | 이동과 장애물 검사 |
| [FishSpawner.cs](Assets/02.Scripts/Fish/FishSpawner.cs) | 그룹별 생성 설정과 패턴 실행 관리 |
| [FishSpawnPattern.cs](Assets/02.Scripts/Fish/Patterns/FishSpawnPattern.cs) | ScriptableObject 기반 이동 패턴의 공통 구조 |
| [BackgroundFishSchool.cs](Assets/02.Scripts/Fish/BackgroundFishSchool.cs) | 배경 물고기의 무리 단위 이동과 지형 회피 |

**설계 포인트**

같은 패턴 에셋을 여러 생성 그룹에서 재사용하면서 실행 상태가 섞이지 않도록 런타임 복제본을 사용했습니다. 배경 물고기는 개체별 상호작용 처리 대신 부모 Transform을 이동시키는 무리 단위 구조로 구성했습니다.

### 2. 초밥집 운영 시스템

메뉴 준비부터 손님의 주문, 조리, 운반, 서빙, 영업 정산까지 이어지는 흐름을 구현했습니다.

- 판매 메뉴와 수량 설정
- 재료 재고와 메뉴 예약 관리
- 손님 입장·착석·주문·식사·퇴장 상태 관리
- 조리 직원과 서빙 직원의 작업 처리
- 플레이어의 음식 운반 및 상호작용
- 메뉴·직원 강화 데이터와 영업 결과 연동

| 주요 코드 | 역할 |
| --- | --- |
| [SushiManager.cs](Assets/02.Scripts/Sushi/SushiManager.cs) | 초밥집 운영 흐름 관리 |
| [SushiListManager.cs](Assets/02.Scripts/Sushi/SushiListManager.cs) | 메뉴 목록 관리 |
| [SushiListManager.Reservations.cs](Assets/02.Scripts/Sushi/SushiListManager.Reservations.cs) | 메뉴 예약 처리 |
| [SushiIngredientStock.cs](Assets/02.Scripts/Sushi/SushiIngredientStock.cs) | 재료 재고 관리 |
| [GuestNPC.cs](Assets/02.Scripts/Sushi/NPC/GuestNPC.cs) | 손님의 상태와 주문·서비스 흐름 |
| [CookerNPC.cs](Assets/02.Scripts/Sushi/NPC/CookerNPC.cs) | 조리 직원 행동 |
| [ServingNPC.cs](Assets/02.Scripts/Sushi/NPC/ServingNPC.cs) | 서빙 직원 행동 |

**설계 포인트**

메뉴·재료 관리, 손님과 직원 행동, 플레이어 상호작용의 책임을 나눴습니다. 팀에서 담당한 인벤토리·성장·UI 시스템과는 공용 인터페이스와 이벤트를 통해 연결했습니다.

### 3. 음료 미니게임

입력에 따라 음료를 따르고, 컵에 담긴 양을 바탕으로 결과를 판정하는 미니게임을 구현했습니다.

- 키 입력과 유량 제어
- 컵 형상에 따른 입자 이동과 경계 처리
- 공간 그리드를 활용한 주변 입자 탐색
- 게임 규칙·시뮬레이션·화면 표현 분리
- 손님 주문 및 서빙 흐름과 연결

| 주요 코드 | 역할 |
| --- | --- |
| [DrinkMinigameController.cs](Assets/02.Scripts/Sushi/MiniGame/DrinkMinigameController.cs) | 입력과 라운드 진행 연결 |
| [FluidPourSession.cs](Assets/02.Scripts/Sushi/MiniGame/FluidPourSession.cs) | 라운드 상태와 판정 관리 |
| [FluidSimulation.cs](Assets/02.Scripts/Sushi/MiniGame/FluidSimulation.cs) | 입자 이동과 밀도·압력 계산 |
| [FluidCupGeometry.cs](Assets/02.Scripts/Sushi/MiniGame/FluidCupGeometry.cs) | 컵 형상과 경계 계산 |
| [DrinkFluidRendererView.cs](Assets/02.Scripts/Sushi/MiniGame/DrinkFluidRendererView.cs) | 액체 화면 표현 |
| [SushiDrinkMinigameCoordinator.cs](Assets/02.Scripts/Sushi/MiniGame/SushiDrinkMinigameCoordinator.cs) | 음료 미니게임과 매장 시스템 연결 |

## 공개 폴더 구성

```text
Assets/
├─ 02.Scripts/
│  ├─ Fish/       # 물고기 행동·이동·생성 및 에디터 코드
│  └─ Sushi/      # 매장 운영·NPC·음료 미니게임 및 에디터 코드
└─ 05.Configs/
   ├─ Fish/       # 어종 및 생성 패턴 설정 데이터
   └─ Sushi/      # 초밥집 운영 관련 설정 데이터
```


## AI 활용 및 리소스 제작

Codex·Claude를 분석 및 구현 보조에, Meshy AI 등을 리소스 제작에 활용했습니다. 요구사항과 제약을 정의하고, 생성 결과를 검토·수정하며 게임에 적용했습니다.

초밥집 씬 구성, 리소스 제작 과정, 적용 화면과 문제 해결 과정은 [노션 포트폴리오]에 정리했습니다.

## 실행 및 공개 범위 안내

이 저장소는 전체 게임 프로젝트가 아닌 담당 코드와 설정 데이터의 부분 공개본입니다.

팀 공통 코드, 다른 팀원의 구현, 씬·프리팹 및 외부 에셋은 포함하지 않았습니다. 따라서 이 저장소만으로는 Unity에서 게임을 빌드하거나 실행할 수 없으며, 일부 설정 데이터의 에셋 참조도 누락되어 있습니다.

완성된 게임의 시연 영상과 빌드 다운로드는 노션 포트폴리오에서 확인할 수 있습니다.
