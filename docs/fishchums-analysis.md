# FishChums.dll Analysis

이 문서는 `FishChums.dll`을 ILSpy로 디컴파일해서 핵심 기능, 등록 아이템, Harmony 패치 지점, 호환상 주의점을 정리한 내용이다.

## 확인 대상

```text
C:\Users\blizz\AppData\Roaming\com.kesomannen.gale\valheim\profiles\secondary\BepInEx\plugins\RustyMods-FishChum\FishChums.dll
```

디컴파일 임시 출력 위치:

```text
C:\Users\blizz\AppData\Local\Temp\FishChums_decompiled
```

## 기본 정보

| 항목 | 값 |
|---|---|
| BepInEx GUID | `RustyMods.FishChum` |
| Mod name | `FishChum` |
| Version | `1.0.4` |
| Author | `RustyMods` |
| Embedded asset bundle | `fishingbaitbundle` |
| 설정 파일 | `RustyMods.FishChum.cfg` |

설정은 사실상 `Lock Configuration` 하나만 보인다. 기본값은 `On`이고 ServerSync로 서버 관리자만 설정을 바꿀 수 있게 잠그는 용도다.

## 한 줄 요약

FishChums는 낚시 자체의 `FishingFloat`, bite 시간, 낚싯대 로직을 직접 바꾸는 모드라기보다, chum 아이템과 chum spawner를 추가해서 특정 biome에서 물고기 아이템, serpent, leviathan을 유도 생성하는 모드다.

## 주요 기능

- 여러 종류의 `FishChum*` 아이템과 `SerpentChum`, `LeviathanChum`을 추가한다.
- 일반 fish chum은 biome이 맞으면 `LootSpawner`를 통해 물고기 아이템을 드롭한다.
- `SerpentChum`과 `LeviathanChum`은 Ocean biome에서만 `SpawnArea`로 실제 생물 prefab을 소환한다.
- 생선 기반 음식과 조리 전/후 아이템을 추가한다.
- 생선을 재료로 음식을 만들 때, 사용한 생선 quality가 높으면 결과 음식을 추가 지급하는 패치를 둔다.
- DLL 버전과 SHA256 해시를 비교하는 별도 version check를 수행한다.

## Chum 동작 방식

일반 fish chum은 실제 물고기 AI를 끌어오는 방식이 아니다. Chum spawner prefab에 `LootSpawner` 드롭 테이블을 붙이고, 해당 spawner가 있는 biome이 맞을 때만 물고기 item prefab을 드롭하게 한다.

| Chum spawner | 허용 biome | 드롭 물고기 |
|---|---:|---|
| `FishChumMeadowsSpawner` | Meadows | `Fish1`, `Fish2` |
| `FishChumBlackforestSpawner` | BlackForest | `Fish1`, `Fish2`, `Fish5` |
| `FishChumSwampsSpawner` | Swamp | `Fish2`, `Fish6` |
| `FishChumMountainsSpawner` | Mountain | `Fish4_cave` |
| `FishChumPlainsSpawner` | Plains | `Fish7`, `Fish8` |
| `FishChumOceanSpawner` | Ocean | `Fish3`, `Fish8`, `Fish12` |
| `FishChumMistlandsSpawner` | Mistlands | `Fish9`, `Fish12` |
| `FishChumAshlandsSpawner` | Ashlands | `Fish11`, `Fish12` |
| `FishChumDeepnorthSpawner` | DeepNorth | `Fish10`, `Fish12` |

각 드롭은 stack min/max 1, weight 1이고, spawner의 `m_respawnTimeMinuts`는 `1f`로 설정된다.

Biome이 맞지 않으면 `LootSpawner.UpdateSpawner` Prefix가 false를 반환해서 spawner update 자체를 막고, 로컬 플레이어에게 다음 형태의 메시지를 띄운다.

```text
<chum shared name> is not attracting any fish in this area
```

## Serpent / Leviathan Chum

`SerpentChumSpawner`와 `leviathanChumSpawner`는 `SpawnArea` 기반이다. 둘 다 Ocean biome에서만 작동한다.

| Spawner | 소환 prefab | level | interval | maxNear / maxTotal |
|---|---|---:|---:|---:|
| `SerpentChumSpawner` | `Serpent` | 1-2 | 10s | 1 / 1 |
| `leviathanChumSpawner` | `Leviathan` | 1 | 40s | 1 / 1 |

공통 SpawnArea 설정:

| 필드 | 값 |
|---|---:|
| `m_levelupChance` | `0f` |
| `m_triggerDistance` | `50f` |
| `m_setPatrolSpawnPoint` | `true` |
| `m_spawnRadius` | `25f` |
| `m_nearRadius` | `20f` |
| `m_farRadius` | `1000f` |
| `m_onGroundOnly` | `false` |

Leviathan이 들어간 SpawnArea는 `SpawnArea.SpawnOne` Prefix에서 별도로 처리된다. weighted prefab을 고르고 `FindSpawnPoint`가 성공하면 `Object.Instantiate`로 직접 생성한 뒤 vanilla `SpawnOne`은 실행하지 않는다.

## 추가 Chum 아이템 / 레시피

| Item prefab | 표시명 | 제작대 | 재료 | 제작량 |
|---|---|---|---|---:|
| `LeviathanChum` | Leviathan Chum | Forge 4 | `FishingBait` 100, `SerpentScale` 5, `HardAntler` 10, `Copper` 5 | 1 |
| `FishChumMeadows` | Common Chum | Cauldron 0 | `FishingBait` 5, `Dandelion` 10, `LeatherScraps` 2 | 10 |
| `FishChumBlackforest` | Curious Chum | Cauldron 0 | `FishingBait` 5, `Thistle` 3, `TrollHide` 1 | 10 |
| `FishChumSwamps` | Bloody Chum | Cauldron 1 | `FishingBait` 5, `Bloodbag` 3, `Guck` 1 | 10 |
| `FishChumMountains` | Frozen Chum | Cauldron 2 | `FishingBait` 5, `FreezeGland` 3, `WolfPelt` 2 | 10 |
| `FishChumPlains` | Dry Chum | Cauldron 2 | `FishingBait` 5, `Barley` 5, `Flax` 5 | 10 |
| `FishChumMistlands` | Mysterious Chum | Cauldron 3 | `FishingBait` 5, `ChickenEgg` 1, `HareMeat` 1, `LinenThread` 2 | 10 |
| `FishChumAshlands` | Burnt Chum | Cauldron 3 | `FishingBait` 5, `SurtlingCore` 5, `Coal` 5, `LinenThread` 2 | 10 |
| `FishChumDeepnorth` | Frosted Chum | Cauldron 3 | `FishingBait` 5, `BugMeat` 2, `MushroomJotunPuffs` 1, `LinenThread` 2 | 10 |
| `FishChumOcean` | Deepsea Chum | Cauldron 1 | `FishingBait` 5, `Ooze` 3, `LeatherScraps` 2, `Dandelion` 5 | 10 |
| `SerpentChum` | Serpent chum | Cauldron 3 | `WolfMeat` 1, `Bloodbag` 2, `LeatherScraps` 3, `Thistle` 2 | 1 |

위 chum 아이템들은 recipe configurable로 등록된다.

## 추가 음식 / 생선 가공

| Item prefab | 표시명 | 제작대 | 재료 | 비고 |
|---|---|---|---|---|
| `PerchStew` | Perch Stew | Cauldron 1 | `Fish1`, `Carrot`, `Turnip` | `QualityResultAmountMultiplier = 2f` |
| `TrollfishMeat` | Trollfish Meat | Cauldron 0 | `Fish5` | 조리 전 재료 |
| `TrollfishMeatCooked` | Cooked Trollfish | Cauldron 0 | `SwordCheat` | stats configurable |
| `PikeSoup` | Pike Soup | Cauldron 0 | `Fish2`, `Thistle` 2, `Turnip` 3 |  |
| `FishBallUncooked` | Uncooked fishballs | Cauldron 2 | `Fish7`, `BarleyFlour` 5, `Dandelion` 3 | oven 조리 전 |
| `FishBallCooked` | Fishballs | Cauldron 4 | `SwordCheat` | stats configurable |
| `TunaSalad` | Tuna salad | Cauldron 2 | `Fish3`, `Thistle` 3, `Onion`, `Dandelion` 3 |  |
| `TetraStew` | Tetra stew | Cauldron 2 | `Fish4_cave`, `FreezeGland`, `Onion`, `Turnip` 3 |  |
| `SteamedHerring` | Steamed herring | Cauldron 2 | `Fish6`, `Root` |  |
| `CoralDelightUncooked` | Uncooked coral delight | Cauldron 2 | `Fish8`, `ChickenEgg`, `BarleyFlour` 10 | oven 조리 전 |
| `CoralDelightCooked` | Coral Delight | Cauldron 4 | `SwordCheat` | stats configurable |
| `BakedSalmonUncooked` | Uncooked salmon | Cauldron 2 | `Fish10`, `MushroomMagecap` 5, `Thistle` 10 | oven 조리 전 |
| `BakedSalmonCooked` | Baked Salmon | Cauldron 4 | `SwordCheat` | stats configurable |
| `BakedMagmafishUncooked` | Uncooked Magmafish | Cauldron 2 | `Fish11`, `Dandelion` 5, `Thistle` 5, `RoyalJelly` 10 | oven 조리 전 |
| `BakedMagmafishCooked` | Baked Magmafish | Cauldron 4 | `SwordCheat` | stats configurable |

## CookingStation 변환

`ZNetScene.Awake` 이후 cooking station 변환을 직접 추가한다.

| From | To | 시간 | Station |
|---|---|---:|---|
| `TrollfishMeat` | `TrollfishMeatCooked` | 25s | `piece_cookingstation`, `piece_cookingstation_iron` |
| `FishBallUncooked` | `FishBallCooked` | 10s | `piece_oven` |
| `CoralDelightUncooked` | `CoralDelightCooked` | 10s | `piece_oven` |
| `BakedSalmonUncooked` | `BakedSalmonCooked` | 10s | `piece_oven` |
| `BakedMagmaFishUncooked` | `BakedMagmaFishCooked` | 10s | `piece_oven` |

주의: 등록된 item prefab 이름은 `BakedMagmafishUncooked` / `BakedMagmafishCooked`인데 cooking conversion은 `BakedMagmaFishUncooked` / `BakedMagmaFishCooked`를 찾는다. `Fish`의 `F` 대소문자가 다르므로 prefab lookup이 exact match라면 Magmafish oven 변환이 실패할 가능성이 높다.

## Harmony 패치 지점

| Patch class | 대상 | Prefix/Postfix | 기능 |
|---|---|---|---|
| `LootSpawnerUpdateSpawnerPatch` | `LootSpawner.UpdateSpawner` | Prefix | 일반 fish chum spawner의 biome 검증. 맞지 않으면 메시지 출력 후 update 차단 |
| `SpawnAreaUpdateSpawnPatch` | `SpawnArea.UpdateSpawn` | Prefix | serpent/leviathan chum이 Ocean에서만 작동하게 제한 |
| `OvenPatch` | `ZNetScene.Awake` | Postfix | cooking station 변환 추가 |
| `LootSpawnerPatch` | `ZNetScene.Awake` | Postfix | fish chum spawner별 드롭 테이블 추가 |
| `RegisterPrefabs` | `ZNetScene.Awake` | Postfix | `sfx_fishchum_throw`, `sfx_fishchum_explode`를 ZNetScene prefab으로 등록 |
| `SpawnAreaPatch` | `ZNetScene.Awake` | Postfix | serpent/leviathan SpawnArea 데이터 추가 |
| `SpawnAreaSpawnOnePatch` | `SpawnArea.SpawnOne` | Prefix | Leviathan spawner는 직접 Instantiate로 소환 |
| `DoCraftingPatch` | `InventoryGui.DoCrafting` | Postfix | fish quality 기반 음식 추가 지급 |
| `RegisterAndCheckVersion` | `ZNet.OnNewConnection` | Prefix | DLL version/hash RPC 등록 및 호출 |
| `VerifyClient` | `ZNet.RPC_PeerInfo` | Prefix/Postfix | 서버에서 검증 안 된 peer 차단, admin sync 요청 |
| `ShowConnectionError` | `FejdStartup.ShowConnectError` | Postfix | 접속 실패 UI에 FishChum version/hash 오류 추가 |
| `RemoveDisconnectedPeerFromVerified` | `ZNet.Disconnect` | Prefix | 검증된 peer 목록에서 제거 |

## 생선 quality 보너스 제작

`InventoryGui.DoCrafting` Postfix는 다음 조건일 때 작동한다.

1. 제작 결과 item type이 food다.
2. recipe resource 중 item type이 fish인 재료가 있다.
3. 플레이어 인벤토리에서 같은 shared name의 fish를 찾는다.
4. fish quality가 2 이상이면 제작 결과 item을 추가로 지급한다.

추가 지급량은 `item.m_quality` 그대로다. vanilla crafting이 이미 기본 결과물을 지급한 뒤 Postfix가 실행되므로, quality 2 생선을 사용하면 총 결과물이 3개가 될 수 있다. 의도한 총량이 quality 배수라면 off-by-one 성격의 과지급 가능성이 있다.

## ServerSync / Version check

일반 ServerSync config lock 외에 자체 version check가 있다.

- 접속 시 `FishChum_VersionCheck` RPC를 등록하고 호출한다.
- payload는 mod version `1.0.4`와 현재 DLL의 SHA256 hash다.
- 서버는 version 또는 hash가 다르면 peer를 disconnect한다.
- 클라이언트는 접속 실패 UI에 mismatch 정보를 추가로 표시한다.

따라서 같은 FishChums version이라도 DLL 파일 내용이 다르면 서버 접속이 거부될 수 있다.

## SecondaryAttacks와의 호환 관점

- FishChums는 낚싯대의 cast, bite, reel, bait consumption 자체를 직접 바꾸지 않는다.
- 일반 chum은 fish item prefab을 월드에 드롭하는 방식이므로, SecondaryAttacks의 fishing rod bag이 fish item type을 허용한다면 드롭된 fish item과는 큰 충돌 가능성이 낮다.
- Chum 아이템 자체는 fish/bait item type이 아닐 가능성이 높다. rod bag에 chum까지 넣고 싶다면 별도 허용 규칙이 필요할 수 있다.
- FishChums가 추가하는 음식/조리 아이템은 대부분 food/material 흐름이라 multi-line fishing과 직접 충돌하지 않는다.
- 자체 version/hash check가 있으므로 FishChums DLL을 패치하거나 리패킹하면 서버와 클라이언트가 같은 파일을 써야 한다.

## 눈에 띄는 주의점

- `BakedMagmafish*` item 등록명과 cooking conversion lookup명이 대소문자 불일치다.
- `leviathanChumSpawner`가 Ocean이 아닐 때도 메시지는 `"Serpent Chum doesn't work in the shallow waters"`로 나온다.
- `DoCraftingPatch`의 fish quality 보너스는 기본 제작 결과에 `quality`개를 추가로 더하는 구조라, 의도보다 하나 더 많이 나올 가능성이 있다.
- `ShowConnectionError`에서 `fontSizeMax`를 `25f`로 설정한 직후 `15f`로 다시 설정한다. 최종값은 `15f`다.
- 일반 fish chum은 물고기 생물을 더 많이 무는 기능이 아니라 물고기 아이템 드롭 장치에 가깝다.
