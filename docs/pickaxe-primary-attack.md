# Pickaxe Primary Attack Analysis

이 문서는 Valheim 기본 pickaxe의 primary attack이 어떤 판정 구조를 쓰는지 정리한 내용입니다.

## 결론

Pickaxe primary attack은 `AttackType.Area` 기반의 원형 AOE 공격은 아닙니다.

대신 `AttackType.Vertical` melee 공격이며, 공격 방향을 기준으로 위아래 방향에 여러 개의 `SphereCast`를 쏘는 vertical fan 판정입니다. 그래서 코드 구조상으로는 일반 melee 판정에 가깝지만, 다음 설정 때문에 실제 플레이에서는 범위 공격처럼 느껴질 수 있습니다.

- `m_attackAngle: 120`
- `m_attackRayWidth: 0.2`
- `m_multiHit: 1`
- `m_pickaxeSpecial: 1`
- terrain hit 시 `digg_v3` terrain modifier가 생성됨

즉 "데미지 판정"은 원형 AOE가 아니라 여러 ray/spherecast 기반이고, "지형 파기"는 맞은 지점에 terrain modifier를 생성해서 반경 효과를 냅니다.

## 기본 프리팹 값

확인한 에셋 위치:

```text
C:\Users\blizz\Documents\vunity\ValheimDefault\Assets\GameObject\PickaxeStone.prefab
C:\Users\blizz\Documents\vunity\ValheimDefault\Assets\GameObject\PickaxeAntler.prefab
C:\Users\blizz\Documents\vunity\ValheimDefault\Assets\GameObject\PickaxeBronze.prefab
C:\Users\blizz\Documents\vunity\ValheimDefault\Assets\GameObject\PickaxeIron.prefab
C:\Users\blizz\Documents\vunity\ValheimDefault\Assets\GameObject\PickaxeBlackMetal.prefab
```

모든 기본 pickaxe primary attack의 핵심 판정 값은 동일합니다.

| Field | Value | Meaning |
| --- | ---: | --- |
| `m_attackType` | `1` | `AttackType.Vertical` |
| `m_attackAnimation` | `swing_pickaxe` | pickaxe swing animation |
| `m_hitTerrain` | `1` | terrain layer까지 판정 |
| `m_attackRange` | `1.8` | 판정 길이 |
| `m_attackHeight` | `1` | origin에서 위쪽으로 올리는 높이 |
| `m_attackAngle` | `120` | vertical fan 전체 각도 |
| `m_attackRayWidth` | `0.2` | SphereCast 반경 |
| `m_attackRayWidthCharExtra` | `0` | 캐릭터 추가 반경 없음 |
| `m_hitPointtype` | `2` | `HitPointType.First` |
| `m_lowerDamagePerHit` | `1` | 다중 히트 시 데미지 보정 사용 |
| `m_multiHit` | `1` | 여러 hit target 처리 가능 |
| `m_pickaxeSpecial` | `1` | MineRock/MineRock5와 terrain 처리 특수 분기 |

Pickaxe별 damage/stamina 차이는 있습니다.

| Prefab | Pickaxe damage | Damage per level | Stamina | Tool tier |
| --- | ---: | ---: | ---: | ---: |
| `PickaxeStone` | 15 | 0 | 4 | 0 |
| `PickaxeAntler` | 18 | 0 | 6 | 0 |
| `PickaxeBronze` | 25 | 4 | 8 | 1 |
| `PickaxeIron` | 33 | 5 | 10 | 2 |
| `PickaxeBlackMetal` | 49 | 5 | 14 | 3 |

`m_skillType: 12`이며, Valheim 코드상 `Skills.SkillType.Pickaxes`입니다.

## 실제 melee 판정 흐름

확인한 코드 위치:

```text
C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\Managed\publicized_assemblies\assembly_valheim_publicized.dll
Type: Attack
Method: DoMeleeAttack()
```

핵심 흐름은 다음과 같습니다.

```text
origin = attack origin + up * m_attackHeight + right * m_attackOffset
halfAngle = m_attackAngle / 2
step = 4 degrees

for angle from -halfAngle to +halfAngle step 4:
    if AttackType.Vertical:
        rotate attack direction vertically by angle

    if m_attackRayWidth > 0:
        SphereCast(origin, m_attackRayWidth, direction, m_attackRange - m_attackRayWidth)
    else:
        Raycast(origin, direction, m_attackRange)

    sort hits by distance
    add the first valid hit for that ray
    if !m_hitThroughWalls:
        stop this ray after first valid hit
```

Pickaxe primary는 `m_attackAngle = 120`이므로 `-60`도부터 `+60`도까지 4도 간격으로 검사합니다. 대략 31개의 방향이 생깁니다.

`m_attackRayWidth = 0.2`이므로 실제 ray는 얇은 선이 아니라 반경 0.2m의 SphereCast입니다. SphereCast 거리 인자는 `m_attackRange - m_attackRayWidth`, 즉 `1.6`이지만 구의 반경까지 포함하면 감각상 최대 reach는 `1.8`에 가깝습니다.

## Vertical fan의 의미

Pickaxe는 `AttackType.Vertical`이라서 `m_attackAngle`이 좌우 부채꼴이 아니라 위아래 방향으로 펼쳐집니다.

```text
side view

       ray +60 deg
          /
         /
player ---- aim direction
         \
          \
       ray -60 deg
```

수평 부채꼴로 넓게 베는 공격이라기보다, 플레이어가 바라보는 방향 주변을 위아래로 훑는 내려찍기 판정입니다. 그래서 바닥, 바위, 경사면에 잘 닿습니다.

## Multi-hit와 pickaxeSpecial

Pickaxe primary는 `m_multiHit = 1`이라서 한 번의 공격에서 여러 hit point를 처리할 수 있습니다.

일반 대상은 같은 GameObject 기준으로 중복 hit가 합쳐집니다. 하지만 `m_pickaxeSpecial = 1`이고 대상이 `MineRock` 또는 `MineRock5`이면 collider 단위로 hit point를 분리합니다.

코드상 핵심 조건:

```csharp
bool multiCollider =
    m_pickaxeSpecial &&
    (target.GetComponent<MineRock5>() || target.GetComponent<MineRock>());
```

그래서 광맥/큰 바위류는 같은 오브젝트 안의 여러 collider가 한 swing에서 별도 hit처럼 처리될 수 있습니다. 이 부분이 pickaxe가 일반 melee보다 "넓게 먹는" 느낌을 주는 핵심입니다.

다만 이 역시 원형 `OverlapSphere` AOE가 아니라, 여러 SphereCast가 맞춘 collider들을 모아서 처리하는 방식입니다.

## Terrain hit 처리

Pickaxe primary의 `m_hitTerrain = 1` 때문에 terrain layer가 공격 mask에 포함됩니다.

terrain을 맞으면 weapon shared field의 `m_spawnOnHitTerrain` prefab을 생성합니다. 기본 pickaxe들은 다음 prefab을 참조합니다.

```text
digg_v3
C:\Users\blizz\Documents\vunity\ValheimDefault\Assets\GameObject\digg_v3.prefab
```

`digg_v3`의 terrain modifier 설정:

| Field | Value |
| --- | ---: |
| `m_levelRadius` | `1.5` |
| `m_raiseRadius` | `1.5` |
| `m_raisePower` | `0.5` |
| `m_raiseDelta` | `-0.5` |
| `m_smoothRadius` | `2` |
| `m_paintRadius` | `2.5` |

따라서 바닥을 팔 때 실제 지형 변화는 hit point 하나에서 발생하지만, 생성되는 terrain modifier 자체가 반경을 가지고 작동합니다. 이것은 melee 데미지 AOE와는 별개입니다.

## Damage와 내구도

`DoMeleeAttack()`은 hit list를 만든 뒤 각 `IDestructible` 대상에 `HitData`를 적용합니다.

`m_multiHit`와 `m_lowerDamagePerHit`가 모두 true이고 hit list가 2개 이상이면, 각 hit의 skill factor가 다음처럼 줄어듭니다.

```text
skillFactor /= hitCount * 0.75
```

즉 여러 대상 또는 여러 MineRock collider를 맞추면 데미지 보정이 들어갈 수 있습니다.

내구도는 target마다 깎이는 구조가 아니라, `numHits > 0`이면 공격 1회에 대해 `m_useDurabilityDrain`만큼 한 번 감소합니다.

## SecondaryAttacks 구현 관점

Pickaxe primary를 복제하거나 비슷한 preset을 만들 때는 다음처럼 이해하는 편이 안전합니다.

- 원형 AOE를 재현하려면 `AttackType.Area`나 `OverlapSphere` 계열을 새로 써야 합니다.
- 바닐라 pickaxe primary 느낌을 재현하려면 `AttackType.Vertical`, `m_attackAngle`, `m_attackRayWidth`, `m_multiHit`, `m_pickaxeSpecial` 조합을 유지하는 쪽이 맞습니다.
- 바닥 파기까지 재현하려면 `m_hitTerrain = true`와 `m_spawnOnHitTerrain = digg_v3` 흐름이 필요합니다.
- 광맥에 여러 지점이 맞는 느낌은 `m_pickaxeSpecial`의 MineRock/MineRock5 collider 단위 처리에서 옵니다.

정리하면, pickaxe primary는 "AOE 공격"이라기보다는 "terrain까지 포함하는 vertical multi-spherecast melee + terrain modifier 생성"입니다.
