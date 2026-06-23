# Vanilla Valheim Adrenaline Logic

기준: 로컬 설치의 `assembly_valheim.dll` 분석.  
파일: `C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\Managed\assembly_valheim.dll`  
확인 시점: 2026-06-13

## 핵심 구조

`Character.AddAdrenaline(float)`는 virtual 메서드지만 base `Character` 구현은 비어 있다. 실제 게이지 처리는 `Player.AddAdrenaline(float)`에서 한다. 따라서 vanilla 기준으로 adrenaline 게이지가 실제로 의미 있게 누적되는 대상은 `Player`다.

기본 생성자 값은 다음과 같다.

```text
Attack.m_attackAdrenaline = 1
Projectile.m_adrenaline = 2
Character.m_enemyAdrenalineMultiplier = 1
Player.m_maxAdrenaline = 100
Player.m_perfectDodgeAdrenaline = 10
Player.m_attackMissAdrenaline = -5
Player.m_nonBlockDamageAdrenaline = -5
Player.m_staggerEnemyAdrenaline = 5
Player.m_adrenalineGuardianPower = 10
Game.m_adrenalineRate = 1
```

`Game.m_adrenalineRate`는 기본값이 1이고, 월드 rate/global key 처리에서 갱신될 수 있다.

## Player.AddAdrenaline 흐름

양수 입력일 때:

```text
ratio = currentAdrenaline / maxAdrenaline
degenTimer = adrenalineDegenDelay.Evaluate(ratio)
amount *= Game.m_adrenalineRate
amount *= adrenalineGainMultiplier.Evaluate(ratio)
SEMan.ModifyAdrenaline(baseValue: amount, ref amount)
```

`SE_Stats.ModifyAdrenaline`은 다음 형태다.

```text
amount += baseValue * m_adrenalineModifier
```

즉 여러 status effect가 있으면 “이미 rate/gain curve가 적용된 baseValue”에 대해 modifier가 누적 가산된다.

음수 입력일 때:

양수 전용 보정인 `Game.m_adrenalineRate`, gain curve, status effect modifier를 거치지 않고 그대로 감소값으로 더해진다. 감소 후 0 미만이면 0으로 클램프된다.

## 최대치 도달 처리

`m_adrenaline >= GetMaxAdrenaline()`이고 max가 0보다 크면 다음 처리를 한다.

1. 장착 중인 모든 아이템을 훑는다.
2. 장착 아이템의 `m_shared.m_fullAdrenalineSE`가 있으면 해당 status effect를 추가하거나 이미 있으면 시간을 리셋한다.
3. full adrenaline SE가 하나라도 있었다면 adrenaline을 0으로 비운다.
4. full adrenaline SE가 없었다면 adrenaline을 max로 클램프한다.
5. full adrenaline SE가 발동한 경우 `m_adrenalinePopEffects`를 재생한다.

그 뒤 `Player.m_adrenalineEffects` threshold 목록을 보고 현재 adrenaline 값에 맞는 status effect level을 선택한다. 선택 상태가 바뀌면 기존 adrenalineEffects를 제거하고 새 status effect를 붙이며 HUD flash를 호출한다.

## 자연 감쇠

`Player.UpdateStats(float dt)`에서 adrenaline 감쇠가 처리된다.

```text
current = GetAdrenaline()
max = GetMaxAdrenaline()
if max > 0:
    lastMaxAdrenaline = max

adrenalineDegenTimer -= dt

if current > 0 and adrenalineDegenTimer <= 0:
    ratio = current / lastMaxAdrenaline
    drain = adrenalineDegen.Evaluate(ratio) * dt
    AddAdrenaline(-drain)
```

즉 양수 획득이 들어오면 현재 ratio 기준으로 degen delay가 다시 잡히고, 타이머가 끝난 뒤 curve 기반으로 초당 감소한다.

## 공격 사용 시 획득

`Attack.OnAttackTrigger`:

```text
if m_attackUseAdrenaline > 0:
    character.AddAdrenaline(m_attackUseAdrenaline)
```

이 값은 hit 여부와 무관하게 attack trigger 시점에 들어간다.

`Attack.FireProjectileBurst`:

`m_perBurstResourceUsage`가 켜진 projectile burst에서는 burst마다 stamina/eitr/health 사용을 처리한 뒤:

```text
if m_attackUseAdrenaline > 0:
    character.AddAdrenaline(m_attackUseAdrenaline)
```

따라서 일부 projectile/rapidfire 계열은 “사용 시점 adrenaline”과 “projectile hit adrenaline”이 별개로 존재할 수 있다.

## Melee hit

`Attack.DoMeleeAttack`에서 유효한 `Character` target을 맞추면:

```text
attacker.AddAdrenaline(m_attackAdrenaline * target.m_enemyAdrenalineMultiplier)
```

기본값 기준으로는 보통 `1 * enemyMultiplier`다.

반대로 melee 공격에서 character target을 맞추지 못했고 공격자가 `Player`이면:

```text
player.AddAdrenaline(player.m_attackMissAdrenaline)
```

기본값은 `-5`다. 즉 melee miss는 adrenaline을 깎는다.

## Area attack

`Attack.DoAreaAttack`은 여러 대상을 맞춰도 대상마다 adrenaline을 더하지 않는다. 내부 hit check에서 맞은 character들의 `m_enemyAdrenalineMultiplier` 중 가장 큰 값을 저장한 뒤, area attack 처리가 끝난 후 한 번만 더한다.

```text
maxMultiplier = max(hitCharacter.m_enemyAdrenalineMultiplier)
if maxMultiplier > 0:
    attacker.AddAdrenaline(m_attackAdrenaline * maxMultiplier)
```

즉 sledge류 area attack이 여러 적을 맞춰도 vanilla 로직은 adrenaline을 1회만 지급한다.

## Projectile hit

`Projectile.OnHit`에서는 valid hit가 character 대상이고 owner가 있으면 skill raise와 함께 adrenaline을 더한다.

```text
owner.RaiseSkill(projectile.m_skill, projectile.m_raiseSkillAmount)
owner.AddAdrenaline(projectile.m_adrenaline)
```

중요한 점:

- `Projectile.m_adrenaline` 기본 생성자 값은 2다.
- `Projectile.Setup(...)`은 `HitData.m_skill`, `HitData.m_skillRaiseAmount`를 projectile에 복사하지만, `m_adrenaline`은 복사하지 않는다.
- 따라서 projectile adrenaline은 `Attack.m_attackAdrenaline`이 아니라 projectile prefab/component의 `m_adrenaline` 값에 의존한다.
- terrain/piece/destructible hit가 아니라 character hit 조건을 탄다.

## Block / perfect block

`Humanoid.BlockAttack`에서 block이 성공하고 attacker character가 있으면 shield/item shared data의 adrenaline 값이 쓰인다.

일반 block:

```text
blocker.AddAdrenaline(shield.m_shared.m_blockAdrenaline)
```

perfect block:

```text
blocker.AddAdrenaline(shield.m_shared.m_perfectBlockAdrenaline)
```

이 값들은 shield/item prefab의 shared data에 달린다.

## Stagger enemy

`Character.AddStaggerDamage`에서 공격자가 player이면:

```text
player.AddAdrenaline(player.m_staggerEnemyAdrenaline * target.m_enemyAdrenalineMultiplier)
```

기본값은 `5 * enemyMultiplier`다. 대상 owner가 다른 peer인 경우에는 `RPC_AddAdrenaline`으로 owner 쪽 player에게 전달하는 경로가 있다.

## Player가 피해를 받을 때

`Character.RPC_Damage`에서 block 처리로 들어가지 않은 damage를 player가 받으면:

```text
player.AddAdrenaline(player.m_nonBlockDamageAdrenaline)
```

기본값은 `-5`다. 즉 block하지 못하고 맞으면 adrenaline이 감소한다.

## Perfect dodge

`Player.RPC_HitWhileDodging`에서 perfect dodge가 성립하면:

```text
player.AddStamina(GetDodgeStaminaUse() * m_perfectDodgeStaminaReturnMultiplier)
player.AddAdrenaline(m_perfectDodgeAdrenaline)
RaiseSkill(Dodge, 1)
```

기본 `m_perfectDodgeAdrenaline`은 10이다.

참고로 `Player.m_dodgeAdrenaline` 필드는 기본값 10으로 존재하지만, 이 assembly 안에서는 생성자 외 참조가 발견되지 않았다.

## Guardian power / status effect

Guardian power 활성화 시:

```text
if m_adrenalineGuardianPower != 0:
    player.AddAdrenaline(m_adrenalineGuardianPower)
```

기본값은 10이다.

`SE_Stats.StartupEffects`는 status effect 시작 시 `m_adrenalineUpFront > 0`이면 그만큼 즉시 adrenaline을 더한다.

## SecondaryAttacks 구현에 중요한 결론

vanilla 기준으로 direct melee/area와 projectile은 지급 단위가 다르다.

- melee hit: 유효 character hit마다 `Attack.m_attackAdrenaline * enemyMultiplier`
- area attack: 여러 target 중 최대 enemyMultiplier 기준으로 한 번
- projectile hit: character hit마다 `Projectile.m_adrenaline`
- attack use: `m_attackUseAdrenaline`이 있으면 hit 여부와 무관하게 사용 시점에 지급
- miss / non-block damage: 기본적으로 `-5` 감소

따라서 secondary preset에서 vanilla 느낌을 맞추려면 다음 식이 가장 자연스럽다.

- direct melee/area preset: activation당 첫 유효 hit에 한 번 지급
- projectile preset: projectile hit마다 `Projectile.m_adrenaline`을 factor로 스케일
- `m_attackUseAdrenaline`은 hit 여부와 무관한 “사용 보상”이므로, hit 기반 보상으로 바꾸려면 별도로 억제해야 한다.
