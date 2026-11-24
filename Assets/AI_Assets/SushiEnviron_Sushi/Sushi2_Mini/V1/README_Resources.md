# Sushi2 음료 미니게임 리소스
새 PNG 19개. 기존 씬, 프리팹, 스크립트는 수정하지 않았습니다.
일러스트는 내장 image_gen으로 생성한 투명 시트를 동일한 2×2 셀로 분리했습니다. 단순 UI 도형은 직접 생성했습니다.
실제 씬에서의 배치와 동작은 아직 연결하지 않았습니다.

## Unity 임포트
PNG 선택 → Texture Type: Sprite (2D and UI), Sprite Mode: Single.
Alpha Is Transparency: On, Filter Mode: Bilinear, Compression: None 권장.
개별 일러스트는 640×640이며 투명 여백이 있습니다. UI는 작은 별도 크기입니다.
UI Image의 Preserve Aspect를 사용하고, 그림마다 투명 여백을 고려해 크기와 위치를 조정하세요.
자동 임포트로 만들어지는 .meta는 팀 공유 시 PNG와 함께 커밋하세요.

## 에이드
Ade_Chamber: 고정 착즙통
Ade_Press: 위아래 움직이는 압착부
Ade_GreenGrape / Ade_Grapefruit / Ade_Lemon: 주문에 따른 과일
Ade_JuiceTint: 흰색 즙 표면. Image.color로 청포도 연두, 자몽 분홍, 레몬 노랑 적용.
권장 겹침 순서: 착즙통 → 즙 → 과일 → 압착부.
앞면을 통한 자연스러운 가림이 필요하면 별도 마스크 또는 전면 레이어 구성이 필요합니다.
체력 비율 1이면 압착부 위, 0이면 아래. 시간 경과 시 최대 체력까지만 회복.
입력 한 번마다 체력 감소, 0 도달 시 성공 확정 후 회복과 입력 중지.
과일은 체력 감소에 따라 세로 크기를 줄이고 즙은 점진적으로 표시할 수 있습니다.

## 맥주
Beer_Keg / Beer_Mug / Beer_Fill / Beer_Stream: 통, 빈 잔, 맥주와 거품, 따르는 줄기.
잔과 내용물은 별도 그림이므로 크기·위치를 수동 정렬하고 필요하면 마스크를 사용하세요.
UI_Bar: 가로 트랙, UI_CatchZone: 성공 영역, UI_Marker: 좌우 이동 가늠자.
같은 부모 좌표계에서 가늠자 중심이 캐치존 범위 안인지 판정.
잔 회전은 같은 이동 값에 연결하고, 성공 시 따르기 연출로 연결할 수 있습니다.
입력 판정과 실제 따르기 애니메이션은 아직 구현되지 않았습니다.

## 차
Tea_Bowl / Tea_Whisk / Tea_Foam / Tea_Ripple: 찻잔, 차선, 거품, 파문.
Tea_Whisk를 독립 Image로 배치하고 WASD 입력에 따라 상하좌우 이동 후 원위치.
UI_DirectionUp은 회전해서 4방향 표시. 키 문자는 TMP로 별도 표시 권장.
여기서는 요청을 WASD 방향 순서 입력 방식으로 해석했습니다.
실제 3×3 틱택토 규칙이나 입력 순서/실패 처리는 구현하지 않았습니다.
차선이 그릇 밖으로 나오지 않는 표현은 마스크와 이동 범위 조정이 필요합니다.

## 공통 UI
UI_Fill: 흰색 직사각형. 색상과 Image Filled 설정으로 체력/진행률에 활용.
UI 도형은 장식 없는 기본형이며 가로세로 크기 변경이 가능합니다.

