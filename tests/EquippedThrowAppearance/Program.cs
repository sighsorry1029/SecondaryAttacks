using SecondaryAttacks;
using UnityEngine;
using HarmonyLib;
using System.Reflection.Emit;

int passed=0;
void Check(bool value,string name)
{
    if(!value) throw new Exception(name);
    passed++; Console.WriteLine("PASS "+name);
}
GameObject Prefab(string name)
{
    GameObject prefab=new(name);
    prefab.AddComponent<ItemDrop>().m_itemData.m_dropPrefab=prefab;
    GameObject attach=new("attach");attach.AddComponent<EquipmentVisual>();attach.transform.SetParent(prefab.transform,false);
    ObjectDB.instance!.Prefabs[name.GetStableHashCode()]=prefab;
    return prefab;
}
Projectile NewProjectile(bool owner=true)
{
    GameObject go=new("projectile");ZNetView view=go.AddComponent<ZNetView>();view.Owner=owner;
    Projectile projectile=go.AddComponent<Projectile>();projectile.m_visual=new("initial");
    return projectile;
}
ObjectDB.instance=new();
var lookup=AccessTools.Method(typeof(ObjectDB),nameof(ObjectDB.GetItemPrefab),new[]{typeof(string)});
var rewritten=CopiedThrowProjectileVisualSystem.RewriteVisualPrefabLookup(new[]{new CodeInstruction(OpCodes.Callvirt,lookup)}).ToList();
Check(rewritten.Count==2 && rewritten[0].opcode==OpCodes.Ldarg_0 && rewritten[1].Calls(AccessTools.Method(typeof(CopiedThrowProjectileVisualSystem),nameof(CopiedThrowProjectileVisualSystem.ResolveEquippedVisualPrefab))),"single original lookup is replaced without extra mesh creation path");
GameObject original=Prefab("SwordOriginal"),cosmetic=Prefab("SwordCosmetic");
original.GetComponent<ItemDrop>()!.m_itemData.m_shared.m_hitEffect.m_effectPrefabs=new[]{new EffectList.EffectData()};
ItemDrop.ItemData weapon=original.GetComponent<ItemDrop>()!.m_itemData;
weapon.m_variant=2;
GameObject playerObject=new();Player player=playerObject.AddComponent<Player>();ZNetView playerView=playerObject.AddComponent<ZNetView>();
player.LeftItem=weapon;playerView.Data.Set(ZDOVars.s_leftItem,cosmetic.name.GetStableHashCode());playerView.Data.Set(ZDOVars.s_leftItemVariant,3);
long snapshot=ProjectileAccess.CaptureEquippedAppearance(player,weapon);
Check(ProjectileAccess.AppearancePrefabHash(snapshot)==cosmetic.name.GetStableHashCode() && ProjectileAccess.AppearanceVariant(snapshot)==3,"capture resolved left appearance and variant");
long signed=ProjectileAccess.PackAppearance(int.MinValue+15,7);
Check(ProjectileAccess.AppearancePrefabHash(signed)==int.MinValue+15 && ProjectileAccess.AppearanceVariant(signed)==7,"atomic payload preserves signed hash and variant");
Check(ProjectileAccess.AppearanceVariant(ProjectileAccess.PackAppearance(12,-4))==0,"negative variant normalized");
player.LeftItem=null;player.RightItem=weapon;playerView.Data.Set(ZDOVars.s_rightItem,cosmetic.name.GetStableHashCode());
Check(ProjectileAccess.AppearanceVariant(ProjectileAccess.CaptureEquippedAppearance(player,weapon))==0,"right hand follows actual displayed variant zero");
Check(ProjectileAccess.CaptureEquippedAppearance(player,new ItemDrop.ItemData{m_dropPrefab=original})==0L,"same prefab different item cannot borrow another hand appearance");
playerView.Owner=false;Check(ProjectileAccess.CaptureEquippedAppearance(player,weapon)==0L,"non-owner cannot capture");playerView.Owner=true;
playerView.Data.Set(ZDOVars.s_rightItem,0);Check(ProjectileAccess.CaptureEquippedAppearance(player,weapon)==0L,"hidden hand falls back");
playerView.Data.Set(ZDOVars.s_rightItem,original.name.GetStableHashCode());Check(ProjectileAccess.CaptureEquippedAppearance(player,weapon)==0L,"normal equipped appearance stays on existing path");
playerView.Data.Set(ZDOVars.s_rightItem,cosmetic.name.GetStableHashCode());
weapon.m_shared.m_itemType=ItemDrop.ItemData.ItemType.Bow;Check(ProjectileAccess.CaptureEquippedAppearance(player,weapon)==0L,"bow excluded");
weapon.m_shared.m_itemType=ItemDrop.ItemData.ItemType.OneHandedWeapon;weapon.m_shared.m_skillType=Skills.SkillType.ElementalMagic;Check(ProjectileAccess.CaptureEquippedAppearance(player,weapon)==0L,"one-hand magic excluded");weapon.m_shared.m_skillType=Skills.SkillType.Swords;

Attack attack=new(){m_weapon=weapon,m_character=player,m_attackProjectile=new("throwTemplate")};
CopiedThrowProjectileVisualSystem.CaptureAttackAppearance(attack,player,weapon);
long atStart=CopiedThrowProjectileVisualSystem.GetAttackAppearance(attack);
player.RightItem=null;playerView.Data.Set(ZDOVars.s_rightItem,0);
Check(atStart!=0L && CopiedThrowProjectileVisualSystem.GetAttackAppearance(attack)==atStart,"attack snapshot survives unequip");
Projectile projectile=NewProjectile();projectile.m_weapon=weapon;projectile.m_spawnItem=weapon;projectile.m_respawnItemOnHit=true;
var context=CopiedThrowProjectileVisualSystem.CreateSpawnedProjectileVisualContext(weapon,attack.m_attackProjectile,snapshot);
int before=UnityEngine.Object.Instances;
CopiedThrowProjectileVisualSystem.ApplyCurrentWeaponVisualForSpawnedProjectile(projectile,context);
ZDO projectileData=projectile.GetComponent<ZNetView>()!.Data;
Check(projectileData.GetLong(CopiedThrowProjectileVisualSystem.EquippedAppearanceKey)==snapshot,"owner writes one atomic appearance payload");
Check(projectileData.GetString(ZDOVars.s_visual,out string source) && source==original.name,"source visual identity preserved");
Check(UnityEngine.Object.Instances-before==1,"initial cosmetic uses one mesh creation");
Check(projectile.m_visual.GetComponent<EquipmentVisual>()!.Variant==3,"cosmetic variant applied once");
Check(ReferenceEquals(projectile.m_spawnItem,weapon) && ReferenceEquals(projectile.m_weapon,weapon) && weapon.m_dropPrefab==original && projectile.m_respawnItemOnHit,"cosmetic leaves drop and weapon identity unchanged");
Check(ReferenceEquals(projectile.m_hitEffects.m_effectPrefabs[0],weapon.m_shared.m_hitEffect.m_effectPrefabs[0]),"hit effects remain source weapon effects");
int lookups=ObjectDB.instance.CosmeticLookups;before=UnityEngine.Object.Instances;
for(int i=0;i<100;i++)projectile.UpdateVisual();
Check(UnityEngine.Object.Instances==before && ObjectDB.instance.CosmeticLookups==lookups,"unchanged frames do not search cosmetic prefabs or recreate meshes");
Check(projectile.m_visual.GetComponent<EquipmentVisual>()!.Calls==1,"unchanged frames do not reapply variants");

Projectile remote=NewProjectile(false);ZDO remoteData=remote.GetComponent<ZNetView>()!.Data;
remoteData.Set("SecondaryAttacks_CopiedThrowProjectile",true);remoteData.Set(ZDOVars.s_visual,original.name);
remote.UpdateVisual();GameObject firstVisual=remote.m_visual;
remoteData.Set(CopiedThrowProjectileVisualSystem.EquippedAppearanceKey,snapshot);before=UnityEngine.Object.Instances;
remote.UpdateVisual();
Check(UnityEngine.Object.Instances-before==1 && !firstVisual.active && remote.m_visual.GetComponent<EquipmentVisual>()!.Variant==3,"late remote appearance replaces completed base visual exactly once");
Check(remoteData.GetString(ZDOVars.s_visual,out source) && source==original.name,"remote presentation does not replace network source identity");
var noAppearance=CopiedThrowProjectileVisualSystem.CreateSpawnedProjectileVisualContext(weapon,attack.m_attackProjectile);
Projectile unauthorized=NewProjectile(false);CopiedThrowProjectileVisualSystem.ApplyCurrentWeaponVisualForSpawnedProjectile(unauthorized,context);
Check(unauthorized.GetComponent<ZNetView>()!.Data.Values.Count==0,"non-owner writes no appearance or source keys");

Projectile missing=NewProjectile();long absent=ProjectileAccess.PackAppearance(123456789,4);
CopiedThrowProjectileVisualSystem.ApplyCurrentWeaponVisualForSpawnedProjectile(missing,CopiedThrowProjectileVisualSystem.CreateSpawnedProjectileVisualContext(weapon,attack.m_attackProjectile,absent));
lookups=ObjectDB.instance.CosmeticLookups;before=UnityEngine.Object.Instances;
for(int i=0;i<100;i++)missing.UpdateVisual();
Check(missing.m_changedVisual && UnityEngine.Object.Instances==before && ObjectDB.instance.CosmeticLookups==lookups,"unavailable cosmetic uses normal visual without repeated failed lookups");
weapon.m_shared.m_secondaryAttack.m_attackProjectile=attack.m_attackProjectile;
Check(CopiedThrowProjectileVisualSystem.CreateSpawnedProjectileVisualContext(weapon,attack.m_attackProjectile).SkipVisualSwap,"native projectile remains untouched without appearance override");
Check(!CopiedThrowProjectileVisualSystem.CreateSpawnedProjectileVisualContext(weapon,attack.m_attackProjectile,snapshot).SkipVisualSwap,"native copied melee throw accepts captured cosmetic only");
var followup=CopiedThrowProjectileVisualSystem.CreateSpawnedProjectileVisualContext(weapon,attack.m_attackProjectile,atStart);
player.RightItem=weapon;playerView.Data.Set(ZDOVars.s_rightItem,original.name.GetStableHashCode());
CopiedThrowProjectileVisualSystem.CaptureAttackAppearance(attack,player,weapon);
Check(CopiedThrowProjectileVisualSystem.GetAttackAppearance(attack)==0L && followup.EquippedAppearance==atStart,"new start clears stale attack state while followup keeps original snapshot");
Projectile failure=NewProjectile();failure.m_spawnItem=weapon;failure.m_weapon=weapon;
UnityEngine.Object.ThrowOnceOnPrefab=cosmetic.transform.Find("attach")!.gameObject;
CopiedThrowProjectileVisualSystem.ApplyCurrentWeaponVisualForSpawnedProjectile(failure,context);
Check(failure.m_changedVisual && ReferenceEquals(failure.m_spawnItem,weapon) && ReferenceEquals(failure.m_weapon,weapon),"cosmetic creation exception falls back without escaping item setup or changing item identity");
Exception unrelated=new InvalidOperationException("other mod");
Check(ReferenceEquals(unrelated,CopiedThrowProjectileVisualSystem.RestoreAfterAppearanceFailure(failure,unrelated)),"unrelated exception remains visible");
var label=new DynamicMethod("labels",typeof(void),Type.EmptyTypes).GetILGenerator().DefineLabel();
var originalCall=new CodeInstruction(OpCodes.Callvirt,lookup);originalCall.labels.Add(label);originalCall.blocks.Add(new object());
rewritten=CopiedThrowProjectileVisualSystem.RewriteVisualPrefabLookup(new[]{originalCall}).ToList();
Check(rewritten[0].labels.Contains(label) && rewritten[0].blocks.Count==1 && rewritten[1].labels.Count==0,"transpiler transfers branch and exception labels to inserted instance load");
var untouched=new CodeInstruction(OpCodes.Ret);
var mismatch=CopiedThrowProjectileVisualSystem.RewriteVisualPrefabLookup(new[]{untouched}).ToList();
Check(mismatch.Count==1 && ReferenceEquals(mismatch[0],untouched) && CopiedThrowProjectileVisualSystem.CreateSpawnedProjectileVisualContext(weapon,attack.m_attackProjectile,snapshot).SkipVisualSwap,"zero lookup match leaves IL and native fallback untouched");
var firstCall=new CodeInstruction(OpCodes.Callvirt,lookup);var secondCall=new CodeInstruction(OpCodes.Callvirt,lookup);
mismatch=CopiedThrowProjectileVisualSystem.RewriteVisualPrefabLookup(new[]{firstCall,secondCall}).ToList();
Check(mismatch.Count==2 && firstCall.Calls(lookup) && secondCall.Calls(lookup),"ambiguous lookup matches disable cosmetic feature without mutating IL");
Console.WriteLine($"RESULT: {passed} passed");
