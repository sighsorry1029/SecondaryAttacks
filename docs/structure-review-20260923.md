# SecondaryAttacks 구조 검토 및 최소 개선 — 2026-09-23

## 기준과 범위

- 시작: 실제 `main`, `44b1b7efa41ed1c07a8080db820fa8743b1ba319`, 모드 1.2.11. 새 브랜치/worktree는 만들지 않았다. 전역 `C:/Users/blizz/.codex/AGENTS.md`를 확인했고 적용되는 상위/프로젝트 AGENTS.md는 추가로 발견되지 않았다.
- 시작부터 `CleavingThrustSystem.cs`, `README.md`, `tests/GameCompatibility/Verify-Compatibility.ps1` 수정과 미추적 `tests/CleavingThrust/`가 있었다. 이것들은 앞선 기능 작업으로 보존하고 이번 커밋에서 제외했다. **기준 빌드와 배포 DLL은 이 기존 작업을 포함한다.**
- 기존 DLL 분리 제안과 `Valheim-1.0.7-compatibility.md`는 과거 자료로 참고하고 현재 소스/빌드와 대조했다. 현재 필요가 확인되지 않은 DLL 분리나 새 API 계층은 도입하지 않았다.
- 전체 소스의 줄별 감사는 아니다. 아래 진입점·상태 소유권·호출 경로와 관련 Git 변경을 수동 검토하고, 최종 DLL의 게임 참조·Harmony/간접 계약을 정적 검사했다. 병렬 조사는 읽기 전용이며 편집·빌드·커밋은 한 세션에서 수행했다.

### 실제 빌드와 실행 대상

`SecondaryAttacks.sln`의 실제 모드는 명시적 Compile 목록을 사용하는 `SecondaryAttacks.csproj` 하나이며, .NET Framework 4.8 라이브러리다. `SecondaryAttacksPlugin : BaseUnityPlugin`의 `Awake`가 진입점인 **BepInEx 플러그인**이고 프리로더 패처가 아니다.

`Awake`는 localization, config 바인딩, 선택적 연동, 설정 이벤트, Harmony `PatchAll`, YAML Facade, config watcher를 초기화한다. `OnDestroy`는 watcher/debouncer, Shield/Quickstep, 설정 이벤트와 Facade를 정리한다. Unity 메시지와 Harmony로 들어오는 경로를 일반 직접 호출 검색과 구분했다.

설정/리소스 경로는 다음과 같다.

- BepInEx cfg: `Plugin.cs`의 키·기본값·ServerSync 등록 및 live 변경 이벤트.
- YAML: `SecondaryAttackConfigFiles.cs`의 세 도메인 → ConfigLoader → 도메인별 normalizer → compiled snapshot → Facade → WorldApply/definition compiler → applied snapshot → runtime rebind.
- `Resources/Defaults/SecondaryAttacks.{Ranged,Melee,BloodMagic}.yml`과 `Resources/Translations/{English,Korean}.yml`을 임베드한다. 이번 수정 전후 임베드 리소스 6개의 바이트가 모두 같다.
- 공개 연동에는 `WarfareTweaksBridge`, Plugin의 공개 표면, localization 이벤트/도우미가 있다. 해당 서명과 구현을 변경하지 않았다.
- 별도 테스트 프로젝트는 StructureRegression, NativeSecondaryCompatibility, SkillTooltipLayout, SummonQuality, SubSummon이며, 기존 미커밋 CleavingThrust 테스트도 있다. 게임 솔루션의 배포 대상에 포함되지 않는다.

선택적 의존성은 Magic Supremacy, Wizardry, MagicPlugin, Quickstep, ShieldMeBruh, CreatureManager, CLLC, StarLevelSystem의 soft dependency다. 그 외 툴팁 패치 순서·reflection/bridge 연동은 해당 코드에 남아 있다. 직접 Jotunn 의존성을 추가하거나 외부 모드 버전을 바꾸지 않았다.

최종 DLL 경로는 다음과 같다.

1. 프로젝트와 고정 `Libs/ServerSync.dll`, YamlDotNet 16.3.0으로 컴파일.
2. `build/PrepareServerSync.ps1`이 입력 해시와 원본 게임 계약을 확인하고 빌드 사본의 상수 읽기 3곳만 보정. vendor 원본은 유지.
3. `ILRepack.targets`가 모드 + 보정된 ServerSync + YamlDotNet을 Internalize 병합.
4. `GetAssemblyVersion` 이후 `CopyOutputDLL`이 최종 DLL을 `BepInEx/plugins`에 복사.

Debug에서는 Release 전용 ZIP/manifest 갱신 타깃이 실행되지 않는다. ServerSync 전역 기준본 문서도 확인했지만 이번 범위에서 내장 라이브러리를 교체하지 않았다.

### 게임 버전과 접근 제한

전역 `C:/Users/blizz/.codex/references/valheim/INDEX.md`의 기존 자료를 사용했다. 설치된 컴파일 원본은 **Windows x64 Valheim 1.0.15 클라이언트, Steam 25390630**이며 보관 원본과 `assembly_valheim.dll` SHA-256이 같다:

`59F53FB55D99D22A33E8ED094EEC8D21E9F133543BCE92BC3D80DCE44033ADB1`

데디케이트 정적 검사는 1.0.15 / 25390671 원본을 사용했다:

`53ED3C85E0CB78F28084B25CE4DAE6F2B929BA9B9C53B788B9A24F950D73F183`

과거 1.0.7 대응 문서는 현재 실행 버전의 증거가 아니다. 이번 확인 범위는 1.0.15에 대한 빌드·정적/격리 검사이며 게임 실행으로 지원 범위를 새로 보증하거나 확대하지 않는다.

분석은 원본 DLL/보관된 원본 디컴파일을 사용했다. 기존 빌드는 `build/GameReferences.targets`에서 게임 어셈블리 3개의 **컴파일용 공개화 사본을 obj에 생성**하고 접근 지원 특성을 포함한다. 이를 원본만 사용하는 컴파일로 설명하지 않는다. 기존 비공개 직접 참조 65개는 그대로이며 존재 확인이 Mono 런타임 접근 보장은 아니다. 이번 신규 호출 `Attack.Clone`은 원본에서도 public이다.

## 영역별 구조 판정

| 영역 | 판정과 근거 | 이번 결정 |
| --- | --- | --- |
| 설정/컴파일/월드 적용 | 책임별 분리가 적절하다. Facade의 authoritative YAML·pending/current compiled 상태와 실제 적용 snapshot은 성공 시점이 다르다. WorldApply가 복구→적용→revision 발급을 소유한다. | 상태를 합치거나 cfg watcher와 YAML 권한 정책을 통합하지 않는다. rollback은 이전 snapshot 재적용인 최선 노력 보상이며 원자적 트랜잭션이 아니다. |
| 공격 런타임 | Manager/RuntimeFacade는 조정 책임이 다소 집중돼 있다. 하지만 StartAttackDispatch와 RuntimeContext가 임시 교체, 중첩 scope, 예외 복구를 구분한다. | 파일 크기를 근거로 재분리하지 않는다. 공격 복제의 불필요한 reflection만 제거한다. |
| 복제 투사체 시각 처리 | context 방식으로 이행한 뒤 옛 private overload가 남아 간접 호출과 탐색 경로가 불필요하게 길다. | 잔여 overload 삭제/전달 인라인. 새 파일·상태·캐시 없이 기존 진입점 유지. |
| HUD/툴팁 | 대체로 균형이 맞다. slot/text 재사용, dialog/tooltip WeakTable, 변경 시에만 높이 계산, 소유 marker 기반 정리가 있다. | 위치 갱신은 스크롤/안전 영역 변화 때문에 유지. OverheadStatusUiManager의 동일 instanceId Dictionary 3개 통합은 가능하지만 이득 대비 수명주기 검증 부담이 커 보류. |
| 소환/하위 소환 | 품질 시스템은 큰 편이나 spawn coroutine/finally, 귀속·한도·지연 제거가 함께 바뀐다. prefab 등록과 인스턴스별 슬롯 수명주기는 다르다. | 부모별 슬롯·GUID·owner 이동·서버 파괴 확인·5초 pending-removal을 유지. 단순 사망 검사로 합치지 않는다. |
| 선택적 모드 호환성 | NativeSecondaryAttackCompat의 원본 보존/탐색과 MagicPlugin의 live 비용 복원은 서로 다른 정책이다. | 이름 기준 캐시와 SharedData identity 상태를 통합하지 않는다. 공개 bridge·soft dependency 유지. |
| 네트워크/observer | owner 검사, sender 검사, sequence/시간 검사는 각각 역할이 다르다. 로컬 플레이어/UI가 없는 서버 경로도 있다. | 비슷한 RPC를 공통 처리로 합치거나 권한을 완화하지 않는다. 기존 검증 부족은 별도 후보로 기록. |

Git 근거도 현재 코드와 대조했다. `a9433b6`은 기본 YAML resource loading을 이미 공동 배치했고, `a7d3f6c`는 runtime rebind의 compiled attack 재사용을 도입했다. `9a8b434`는 CLLC predicate 중복을 이미 제거했다. `cd88903`/`006242e`의 tooltip 위치·본문 폭 변경은 같은 시스템에서 처리돼 현재 공동 배치가 유효하다. `82b563c`의 하위 소환 권한/저장 경계와 `3df6c1e`의 중복 제거 회귀 대응은 단순화로 없애면 안 되는 상태의 근거다.

## 구현한 독립 변경

### 1. `d156e8b` — 공개 공격 복제 API 사용

- 파일/심볼: `SecondaryAttackManager.CommonRuntime.cs`, `CloneAttack`.
- 호출: ObjectDB snapshot/복구, native secondary 복구, BuildSecondaryAttack와 runtime rebind 등 → CloneAttack.
- 문제: 1.0.0부터 남은 `AccessTools.Method(object.MemberwiseClone)` 캐시와 `MethodInfo.Invoke`를 사용한다. 원본 1.0.15 `Attack.Clone()`은 public/nonvirtual이며 IL이 정확히 MemberwiseClone 후 Attack 변환이다.
- 최소 변경: non-null은 `sourceAttack.Clone()`, null은 기존 `new Attack()`. reflection 필드와 사용하지 않는 using을 제거했다.
- 효과: 비공개 접근/반사 호출 의존성을 줄이고 복제 정책을 게임의 공개 계약으로 표현한다. shallow-copy 의미, 원본 보관 시점과 상태 소유권은 같다. 실행 속도 향상은 측정하지 않았다.
- 위험: 별도 외부 모드가 `Attack.Clone()` 자체를 Harmony 패치하면 이제 그 패치가 적용된다. 검토한 현재 소스에는 해당 패치가 없지만 모든 외부 조합을 보장하지 않는다.
- 검증: 원본 metadata/IL, 최종 DLL call target, Debug/배포 해시, 원본 DLL을 사용하는 제한된 managed probe로 129개 non-null clone 검사. primitive 복사, 참조 필드 identity 공유, 복제 객체 값 변경 시 원본 유지 확인.

### 2. `b999a6b` — 복제 투사체의 잔여 wrapper 제거

- 파일/심볼: `CopiedThrowProjectileVisualSystem.cs`, `CreateSpawnedProjectileVisualContext`, `ApplyCurrentWeaponVisual`, `ApplyLocalFallbackVisual`.
- 호출: projectile setup은 active definition을 담은 context를 만들고, MeleeProjectileHitCascadeSystem은 context를 한 번 생성해 후속 투사체에 전달한다.
- 문제: 실제 caller가 context로 이행한 뒤 ItemData만 받는 private 적용 overload 2개와 전달 전용 factory 2개가 남았다. 선언 외 직접 참조뿐 아니라 Unity 메시지명, Harmony attribute, delegate/nameof, repo reflection 경로도 확인했다. 확인하지 못한 외부 private reflection까지 없다고 단정하지는 않는다.
- 최소 변경: private overload 4개 제거. internal factory 1/2인자 서명은 유지하며 최종 4인자 factory로 바로 전달한다. context factory overload는 5개에서 3개가 됐다.
- 효과: 한 정책을 따라가는 불필요한 탐색/수정 단계 감소. 새 파일·간접 호출·캐시·무효화 조건이 없다. 시각효과 자체의 속도 개선을 주장하지 않는다.
- 위험/검증: source prefab, `definition:null`, `includeHitEffects:true` 전달을 이전과 대조했다. setup의 active definition/false 경로는 동일하다. Debug와 정적 검사 통과. 전후 DLL에서 제거 4개/변경 forwarding body 2개 이외의 이 타입 메서드는 동일함을 확인했다. 실제 remote observer 시각 동작은 게임 확인이 필요하다.

## 별도로 남긴 문제와 최적화 후보

다음은 이번 두 변경이 만든 결함이 아니며 기능/권한 정책이나 무효화 계약의 검증이 더 필요하다.

1. **확인된 sender 미사용:** `SecondaryAttackHarmonyHooks.cs`의 empower/shield-convert/sneak-skill RPC는 수신 객체 `IsOwner()`를 확인하지만 sender를 쓰지 않는다. Sweep observer 경로도 sender를 전달하지 않는다. 정상적으로 다른 캐릭터에 효과를 주는 기능이므로 단순 owner=sender 조건 추가는 별도 정책 변경이다. 실제 악용/멀티플레이 재현은 하지 않았다.
2. **확인된 취소 경로, 발현은 미재현:** `MagicPluginSummonQualitySystem.cs`의 `WrapSpawnWithRestore`는 enumerator가 null이어도 finally에서 한도를 집행한다. 새 소환이 없어도 낮은 품질의 현재 한도로 기존 소환이 줄 가능성이 있다. 외부 cooldown 취소 조합의 실제 게임 검증이 필요하다.
3. **확인된 매 프레임 할당:** 원본 Projectile.Update → UpdateVisual → PrepareProjectileIfNeeded → TryApplyHitEffectsFromSyncedVisual은 `m_changedVisual` 검사 전에 hit-effect 배열 Clone/new EffectList를 수행한다. postfix의 spin offset Split, definition 검색, Find/GetComponent도 반복된다. 비용 크기는 미측정이다. 단순 캐시는 live 원본 효과 변경·지연 ZDO·visual 교체를 놓칠 수 있어 갱신/파괴 조건을 먼저 정해야 한다.
4. **정황 후보:** SummonPrefabOverrideSystem의 DontDestroyOnLoad clone 컨테이너와 scene별 CWT의 수명이 달라 반복 접속 시 숨겨진 clone 잔류 가능성이 있다. 실제 잔류량은 측정하지 않았다. 자식이 없는 projectile visual의 spin-root 생성/파괴 반복 가능성도 해당 prefab의 실제 출현부터 확인해야 한다.

`ProjectileAccess.GetVelocity`는 공개 `Projectile.GetVelocity()`와 정책이 다르다. 공개 getter는 비소유자/invalid/hit 상태에 0을 반환하므로 raw 속도가 필요한 시각 경로를 바꾸지 않았다. 자원 비용 계산은 SEMan/mod callback을 거쳐 재계산하므로 무조건 캐시하지 않았다. 탄약의 다중 스택 제거는 RemoveItem→Changed 재진입이 있어 snapshot 순회를 유지했다.

## 검증 결과와 한계

| 구분 | 수행 결과 |
| --- | --- |
| 수정 전 기준 | Debug 빌드 경고 0/오류 0. StructureRegression 96, NativeSecondaryCompatibility 8모드 297, 기존 CleavingThrust 48 통과. baseline 최종 DLL의 client 정적 검사 1,079 direct / 86 Harmony / 37 indirect 통과. |
| 각 코드 변경 | `dotnet build SecondaryAttacks.csproj -c Debug -p:DeployToGame=true` 성공. 각 단계에서 병합 후 자동 복사와 SHA-256 일치, diff 확인 후 해당 파일만 커밋. |
| 최종 정적 검사 | 원본 client/server 1.0.15 각각 **1,080 direct / 86 Harmony / 37 indirect** 통과. 기존 직접 비공개 참조 65개 유지. direct 증가 1개는 public Attack.Clone이다. |
| 최종 소환/툴팁 검사 | SummonQuality **267**, SubSummon production boundary **37** 및 원본 IL을 입력으로 하는 merged transpiler 검사, SkillTooltipLayout **1,009** 통과. |
| 전후 DLL 비교 | 컴파일러가 바꾼 lambda/closure 일련번호를 정규화하면 application 메서드 **3,726개**의 IL은 동일하다. 변경 body는 CloneAttack, reflection 초기화가 사라진 Manager 정적 생성자, factory 2개뿐이다. private overload 4개 제거. 기존 타입/메서드 attribute 및 리소스 바이트 동일. |
| 제한된 clone 실행 | 원본 DLL + 실제 최종 모드에서 non-null clone **129** 검사 통과. 최초 일반 .NET 생성자 probe는 Unity AnimationCurve native 호출 때문에 실패했다. 이후 생성자 실행 없는 객체로 shallow-copy만 검사했다. null/new Attack 경로는 기존 IL 유지 여부만 확인했다. |
| 실제 게임 | **미수행.** Unity 화면/물리, Harmony 적용/JIT, Mono 접근 제한, host/remote/dedicated/crossplay, 저장·접속 해제·재접속을 통과했다고 보고하지 않는다. |

기존 NativeSecondaryCompatibility의 CS8600은 .NET 9 test double의 nullable annotation 경고다. 모드 .NET Framework 빌드에는 경고가 없었다. 이번 단순 삭제/직접 호출 변경을 그대로 반복하는 새 상시 테스트나 production 추상화는 추가하지 않았다. 일회성 IL 비교/clone probe와 JSON은 `%TEMP%/SecondaryAttacks-structure-20260923`에 두었다.

최종 Debug DLL 및 게임 plugins DLL SHA-256:

`4F2760D265AAB1D43E815EC2096C686CEB076A5850F05610B631FF78439C0371`

재현 명령의 주요 예:

```powershell
dotnet build SecondaryAttacks.csproj -c Debug -p:DeployToGame=true
dotnet run --project tests/SummonQuality/SummonQuality.csproj -c Debug -- bin/Debug/SecondaryAttacks.dll 'C:/Program Files (x86)/Steam/steamapps/common/Valheim'
dotnet run --project tests/SubSummon/SubSummon.csproj -c Debug -- bin/Debug/SecondaryAttacks.dll 'C:/Program Files (x86)/Steam/steamapps/common/Valheim/valheim_Data/Managed'
dotnet run --project tests/SkillTooltipLayout/SkillTooltipLayout.csproj -c Debug -- 'C:/Users/blizz/RiderProjects/SecondaryAttacks/bin/Debug/SecondaryAttacks.dll' 'C:/Program Files (x86)/Steam/steamapps/common/Valheim'
```

미검토/제외 범위: bin/obj/배포 ZIP과 vendor 라이브러리 전체 구현은 구조 수정에서 제외했다. 외부 모드 DLL 전체 재분석, 모든 YAML 조합과 prefab/AssetBundle 의미 분석, 원본 게임 전체 코드/네이티브 부분, 다른 플랫폼 및 모든 공개/비공개 외부 reflection 소비자는 검토하지 않았다. 이미 준비된 게임 자료를 다시 수집하거나 원본 DLL을 수정하지 않았다.

## 안전한 적용·되돌리기 순서

1. `d156e8b`: CloneAttack만 교체 → 원본 계약/Debug/배포/diff 확인 → 커밋.
2. `b999a6b`: visual wrapper만 정리 → 전달 인자와 DLL/Debug/배포/diff 확인 → 커밋.
3. 이 검토 기록을 별도 문서 커밋으로 보관. 버전·의존성·Release ZIP·push는 수행하지 않는다.
4. 실제 실행에서는 초기화 오류 유무 → 자동/명시적 secondary와 YAML 복원 → 복제 투척/후속 투사체/boomerang의 owner 및 원격 visual → 월드 재접속 순으로 확인한다. 특히 Attack.Clone 패치를 가진 모드 조합은 별도로 확인한다.
5. 각 코드 커밋은 독립적으로 되돌린 후 Debug 빌드/배포할 수 있다. 기존 CleavingThrust 작업은 별도로 남겨 재검증/커밋할 수 있게 했다. 원격 저장소나 게임 원본을 되돌릴 필요는 없다.

설정 키·기본값·저장 형식·RPC 식별자·권한·동시 접근 정책은 변경하지 않았다. 네트워크 지연/owner 이동, 탄약·투척 아이템의 소실/복제, 소환 제한과 저장 복구는 실제 멀티플레이/저장 테스트가 남아 있다.
