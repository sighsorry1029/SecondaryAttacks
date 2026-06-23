# AdvancedTerrainModifiers / TerrainTools 분석

분석 대상:

`C:/Users/blizz/AppData/Roaming/com.kesomannen.gale/valheim/profiles/summoner/BepInEx/plugins/Searica-AdvancedTerrainModifiers/TerrainTools.dll`

디컴파일 기준:

`C:/Users/blizz/AppData/Local/Temp/TerrainTools_decompiled_20260506_171501`

## 1. 한 줄 요약

이 모드는 Valheim의 괭이(Hoe), 경작기(Cultivator), 그리고 새로 추가하는 삽(Shovel)에 더 정밀한 지형 편집 도구를 추가하는 Jotunn 기반 BepInEx 모드다. 핵심 기능은 정사각형 격자 기반 평탄화/길/포장/경작, 정밀 높이 올리기, 지형 수정 제거, 삽을 통한 낮추기, 마우스 휠로 반경과 강도 조절, 작업 범위/높이 오버레이 표시다.

## 2. 기본 메타데이터

| 항목 | 값 |
|---|---|
| BepInEx GUID | `Searica.Valheim.TerrainTools` |
| 모드 이름 | `AdvancedTerrainModifiers` |
| DLL/Assembly | `TerrainTools` |
| 버전 | `1.4.1` |
| 제작자 상수 | `Searica` |
| 필수 의존성 | `com.jotunn.jotunn` `2.22.0` |
| Harmony ID | `Searica.Valheim.TerrainTools` |

Jotunn 네트워크 속성은 IL 기준으로 `CompatibilityLevel.VersionCheckOnly`, `VersionStrictness.Patch`로 보인다. 즉 양쪽에 모드가 있을 때 패치 버전 단위까지 확인하는 성격이다. Config 동기화 속성은 `AdminOnlyStrictness.IfOnServer`로 보인다.

## 3. 모드가 추가하는 실제 기능

### 3.1 Hoe에 추가되는 도구

| Config 키 | 표시 이름 | 기반 prefab | 기능 |
|---|---|---|---|
| `mud_road_v2_sq` | `Level ground(square)` | `mud_road_v2` | 플레이어 기준 월드 격자에 맞춰 정사각형 영역을 평탄화한다. Shift+클릭 시 바라보는 지점 기준으로 평탄화/스무딩한다. |
| `raise_v2_precise` | `Raise ground (precision)` | `raise_v2` | 마우스 휠로 목표 높이를 지정해 정밀하게 땅을 올린다. |
| `path_v2_square` | `Pathen (square)` | `path_v2` | 지형 높이를 바꾸지 않고 월드 격자 기준 정사각형 흙길을 만든다. |
| `paved_road_v2_square` | `Paved road (square)` | `paved_road_v2` | 정사각형 포장 + 평탄화를 수행한다. Shift+클릭 설명도 평탄화 기준 변경을 안내한다. |
| `paved_road_v2_path` | `Paved road (path)` | `paved_road_v2` | 지형 높이를 바꾸지 않고 포장만 한다. |
| `paved_road_v2_path_square` | `Paved road (path, square)` | `paved_road_v2` | 지형 높이를 바꾸지 않고 월드 격자 기준 정사각형 포장만 한다. |
| `remove_terrain_mods` | `Remove Terrain Modifications` | `mud_road_v2` | 선택 지점 주변의 높이 변경과 페인트를 초기화한다. |

### 3.2 Cultivator에 추가되는 도구

| Config 키 | 표시 이름 | 기반 prefab | 기능 |
|---|---|---|---|
| `cultivate_v2_square` | `Cultivate (square)` | `cultivate_v2` | 월드 격자 기준 정사각형 경작 + 평탄화를 수행한다. |
| `cultivate_v2_path` | `Cultivate (path)` | `cultivate_v2` | 지형 높이를 바꾸지 않고 경작 페인트만 적용한다. |
| `cultivate_v2_path_square` | `Cultivate (path, square)` | `cultivate_v2` | 지형 높이를 바꾸지 않고 월드 격자 기준 정사각형 경작 페인트를 적용한다. |
| `replant_v2_square` | `Replant (square)` | `replant_v2` | 지형 높이를 바꾸지 않고 월드 격자 기준으로 잔디/자연 지형 페인트를 되돌린다. |

### 3.3 새 아이템: Shovel

모드는 `ATM_Shovel`이라는 삽 아이템을 만든다.

| 항목 | 내용 |
|---|---|
| 기반 prefab | `Hoe`를 복제 |
| 제작대 | Forge |
| 수리대 | Forge |
| 재료 | Wood 5, Iron 2 |
| 전용 PieceTable | `_ShovelPieceTable` |
| 기본 활성화 config | `Shovel` = true |

삽의 모델은 Hoe prefab을 복제한 뒤 blade/handle transform, collider, 아이콘을 수정해 만든다. 이 삽의 전용 도구 테이블에는 기본적으로 `lower_v2`가 들어간다.

| Config 키 | 표시 이름 | 기반 prefab | 기능 |
|---|---|---|---|
| `lower_v2` | `Lower ground` | `raise_v2` | 땅을 낮춘다. 기본 설정은 raise radius 1.5, raise power 0.5, raise delta -0.5다. |

## 4. 설정 구조

주요 전역 설정은 다음과 같다.

| 섹션 | 키 | 기본값 | 동기화 | 의미 |
|---|---|---:|---|---|
| `Global` | `Verbosity` | `Low` | No | 로그 상세도. Medium/High는 디버깅용이며 High는 게임을 느리게 할 수 있다고 설명한다. |
| `Global` | `HoverInfo` | true | Yes | 정사각형 지형 도구 사용 중 좌표/높이 정보를 표시한다. |
| `Radius` | `RadiusModifier` | true | Yes | 마우스 휠로 지형 도구 반경을 조절할 수 있게 한다. 정사각형 도구는 제외된다. |
| `Radius` | `RadiusModKey` | LeftAlt 계열 값(308) | No | 반경 조절 중 누르는 키. |
| `Radius` | `RadiusScrollScale` | 0.1 | No | 휠 1칸당 반경 변화량. -1~1. |
| `Radius` | `MaxRadius` | 10 | Yes | 최대 반경. 4~20. |
| `Hardness` | `HardnessModifier` | true | Yes | 마우스 휠로 raise/smooth 강도를 조절할 수 있게 한다. |
| `Hardness` | `HardnessModKey` | LeftControl 계열 값(306) | No | 강도 조절 중 누르는 키. |
| `Hardness` | `HardnessScrollScale` | 0.1 | No | 휠 1칸당 강도 변화량. -1~1. |
| `Shovel` | `Shovel` | true | Yes | 새 삽 제작 레시피 활성화 여부. 끄면 기존 삽은 유지되고 신규 제작만 막는다. |

각 도구별 config는 기본값 true이며, 끄면 해당 piece를 Hoe/Cultivator/Shovel 메뉴에서 제거한다.

## 5. 초기화 흐름

`TerrainTools.Awake()` 흐름:

1. BepInEx logger를 초기화한다.
2. ConfigManager를 초기화하고 config entry들을 생성한다.
3. 현재 assembly 전체에 Harmony patch를 적용한다.
4. `Game.isModded = true`를 설정한다.
5. Jotunn `PrefabManager.OnVanillaPrefabsAvailable`에서 삽을 생성한다.
6. Jotunn `PieceManager.OnPiecesRegistered`에서 도구 piece들을 생성/등록한다.
7. `GUIManager.Instance`를 접근해 Jotunn GUI 시스템을 준비한다.
8. config 파일 watcher와 ConfigurationManager 연동을 설정한다.
9. config reload, config window close, Jotunn configuration synchronized 이벤트에서 필요한 경우 도구 등록 상태를 갱신한다.

## 6. 도구 piece 생성 방식

`InitManager.MakeToolPiece()`는 `ToolDB` 정의를 바탕으로 기존 Valheim terrain prefab을 복제한다.

복제 후 변경하는 요소:

- `Piece.m_icon`
- `Piece.m_name`
- `Piece.m_description`
- `Piece.m_resources`
- `TerrainOp.m_settings`
- 필요하면 `OverlayVisualizer` 파생 컴포넌트 추가
- `lower_v2`처럼 필요한 경우 ghost transform 회전/위치 보정

도구 등록은 Jotunn `PieceManager.Instance.GetPieceTable(pieceTable)`에 직접 piece prefab을 삽입하는 방식이다. config 변경으로 도구가 켜지거나 꺼지면 기존 등록을 제거하고 활성화된 도구만 다시 등록한다. 업데이트 중 local player가 괭이/경작기를 들고 있으면 강제로 손에서 숨겨, piece table 변경이 손에 든 아이템 UI와 꼬이지 않게 한다.

## 7. 정밀 지형 조작 구현

이 모드의 핵심은 `PreciseTerrainModifier`다.

정밀 도구는 `TerrainOp.ApplyOperation` 직전에 특정 radius 값을 `float.NegativeInfinity`로 바꾼다. 이후 `TerrainComp`의 지형 처리 메서드들이 이 값을 감지하면 vanilla 처리 대신 자체 정밀 처리를 수행한다.

패치되는 지형 처리:

| 대상 | 처리 |
|---|---|
| `ClutterSystem.ResetGrass` | 정밀 radius sentinel이면 풀 제거 반경을 1로 보정한다. |
| `TerrainComp.ApplyOperation` | overlay tool이면 smooth/raise/paint radius를 정밀 sentinel로 바꾼다. precision raise는 `GroundLevelSpinner.Value`를 raise delta로 쓴다. |
| `TerrainComp.RPC_ApplyOperation` | terrain comp 소유자가 아니면 ownership을 claim한다. |
| `TerrainComp.InternalDoOperation` prefix | level/raise/smooth/paint가 모두 false인 특수 도구는 지형 수정 제거 + paint 초기화를 수행한다. |
| `TerrainComp.InternalDoOperation` postfix | 마지막 radius가 sentinel이면 1로 되돌려 기록값을 정상화한다. |
| `TerrainComp.SmoothTerrain` | 정밀 smoothing을 직접 수행한다. |
| `TerrainComp.RaiseTerrain` | 정밀 raise/lower를 직접 수행한다. |
| `TerrainComp.PaintCleared` | 정밀 paint 변경을 직접 수행한다. |

정밀 작업 범위는 `FindExtrema()` 기준으로 대상 vertex 주변 최대 3x3 인덱스를 잡는다. paint는 `WorldToVertexMask`를 사용하고, height는 `WorldToVertex`를 사용한다. `RemoveTerrainModifications`는 해당 인덱스들의 `m_levelDelta`, `m_smoothDelta`, `m_modifiedHeight`를 초기화한다.

## 8. 마우스 휠 조작

### 8.1 반경 조절

`RadiusModifier`는 `Player.Update` prefix에서 local player가 placement mode이고 piece selection UI가 닫혀 있을 때 동작한다.

- 기본 modifier key는 KeyCode 308이다.
- `Input.mouseScrollDelta.y * RadiusScrollScale`만큼 반경을 변경한다.
- 최소 반경은 0.5, 최대 반경은 config `MaxRadius`다.
- level/raise/smooth/paint 중 존재하는 radius에 누적 delta를 적용한다.
- placement ghost의 particle scale도 함께 변경해 사용자가 범위 변화를 볼 수 있게 한다.
- `OverlayVisualizer`가 붙은 정사각형/정밀 도구는 radius modifier 대상에서 제외된다.

### 8.2 강도 조절

`HardnessModifier`도 `Player.Update`와 `TerrainOp.Awake`를 패치한다.

- 기본 modifier key는 KeyCode 306이다.
- smooth power는 1~30 범위에서 조절한다.
- raise power는 0.05~1 범위에서 조절한다.
- smooth delta를 raise delta로 변환할 때 `delta / 29 * -0.95`를 사용한다.
- 변경된 강도는 player message로 표시된다.

## 9. 오버레이/시각화

모드는 terrain tool ghost의 `_GhostOnly` particle transform을 복제해서 작업 범위와 아이콘을 보여준다.

| 클래스 | 역할 |
|---|---|
| `OverlayVisualizer` | primary/secondary/tertiary overlay와 HoverInfo를 생성하고 갱신하는 베이스 클래스 |
| `HoverInfoEnabled` | 좌표/높이 텍스트를 카메라 방향으로 회전시켜 표시 |
| `LevelGroundOverlayVisualizer` | 정사각형 평탄화 범위 표시 |
| `RaiseGroundOverlayVisualizer` | 정밀 raise 높이 표시, 휠로 높이 spinner 변경 |
| `SquarePathOverlayVisualizer` | 정사각형 path/paint 범위 표시 |
| `CultivateOverlayVisualizer` | 정사각형 cultivate 범위와 hover 색상 표시 |
| `RemoveModificationsOverlayVisualizer` | 수정 제거 범위와 cross 아이콘 표시 |
| `SeedGrassOverlayVisualizer` | replant 범위 표시 |

`Player.UpdatePlacementGhost` finalizer는 overlay tool ghost의 X/Z 좌표를 1m 단위로 반올림해 월드 격자에 맞춘다.

`Piece.SetInvalidPlacementHeightlight` prefix는 overlay tool에 빨간 invalid highlight가 씌워지는 것을 막는다. 시각화용 overlay가 placement invalid 상태처럼 보이는 것을 피하기 위한 패치로 보인다.

`GameCamera.UpdateCamera` transpiler는 특정 상황에서 mouse wheel이 카메라 줌이나 piece rotate로 소비되지 않게 막는다. 정밀 raise overlay, radius modifier, hardness modifier 사용 중이면 `CanRotatePiece()` 대신 true를 반환하도록 바꾼다.

## 10. Config/서버 동기화 동작

`ConfigManager.BindConfig()`는 설명 끝에 `[Synced with Server]` 또는 `[Not Synced with Server]`를 붙이고, ConfigurationManager 속성 tag로 admin-only 여부를 설정한다.

동기화되는 항목:

- 도구 활성화 여부
- HoverInfo
- RadiusModifier enable
- MaxRadius
- HardnessModifier enable
- Shovel recipe enable

동기화되지 않는 항목:

- 로그 상세도
- radius modifier key
- radius scroll scale
- hardness modifier key
- hardness scroll scale

Jotunn `SynchronizationManager.OnConfigurationSynchronized`에 연결되어 있어 서버 config 동기화 후에도 piece 등록 상태를 다시 계산한다.

## 11. 리스크/관찰

1. `ConfigManager.SetupWatcher()`가 생성한 `FileSystemWatcher`를 필드에 보관하지 않아 명시적으로 dispose하지 않는다. 일반적인 모드 수명에서는 큰 문제는 아닐 수 있지만, plugin reload 환경에서는 watcher 누적 가능성이 있다.
2. `ConfigManager.CheckForConfigManager()`는 ConfigurationManager의 `DisplayingWindowChanged` 이벤트에 handler를 추가하지만 제거하지 않는다. 마찬가지로 reload 환경에서는 누적 가능성이 있다.
3. `PreciseTerrainModifier.RPC_ApplyOperationPrefix`는 owner가 아니면 `ClaimOwnership()`을 호출한다. 멀티플레이에서 지형 수정 권한/동기화 충돌이 있는 환경에서는 주의해서 볼 부분이다.
4. `PlayerPatch.UpdatePlacementGhostPostfix`는 이름은 Postfix지만 Harmony attribute는 finalizer다. 의도적으로 예외 이후에도 ghost 위치 보정을 실행하려는 설계일 수 있으나, 일반적인 postfix보다 동작 시점 의미가 약간 다르다.
5. Undo/Redo 오버레이 클래스와 아이콘 리소스가 존재하지만 `ToolConfigsMap`에는 실제 undo/redo 도구가 등록되어 있지 않다. 이전 기능의 잔재이거나 향후 기능용 코드로 보인다.
6. `HoverInfo` 텍스트는 월드 공간 `TextMesh`로 생성되며, overlay 사용 중 매 frame 카메라 방향으로 회전한다. 비용은 작지만 overlay tool 사용 중에는 매 frame 작업이다.

## 12. 결론

AdvancedTerrainModifiers는 전투나 아이템 밸런스 모드가 아니라, 건축/농사/지형 정리 편의성을 크게 올리는 terrain editing 확장 모드다. Vanilla Hoe/Cultivator의 기존 terrain operation prefab을 복제해 여러 변형 도구를 만들고, Harmony 패치로 Valheim의 `TerrainComp` 처리에 정밀 격자 기반 동작을 끼워 넣는다.

실제 플레이 관점에서 체감되는 핵심은 다음 네 가지다.

- 정사각형 격자 기반 평탄화/길/포장/경작
- 마우스 휠로 목표 높이, 반경, 강도 조절
- 삽 아이템과 지면 낮추기 도구
- 작업 범위와 좌표/높이를 보여주는 오버레이

