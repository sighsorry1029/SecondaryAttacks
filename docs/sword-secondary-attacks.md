# Sword Secondary Attack Analysis

이 문서는 Valheim 기본 에셋 폴더의 sword 계열 secondary attack을 한손검과 양손검으로 나누어 정리한다.

확인 기준:

- Asset folder: `C:\Users\blizz\Documents\vunity\ValheimDefault\Assets\GameObject`
- 대상: player weapon prefab 중심
  - 한손검: `SwordWood`, `SwordBronze`, `SwordIron`, `SwordSilver`, `SwordBlackmetal`, `SwordMistwalker`, `SwordDyrnwyn`, `SwordNiedhogg*`
  - 양손검: `THSwordWood`, `THSwordKrom`, `THSwordSlayer*`
- 제외: `GoblinSword`, `skeleton_sword`, `draugr_sword`, `charred_*greatsword_*` 같은 NPC/internal attack prefab

## 결론

한손검과 양손검의 secondary attack은 둘 다 `AttackType.Horizontal` 기반의 일반 melee 공격이다. projectile도 아니고, sledge처럼 `AttackType.Area` AOE도 아니다.

둘 다 최종 피해는 기본 무기 피해에 `m_damageMultiplier = 3`을 곱하는 구조다. 즉 "특수 공격은 3배 피해의 찌르기"라고 보면 된다. 다만 판정 모양은 다르다.

| 구분 | 한손검 | 양손검 |
| --- | --- | --- |
| `m_itemType` | `3` | `14` |
| `m_skillType` | `1` / Swords | `1` / Swords |
| secondary animation | `sword_secondary` | `greatsword_secondary` |
| attack type | `0` / Horizontal | `0` / Horizontal |
| damage multiplier | `3` | `3` |
| range | `2.7` | `3.0` |
| angle | `10` | `30` |
| ray width | `0.7` | `0.5` |
| char extra width | `0` | `0` |
| lower damage per hit | `1` | `1` |
| multi hit | `1` | `1` |
| projectile | 없음 | 없음 |

## 판정 구조

두 공격 모두 `DoMeleeAttack()` 계열로 처리된다.

Horizontal melee의 기본 흐름은 다음과 같다.

```text
origin = attack origin + up * attackHeight + right * attackOffset
halfAngle = attackAngle / 2

for angle from -halfAngle to +halfAngle, roughly every 4 degrees:
    rotate attack direction horizontally
    if attackRayWidth > 0:
        SphereCast(origin, attackRayWidth, direction, attackRange - attackRayWidth)
    else:
        Raycast(origin, direction, attackRange)
```

따라서 `m_attackRayWidth`는 여기서 AOE 반경이 아니라 각 ray의 SphereCast 두께다. 실제 최대 도달감은 대략 `m_attackRange`에 가깝고, 그 선 주변을 `m_attackRayWidth`만큼 두껍게 쓸어낸다.

`m_multiHit = 1`이라서 한 번의 secondary attack이 여러 대상을 맞출 수 있다. 다만 `m_lowerDamagePerHit = 1`도 켜져 있으므로, 바닐라 melee의 다중 대상 피해 감소 로직 대상이다.

## 한손검 Secondary

대표 prefab: `SwordBlackmetal`

```text
m_attackType: 0
m_attackAnimation: sword_secondary
m_attackStamina: 28
m_attackAdrenaline: 3
m_damageMultiplier: 3
m_forceMultiplier: 1
m_staggerMultiplier: 1
m_attackRange: 2.7
m_attackHeight: 1
m_attackAngle: 10
m_attackRayWidth: 0.7
m_attackRayWidthCharExtra: 0
m_lowerDamagePerHit: 1
m_hitThroughWalls: 0
m_multiHit: 1
m_attackProjectile: none
```

한손검 secondary는 매우 좁은 전방 찌르기다. `attackAngle = 10`이므로 좌우 부채꼴은 작지만, `attackRayWidth = 0.7`이라 ray 자체는 꽤 두껍다. 그래서 정면 단일 대상용처럼 느껴지지만, 가까이 뭉친 대상은 같이 맞을 수 있다.

Primary와 비교하면 다음 차이가 크다.

| 필드 | 한손검 primary | 한손검 secondary |
| --- | ---: | ---: |
| animation | `swing_longsword` | `sword_secondary` |
| damage multiplier | `1` | `3` |
| range | `2.4` | `2.7` |
| angle | `90` | `10` |
| ray width | `0.5` | `0.7` |

즉 primary는 넓은 베기, secondary는 더 길고 더 두꺼운 좁은 찌르기다.

### 한손검 소모값

| prefab | primary stamina | secondary stamina |
| --- | ---: | ---: |
| `SwordWood` | `4` | `4` |
| `SwordBronze` | `8` | `16` |
| `SwordIron` | `10` | `20` |
| `SwordSilver` | `12` | `24` |
| `SwordBlackmetal` | `14` | `28` |
| `SwordMistwalker` | `16` | `32` |
| `SwordDyrnwyn` | `16` | `28` |
| `SwordNiedhogg` | `16` | `28` |
| `SwordNiedhoggBlood` | `16` | `28` |
| `SwordNiedhoggLightning` | `16` | `28` |
| `SwordNiedhoggNature` | `16` | `28` |

대부분의 progression 한손검은 secondary stamina가 primary의 2배지만, `SwordWood`와 일부 late-game sword는 예외다.

## 양손검 Secondary

대표 prefab: `THSwordKrom`

```text
m_attackType: 0
m_attackAnimation: greatsword_secondary
m_attackStamina: 40
m_attackAdrenaline: 1
m_damageMultiplier: 3
m_forceMultiplier: 1
m_staggerMultiplier: 1
m_attackRange: 3
m_attackHeight: 1
m_attackAngle: 30
m_attackRayWidth: 0.5
m_attackRayWidthCharExtra: 0
m_lowerDamagePerHit: 1
m_hitThroughWalls: 0
m_multiHit: 1
m_attackProjectile: none
```

양손검 secondary도 찌르기 계열이지만, 한손검보다 `attackAngle`이 넓고 `attackRange`가 길다. 대신 ray width는 한손검 secondary보다 작다.

Primary와 비교하면 다음과 같다.

| 필드 | 양손검 primary | 양손검 secondary |
| --- | ---: | ---: |
| animation | `greatsword` | `greatsword_secondary` |
| damage multiplier | `1` | `3` |
| range | `2.6` | `3.0` |
| angle | `90` | `30` |
| ray width | `0.5` | `0.5` |

즉 양손검 primary는 넓은 베기, secondary는 더 길고 3배 피해인 좁은 전방 찌르기다. 한손검 secondary와 비교하면 양손검 secondary는 angle이 `10`이 아니라 `30`이라서 더 넓은 전방 부채꼴을 훑는다.

### 양손검 소모값

| prefab | primary stamina | secondary stamina |
| --- | ---: | ---: |
| `THSwordWood` | `8` | `8` |
| `THSwordKrom` | `20` | `40` |
| `THSwordSlayer` | `20` | `40` |
| `THSwordSlayerBlood` | `20` | `40` |
| `THSwordSlayerLightning` | `20` | `40` |
| `THSwordSlayerNature` | `20` | `40` |

`THSwordWood`는 예외적으로 primary와 secondary stamina가 같다. Krom/Slayer 계열은 secondary stamina가 primary의 2배다.

## 한손검과 양손검의 실제 차이

한손검 secondary:

- 더 좁은 angle: `10`
- 더 두꺼운 ray: `0.7`
- range: `2.7`
- 정면 찌르기 집중도가 높다.

양손검 secondary:

- 더 넓은 angle: `30`
- 더 얇은 ray: `0.5`
- range: `3.0`
- 한손검보다 조금 더 긴 전방 부채꼴 찌르기다.

두 공격 모두 `m_lowerDamagePerHit = 1`이라 다중 대상에게 모두 같은 피해를 넣는 AOE로 보기는 어렵다. 여러 대상이 맞을 수는 있지만, 바닐라 melee의 다중 히트 보정이 적용되는 좁은 melee 판정이다.

## SecondaryAttacks 구현 관점

`copyFrom`으로 이 secondary들을 가져오면 다음 의미가 된다.

- `copyFrom: SwordBlackmetal`
  - `sword_secondary` 구조를 가져온다.
  - 좁은 `10`도 찌르기, range `2.7`, ray width `0.7`, damage multiplier `3`.
- `copyFrom: THSwordKrom`
  - `greatsword_secondary` 구조를 가져온다.
  - `30`도 찌르기, range `3.0`, ray width `0.5`, damage multiplier `3`.

일반 melee copy에서는 공격 구조는 `copyFrom` source secondary에서 오지만, resource cost는 이 모드의 현재 규칙상 대상 prefab의 primary attack cost에 `resourceMultiplier`를 곱해서 덮어쓴다. 따라서 sword secondary의 vanilla stamina 값을 그대로 가져오는 구조는 아니다.

`cleavingThrust` 같은 custom cone preset을 만들 때 바닐라 sword secondary와 맞추려면 다음이 기준점이다.

- 한손검 느낌: `range 2.7`, `angle 10`, `rayWidth 0.7`, `damageFactor 3`
- 양손검 느낌: `range 3.0`, `angle 30`, `rayWidth 0.5`, `damageFactor 3`

다만 바닐라 melee는 SphereCast fan이고, `cleavingThrust`는 별도 cone/interval 판정이므로 같은 수치를 넣어도 완전히 같은 체감은 아니다.
