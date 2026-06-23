# Smoothbrain Farming / Foraging / Mining DLL Analysis

이 문서는 Smoothbrain 계열 `Farming.dll`, `Foraging.dll`, `Mining.dll`을 ILSpy로 디컴파일해서 핵심 기능, 설정값, Valheim 패치 지점을 정리한 내용입니다.

## 확인 대상

```text
C:\Users\blizz\AppData\Roaming\com.kesomannen.gale\valheim\profiles\pvp\BepInEx\plugins\Smoothbrain-Farming\Farming.dll
C:\Users\blizz\AppData\Roaming\com.kesomannen.gale\valheim\profiles\pvp\BepInEx\plugins\Smoothbrain-Foraging\Foraging.dll
C:\Users\blizz\AppData\Roaming\com.kesomannen.gale\valheim\profiles\pvp\BepInEx\plugins\Smoothbrain-Mining\Mining.dll
```

디컴파일 임시 출력 위치:

```text
C:\Users\blizz\AppData\Local\Temp\SmoothbrainSkillDlls_Decompiled
```

## 한줄 요약

| Mod | Version | 핵심 기능 |
| --- | --- | --- |
| `Farming` | `2.2.2` | Valheim의 기본 Farming skill 106을 활용해서 작물 성장 속도, 수확량, 대량 심기/수확, 작물 진행률 표시를 조정합니다. |
| `Foraging` | `1.0.10` | `SkillManager`로 커스텀 Foraging skill을 추가하고, 버섯/베리류 같은 pickable의 수확량, 리스폰 속도, 대량 채집 범위를 조정합니다. |
| `Mining` | `1.1.6` | `SkillManager`로 커스텀 Mining skill을 추가하고, pickaxe damage, 광물 드랍량, 확률적 광맥 폭발을 조정합니다. |

공통적으로 `ServerSync`를 사용하며, `Lock Configuration = On` 기본값으로 서버 설정 잠금을 지원합니다. 세 모드 모두 `valheim_plus`와 incompatibility가 선언되어 있습니다.

## Farming.dll

플러그인 선언:

```csharp
[BepInPlugin("org.bepinex.plugins.farming", "Farming", "2.2.2")]
[BepInIncompatibility("org.bepinex.plugins.valheim_plus")]
```

### 스킬 구조

`Farming`은 `SkillManager` 커스텀 스킬을 새로 만들지 않고, Valheim 쪽에 이미 있는 `Skills.SkillType.Farming` 값 `106`을 직접 사용합니다.

`Skills.Awake` postfix에서 skill id `106`을 찾아 `m_increseStep`을 `Skill Experience Gain Factor` 값으로 바꿉니다.

죽을 때는 `Skills.OnDeath` prefix/finalizer로 Farming skill을 잠시 `m_skillData`에서 제거한 뒤, 모드 설정의 `Skill Experience Loss`만큼만 직접 감소시키고 다시 넣습니다. 즉 바닐라 death skill loss와 별도 관리입니다.

### 주요 설정값

| Section | Name | Default | 의미 |
| --- | --- | ---: | --- |
| `1 - General` | `Lock Configuration` | `On` | 서버 설정 잠금 |
| `2 - Crops` | `Grow Speed Factor` | `3` | Farming 100에서 작물 성장 속도 배율 |
| `2 - Crops` | `Crop Yield Factor` | `2` | Farming 100에서 작물 수확량 배율 후보 |
| `2 - Crops` | `Show Progress Level` | `30` | 작물 성장률 표시 요구 레벨, 0이면 비활성 |
| `2 - Crops` | `Ignore Biome Level` | `50` | 작물 biome 제한 무시 요구 레벨, 0이면 비활성 |
| `2 - Crops` | `Plant Increase Interval` | `20` | 대량 심기 크기 증가 간격, 0이면 비활성 |
| `2 - Crops` | `Harvest Increase Interval` | `20` | 대량 수확 반경 증가 간격, 0이면 비활성 |
| `2 - Crops` | `Stamina Reduction per Level` | `1` | 심기 스태미나 감소 계산에 사용 |
| `2 - Crops` | `Random Rotation` | `Off` | 작물별 랜덤 회전, 클라이언트 로컬 |
| `3 - Other` | `Skill Experience Gain Factor` | `1` | Farming 경험치 증가량 배율 |
| `3 - Other` | `Skill Experience Loss` | `0` | 사망 시 Farming 경험치 손실률 |
| `3 - Other` | `Toggle Mass Plant Hotkey` | `LeftShift` | 대량 심기 토글, 클라이언트 로컬 |
| `3 - Other` | `Toggle Snapping Hotkey` | `LeftControl` | 스냅 토글, 클라이언트 로컬 |

### 작물 성장 속도

작물을 심을 때 해당 플레이어의 Farming skill factor를 `Plant`의 ZDO에 저장합니다.

```text
ZDO["Farming Skill Level"] = planterFarmingSkillFactor
```

이후 `Plant.GetGrowTime` postfix에서 성장 시간이 줄어듭니다.

```text
growTime = vanillaGrowTime / (1 + savedFarmingFactor * (GrowSpeedFactor - 1))
```

기본값 기준으로 Farming factor가 `1.0`이면 `Grow Speed Factor = 3`이므로 성장 시간이 1/3이 됩니다.

### 작물 수확량

작물이 `Pickable`로 깨어날 때 `Farming Yield Multiplier`를 ZDO에 저장하고, `m_amount`를 곱합니다.

```text
if random < savedFarmingFactor:
    multiplier = floor(CropYieldFactor) + fractionalRoll
else:
    multiplier = 1

pickable.m_amount *= multiplier
```

기본값 `Crop Yield Factor = 2`에서는 skill factor가 높을수록 2배 수확량이 붙을 확률이 올라가고, Farming 100에서는 사실상 2배가 됩니다.

### Biome 제한 무시

`Ignore Biome Level`보다 저장된 Farming skill factor가 높으면:

```text
Plant.m_biome = (Biome)(-1)
```

즉 해당 작물의 biome 체크를 무력화하는 방식입니다.

### 작물 진행률 표시

`Plant.GetHoverText` postfix에서 local player의 Farming level이 `Show Progress Level` 이상이면 hover text에 다음 형태를 붙입니다.

```text
NN% grown
```

### 바닐라 Farming 경험치 억제

이 모드는 바닐라 쪽 Farming 경험치 증가를 몇 군데에서 막고, 직접 제어합니다.

| Patch | 내용 |
| --- | --- |
| `Player.UpdatePlacement` transpiler | cultivator placement에서 바닐라 `RaiseSkill` amount를 0으로 바꿈 |
| `Pickable.Interact` transpiler | `m_pickRaiseSkill == 106`인 pickable의 바닐라 Farming gain을 0으로 바꿈 |
| `Player.GetBuildStamina` transpiler | skill 106을 `None`으로 바꿔 바닐라 build stamina 계산에 Farming이 들어가지 않게 함 |

### 대량 수확

`Pickable.Interact` postfix에서 `Farming Yield Multiplier > 0`인 pickable을 Farming crop으로 보고 근처 작물을 추가로 수확합니다.

```text
radius = floor(farmingSkillFactor * 100 / HarvestIncreaseInterval) * 1.5
mask = piece_nonsolid | item
```

기본 `Harvest Increase Interval = 20`이면 Farming 100에서:

```text
floor(100 / 20) * 1.5 = 7.5m
```

### 대량 심기

`MassPlant`는 cultivator로 작물을 심을 때 grid를 만들고 추가 작물을 자동 배치합니다.

```text
GridWidth  = 1 + floor(skillFactor * (100 / PlantIncreaseInterval)) / 2
GridHeight = 1 + floor(min(skillFactor, 0.999) * (100 / PlantIncreaseInterval) + 1) / 2
```

기본 `Plant Increase Interval = 20`이면 Farming 100 근처에서 대략 `3 x 3` 배치가 됩니다.

심기 스태미나 비용은 cultivator의 attack stamina를 일시적으로 줄이는 방식입니다.

```text
attackStamina *= max(0, 1 - farmingSkillFactor * StaminaReductionPerLevel)
```

주의할 점은 config 설명은 "percentage per level"처럼 보이지만, 코드상으로는 `skillFactor * 설정값`을 그대로 씁니다. 기본값 `1`이면 Farming 100에서 심기 stamina가 0까지 줄어듭니다.

## Foraging.dll

플러그인 선언:

```csharp
[BepInPlugin("org.bepinex.plugins.foraging", "Foraging", "1.0.10")]
[BepInIncompatibility("org.bepinex.plugins.valheim_plus")]
```

### 스킬 구조

`SkillManager`로 커스텀 스킬을 등록합니다.

```csharp
foraging = new Skill("Foraging", "foraging.png");
foraging.Description.English("Increases item yield for foraging and makes mushrooms and berries respawn quicker.");
foraging.Configurable = false;
```

`Player.Update`에서 local player의 skill factor를 ZDO에 계속 저장합니다.

```text
ZDO["Foraging Skill Factor"] = player.GetSkillFactor("Foraging")
```

`Player.Awake`에서는 RPC `"Foraging IncreaseSkill"`을 등록하고, pickable을 실제로 딸 때 서버/소유자 쪽에서 이 RPC로 스킬을 올립니다.

### 주요 설정값

| Section | Name | Default | 의미 |
| --- | --- | ---: | --- |
| `1 - General` | `Lock Configuration` | `On` | 서버 설정 잠금 |
| `2 - Foraging` | `Foraging Scope` | `Foraging` | 적용 pickable 범위 |
| `2 - Foraging` | `Foraging Yield Factor` | `2` | Foraging 100에서 수확량 배율 후보 |
| `2 - Foraging` | `Minimum Level Respawn Display` | `30` | 리스폰 시간 표시 요구 레벨 |
| `2 - Foraging` | `Maximum Mass Picking Range` | `10` | Foraging 100에서 대량 채집 최대 반경 |
| `2 - Foraging` | `Multiplier for Respawn Speed` | `2` | Foraging 100에서 리스폰 가속 배율 |
| `3 - Other` | `Skill Experience Gain Factor` | `1` | Foraging 경험치 증가 배율 |
| `3 - Other` | `Skill Experience Loss` | `0` | 사망 시 Foraging 경험치 손실률 |

### Foraging Scope 판정

`isForaging(Pickable pickable)`의 범위는 설정에 따라 달라집니다.

| Scope | 판정 |
| --- | --- |
| `Foraging` | `m_respawnTimeMinutes > 0`, item prefab name이 `Wood`, `DragonEgg`가 아니면 true |
| `NoCrops` | `piece_nonsolid` layer는 제외. `item` layer는 `m_amount <= 1`인 경우만 포함. 그 외는 포함 |
| `Everything` | 모든 pickable 포함 |

기본 `Foraging`은 이름 그대로 베리/버섯처럼 리스폰되는 채집물을 대상으로 삼는 구조입니다.

### 수확량 증가

`Pickable.RPC_Pick` prefix에서 pickable owner가 처리합니다.

```text
if random < playerForagingSkillFactor:
    oldAmount = pickable.m_amount
    pickable.m_amount *= floor(ForagingYieldFactor) + fractionalRoll
```

`RPC_Pick` finalizer에서 `m_amount`는 원래 값으로 되돌립니다. 즉 드랍 순간에만 수확량을 늘리고, prefab 상태 자체를 영구 변경하지 않습니다.

### 리스폰 시간 단축

`Pickable.RPC_Pick` postfix에서 `picked_time`을 과거로 당깁니다.

```text
picked_time -= respawnTimeMinutes * 600000000 * (1 - 1 / RespawnSpeedMultiplier) * foragingSkillFactor
```

`600000000`은 DateTime tick 기준 1분입니다.

기본 `RespawnSpeedMultiplier = 2`이고 Foraging 100이면, 리스폰 시간의 절반만 기다리면 되도록 `picked_time`을 조정하는 셈입니다.

### 대량 채집

`Pickable.Interact` postfix에서 주변 pickable을 `OverlapSphere`로 찾아 `Interact`를 연쇄 호출합니다.

```text
radius = foragingSkillFactor * MaximumMassPickingRange
mask = item | Default_small | piece_nonsolid
```

기본값 기준 Foraging 100에서는 10m 반경입니다.

## Mining.dll

플러그인 선언:

```csharp
[BepInPlugin("org.bepinex.plugins.mining", "Mining", "1.1.6")]
[BepInIncompatibility("org.bepinex.plugins.valheim_plus")]
```

### 스킬 구조

`SkillManager`로 커스텀 스킬을 등록합니다.

```csharp
mining = new Skill("Mining", "mining.png");
mining.Description.English("Increases damage done while mining and item yield from ore deposits.");
mining.Configurable = false;
```

`Player.Update`에서 local player의 skill factor를 ZDO에 계속 저장합니다.

```text
ZDO["Mining Skill Factor"] = player.GetSkillFactor("Mining")
```

`Player.Awake`에서는 RPC `"Mining IncreaseSkill"`을 등록합니다.

### 주요 설정값

| Section | Name | Default | 의미 |
| --- | --- | ---: | --- |
| `1 - General` | `Lock Configuration` | `On` | 서버 설정 잠금 |
| `2 - Mining` | `Mining Damage Factor` | `3` | Mining 100에서 pickaxe damage 배율 |
| `2 - Mining` | `Mining Yield Factor` | `2` | Mining 100에서 광물 드랍량 배율 |
| `2 - Mining` | `Mining Explosion Level Requirement` | `50` | 광맥 폭발 발동 최소 레벨, 0이면 비활성 |
| `2 - Mining` | `Mining Explosion Chance` | `1` | Mining 100에서 광맥 폭발 확률, 퍼센트 |
| `2 - Mining` | `Toggle Explosive Mining Hotkey` | `LeftControl + T` | 폭발 채굴 토글, 클라이언트 로컬 |
| `3 - Other` | `Skill Experience Gain Factor` | `1` | Mining 경험치 증가 배율 |
| `3 - Other` | `Skill Experience Loss` | `0` | 사망 시 Mining 경험치 손실률 |

디컴파일상 hotkey는 `(KeyCode)116 + (KeyCode)306`이며, Unity `KeyCode` 기준으로 `116 = T`, `306 = LeftControl`입니다.

### Pickaxe damage 증가

`SEMan.ModifyAttack` prefix에서 공격자가 player이면 `HitData.m_damage.m_pickaxe`를 직접 배율 조정합니다.

```text
pickaxeDamage *= 1 + miningSkillFactor * (MiningDamageFactor - 1)
```

기본 `Mining Damage Factor = 3`이면 Mining 100에서 pickaxe damage가 3배입니다.

이 패치는 `m_pickaxe` damage를 가진 player 공격 전반에 걸립니다. 따라서 다른 모드가 pickaxe damage 타입을 사용하고 그 hit가 `SEMan.ModifyAttack` 경로를 탄다면 Mining damage 배율을 받을 수 있습니다.

### Mining skill 증가 조건

Mining skill은 아무 pickaxe damage에서 항상 오르는 것이 아니라, 다음 RPC 패치에서 `HandleMining` 조건을 통과할 때 오릅니다.

| 대상 | Patch |
| --- | --- |
| `MineRock5` | `MineRock5.RPC_Damage` prefix |
| `MineRock` | `MineRock.RPC_Hit` prefix |
| 일부 `Destructible` | `Destructible.RPC_Damage` prefix |

공통 조건:

```text
attacker is Player
hit.m_toolTier >= targetMinToolTier
hit.m_damage.m_pickaxe > 0
netView.IsValid()
netView.IsOwner()
```

조건을 통과하면:

```text
player RPC "Mining IncreaseSkill" amount 1
```

### Destructible 대상 조건

`Destructible.RPC_Damage`에서는 다음 조건을 추가로 확인합니다.

```text
m_damages.m_pickaxe != Immune
m_damages.m_chop == Immune
m_destructibleType != Character
```

`HitData.DamageModifier` enum에서 `Immune`은 값 `3`입니다. 따라서 "pickaxe에는 면역이 아니고, chop에는 면역이며, character destructible은 아닌 대상"만 Mining destructible 대상으로 봅니다.

### 광물 드랍량 증가

`DropTable.GetDropList(int)` postfix에서 `SetMiningFlag.IsMining`이 켜져 있을 때만 결과 리스트를 복제합니다.

```text
repeatCount = floor(1 + miningSkillFactor * (MiningYieldFactor - 1) + random(0, 1))
```

기본 `Mining Yield Factor = 2`이면 Mining 100에서 거의 2배 드랍입니다.

이 플래그는 `MineRock5`, `MineRock`, qualifying `Destructible`의 mining RPC 처리 중에만 켜집니다. 그래서 일반 몬스터 드랍이나 unrelated DropTable에는 적용되지 않습니다.

### 광맥 폭발

폭발 채굴이 켜져 있고, `Mining Explosion Level Requirement`를 만족하며, 확률 체크에 성공하면 원래 RPC를 막고 광맥/파괴 가능 오브젝트의 남은 체력을 한 번에 깎습니다.

확률식:

```text
chance =
    (miningSkillFactor - (ExplosionLevelRequirement - 10) / 100)
    / (1 - (ExplosionLevelRequirement - 10) / 100)
    * MiningExplosionChance / 100
```

단, 코드 앞에서 이미:

```text
miningSkillFactor >= ExplosionLevelRequirement / 100
```

를 요구합니다.

기본값 예시:

```text
requirement = 50
explosionChance = 1%

Mining 50:
    ((0.50 - 0.40) / 0.60) * 0.01 = 0.001666... = 약 0.1667%

Mining 100:
    ((1.00 - 0.40) / 0.60) * 0.01 = 0.01 = 1%
```

폭발 처리 방식:

| 대상 | 폭발 처리 |
| --- | --- |
| `MineRock5` | 모든 hit area에 남은 health만큼 damage, toolTier 100 |
| `MineRock` | 모든 hit area에 남은 health만큼 damage, 이후 hitAreas를 empty로 변경 |
| `Destructible` | 전체 health만큼 damage, toolTier 100 |

## 세 모드의 상호작용과 현재 모드 관점 메모

`Farming`과 `Foraging`은 둘 다 `Pickable.Interact`를 패치하지만, 목적이 다릅니다.

- `Farming`의 대량 수확은 `Farming Yield Multiplier > 0`인 crop pickable만 대상으로 봅니다.
- `Foraging`의 대량 채집은 `Foraging Scope`에 따라 pickable을 대상으로 봅니다.
- 둘 다 재귀 방지를 위한 static bool을 두고 있습니다.

`Farming`은 바닐라 Farming skill 106을 건드리고, `Foraging`/`Mining`은 `SkillManager` 커스텀 스킬을 추가합니다. 그래서 skill id 충돌 구조는 아닙니다.

`Mining`은 현재 `SecondaryAttacks`의 pickaxe 계열 preset과 연결될 가능성이 가장 큽니다.

- Secondary attack이 `HitData.m_damage.m_pickaxe`를 사용하고 `SEMan.ModifyAttack`을 통과하면 Mining damage factor가 적용될 수 있습니다.
- Mining skill gain, yield, explosion은 `MineRock5.RPC_Damage`, `MineRock.RPC_Hit`, `Destructible.RPC_Damage` 쪽에 도달해야 동작합니다.
- 단순히 custom overlap으로 `IDestructible`을 찾아 damage를 주는 경우에도 내부적으로 해당 RPC/`Damage` 경로를 타는지에 따라 Mining 연동 여부가 달라질 수 있습니다.

즉, pickaxe secondary preset을 Mining과 자연스럽게 호환시키려면 "pickaxe damage 타입을 유지"하고, 가능하면 바닐라의 MineRock/MineRock5/Destructible damage 경로를 우회하지 않는 편이 좋습니다.
