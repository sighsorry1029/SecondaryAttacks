# Melee `copyFrom` 정리

이 문서는 `SecondaryAttacks.Melee.yml`의 `copyFrom`이 실제로 무엇을 가져오고, 무엇을 가져오지 않는지 코드 기준으로 정리한다.

## 핵심 결론

`copyFrom`은 소스 무기의 스탯 전체를 상속하는 기능이 아니다.

일반 melee copy는 `copyFrom`에 적은 prefab의 **secondary `Attack` 객체**를 찾아서 복제한 뒤, 현재 무기의 secondary attack으로 넣는다. 따라서 공격 방식, 투사체, 판정 형태 같은 `Attack` 설정은 소스 secondary에서 온다.

다만 최종 피해의 기본값과 최종 resource cost는 현재 대상 prefab 쪽을 따른다. 피해는 현재 들고 있는 무기의 `ItemData`/`SharedData`에서 나오고, stamina/eitr/health 비용은 현재 대상 prefab의 primary attack cost에 `resourceMultiplier`를 곱한 값으로 덮어쓴다.

예를 들어:

```yml
MaceEldner:
  copyFrom: SpearFlint
  animation: spear_throw
```

이 경우 `SpearFlint`의 약한 기본 데미지를 가져오는 것이 아니다. `SpearFlint` secondary의 투척 공격 구조를 가져와서 `MaceEldner`를 던지는 secondary로 만든다. 기본 피해 타입/수치는 현재 무기인 `MaceEldner` 쪽을 따르고, resource cost도 `MaceEldner` primary attack cost에 `resourceMultiplier`를 곱한 값으로 적용된다.

## 처리 흐름

1. YAML을 읽어 `copyFrom` 값을 정규화한다.
2. `copyFrom`이 비어 있으면 현재 prefab 이름을 소스로 사용한다.
3. 일반 copy melee는 `ObjectDB`에서 소스 prefab을 찾는다.
4. 소스 prefab의 `m_itemData.m_shared.m_secondaryAttack`을 가져온다.
5. 해당 `Attack`을 `MemberwiseClone`으로 복제한다.
6. YAML의 `animation`, `resourceMultiplier`, `outputMultiplier` 등 현재 엔트리의 값으로 일부 attack 필드를 덮어쓴다.
7. 복제된 공격을 현재 무기의 `m_shared.m_secondaryAttack`에 넣는다.
8. preset 블록이 있으면 복제된 `Attack` 자체가 아니라 runtime `SecondaryAttackDefinition`에 별도 동작으로 붙는다.

`MemberwiseClone`은 얕은 복사다. 따라서 `m_attackProjectile`, `EffectList`, curve 같은 참조형 필드는 소스 attack의 참조를 그대로 들고 온다. 이 모드는 그 참조들을 직접 수정하기보다는, 런타임 projectile setup이나 hit 시점에 현재 무기에 맞는 visual/hit effect를 보정하는 방식을 쓴다.

## 가져오는 것

일반 `copyFrom`은 소스 prefab의 **secondary Attack 설정**을 기본으로 가져온다. 대표적으로 다음 값들이 포함된다.

- 공격 타입: `m_attackType`
- 원래 animation 이름: `m_attackAnimation`
- charge/loop/reload/bow draw 관련 attack 플래그
- 소모 방식의 원본 값: stamina, eitr, health, draw/reload drain
- 이동/회전 계수: `m_speedFactor`, `m_speedFactorRotation`
- 공격 범위/형태: `m_attackRange`, `m_attackHeight`, `m_attackAngle`, `m_attackRayWidth`, `m_attackRayWidthCharExtra`
- 지형 타격 여부, friendly hit 여부, through wall 여부
- multi hit, lower damage per hit, chain 관련 값
- attack damage multiplier, force multiplier, stagger multiplier
- self damage, recoil, noise, adrenaline 관련 값
- projectile 공격인 경우:
  - `m_attackProjectile`
  - projectile velocity
  - projectile accuracy
  - projectile count/burst
  - launch angle, circular/distributed projectile 설정
- attack-local effect list:
  - `m_hitEffect`
  - `m_hitTerrainEffect`
  - `m_startEffect`
  - `m_triggerEffect`
  - `m_trailStartEffect`
  - `m_burstEffect`
- harvest 관련 attack 설정
- `m_consumeItem` 같은 공격 사용 시 아이템 소비 플래그

즉, “어떻게 공격하는가”는 상당 부분 소스 secondary에서 온다.

주의: 위 소모값 필드도 `Attack` 복제 자체에는 포함되지만, 일반 melee copy에서는 최종 빌드 단계에서 현재 대상 prefab의 primary attack cost로 다시 덮어쓴다. 그래서 실제 사용 비용은 copyFrom 소스 secondary cost가 아니라 대상 무기 primary cost 기준이다. 소스 secondary의 draw/reload 플래그나 projectile/reload 구조는 복제될 수 있지만, raw cost 숫자는 대상 primary 기준으로 다시 계산된다.

## 현재 엔트리/YAML이 덮어쓰는 것

복제 후 현재 YAML 엔트리에서 다음 값들이 다시 적용된다.

- `animation`
  - 있으면 `m_attackAnimation`을 이 값으로 덮어쓴다.
  - custom animation이 있으면 attack chain/random animation도 단순화한다.
- `resourceMultiplier`
  - 일반 melee copy에서는 현재 대상 prefab의 primary attack stamina/eitr/health/draw/reload 비용에 곱해서 최종 raw cost로 넣는다.
  - 예: `MaceEldner` primary stamina가 16이고 `resourceMultiplier: 1.5`면 copied secondary stamina는 24.
  - 즉 `copyFrom: SpearFlint`라 해도 `SpearFlint` secondary stamina가 최종 비용 기준이 되지는 않는다.
- `outputMultiplier`
  - 복제된 attack의 `m_damageMultiplier`, `m_forceMultiplier`, `m_staggerMultiplier`에 곱한다.
  - 기본 피해 자체를 바꾸는 것이 아니라, 현재 무기 피해에 적용되는 attack multiplier를 바꾸는 쪽에 가깝다.
- melee preset 블록
  - `sneakAmbush`
  - `cleavingThrust`
  - `spearRain` / `onProjectileHit`
  - `impactBurst`
  - `boomerang`
  - `launchSlam`
  - `knockbackChain`
  - `aftershock`
  - `riftTrail`
  - `fractureLine`
  - 이 블록들은 `Attack` 필드를 직접 덮어쓰기보다는 `SecondaryAttackDefinition`에 runtime 보정/후속 효과로 붙는다.
- `equip`
  - YAML의 장착 효과 설정은 현재 prefab 정의에 붙는다.

## 가져오지 않는 것

`copyFrom`은 소스 무기의 `SharedData` 전체를 가져오지 않는다. 특히 다음은 소스 무기에서 오지 않는 것으로 보는 것이 안전하다.

- 소스 무기의 기본 데미지 수치
- 소스 무기의 데미지 타입 구성
- 소스 무기의 skill type
- 소스 무기의 item type
- 소스 무기의 durability/max durability
- 소스 무기의 icon, name, description, crafting data
- 소스 무기의 equip status effect
- 소스 무기의 block/parry/deflection 같은 방패 스탯
- 소스 무기의 item-level metadata
- 소스 무기의 YAML 엔트리 전체

최종 hit data를 만들 때는 현재 공격을 사용하는 무기, 즉 대상 prefab의 `m_weapon.GetDamage()`와 `m_weapon.m_shared`가 주로 사용된다. 그래서 약한 창의 secondary를 복사해서 강한 메이스를 던지면, 공격 구조는 창에서 오지만 기본 피해는 메이스 쪽을 따른다.

일반 melee copy의 최종 resource cost도 대상 prefab의 primary attack cost 기준이다. 따라서 약한 창의 secondary가 싸다고 해서 그 비용까지 가져오지는 않는다.

## 투척 copy의 시각/히트 효과

소스 secondary가 spear throw 같은 projectile attack이면 `m_attackProjectile` 자체는 소스 attack에서 온다.

다만 이 모드는 copied throw projectile에 대해 별도 보정을 시도한다.

- 날아가는 투사체 visual은 현재 무기 prefab visual로 교체하려고 한다.
  - projectile이 visual 교체를 지원하면 synced visual 이름을 현재 무기 prefab으로 넣는다.
  - 그렇지 않으면 `ItemStand` attach prefab을 projectile 아래에 붙이는 fallback을 쓴다.
  - attach prefab이 없거나 projectile setup hook을 타지 못하면 완전한 visual 교체가 안 될 수 있다.
- projectile hit effects도 현재 무기의 `m_shared.m_hitEffect`로 교체하려고 한다.
  - 현재 무기에 hit effect가 없으면 교체할 effect가 없으므로 source projectile의 기존 effect가 남을 수 있다.
- `spearRain` 등 추가 생성 projectile은 실제 아이템을 복제하지 않도록 pickup/drop 쪽을 억제한다.
- 일반 copied throw의 실제 아이템 흐름은 소스 attack의 `m_consumeItem` 같은 플래그와 source projectile의 respawn/drop 설정을 따른다. 이때 vanilla projectile setup에 전달되는 item은 현재 무기이므로, 소비/드롭되는 대상은 현재 무기 쪽이다.

즉:

```yml
BattleaxeCrystal:
  copyFrom: SpearFlint
  animation: spear_throw
```

이런 식이면 `SpearFlint`의 투척 구조와 projectile 설정을 빌리지만, 던져지는 표현과 hit effect는 가능한 한 `BattleaxeCrystal` 쪽을 따르도록 보정한다.

## 특수 preset에서의 `copyFrom`

일반 copy와 일부 특수 preset은 소스 attack을 고르는 방식이 조금 다르다.

### 일반 copy

- 소스 prefab의 valid secondary attack만 사용한다.
- 소스 secondary에 animation이 없으면 invalid로 보고 스킵한다.
- 공격 구조는 소스 secondary에서 오지만, resource cost는 대상 prefab primary attack cost에 `resourceMultiplier`를 곱해서 덮어쓴다.

### `aftershock`

- 소스 prefab의 Area primary를 먼저 찾는다.
- valid Area primary가 없으면 Area secondary를 찾는다.
- Area attack이고, animation이 있고, `m_attackRayWidth > 0`이어야 한다.
- Warfare처럼 sledge 원본 공격이 secondary로 옮겨진 경우도 잡기 위해 primary 다음 secondary 순서로 본다.
- 공격 구조는 실제로 고른 Area source attack에서 오지만, resource cost는 대상 prefab primary attack cost에 `resourceMultiplier`를 곱해서 덮어쓴다.

### `fractureLine`

- valid melee secondary를 먼저 찾는다.
- 없으면 valid melee primary를 찾는다.
- attack type은 Horizontal 또는 Vertical이어야 하고 animation이 있어야 한다.
- pickaxe처럼 secondary가 약한 경우 primary를 secondary용 소스로 쓸 수 있게 하기 위한 예외에 가깝다.
- 공격 구조는 실제로 고른 melee source attack에서 오지만, resource cost는 대상 prefab primary attack cost에 `resourceMultiplier`를 곱해서 덮어쓴다.

## 주의할 점

`copyFrom`은 YAML 상속 기능이 아니다. 다음처럼 생각하면 덜 헷갈린다.

- `copyFrom`: 어떤 prefab의 실제 attack 객체를 베이스로 삼을지 정한다.
- 현재 YAML 엔트리: 그 복제본 위에 animation/resource/output 보정을 얹고, preset 블록은 runtime definition으로 붙인다.
- 현재 무기: 최종 피해, skill type, durability, 장착 상태, 실제 소비/드롭 아이템 정체성을 제공한다.
- 일반 melee copy의 resource cost: 현재 대상 prefab의 primary attack cost가 기준이다.

따라서 “약한 무기의 데미지를 복사하고 싶다”는 목적에는 `copyFrom`만으로는 맞지 않는다. 그런 경우에는 `outputMultiplier`, preset별 `damageFactor`, 또는 별도의 효과/피해 보정 필드를 써야 한다.
