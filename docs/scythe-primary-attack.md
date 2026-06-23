# Scythe Primary Attack Analysis

이 문서는 Valheim 기본 `Scythe` primary attack의 판정 구조를 정리한 내용입니다.

## 결론

`Scythe` primary는 전투용 광역 피해 공격이 아닙니다.

공격 자체는 `AttackType.Horizontal` 기반의 일반 melee 흐름을 타지만, 무기 피해와 push가 모두 `0`입니다. 실제 용도는 melee 처리 뒤에 별도로 실행되는 `m_harvest` 처리입니다.

```text
DoMeleeAttack()
  1. 얇은 horizontal melee ray 판정 실행
  2. 피해 0 / push 0 HitData 처리 가능
  3. local player이고 m_harvest == true이면 harvest sphere 실행
```

즉, scythe의 실질 기능은 적이나 오브젝트에 데미지를 주는 것이 아니라, 플레이어 앞쪽 지점을 중심으로 주변 `Pickable`을 수확하고, 비정상 상태의 `Plant`를 제거하는 것입니다.

## 확인한 대상

확인한 에셋 위치:

```text
C:\Users\blizz\Documents\vunity\ValheimDefault\Assets\GameObject\Scythe_0.prefab
```

확인한 코드 위치:

```text
C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\decompiled_full\Attack.cs
Type: Attack
Method: DoMeleeAttack()

C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\decompiled_full\Skills.cs
Type: Skills.SkillType
```

## 아이템 분류

| Field | Value | Meaning |
| --- | ---: | --- |
| `m_itemType` | `14` | `ItemType.TwoHandedWeapon` |
| `m_animationState` | `17` | `AnimationState.Scythe` |
| `m_skillType` | `106` | `Skills.SkillType.Farming` |
| `m_toolTier` | `0` | tool tier 없음 |
| `m_maxDurability` | `200` | 기본 내구도 |
| `m_durabilityPerLevel` | `200` | 품질당 내구도 증가 |
| `m_useDurabilityDrain` | `1` | 적중 시 내구도 1 감소 |

`Skills.SkillType.Farming = 106`입니다.

## 기본 피해 수치

`Scythe`의 shared damage는 모두 0입니다.

| Damage Field | Value |
| --- | ---: |
| `m_damage` | `0` |
| `m_blunt` | `0` |
| `m_slash` | `0` |
| `m_pierce` | `0` |
| `m_chop` | `0` |
| `m_pickaxe` | `0` |
| elemental damage | `0` |

추가로 다음 값도 전투용 무기와 다릅니다.

| Field | Value | Meaning |
| --- | ---: | --- |
| `m_attackForce` | `0` | push 없음 |
| `m_backstabBonus` | `1` | 백스탭 보너스 없음 |
| `m_dodgeable` | `0` | dodgeable 아님 |
| `m_blockable` | `0` | blockable 아님 |

## Primary attack 값

| Field | Value | Meaning |
| --- | ---: | --- |
| `m_attackType` | `0` | `AttackType.Horizontal` |
| `m_attackAnimation` | `scything` | scythe 전용 휘두르기 애니메이션 |
| `m_hitTerrain` | `1` | terrain/piece layer 포함 |
| `m_isHomeItem` | `1` | home item 취급 |
| `m_attackStamina` | `5` | 스태미나 소모 |
| `m_attackStartNoise` | `10` | 시작 소음 |
| `m_attackHitNoise` | `10` | 히트 소음 |
| `m_speedFactor` | `0.2` | 공격 중 이동 속도 계수 |
| `m_speedFactorRotation` | `0.2` | 공격 중 회전 속도 계수 |
| `m_attackRange` | `1.5` | melee ray 길이 및 harvest 중심 전방 거리 |
| `m_attackHeight` | `0.6` | melee origin 높이 |
| `m_attackOffset` | `0` | 좌우 offset 없음 |
| `m_attackAngle` | `90` | horizontal fan 전체 각도 |
| `m_attackRayWidth` | `0` | SphereCast가 아니라 Raycast 사용 |
| `m_attackRayWidthCharExtra` | `0` | 캐릭터 추가 두께 없음 |
| `m_lowerDamagePerHit` | `1` | 다중 hit 시 skill factor 보정 사용 |
| `m_hitPointtype` | `0` | `HitPointType.Closest` |
| `m_hitThroughWalls` | `0` | 첫 유효 hit 뒤 해당 ray 중단 |
| `m_multiHit` | `1` | 여러 hit target 처리 가능 |
| `m_pickaxeSpecial` | `0` | pickaxe 특수 처리 없음 |
| `m_raiseSkillAmount` | `1` | skill raise amount |
| `m_skillHitType` | `4` | `DestructibleType.Character` |
| `m_harvest` | `1` | melee 뒤 harvest 처리 실행 |
| `m_harvestRadius` | `1.5` | Farming 0 기준 수확 반경 |
| `m_harvestRadiusMaxLevel` | `2.5` | Farming 100 기준 수확 반경 |

## 일반 melee 판정

`Scythe` primary는 `DoMeleeAttack()`의 일반 horizontal melee 판정을 사용합니다.

```text
origin = attack origin + up * 0.6
halfAngle = 90 / 2 = 45 degrees
step = 4 degrees
range = 1.5
rayWidth = 0

for angle from -45 to +45 step 4:
    direction = player attack direction rotated horizontally by angle
    Raycast(origin, direction, range)
```

`m_attackRayWidth = 0`이므로 `SphereCast`가 아니라 `RaycastNonAlloc`입니다. 따라서 이 단계의 전투 판정은 아주 얇은 여러 개의 ray입니다.

다만 이 melee 판정으로 생성되는 `HitData`의 피해와 push가 모두 0이기 때문에, 전투 공격으로는 실질적인 의미가 거의 없습니다.

## Harvest 처리

`DoMeleeAttack()`의 마지막 부분에서 다음 조건이면 harvest 처리가 실행됩니다.

```text
m_harvest == true
m_character == Player.m_localPlayer
```

처리 위치와 반경은 다음과 같습니다.

```text
harvestCenter = meleeOrigin + attackDir * attackRange
radius = lerp(harvestRadius, harvestRadiusMaxLevel, FarmingSkillFactor)
```

`Scythe` 기본값을 넣으면:

```text
harvestCenter = origin + attackDir * 1.5
radius = lerp(1.5, 2.5, FarmingSkillFactor)
```

따라서 Farming 0이면 반경 1.5m, Farming 100이면 반경 2.5m입니다.

대상 layer mask:

```text
piece
piece_nonsolid
item
```

대상 처리:

```text
for each collider in OverlapSphere(harvestCenter, radius):
    if Pickable exists and CanBePicked() and (m_harvestable or foraging target):
        Pickable.Interact(localPlayer, repeat: false, alt: false)

    else if Plant exists and Plant.GetStatus() != Healthy:
        Destructible.Destroy()
```

즉 수확 가능한 `Pickable`은 상호작용으로 수확하고, healthy가 아닌 `Plant`는 파괴합니다.

## Farming skill과 보너스

Scythe의 harvest 반경은 Farming skill factor로 증가합니다.

```text
radius = 1.5 + (2.5 - 1.5) * FarmingSkillFactor
```

수확 자체의 skill 상승과 bonus yield는 `Pickable.Interact()` 쪽에서 처리됩니다.

`Pickable`이 `m_pickRaiseSkill = Farming`을 가지고 있으면 수확 시 Farming skill을 올리고, 다음 확률로 bonus yield를 줍니다.

```text
bonusChance = FarmingSkillFactor * m_maxLevelBonusChance
```

많은 harvestable prefab에서 `m_pickRaiseSkill: 106`과 `m_maxLevelBonusChance: 0.25`가 확인됩니다.

## Multi-target penalty

Scythe primary에는 다음 값이 있습니다.

```text
m_multiHit = true
m_lowerDamagePerHit = true
```

따라서 일반 melee hit list가 2개 이상이면 `DoMeleeAttack()`의 바닐라 보정이 적용됩니다.

```text
skillFactor /= hitCount * 0.75
```

하지만 Scythe의 melee damage와 push가 모두 0이므로, 이 보정은 전투 피해량 관점에서는 거의 의미가 없습니다.

중요한 점은 harvest sphere에는 이 multi-target damage penalty가 적용되지 않는다는 것입니다. Harvest는 `Damage()`가 아니라 `Pickable.Interact()`와 `Plant.Destroy()`를 호출하는 별도 처리입니다.

## Area attack이 아닌 점

Scythe primary는 `Sledge`처럼 `AttackType.Area`가 아닙니다.

| 구분 | Scythe primary | Sledge primary |
| --- | --- | --- |
| 공격 타입 | `AttackType.Horizontal` | `AttackType.Area` |
| 피해 판정 | 여러 방향의 얇은 ray | `OverlapSphere` |
| 실질 기능 | harvest sphere | damage sphere |
| 피해량 | 0 | 무기 damage |
| harvest | 있음 | 없음 |

Scythe에 있는 sphere는 피해용 sphere가 아니라 수확용 sphere입니다.

## Secondary attack

`Scythe` prefab에는 secondary attack 블록도 있지만, `m_attackAnimation`이 비어 있고 `m_harvest = 0`입니다.

따라서 바닐라 기준으로 Scythe의 의미 있는 동작은 primary `scything` harvest 쪽입니다.

## 모드 구현 관점 메모

Scythe primary를 복사해서 전투 preset의 기반으로 쓰면, 기본 피해와 push가 모두 0이라는 점을 조심해야 합니다.

Scythe다운 secondary를 만들려면 단순히 피해 판정을 복사하기보다 다음 중 하나가 더 자연스럽습니다.

- `m_harvest` 흐름을 참고해서 별도 harvest/plant 처리 preset을 만든다.
- Farming skill에 따라 radius가 증가하는 별도 melee utility preset을 만든다.
- 전투용으로 쓰려면 damage/push는 YAML preset 전용 값으로 따로 부여한다.

Scythe의 핵심은 `Horizontal melee hit`가 아니라 `Farming-scaled harvest sphere`입니다.

## SecondaryAttacks 보정

이 모드는 `Farming Skill Override / Scythe Harvest Improvements = On`일 때 scythe harvest를 한 번 가로채서 vanilla harvest 대신 보정된 harvest를 실행합니다.

목적은 두 가지입니다.

- collider가 child에 있고 `Pickable`이 parent/root에 있는 작물도 수확되게 한다.
- respawn 시간이 있는 자연 채집물/foraging형 pickable도 edible drop foraging target이고 `CanBePicked()`이면 scythe 수확 대상에 포함한다.

보정된 pickable 조건:

```text
pickable = collider.GetComponentInParent<Pickable>()

pickable.CanBePicked() == true
pickable.m_harvestable == true OR IsForagingTarget(pickable) == true
```

따라서 `Pickable_Mushroom_JotunPuffs`, `Pickable_Mushroom_Magecap`처럼 `Pickable`은 root에 있고 collider는 child에 있는 작물형 pickable도 scythe 수확 대상이 됩니다.

또한 `BlueberryBush`, `Pickable_Fiddlehead`, `Pickable_Mushroom`처럼 `m_respawnTimeMinutes > 0`이고 edible item을 드랍하는 대상도 `CanBePicked()`이면 scythe 수확 대상에 포함됩니다.

관련 config:

| Config | Default | Meaning |
| --- | ---: | --- |
| `Scythe Harvest Improvements` | `On` | scythe가 child collider 대상도 잡고, harvestable pickable과 edible foraging pickable을 수확 |
| `Foraging Pickup Max Range` | `5` | `respawnTimeMinutes > 0`이고 edible item을 드랍하는 pickable을 직접 주울 때, Farming skill에 따라 주변 같은 대상도 줍기. Scythe primary와 harvestSweep 수확에는 적용되지 않음. 0이면 off |
| `Foraging Respawn Speed Factor` | `10` | 위 foraging 대상의 리스폰 시간을 Farming skill에 따라 단축. 0이면 off |
| `Plant Grow Speed Factor` | `5` | 심어서 생성된 Plant의 성장 시간을 심은 사람의 Farming skill에 따라 단축. 0이면 off |

정수 factor/range config 중 `Foraging Pickup Max Range`와 `Plant Grow Speed Factor`는 `0..10`, `Foraging Respawn Speed Factor`는 `0..20` 범위입니다. 속도 factor는 Farming 0에서 1배, Farming 100에서 설정값 배율이 되도록 선형 보간합니다.
