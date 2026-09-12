# SecondaryAttacks — Valheim 1.0.7 대응

검토일: 2026-09-09~10. 검토 시작 기준 커밋은 `cd889039ef197ef75600aa3f0e109084c3c0be93` / 버전 1.2.3이며, 대응 결과는 버전 1.2.4에 포함했다.

## 대상과 근거

- Windows x64 클라이언트: 0.221.12 / Steam 21981559 → **1.0.7 / 25185596**.
- Windows x64 데디케이트 서버: 0.221.12 / Steam 21981590 → **1.0.7 / 25185644**.
- 현재 클라이언트 로그도 게임 1.0.7, Unity 6000.0.75를 보고한다. 제공 로그의 SecondaryAttacks 초기화 실패는 `ItemDataGetTooltipSecondaryAttackPatch`의 대상 탐색 실패다. 다른 모드의 오류를 SecondaryAttacks 오류로 분류하지 않았다.
- 원본 보관·추출은 이미 완료된 전역 스냅샷을 이용했다. 현재 설치된 양쪽 `assembly_valheim`, `assembly_utils`, `assembly_guiutils`, `UnityEngine.CoreModule`의 SHA-256이 각각 보관 원본과 일치했다. 분석용 DLL을 publicize하지 않았다.
- 전역 근거: `C:/Users/blizz/.codex/references/valheim/comparisons/0.221.12--1.0.7-windows-x64/secondaryattacks-review-20260909/`. 제공 로그 사본, 원본 해시, 양쪽 정적 검사 JSON, 배포 해시, 제한된 Mono probe 소스를 보관했다. 계정·세션 식별자는 이 문서에 옮기지 않았다.

## 변경

| 근거와 호출 경로 | 최소 변경과 보존 범위 |
| --- | --- |
| 정적 `ItemDrop.ItemData.GetTooltip`이 5인자에서 6인자로 바뀌었다. 새 `appending`은 `m_appendToolTip` 재귀 호출에서 true다. | `SecondaryAttackItemTooltipSystem.cs`: 정확한 6인자 오버로드를 패치하고 재귀 설명에는 모드 문구를 추가하지 않는다. 제작 툴팁 제외, 중복 문자열 방지, EpicLoot/Jewelcrafting/EpicJewels 이후 순서와 Last 우선순위를 유지한다. |
| `ZRoutedRpc.Everybody`가 정적 필드에서 값 0인 상수로 바뀌었다. 내장 ServerSync에 구 `ldsfld`가 3곳 남아 있었다. 제공 로그의 본 모드는 앞선 툴팁 실패로 초기화가 중단되므로, 이것은 본 모드에서 재현된 후속 오류가 아닌 바이너리로 확인한 불일치다. | `build/PrepareServerSync.ps1`, `ILRepack.targets`: 원본 라이브러리는 보존하고 빌드 사본의 3곳만 `ldc.i8 0`으로 바꾼 뒤 병합한다. 입력 SHA-256, 새 상수 타입/값과 교체 횟수가 다르면 빌드를 실패시킨다. 수신 대상 0, RPC 이름·데이터·권한 정책은 유지한다. |
| 기존 참조는 게임 폴더에 남은 과거 publicized DLL에 의존했다. 원본 참조로 일괄 전환하면 기존 비공개 접근과 `EnemyHud.HudData` 등에서 컴파일 오류가 발생한다. | `SecondaryAttacks.csproj`, `build/GameReferences.targets`: 현재 원본 3개를 입력으로 기존 공개화 컴파일 방식을 `obj` 안에서 재현한다. 게임 원본을 덮어쓰지 않는다. 공식 BepInEx publicizer 0.4.2 작업을 사용하며 SDK 전환은 하지 않는다. **최종 컴파일 입력은 공개화 사본**이다. 원본만으로 컴파일했다고 보고하지 않는다. 병합 DLL의 접근 지원 특성을 확인하고 원본/설치 Mono에서 제한된 실제 접근도 검증했다. |
| 새 비공개 `Attack.GetAttackEitr()`가 공개 오버로드에 위임하면서 현재 공격 대신 `weapon.m_shared.m_attack.m_attackEitr`를 읽는다. 모드는 `BuildSecondaryAttack`에서 별도 Eitr 비용을 설정한다. `Attack.Start`의 시작 검사와 이후 공격·연사 비용이 모두 영향을 받는다. | `SecondaryAttackHarmonyHooks.cs`: 0인자 메서드에 한정한 Prefix를 추가했다. 설정된 무기이고 현재 비용이 기본 공격과 다를 때 현재 공격 비용과 기존 스킬 감산을 사용한다. 같은 비용·미설정 무기는 원본에 맡기고 공개 툴팁 오버로드는 패치하지 않는다. 모드의 비용 정책을 유지하기 위한 호환성 수정이다. |
| 새 바닐라 `Attack`은 내구도 소모에 `Game.m_durabilityRate`를 곱한다. 모드가 직접 실행하는 공격은 기존 수동 소모 경로를 이용한다. | `SecondaryAttackManager.CommonRuntime.cs`: 직접 소모에도 월드 배율을 한 번 곱한다. 바닐라 공격 후 실제 감소분에 모드 배율을 적용하는 경로에는 추가로 곱하지 않는다. 배율 1에서는 기존 값, 0에서는 소모 없음이 의도된 동작이다. |
| 사용자 공통 빌드 지침의 BepInEx 패키지 기준. | `Thunderstore/manifest.json` 의존성만 `denikson-BepInExPack_Valheim-5.4.2350`으로 정정했다. 설치된 BepInEx/외부 모드 DLL은 교체하지 않았다. |

## 검토 범위와 한계

실제 빌드 대상은 `SecondaryAttacks.csproj`의 명시적 Compile 목록과 ILRepack으로 병합되는 ServerSync/YamlDotNet이다. 로더는 `SecondaryAttacksPlugin.Awake`, 게임 연결은 Harmony 패치·Unity 메시지·RPC 등록 경로다. 설정/YAML/로컬라이제이션 및 공개 연동 API를 변경하지 않았다. 신규 버전에 맞춘 별도 저장 코덱이나 구버전 fallback/migration을 추가하지 않았다.

최종 DLL의 직접 게임/Unity 참조와 명시적 Harmony 속성을 원본 메타데이터에 대조했다. ProjectileAccess, ShieldAccess, WeaponTrailAccess, SweepTrailResetSystem, backstab 필드, Tameable.UnSummon, 로컬라이제이션 수동 패치, 이동 transpiler의 3개 대상/필드 읽기도 확인했다. `Player.UpdateStealth`의 보간식, SkillsDialog/EnemyHud/TextsDialog 변경점, 공격의 비용·내구도, Inventory의 현재 Changed/아이템 전달 경로와 DoT 호출 범위를 선택적으로 대조했다. 전체 게임 C# diff의 수동 검토를 완료했다는 의미는 아니다.

기존 보호 코드, 소유권 확인, Prefix/Postfix/Finalizer 상태 복원, 파괴·이벤트 정리, RPC 식별자와 중복 제어는 유지했다. 모든 RPC가 송신자를 인증한다는 의미는 아니다. 예를 들어 기존 일부 캐릭터 RPC는 소유권만 검사하고 sender를 별도로 사용하지 않는다. 이번 작업은 그 기존 권한 설계를 재설계하지 않는다.

선택적 EpicLoot/Jewelcrafting/EpicJewels/MagicPlugin/Groundwork 등의 로드 순서·기존 연동은 유지했으나 외부 모드 바이너리 전체와 조합 실행은 검증하지 않았다. 본 모드에는 직접 Jotunn 의존성이 없으며 다른 모드의 Jotunn 오류를 수정하지 않았다.

생성된 obj/bin, 참고용 디컴파일, 외부 라이브러리 전체 구현은 구조 수정 대상에서 제외했다. ServerSync는 필요한 IL 3곳과 병합 결과의 직접 참조만 수정·검사했다. 프리팹/AssetBundle 전체, 새로운 장비와 모든 전투 정책·저장 형식·네트워크 조합은 미검토다. 매 프레임 새 UI 생성이나 새 검색 루프를 추가하지 않았다. 실행 성능 수치는 측정하지 않았다.

## 수행한 검증

- **빌드:** `dotnet build SecondaryAttacks.csproj -c Debug -p:DeployToGame=true` 및 `dotnet build SecondaryAttacks.csproj -c Release` 성공, 각각 경고 0 / 오류 0. ServerSync 후처리·ILRepack 성공 후 최종 모드 DLL 자동 복사. Release에서 Thunderstore·Nexus 1.2.4 ZIP을 생성했다.
- **자동 정적 검사:** 원본 클라이언트·서버 각각 직접 참조 **1,057개**, Harmony 속성 대상 **82개**, 간접 계약 **36개** 통과. literal 필드 IL 잔존 없음. 기존 직접 private/internal 참조 65개를 별도 기록했으며 존재 확인과 런타임 접근 보장을 혼동하지 않았다.
- **자동 회귀:** 실제 DLL 스킬 HUD 기하/텍스트 테스트 **1,009개** 통과. 테스트 resolver에서도 publicized fallback을 제거했다. 구조 회귀는 기준 커밋/작업 트리 각각 **96개** 통과. 구조 테스트에 기존 nullable 경고 CS8600이 있으나 모드 빌드 경고는 없다.
- **제한된 Mono 실행:** 설치된 `mono-2.0-bdwgc.dll`과 원본 게임 DLL을 별도 프로세스에서 사용했다. 실제 최종 모드의 `CommitConfiguredAmmo` 비공개 필드 2개 쓰기와 `FractureLineSystem.IsValidDestructibleTarget`의 비공개 weapon 읽기/대상 거절을 확인했다. 처음 Unity Object 비교를 포함한 probe는 Unity 네이티브 초기화가 없어 실패했으며, 네이티브 호출 없는 범위로 한정했다. 가짜 게임 DLL이나 publicized 런타임을 사용하지 않았다.
- **배포:** `bin/Release/SecondaryAttacks.dll`, Thunderstore ZIP 내부 DLL과 `C:/Program Files (x86)/Steam/steamapps/common/Valheim/BepInEx/plugins/SecondaryAttacks.dll` SHA-256 일치: `5669E5F089D9968DB967C69B19E34CF36249931703D24918C9453889603F91A4`.

**게임 실제 실행은 아직 검증하지 않았다.** Mono probe는 전체 Harmony PatchAll, Unity 화면·씬 수명주기, 게임 접속 또는 멀티플레이 성공의 증거가 아니다. 정적 검사만으로 새 Eitr 보정과 월드 내구도 배율의 게임 동작이 입증되는 것도 아니다.

## 게임에서 필요한 확인

1. 클라이언트를 완전히 재시작하고 SecondaryAttacks 초기화 완료, Undefined target/literal-field/FieldAccess/MethodAccess 오류가 없는지 확인한다. 다른 모드 오류는 별도로 분류한다.
2. 일반·제작·append 툴팁과 선택적 툴팁 모드 조합, Sneak/Blood Magic hover·스크롤·배율 변경·닫기/재열기를 확인한다. 본문 250, 제목 중앙/본문 왼쪽, 간격 8 UI 단위의 기존 설정을 유지한다.
3. 기본 공격과 보조 공격 비용을 다르게 설정한 지팡이에서 시작/부족 시 거절/연사 Eitr 소모를 확인한다. 미설정 무기와 기본 공격은 기존 동작이어야 한다. 내구도 월드 배율 0/1/다른 값에서 수동 공격과 바닐라 위임 공격을 비교해 이중 적용이 없는지 확인한다.
4. Sneak 은신·이동·DoT 웅크림 유지, 소환물 걷기/비행/수영 강화와 시간 만료, 방패 변환·소환물 HUD를 확인한다.
5. 같은 패치 DLL을 사용하는 호스트·원격 클라이언트·데디케이트 서버에서 설정 동기화/잠금, 접속·재접속, 소환물 소유권 이전, 권한 없는 RPC와 중복·지연 수신을 확인한다. 실제 데디케이트 서버에는 이번에 DLL을 복사하지 않았다.
6. 탄약이 여러 스택에 나뉜 경우와 부족한 경우, 투척·회수·부메랑·인벤토리 가득 참, 저장 후 재접속을 확인한다. 아이템 수량·customData·장전 상태의 소실/복제가 없어야 한다.

게임 원본·사용자 설정은 보존되어 있다. 롤백은 이 소스 변경을 되돌린 뒤 해당 게임 버전에 맞는 DLL로 다시 Debug 빌드/배포하는 단위로 수행한다. 패치 전 1.2.3 DLL은 1.0.7 툴팁 초기화 실패가 확인되어 있으므로 그 DLL로 돌아가는 것을 신규 게임 호환성 복구로 간주하지 않는다.
