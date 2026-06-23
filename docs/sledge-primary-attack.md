# Sledge Primary Attack Analysis

이 문서는 `SledgeDemolisher` 같은 Valheim 기본 양손 둔기/sledge 계열 primary attack의 판정 구조를 정리한 내용입니다.

## 결론

Sledge primary attack은 pickaxe primary와 달리 진짜 `AttackType.Area` 기반의 구체 AOE 공격입니다.

`SledgeDemolisher`, `SledgeIron`, `SledgeStagbreaker` 모두 primary attack에서 다음 구조를 씁니다.

```text
AttackType.Area
origin = attackOrigin.position + up * attackHeight + forward * attackRange + right * attackOffset
OverlapSphereNonAlloc(origin, attackRayWidth)
```

기본 sledge 값은 `attackRange = 2`, `attackRayWidth = 4`입니다. 따라서 실제 AOE 중심은 플레이어 정면 약 2m 지점이고, 반경은 4m입니다.

중요한 점은 `attackRayWidth`가 일반 melee에서는 SphereCast 두께에 가깝지만, `AttackType.Area`에서는 그대로 `OverlapSphere`의 반경으로 사용된다는 것입니다.

## 확인한 대상

확인한 에셋 위치:

```text
C:\Users\blizz\Documents\vunity\ValheimDefault\Assets\GameObject\SledgeStagbreaker.prefab
C:\Users\blizz\Documents\vunity\ValheimDefault\Assets\GameObject\SledgeIron.prefab
C:\Users\blizz\Documents\vunity\ValheimDefault\Assets\GameObject\SledgeDemolisher.prefab
```

확인한 코드 위치:

```text
C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\Managed\publicized_assemblies\assembly_valheim_publicized.dll
Type: Attack
Method: DoAreaAttack()
```

## 공통 무기 분류

세 sledge 모두 다음 분류를 가집니다.

| Field | Value | Meaning |
| --- | ---: | --- |
| `m_itemType` | `14` | `ItemType.TwoHandedWeapon` |
| `m_animationState` | `2` | `AnimationState.TwoHandedClub` |
| `m_skillType` | `3` | `Skills.SkillType.Clubs` |

즉 코드 관점에서는 "two handed mace"라기보다 `TwoHandedWeapon + TwoHandedClub animation + Clubs skill` 조합입니다.

## 기본 수치

| Prefab | Blunt | Pierce | Damage per level | Attack force | Stamina | Tool tier | Stagger multiplier |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `SledgeStagbreaker` | 20 | 5 | blunt +6 | 150 | 12 | 0 | 1 |
| `SledgeIron` | 55 | 0 | blunt +6 | 200 | 20 | 2 | 2 |
| `SledgeDemolisher` | 145 | 0 | blunt +6 | 210 | 28 | 5 | 2 |

공통 primary attack 판정 값:

| Field | Value | Meaning |
| --- | ---: | --- |
| `m_attackType` | `4` | `AttackType.Area` |
| `m_attackAnimation` | `swing_sledge` | sledge swing animation |
| `m_hitTerrain` | `1` | terrain/piece layer까지 OverlapSphere 대상에 포함 |
| `m_attackRange` | `2` | AOE 중심을 정면으로 밀어내는 거리 |
| `m_attackHeight` | `0` | AOE 중심의 y offset |
| `m_attackOffset` | `0` | AOE 중심의 right offset |
| `m_attackAngle` | `90` | Area attack에서는 실질 판정에 사용되지 않음 |
| `m_attackRayWidth` | `4` | AOE 구체 반경 |
| `m_attackRayWidthCharExtra` | `0` | 추가 character sphere 없음 |
| `m_lowerDamagePerHit` | `0` | 다중 대상 피해 감소 없음 |
| `m_multiHit` | `1` | 여러 대상 처리 가능 |
| `m_skillHitType` | `4` | `DestructibleType.Character` |
| `m_raiseSkillAmount` | `1` | skill raise amount |

## DoAreaAttack 흐름

코드 흐름을 간단히 쓰면 다음과 같습니다.

```text
origin = attackOrigin.position
       + Vector3.up * attackHeight
       + character.forward * attackRange
       + character.right * attackOffset

triggerEffect.Create(origin)

mask = hitTerrain ? attackMaskTerrain : attackMask
OverlapSphereNonAlloc(origin, attackRayWidth, mask)

for each collider:
    ignore own character
    target = Projectile.FindHitObject(collider)
    ignore duplicate target GameObjects
    hitPoint = collider.ClosestPoint(origin)
    destructible = target.GetComponent<IDestructible>()
    if destructible exists:
        build HitData from weapon damage
        apply skill factor, damage multiplier, stagger multiplier, push
        apply friendly / pvp / tamed / dodge checks for Character targets
        destructible.Damage(hitData)
        if target destructible type matches skillHitType:
            raiseSkill = true

if any hit:
    play hit effect at average hit point
    reduce durability once
    add hit noise
    raise Clubs skill once if raiseSkill
```

## 판정 형태

Demolisher 기준으로 그리면 대략 이렇습니다.

```text
top view

          radius 4m
      .---------------.
    .'                 '.
   /                     \
  |          X            |   X = AOE center
   \                     /
    '.                 .'
      '---------------'

player P ---- 2m ----> X
```

AOE 중심은 플레이어 정면 2m이고, 반경은 4m입니다.

그래서 실제로는 플레이어 앞쪽으로 크게 뻗지만, 반경이 4m라서 플레이어 뒤쪽 일부도 포함될 수 있습니다. 단, 중심이 정면으로 밀려 있기 때문에 완전히 플레이어 중심 원형 AOE는 아닙니다.

## 다중 대상 피해 감소

Sledge primary는 다중 대상 피해 감소가 없습니다.

일반 melee `DoMeleeAttack()`에는 다음과 같은 로직이 있습니다.

```text
if multiHit && lowerDamagePerHit && hitCount > 1:
    skillFactor /= hitCount * 0.75
```

하지만 `DoAreaAttack()`에는 이 보정이 없습니다. Sledge primary의 `m_lowerDamagePerHit`도 기본적으로 `0`입니다.

따라서 여러 적이 같은 sledge AOE에 맞아도 각 대상은 같은 attack skill factor를 기준으로 피해를 받습니다. 스킬 난수는 공격 1회당 한 번 뽑히고, 그 값이 모든 대상에 공유됩니다.

## Skill raise

Sledge primary는 `m_skillType = Clubs`이고 `m_skillHitType = Character`입니다.

`DoAreaAttack()`은 대상마다 바로 skill을 올리는 방식이 아니라, 캐릭터 타입 대상이 하나라도 피해 처리되면 `raiseSkill = true`로 표시한 뒤 공격 처리 마지막에 한 번만 스킬을 올립니다.

```text
if any hit target has DestructibleType.Character:
    RaiseSkill(Clubs, raiseSkillAmount)
```

즉 한 번의 sledge swing이 여러 캐릭터를 맞혀도 Clubs 숙련도 상승은 기본적으로 한 번입니다.

## 내구도

`DoAreaAttack()`은 hit가 하나 이상 있으면 내구도를 한 번 감소시킵니다.

코드상으로는 area attack에서 다음처럼 처리합니다.

```text
if nrOfHits > 0 and weapon uses durability and attacker is player:
    weapon.durability -= 1
```

Sledge prefab의 `m_useDurabilityDrain`도 1이므로 실제로는 공격 1회 적중당 내구도 1 감소로 보면 됩니다.

여러 대상을 맞혀도 target 수만큼 내구도가 추가로 줄어들지는 않습니다.

## Friendly/PVP/Dodge/Tamed 필터

`DoAreaAttack()`도 캐릭터 대상에 대해 바닐라의 기본 필터를 적용합니다.

- 자기 자신은 제외
- 같은 target GameObject는 한 번만 처리
- player PVP 설정과 enemy 판정 확인
- `m_tamedOnly`이면 tamed 대상만 허용
- dodge invincible이면 피해 스킵
- `BaseAI.IsEnemy()` 또는 aggravatable 조건으로 적대 판정

따라서 sledge AOE는 반경 안의 모든 collider를 일단 찾지만, 캐릭터 데미지는 위 필터를 통과한 대상에게만 들어갑니다.

## Terrain과 props

Sledge primary는 `m_hitTerrain = 1`이어서 terrain/piece 계열 layer까지 overlap 검색 대상에 들어갑니다.

다만 pickaxe처럼 `digg_v3` terrain modifier를 생성해서 지형을 파는 구조는 아닙니다. `SledgeDemolisher`, `SledgeIron`, `SledgeStagbreaker` 모두 shared `m_spawnOnHitTerrain`이 비어 있습니다.

`IDestructible`이 있는 props/pieces는 AOE damage를 받을 수 있고, terrain collider 자체는 damage target이 아니라 hit/effect/noise 계산에만 영향을 줄 수 있습니다.

## sledge_aoe prefab과의 관계

에셋에는 `sledge_aoe.prefab`도 존재하지만, 확인한 `SledgeDemolisher`, `SledgeIron`, `SledgeStagbreaker` primary attack은 `m_spawnOnTrigger`나 `m_attackProjectile`로 이 prefab을 직접 사용하지 않습니다.

기본 sledge primary의 실제 데미지 판정은 `Attack.DoAreaAttack()`의 `OverlapSphereNonAlloc`입니다.

## SecondaryAttacks 구현 관점

Sledge primary를 복제하거나 비슷한 preset을 만들 때는 다음처럼 보는 편이 좋습니다.

- `m_attackType = Area`가 핵심입니다.
- `m_attackRayWidth`는 AOE 반경입니다.
- `m_attackRange`는 실제 사거리라기보다 AOE 중심을 정면으로 밀어내는 offset입니다.
- 기본 반경 4m는 꽤 넓고, 중심이 정면 2m라 앞쪽 총 도달감은 약 6m에 가깝습니다.
- 다중 대상 피해 감소는 없습니다.
- Clubs 스킬 상승은 여러 대상 수만큼이 아니라 공격 1회당 최대 한 번입니다.
- 내구도도 여러 대상 수만큼 줄지 않고, 적중한 공격 1회당 한 번 줄어듭니다.

정리하면, sledge primary는 "정면 2m 지점 중심의 반경 4m 구체 AOE"이며, 일반 melee fan/spherecast가 아니라 명확한 `OverlapSphere` 기반 area attack입니다.
