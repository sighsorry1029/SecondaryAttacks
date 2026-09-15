using SecondaryAttacks;
using UnityEngine;
using Object = UnityEngine.Object;

const string checkRpc = "SecondaryAttacks_CheckSubSummon";
const string releaseRpc = "SecondaryAttacks_ReleaseSubSummon";
int parentKey = "SecondaryAttacks_SubSummonParent".GetStableHashCode();
int childKey = "SecondaryAttacks_SubSummonChild".GetStableHashCode();
int slot0 = "SecondaryAttacks_SubSummonSlot0".GetStableHashCode();
int slot1 = "SecondaryAttacks_SubSummonSlot1".GetStableHashCode();
int checks = 0;
long next = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; }
ZDO Data(GameObject obj) => obj.GetComponent<ZNetView>()!.Zdo!;
GameObject Make(bool tamed = true, bool player = false)
{
    var obj = new GameObject();
    obj.Add<Character>(player ? new Player() : new Character()).Tamed = tamed;
    ZDO zdo = new() { m_uid = new(++next), Owner = ZNet.Local };
    obj.Add(new ZNetView { Zdo = zdo });
    ZDOMan.instance.Objects.Add(zdo.m_uid, zdo);
    return obj;
}
GameObject? Spawn(GameObject prefab, GameObject owner, bool blood = true) => SubSummonSystem.Instantiate(prefab, default, default, new SpawnAbility(owner.GetComponent<Character>()!, blood));
void Reset()
{
    ZDOMan.instance = new();
    ZNet.Local = 1;
    ZNet.instance.Server = true;
    ZRoutedRpc.instance = new();
    SubSummonSystem.RegisterRpc(ZRoutedRpc.instance);
    Object.Created = 0;
    Object.Create = p => Make(p.GetComponent<Character>()!.Tamed);
}

Reset();
GameObject player = Make(player: true);
GameObject friendlyPrefab = Make();
GameObject hostilePrefab = Make(false);
GameObject parent = Spawn(friendlyPrefab, player)!;
Check(Data(parent).GetString(parentKey, "").Length == 32, "Blood Magic direct summon is marked");
Check(Data(Spawn(friendlyPrefab, player, false)!).GetString(parentKey, "") == "", "Non-Blood-Magic casts stay unmarked");
Check(Data(Spawn(hostilePrefab, player)!).GetString(parentKey, "") == "", "Untamed direct summons stay unmarked");
GameObject unrelated = Make();
GameObject unrelatedChild = Spawn(hostilePrefab, unrelated)!;
Check(!unrelatedChild.GetComponent<Character>()!.Tamed && Data(unrelatedChild).GetString(childKey, "") == "", "Other tame creatures keep vanilla children");

GameObject first = Spawn(hostilePrefab, parent)!;
GameObject second = Spawn(hostilePrefab, parent)!;
int created = Object.Created;
for (int i = 0; i < 5; i++) Check(Spawn(hostilePrefab, parent) == null, "Third and later iterations denied");
Check(Object.Created == created, "Denied spawns never instantiate");
Check(first.GetComponent<Character>()!.Tamed && first.GetComponent<Character>()!.m_faction == Character.Faction.PlayerSpawned, "Child becomes tamed PlayerSpawned");
Check(hostilePrefab.GetComponent<Character>()!.m_faction == Character.Faction.Undead && !hostilePrefab.GetComponent<Character>()!.Tamed, "Original prefab untouched");
Check(Data(first).GetString(parentKey, "") == "", "Child does not become a supported parent");
Check(!Spawn(hostilePrefab, first)!.GetComponent<Character>()!.Tamed, "Grandchild keeps vanilla behavior");

Data(first).Data[ZDOVars.s_health] = 0f;
GameObject replacement = Spawn(hostilePrefab, parent)!;
Check(replacement != null && Spawn(hostilePrefab, parent) == null, "One death releases exactly one slot");
GameObject otherParent = Spawn(friendlyPrefab, player)!;
Check(Spawn(hostilePrefab, otherParent) != null && Spawn(hostilePrefab, otherParent) != null, "Limits are per parent");

// Remote uncertainty stays occupied and can only be released with evidence.
ZNet.instance.Server = false; ZNet.Local = 2;
Data(parent).Owner = 2;
created = Object.Created;
Check(Spawn(hostilePrefab, parent) == null && Object.Created == created, "Full client parent cannot create");
Check(ZRoutedRpc.instance.Sent.Count == 2, "At most two slot confirmations requested per attempt");
Data(parent).Owner = 3;
Check(Spawn(hostilePrefab, parent) == null && Object.Created == created, "Non-owner cannot spawn children");
ZNet.instance.Server = true; ZNet.Local = 1;
Data(parent).Owner = 1;
int sent = ZRoutedRpc.instance.Sent.Count;
ZRoutedRpc.instance.Deliver(1, checkRpc, Data(parent).m_uid, Data(second).GetString(childKey, ""));
Check(ZRoutedRpc.instance.Sent.Count == sent, "Server never releases a living child");

// Missing data is not death, even on the server before initial synchronization.
ZDO secondData = Data(second);
ZDOMan.instance.Objects.Remove(secondData.m_uid);
Check(Spawn(hostilePrefab, parent) == null, "Unknown server child remains occupied");
ZDOMan.instance.Objects.Add(secondData.m_uid, secondData);
string destroyedState = SubSummonSystem.BeforeDestroyed(ZDOMan.instance, secondData.m_uid);
SubSummonSystem.AfterDestroyed(ZDOMan.instance, secondData.m_uid, destroyedState);
Check(Spawn(hostilePrefab, parent) == null, "Skipped destruction does not free a slot");
ZDOMan.instance.Objects.Remove(secondData.m_uid);
SubSummonSystem.AfterDestroyed(ZDOMan.instance, secondData.m_uid, destroyedState);
Check(Spawn(hostilePrefab, parent) != null, "Confirmed server destruction releases a slot");

// An Awake callback that reenters the same parent must not overbook its slots.
GameObject reentrantParent = Spawn(friendlyPrefab, player)!;
bool attempted = false;
Object.Create = p => { if (!attempted) { attempted = true; Check(Spawn(hostilePrefab, reentrantParent) == null, "Reentrant creation denied"); } return Make(false); };
Check(Spawn(hostilePrefab, reentrantParent) != null, "Outer creation succeeds");
Object.Create = p => throw new InvalidOperationException("simulated instantiate failure");
try { Spawn(hostilePrefab, reentrantParent); } catch (InvalidOperationException) { }
Object.Create = p => Make(false);
Check(Spawn(hostilePrefab, reentrantParent) != null, "Exception releases transient creation guard");

// GUID relations survive ZDOID remapping and loss of all local static world state.
var saved = ZDOMan.instance.Objects.Values.Select(z => (z.Owner, Data: new Dictionary<int, object>(z.Data), IsParent: z == Data(parent))).ToList();
Reset();
ZDO loadedParent = null!;
foreach (var entry in saved)
{
    var z = new ZDO { m_uid = new(++next), Owner = 1 };
    foreach (var pair in entry.Data) z.Data[pair.Key] = pair.Value;
    ZDOMan.instance.Objects.Add(z.m_uid, z);
    if (entry.IsParent) loadedParent = z;
}
SubSummonSystem.AfterWorldLoad(ZDOMan.instance);
Check(loadedParent.GetString(slot0, "") != "" && loadedParent.GetString(slot1, "") != "", "Load preserves both live slots after ID remapping");
foreach (int i in new[] { 0, 1 })
{
    ZDOID reference = loadedParent.GetZDOID(ZDO.GetHashZDOID("SecondaryAttacks_SubSummonRef" + i));
    Check(ZDOMan.instance.GetZDO(reference)?.GetString(childKey, "") == loadedParent.GetString(i == 0 ? slot0 : slot1, ""), "Load rebuilds lookup reference to the actual child");
}
string orphanToken = loadedParent.GetString(slot0, "");
ZDO orphan = ZDOMan.instance.Objects.Values.Single(z => z.GetString(childKey, "") == orphanToken);
ZDOMan.instance.Objects.Remove(orphan.m_uid);
SubSummonSystem.AfterWorldLoad(ZDOMan.instance);
Check(loadedParent.GetString(slot0, "") == "" && loadedParent.GetString(slot1, "") != "", "Complete world load clears only absent children");

// RPC authorization and compare-and-clear using the actual production handlers.
Reset();
player = Make(player: true); friendlyPrefab = Make(); hostilePrefab = Make(false);
parent = Spawn(friendlyPrefab, player)!;
first = Spawn(hostilePrefab, parent)!;
string deadToken = Data(first).GetString(childKey, "");
destroyedState = SubSummonSystem.BeforeDestroyed(ZDOMan.instance, Data(first).m_uid);
ZDOMan.instance.Objects.Remove(Data(first).m_uid);
SubSummonSystem.AfterDestroyed(ZDOMan.instance, Data(first).m_uid, destroyedState);
Data(parent).Owner = 2;
ZRoutedRpc.instance.Deliver(3, checkRpc, Data(parent).m_uid, deadToken);
Check(ZRoutedRpc.instance.Sent.Count == 0, "Server rejects non-owner requests");
ZRoutedRpc.instance.Deliver(2, checkRpc, Data(parent).m_uid, deadToken);
Check(ZRoutedRpc.instance.Sent.Count == 1 && ZRoutedRpc.instance.Sent[0].Target == 2, "Server confirms known destruction only to owner");
ZNet.instance.Server = false; ZNet.Local = 2;
ZRoutedRpc.instance.Deliver(3, releaseRpc, Data(parent).m_uid, deadToken);
Check(Data(parent).GetString(slot0, "") == deadToken, "Client rejects non-server release");
ZRoutedRpc.instance.Deliver(1, releaseRpc, Data(parent).m_uid, deadToken);
Check(Data(parent).GetString(slot0, "") == "", "Owner accepts confirmed release");
GameObject latest = Spawn(hostilePrefab, parent)!;
ZRoutedRpc.instance.Deliver(1, releaseRpc, Data(parent).m_uid, deadToken);
Check(Data(parent).GetString(slot0, "") == Data(latest).GetString(childKey, ""), "Delayed duplicate reply cannot clear replacement");
Data(parent).Owner = 3;
ZRoutedRpc.instance.Deliver(1, releaseRpc, Data(parent).m_uid, Data(latest).GetString(childKey, ""));
Check(Data(parent).GetString(slot0, "") != "", "Previous owner cannot clear slots after ownership transfer");

Character c = latest.GetComponent<Character>()!;
c.m_faction = Character.Faction.Undead;
Character.Faction result = c.m_faction;
SubSummonSystem.RestoreFaction(c, latest.GetComponent<ZNetView>(), ref result);
Check(result == Character.Faction.PlayerSpawned && c.m_faction == result, "Saved child marker restores faction");
Console.WriteLine($"PASS {checks} production sub-summon checks with simulated Unity/ZDO/RPC boundaries; no game execution.");
if (args.Length == 2) OriginalContract.Run(args[0], args[1]);
