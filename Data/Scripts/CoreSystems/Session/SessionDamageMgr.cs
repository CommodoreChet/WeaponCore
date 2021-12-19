﻿using System;
using System.Collections.Generic;
using CoreSystems.Projectiles;
using CoreSystems.Support;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Interfaces;
using VRage.Utils;
using VRageMath;
using static CoreSystems.Support.WeaponDefinition.AmmoDef.AreaOfDamageDef;
using static CoreSystems.Support.WeaponDefinition.AmmoDef.DamageScaleDef;

namespace CoreSystems
{

    public partial class Session
    {
        private bool _shieldNull;
        internal void ProcessHits()
        {
            LastDamageTick = Tick;
            _shieldNull = false;
            for (int x = 0; x < Hits.Count; x++)
            {

                var p = Hits[x];
                var info = p.Info;
                var maxObjects = info.AmmoDef.Const.MaxObjectsHit;
                var phantom = info.AmmoDef.BaseDamage <= 0;
                var pInvalid = (int)p.State > 3;
                var tInvalid = info.Target.IsProjectile && (int)info.Target.Projectile.State > 1;
                if (tInvalid) info.Target.Reset(Tick, Target.States.ProjectileClosed);
                var skip = pInvalid || tInvalid;
                for (int i = 0; i < info.HitList.Count; i++)
                {
                    var hitEnt = info.HitList[i];
                    var hitMax = info.ObjectsHit >= maxObjects;
                    var outOfPew = info.BaseDamagePool <= 0 && !(phantom && hitEnt.EventType == HitEntity.Type.Effect);

                    if (outOfPew && p.State == Projectile.ProjectileState.Detonate && i != info.HitList.Count - 1)
                    {
                        outOfPew = false;
                        info.BaseDamagePool = 0.01f;

                    }
                    if (skip || hitMax || outOfPew)
                    {
                        if (hitMax || outOfPew || pInvalid)
                        {
                            p.State = Projectile.ProjectileState.Depleted;
                        }
                        Projectiles.HitEntityPool.Return(hitEnt);
                        continue;
                    }

                    switch (hitEnt.EventType)
                    {
                        case HitEntity.Type.Shield:
                            DamageShield(hitEnt, info);  //set to 2 for new det/radiant
                            continue;
                        case HitEntity.Type.Grid:
                            DamageGrid(hitEnt, info);  //set to 2 for new det/radiant
                            continue;
                        case HitEntity.Type.Destroyable:
                            DamageDestObj(hitEnt, info);
                            continue;
                        case HitEntity.Type.Voxel:
                            DamageVoxel(hitEnt, info);
                            continue;
                        case HitEntity.Type.Projectile:
                            DamageProjectile(hitEnt, info);
                            continue;
                        case HitEntity.Type.Field:
                            UpdateField(hitEnt, info);
                            continue;
                        case HitEntity.Type.Effect:
                            UpdateEffect(hitEnt, info);
                            continue;
                    }

                    Projectiles.HitEntityPool.Return(hitEnt);
                }

                if (info.BaseDamagePool <= 0)
                    p.State = Projectile.ProjectileState.Depleted;

                info.HitList.Clear();
            }
            Hits.Clear();
        }

        private void DamageShield(HitEntity hitEnt, ProInfo info)
        {
            var shield = hitEnt.Entity as IMyTerminalBlock;
            if (shield == null || !hitEnt.HitPos.HasValue) return;
            if (!info.ShieldBypassed)
                info.ObjectsHit++;

            var directDmgGlobal = Settings.Enforcement.DirectDamageModifer;
            var areaDmgGlobal = Settings.Enforcement.AreaDamageModifer;
            var shieldDmgGlobal = Settings.Enforcement.ShieldDamageModifer;

            var damageScale = 1 * directDmgGlobal;
            var distTraveled = info.AmmoDef.Const.IsBeamWeapon ? hitEnt.HitDist ?? info.DistanceTraveled : info.DistanceTraveled;
            var fallOff = info.AmmoDef.Const.FallOffScaling && distTraveled > info.AmmoDef.Const.FallOffDistance;
            if (info.AmmoDef.Const.VirtualBeams) damageScale *= info.WeaponCache.Hits;
            var damageType = info.AmmoDef.DamageScales.Shields.Type;
            var heal = damageType == ShieldDef.ShieldType.Heal;
            var energy = info.AmmoDef.Const.EnergyShieldDmg;
            var detonateOnEnd = info.AmmoDef.AreaOfDamage.EndOfLife.Enable && info.Age >= info.AmmoDef.AreaOfDamage.EndOfLife.MinArmingTime && !info.ShieldBypassed;
            var areaDamage = info.AmmoDef.AreaOfDamage.ByBlockHit.Enable;
            var scaledBaseDamage = info.BaseDamagePool * damageScale;
            var scaledDamage = (scaledBaseDamage) * info.AmmoDef.Const.ShieldModifier * shieldDmgGlobal* info.ShieldResistMod* info.ShieldBypassMod;

            var areafalloff = info.AmmoDef.AreaOfDamage.ByBlockHit.Falloff;
            var aoeMaxAbsorb = info.AmmoDef.AreaOfDamage.ByBlockHit.MaxAbsorb;
            var unscaledAoeDmg = info.AmmoDef.Const.AreaEffectDamage;
            var aoeRadius = (float)info.AmmoDef.Const.AreaEffectSize;

            //Detonation info
            var detfalloff = info.AmmoDef.AreaOfDamage.EndOfLife.Falloff;
            var detmaxabsorb = info.AmmoDef.AreaOfDamage.EndOfLife.MaxAbsorb;
            var unscaledDetDmg = info.AmmoDef.Const.DetonationDamage;
            var detradius = info.AmmoDef.Const.DetonationRadius;

            if (fallOff)
            {
                var fallOffMultipler = MathHelperD.Clamp(1.0 - ((distTraveled - info.AmmoDef.Const.FallOffDistance) / (info.AmmoDef.Const.MaxTrajectory - info.AmmoDef.Const.FallOffDistance)), info.AmmoDef.DamageScales.FallOff.MinMultipler, 1);
                scaledDamage *= fallOffMultipler;
            }

            //detonation falloff scaling and capping by maxabsorb
            if (detonateOnEnd)
            {
                switch (detfalloff)
                {
                    case Falloff.Pooled:  //Limited to damage only, retained for future tweaks if needed
                        unscaledDetDmg *= 1;
                        break;
                    case Falloff.NoFalloff:  //No falloff, damage stays the same regardless of distance
                        unscaledDetDmg *= detradius;
                        break;
                    case Falloff.Linear: //Damage is evenly stretched from 1 to max dist, dropping in equal increments
                        unscaledDetDmg *= detradius * 0.55f;
                        break;
                    case Falloff.Curve:  //Drops sharply closer to max range
                        unscaledDetDmg *= detradius * 0.81f;
                        break;
                    case Falloff.InvCurve:  //Drops at beginning, roughly similar to inv square
                        unscaledDetDmg *= detradius * 0.39f;
                        break;
                    case Falloff.Squeeze: //Damage is highest at furthest point from impact, to create a spall or crater
                        unscaledDetDmg *= detradius * 0.22f;
                        break;
                }
            
            }
            var detonateDamage = detonateOnEnd && info.ShieldBypassMod >= 1 ? (unscaledDetDmg * info.AmmoDef.Const.ShieldModifier * areaDmgGlobal * shieldDmgGlobal) * info.ShieldResistMod : 0;
            if (detonateDamage >= detmaxabsorb) detonateDamage = detmaxabsorb;
            //end of new detonation stuffs

            //radiant falloff scaling and capping by maxabsorb
            if (areaDamage)
            {
                switch (areafalloff)
                {
                    case Falloff.Pooled:  //Limited to damage only, retained for future tweaks if needed
                        unscaledAoeDmg *= 1;
                        break;
                    case Falloff.NoFalloff:  //No falloff, damage stays the same regardless of distance
                        unscaledAoeDmg *= aoeRadius;
                        break;
                    case Falloff.Linear: //Damage is evenly stretched from 1 to max dist, dropping in equal increments
                        unscaledAoeDmg *= aoeRadius * 0.55f;
                        break;
                    case Falloff.Curve:  //Drops sharply closer to max range
                        unscaledAoeDmg *= aoeRadius * 0.81f;
                        break;
                    case Falloff.InvCurve:  //Drops at beginning, roughly similar to inv square
                        unscaledAoeDmg *= aoeRadius * 0.39f;
                        break;
                    case Falloff.Squeeze: //Damage is highest at furthest point from impact, to create a spall or crater
                        unscaledAoeDmg *= aoeRadius * 0.22f;
                        break;
                }

            }
            var radiantDamage = areaDamage && info.ShieldBypassMod >= 1 ? (unscaledAoeDmg * info.AmmoDef.Const.ShieldModifier * areaDmgGlobal * shieldDmgGlobal) * info.ShieldResistMod : 0;
            if (radiantDamage >= aoeMaxAbsorb) radiantDamage = aoeMaxAbsorb;
            //end of new radiant stuffs

            scaledDamage += radiantDamage;

            if (heal)
            {
                var heat = SApi.GetShieldHeat(shield);

                switch (heat)
                {
                    case 0:
                        scaledDamage *= -1;
                        detonateDamage *= -1;
                        break;
                    case 100:
                        scaledDamage = -0.01f;
                        detonateDamage = -0.01f;
                        break;
                    default:
                        {
                            var dec = heat / 100f;
                            var healFactor = 1 - dec;
                            scaledDamage *= healFactor;
                            scaledDamage *= -1;
                            detonateDamage *= healFactor;
                            detonateDamage *= -1;
                            break;
                        }
                }
            }
            var hitWave = info.AmmoDef.Const.RealShotsPerMin <= 120;
            var hit = SApi.PointAttackShieldCon(shield, hitEnt.HitPos.Value, info.Target.CoreEntity.EntityId, (float)scaledDamage, (float)detonateDamage, energy, hitWave);
            if (hit.HasValue)
            {

                if (heal)
                {
                    info.BaseDamagePool = 0;
                    return;
                }

                var objHp = hit.Value;


                if (info.EwarActive)
                    info.BaseDamagePool -= 1;
                else if (objHp > 0)
                {

                    if (!info.ShieldBypassed)
                        info.BaseDamagePool = 0;
                    else
                        info.BaseDamagePool -= (info.BaseDamagePool * info.ShieldResistMod) * info.ShieldBypassMod;
                }
                else info.BaseDamagePool = (objHp * -1);

                if (info.AmmoDef.Mass <= 0) return;

                var speed = !info.AmmoDef.Const.IsBeamWeapon && info.AmmoDef.Const.DesiredProjectileSpeed > 0 ? info.AmmoDef.Const.DesiredProjectileSpeed : 1;
                if (Session.IsServer && !shield.CubeGrid.IsStatic && !SApi.IsFortified(shield))
                    ApplyProjectileForce((MyEntity)shield.CubeGrid, hitEnt.HitPos.Value, hitEnt.Intersection.Direction, info.AmmoDef.Mass * speed);
            }
            else if (!_shieldNull)
            {
                Log.Line($"DamageShield PointAttack returned null");
                _shieldNull = true;
            }
        }

        private void DamageGrid(HitEntity hitEnt, ProInfo t)
        {
            var grid = hitEnt.Entity as MyCubeGrid;
            if (grid == null || grid.MarkedForClose || !hitEnt.HitPos.HasValue || hitEnt.Blocks == null)
            {
                hitEnt.Blocks?.Clear();
                Log.Line($"DamageGrid first null check hit");
                return;
            }

            if (t.AmmoDef.DamageScales.Shields.Type == ShieldDef.ShieldType.Heal || (!t.AmmoDef.Const.SelfDamage && !t.AmmoDef.Const.IsCriticalReaction) && t.Ai.AiType == Ai.AiTypes.Grid && t.Ai.GridEntity.IsInSameLogicalGroupAs(grid) || !grid.DestructibleBlocks || grid.Immune || grid.GridGeneralDamageModifier <= 0)
            {
                t.BaseDamagePool = 0;
                return;
            }

            //Global & modifiers
            var canDamage = t.DoDamage;
            _destroyedSlims.Clear();
            _destroyedSlimsClient.Clear();
            var directDmgGlobal = Settings.Enforcement.DirectDamageModifer;
            var areaDmgGlobal = Settings.Enforcement.AreaDamageModifer;
            var sync = MpActive && (DedicatedServer || IsServer);
            float gridDamageModifier = grid.GridGeneralDamageModifier;
            IMySlimBlock rootBlock = null;
            var d = t.AmmoDef.DamageScales;

            //Target/targeting Info
            var largeGrid = grid.GridSizeEnum == MyCubeSize.Large;
            var attackerId = t.Target.CoreEntity.EntityId;
            var maxObjects = t.AmmoDef.Const.MaxObjectsHit;
            var gridMatrix = grid.PositionComp.WorldMatrixRef;
            var playerAi = t.Ai.AiType == Ai.AiTypes.Player;
            var distTraveled = t.AmmoDef.Const.IsBeamWeapon ? hitEnt.HitDist ?? t.DistanceTraveled : t.DistanceTraveled;
            var direction = hitEnt.Intersection.Direction;
            var localpos = Vector3I.Round(Vector3D.Transform(hitEnt.Intersection.To, grid.PositionComp.WorldMatrixNormalizedInv) * grid.GridSizeR - 0.5);

            //Ammo properties
            var hitMass = t.AmmoDef.Mass;

            //overall primary falloff scaling
            var fallOff = t.AmmoDef.Const.FallOffScaling && distTraveled > t.AmmoDef.Const.FallOffDistance;
            var fallOffMultipler = 1d;
            if (fallOff)
            {
                fallOffMultipler = (float)MathHelperD.Clamp(1.0 - ((distTraveled - t.AmmoDef.Const.FallOffDistance) / (t.AmmoDef.Const.MaxTrajectory - t.AmmoDef.Const.FallOffDistance)), t.AmmoDef.DamageScales.FallOff.MinMultipler, 1);
            }
            //hit & damage loop info
            var basePool = t.BaseDamagePool;
            var hits = 1;
            if (t.AmmoDef.Const.VirtualBeams)
            {
                hits = t.WeaponCache.Hits;
            }
            var partialShield = t.ShieldInLine && !t.ShieldBypassed && SApi.MatchEntToShieldFast(grid, true) != null;
            var objectsHit = t.ObjectsHit;
            var blockCount = hitEnt.Blocks.Count;
            var countBlocksAsObjects = t.AmmoDef.ObjectsHit.CountBlocks;

            //General damage data

            //Generics used for both AOE and detonation
            var aoeFalloff = Falloff.NoFalloff;

            var hasAoe = t.AmmoDef.AreaOfDamage.ByBlockHit.Enable; 
            var hasDet = t.AmmoDef.AreaOfDamage.EndOfLife.Enable && t.Age >= t.AmmoDef.AreaOfDamage.EndOfLife.MinArmingTime;

            var damageType = t.ShieldBypassed ? ShieldBypassDamageType : hasAoe || hasDet ? MyDamageType.Explosion : MyDamageType.Bullet;
            //Switches and setup for damage types/event loops
            var detRequested = false;
            var detActive = false;
            var earlyExit = false;
            var destroyed = 0;

            //Main loop (finally)

            for (int i = 0; i < blockCount; i++)
            {
                if (earlyExit || (basePool <= 0 || objectsHit >= maxObjects) && !detRequested)
                {
                    //Log.Line($"Early exit:{earlyExit} - basePool:{basePool} - objhit:{objectsHit} - maxObj:{maxObjects} - detRequested:{detRequested}");
                    basePool = 0;
                    break;
                }

                var aoeAbsorb = 0d;
                var aoeDepth = 0d;
                var aoeDmgTally = 0d;
                var aoeDamage = 0f;
                var aoeRadius = 0d;
                var aoeIsPool = false;

                if (hasAoe && !detRequested)//load in AOE vars
                {
                    aoeDamage = t.AmmoDef.Const.AreaEffectDamage;
                    aoeRadius = t.AmmoDef.Const.AreaEffectSize; //fix type in definitions to float?
                    aoeFalloff = t.AmmoDef.AreaOfDamage.ByBlockHit.Falloff;
                    aoeAbsorb = t.AmmoDef.Const.AoeMaxAbsorb;
                    aoeDepth = t.AmmoDef.AreaOfDamage.ByBlockHit.Depth;
                    aoeIsPool = aoeFalloff == Falloff.Pooled;
                }
                else if (hasDet && detRequested)//load in Detonation vars
                {
                    aoeDamage = t.AmmoDef.Const.DetonationDamage;
                    aoeRadius = t.AmmoDef.Const.DetonationRadius;
                    aoeFalloff = t.AmmoDef.AreaOfDamage.EndOfLife.Falloff;
                    aoeAbsorb = t.AmmoDef.Const.DetMaxAbsorb;
                    aoeDepth = t.AmmoDef.AreaOfDamage.EndOfLife.Depth;
                    aoeIsPool = aoeFalloff == Falloff.Pooled;
                }

                rootBlock = hitEnt.Blocks[i];
                if (!detRequested)
                {
                    if (IsServer && _destroyedSlims.Contains(rootBlock) || IsClient && _destroyedSlimsClient.Contains(rootBlock)) continue;
                    if (rootBlock.IsDestroyed)
                    {
                        destroyed++;
                        if (IsClient)
                        {
                            _destroyedSlimsClient.Add(rootBlock);
                            _slimHealthClient.Remove(rootBlock);
                        }
                        else
                            _destroyedSlims.Add(rootBlock);

                        continue;
                    }
                    var fatBlock = rootBlock.FatBlock as MyCubeBlock;
                    var door = fatBlock as MyDoorBase;
                    if (door != null && door.Open && !HitDoor(hitEnt, door) || playerAi && !RayAccuracyCheck(hitEnt, rootBlock))
                        continue;
                }

                var maxDbc = 0;
                var foundAoeBlocks = false;
                if (hasAoe && !detRequested || hasDet && detRequested)
                {
                    RadiantAoe(rootBlock, localpos, grid, aoeRadius, aoeDepth, direction, ref maxDbc, out foundAoeBlocks);
                    //Log.Line($"got blocks to distance: {maxDbc} - wasDetonating:{detRequested}");
                }
                var offset = !detActive && foundAoeBlocks ? 2 : 1;
                maxDbc = !foundAoeBlocks ? 1 : maxDbc + offset;

                for (int j = 0; j < maxDbc; j++)//Loop through blocks "hit" by damage, in groups by range.  J essentially = dist to root
                {
                    if (earlyExit || i  < 0)
                        break;

                    var rootStep = j == 0 && !detActive;
                    //Log.Line($"i:{i} - j:{j} - detonating:{detRequested} - maxDbc:{maxDbc} - foundAoeBlocks:{foundAoeBlocks} -- (tally:{aoeDmgTally} >= {aoeAbsorb} OR aoeDmt:{aoeDamage} <= 0) - detComp:{detActive}");

                    int dbCount = 1;
                    var aoeDamageFall = 0d;
                    List<IMySlimBlock> dbc = null;

                    if (!rootStep)
                    {
                        try
                        {
                            dbc = DamageBlockCache[j - (offset - 1)];
                        }
                        catch
                        {
                            Log.Line($"[DamageBlockCache crash] detonating:{detRequested} - detActive:{detActive} - i:{i} - j:{j} - offset:{offset} - index:{j - (offset - 1)} - maxDbc:{maxDbc}");
                            foreach (var l in DamageBlockCache)
                                l.Clear();
                            
                            earlyExit = true;
                            break;
                        }
                        dbCount = dbc.Count;
                        if (dbCount == 0)
                        {
                            //Log.Line($"dbCount == 0 - i:{i} - j:{j}");
                            continue;
                        }
                    }

                    if (hasAoe || hasDet && detRequested)
                    {
                        //Falloff switches & calcs for type of explosion & aoeDamageFall as output
                        var maxfalldist = aoeRadius * grid.GridSizeR + 1;
                        switch (aoeFalloff)
                        {

                            case Falloff.NoFalloff:  //No falloff, damage stays the same regardless of distance
                                aoeDamageFall = aoeDamage;
                                break;
                            case Falloff.Linear: //Damage is evenly stretched from 1 to max dist, dropping in equal increments
                                aoeDamageFall = (maxfalldist - j) / maxfalldist * aoeDamage;
                                break;
                            case Falloff.Curve:  //Drops sharply closer to max range
                                aoeDamageFall = 100 - j / maxfalldist / (maxfalldist - j) * aoeDamage;
                                break;
                            case Falloff.InvCurve:  //Drops at beginning, roughly similar to inv square
                                aoeDamageFall = (maxfalldist - j) / maxfalldist * (maxfalldist - j) / maxfalldist * aoeDamage;
                                break;
                            case Falloff.Squeeze: //Damage is highest at furthest point from impact, to create a spall or crater
                                aoeDamageFall = (j + 1) / maxfalldist / (maxfalldist - j) * aoeDamage;
                                break;
                            case Falloff.Pooled:
                                aoeDamageFall = aoeDamage;
                                break;

                        }
                    }

                    //apply to blocks (k) in distance group (j)
                    for (int k = 0; k < dbCount; k++)
                    {
                        if (earlyExit || i < 0)
                            break;

                        var block = rootBlock;//temp for debug purposes in try below
                        try
                        {
                            block = !rootStep ? dbc[k] : rootBlock;

                        }
                        catch
                        {
                            Log.Line($"Index error on calling group DBC[J{j}][K{k}]" +
                                $"hasaoe {hasAoe} or hasdet{hasDet}");
                            continue;
                        }
                        if (block.IsDestroyed)
                            continue;

                        if (partialShield && SApi.IsBlockProtected(block))
                        {
                            earlyExit = true;
                            break;
                        }

                        var cubeBlockDef = (MyCubeBlockDefinition)block.BlockDefinition;
                        float cachedIntegrity;
                        var blockHp = (double)(!IsClient ? block.Integrity - block.AccumulatedDamage : (_slimHealthClient.TryGetValue(block, out cachedIntegrity) ? cachedIntegrity : block.Integrity));
                        var blockDmgModifier = cubeBlockDef.GeneralDamageMultiplier;
                        double damageScale = hits;
                        double directDamageScale = directDmgGlobal;
                        double areaDamageScale = areaDmgGlobal;
                        double detDamageScale = areaDmgGlobal;

                        //Damage scaling for blocktypes
                        if (t.AmmoDef.Const.DamageScaling || !MyUtils.IsEqual(blockDmgModifier, 1f) || !MyUtils.IsEqual(gridDamageModifier, 1f))
                        {
                            if (blockDmgModifier < 0.000000001f || gridDamageModifier < 0.000000001f)
                                blockHp = float.MaxValue;
                            else
                                blockHp = (blockHp / blockDmgModifier / gridDamageModifier);


                            if (d.MaxIntegrity > 0 && blockHp > d.MaxIntegrity)
                            {
                                basePool = 0;
                                continue;
                            }

                            if (d.Grids.Large >= 0 && largeGrid) damageScale *= d.Grids.Large;
                            else if (d.Grids.Small >= 0 && !largeGrid) damageScale *= d.Grids.Small;

                            MyDefinitionBase blockDef = null;
                            if (t.AmmoDef.Const.ArmorScaling)
                            {
                                blockDef = block.BlockDefinition;
                                var isArmor = AllArmorBaseDefinitions.Contains(blockDef) || CustomArmorSubtypes.Contains(blockDef.Id.SubtypeId);
                                if (isArmor && d.Armor.Armor >= 0) damageScale *= d.Armor.Armor;
                                else if (!isArmor && d.Armor.NonArmor >= 0) damageScale *= d.Armor.NonArmor;
                                if (isArmor && (d.Armor.Light >= 0 || d.Armor.Heavy >= 0))
                                {
                                    var isHeavy = HeavyArmorBaseDefinitions.Contains(blockDef) || CustomHeavyArmorSubtypes.Contains(blockDef.Id.SubtypeId);
                                    if (isHeavy && d.Armor.Heavy >= 0) damageScale *= d.Armor.Heavy;
                                    else if (!isHeavy && d.Armor.Light >= 0) damageScale *= d.Armor.Light;
                                }
                            }

                            if (t.AmmoDef.Const.CustomDamageScales)
                            {
                                if (blockDef == null) blockDef = block.BlockDefinition;
                                float modifier;
                                var found = t.AmmoDef.Const.CustomBlockDefinitionBasesToScales.TryGetValue(blockDef, out modifier);
                                if (found) damageScale *= modifier;
                                else if (t.AmmoDef.DamageScales.Custom.IgnoreAllOthers) continue;
                            }

                            if (GlobalDamageModifed)
                            {
                                if (blockDef == null) blockDef = block.BlockDefinition;
                                BlockDamage modifier;
                                var found = BlockDamageMap.TryGetValue(blockDef, out modifier);

                                if (found)
                                {
                                    directDamageScale *= modifier.DirectModifer;
                                    areaDamageScale *= modifier.AreaModifer;
                                    detDamageScale *= modifier.AreaModifer;
                                }
                            }

                            if (ArmorCoreActive)
                            {
                                var subtype = block.BlockDefinition.Id.SubtypeId;
                                if (ArmorCoreBlockMap.ContainsKey(subtype))
                                {
                                    var resistances = ArmorCoreBlockMap[subtype];
                                    directDamageScale /= t.AmmoDef.Const.EnergyBaseDmg ? resistances.EnergeticResistance : resistances.KineticResistance;
                                    areaDamageScale /= t.AmmoDef.Const.EnergyAreaDmg ? resistances.EnergeticResistance : resistances.KineticResistance;
                                    detDamageScale /= t.AmmoDef.Const.EnergyDetDmg ? resistances.EnergeticResistance : resistances.KineticResistance;
                                }
                            }

                            if (fallOff)
                                damageScale *= fallOffMultipler;
                        }

                        var primaryDamage = rootStep && block == rootBlock;//limits application to first run w/AOE, suppresses with detonation
                        var baseScale = damageScale * directDamageScale;
                        var scaledDamage = (float)(basePool * baseScale);
                        var aoeScaledDmg = (float)(aoeDamageFall * (detRequested ? detDamageScale : areaDamageScale));
                        bool deadBlock = false;

                        //Check for end of primary life
                        if (scaledDamage <= blockHp && primaryDamage)
                        {
                            basePool = 0;
                            t.BaseDamagePool = basePool;
                            detRequested = hasDet;
                            //  Log.Line($"basePool exhausted: detonating:{detRequested} - i:{i} - j:{j} - k:{k}[{dbCount - 1}]");
                            if (hitMass > 0)//apply force
                            {
                                var speed = !t.AmmoDef.Const.IsBeamWeapon && t.AmmoDef.Const.DesiredProjectileSpeed > 0 ? t.AmmoDef.Const.DesiredProjectileSpeed : 1;
                                if (Session.IsServer) ApplyProjectileForce(grid, grid.GridIntegerToWorld(rootBlock.Position), hitEnt.Intersection.Direction, (hitMass * speed));
                            }
                        }
                        else
                        {
                            if (primaryDamage)
                            {                       
                                deadBlock = true;
                                basePool -= (float)(blockHp / baseScale);  //check for accuracy?
                                objectsHit++;
                            }
                        }



                        //AOE damage logic applied to aoeDamageFall
                        if (!rootStep && (hasAoe || hasDet) && aoeDamage >= 0 && aoeDamageFall >= 0 && !deadBlock)
                        {
                            if (aoeIsPool)
                            {
                                if (aoeDamage < aoeScaledDmg && blockHp >= aoeDamage)//If remaining pool is less than calc'd damage, only apply remainder of pool
                                {
                                    aoeScaledDmg = aoeDamage;
                                }
                                else if (blockHp <= aoeScaledDmg)
                                {
                                    aoeScaledDmg = (float)blockHp;
                                    deadBlock = true;

                                }
                                aoeDamage -= aoeScaledDmg;
                            }
                            scaledDamage += aoeScaledDmg;//pile in calc'd AOE dmg
                            aoeDmgTally += aoeScaledDmg; //used for absorb
                        }


                        //Kill block if needed, from any source
                        if (deadBlock)
                        {
                            destroyed++;
                            if (IsClient)
                            {
                                _destroyedSlimsClient.Add(block);
                                if (_slimHealthClient.ContainsKey(block))
                                    _slimHealthClient.Remove(block);
                            }
                            else
                                _destroyedSlims.Add(block);
                        }


                        //Apply damage
                        if (canDamage)
                        {
                            //Log.Line($"damage: i:{i} - j:{j} - k:{k}[{dbCount - 1}] - damage:{scaledDamage} of blockHp:{blockHp} - primary:{primaryDamage} - detActive:{detActive} - foundAoeBlocks:{foundAoeBlocks}");
                            try
                            {
                                block.DoDamage(scaledDamage, damageType, sync, null, attackerId);
                            }
                            catch
                            {
                                Log.Line($"[DoDamage crash] detonating:{detRequested} - detActive:{detActive} - i:{i} - j:{j} - offset:{offset} - index:{j - (offset - 1)} - maxDbc:{maxDbc} - scaledDamage:{scaledDamage} - blockHp:{blockHp} - AccumulatedDamage:{block.AccumulatedDamage} - gridMarked:{block.CubeGrid.MarkedForClose}({grid.MarkedForClose})[{rootBlock.CubeGrid.MarkedForClose}] - sameAsRoot:{rootBlock.CubeGrid == block.CubeGrid}");
                                foreach (var l in DamageBlockCache)
                                    l.Clear();

                                earlyExit = true;
                                break;
                            }
                        }
                        else
                        {
                            var realDmg = scaledDamage * gridDamageModifier * blockDmgModifier;

                            if (_slimHealthClient.ContainsKey(block))
                            {
                                if (_slimHealthClient[block] - realDmg > 0)
                                    _slimHealthClient[block] -= realDmg;
                                else
                                    _slimHealthClient.Remove(block);
                            }
                            else if (block.Integrity - realDmg > 0) _slimHealthClient[block] = (float)(blockHp - realDmg);
                        }

                        var endCycle = (!foundAoeBlocks && basePool <= 0) || (!rootStep && (aoeDmgTally >= aoeAbsorb || aoeDamage <= 0)) || objectsHit >= maxObjects;

                        //doneskies
                        if (endCycle)
                        {
                            if (detRequested && !detActive)
                            {
                                //Log.Line($"start det phase: i:{i} - j:{j} - k:{k}[{dbCount - 1}]");
                                detActive = true;
                                --i;
                                dbc?.Clear();
                                break;
                            }

                            if (detRequested) {
                                //Log.Line($"early exit by detActive - aoeDmg:{aoeDamage} <= 0 --- {aoeDmgTally} >= {aoeAbsorb} -- foundAoeBlocks:{foundAoeBlocks} -- primaryExit:{!foundAoeBlocks && basePool <= 0} - objExit:{objectsHit >= maxObjects}");
                                earlyExit = true;
                                break;
                            }

                            if (primaryDamage) {
                                t.BaseDamagePool = 0;
                                t.ObjectsHit = objectsHit;
                            }
                        }
                    }

                    dbc?.Clear();
                }

            }

            //stuff I still haven't looked at yet
            if (rootBlock != null && destroyed > 0)
            {
                var fat = rootBlock.FatBlock;
                MyOrientedBoundingBoxD obb;
                if (fat != null)
                    obb = new MyOrientedBoundingBoxD(fat.Model.BoundingBox, fat.PositionComp.WorldMatrixRef);
                else
                {
                    Vector3 halfExt;
                    rootBlock.ComputeScaledHalfExtents(out halfExt);
                    var blockBox = new BoundingBoxD(-halfExt, halfExt);
                    gridMatrix.Translation = grid.GridIntegerToWorld(rootBlock.Position);
                    obb = new MyOrientedBoundingBoxD(blockBox, gridMatrix);
                }

                var dist = obb.Intersects(ref hitEnt.Intersection);
                if (dist.HasValue)
                    t.Hit.LastHit = hitEnt.Intersection.From + (hitEnt.Intersection.Direction * dist.Value);
            }
            if (!countBlocksAsObjects)
                t.ObjectsHit += 1;

            if (!detRequested)
            {
                t.BaseDamagePool = basePool;
                t.ObjectsHit = objectsHit;
            }

            hitEnt.Blocks.Clear();
        }



        private void DamageDestObj(HitEntity hitEnt, ProInfo info)
        {
            var entity = hitEnt.Entity;
            var destObj = hitEnt.Entity as IMyDestroyableObject;

            if (destObj == null || entity == null) return;

            var directDmgGlobal = Settings.Enforcement.DirectDamageModifer;
            var areaDmgGlobal = Settings.Enforcement.AreaDamageModifer;

            var shieldHeal = info.AmmoDef.DamageScales.Shields.Type == ShieldDef.ShieldType.Heal;
            var sync = MpActive && IsServer;

            var attackerId = info.Target.CoreEntity.EntityId;

            var objHp = destObj.Integrity;
            var integrityCheck = info.AmmoDef.DamageScales.MaxIntegrity > 0;
            if (integrityCheck && objHp > info.AmmoDef.DamageScales.MaxIntegrity || shieldHeal)
            {
                info.BaseDamagePool = 0;
                return;
            }

            var character = hitEnt.Entity as IMyCharacter;
            float damageScale = 1;
            if (info.AmmoDef.Const.VirtualBeams) damageScale *= info.WeaponCache.Hits;
            if (character != null && info.AmmoDef.DamageScales.Characters >= 0)
                damageScale *= info.AmmoDef.DamageScales.Characters;

            var areaEffect = info.AmmoDef.AreaOfDamage;
            var areaDamage = areaEffect.ByBlockHit.Enable ? (info.AmmoDef.Const.AreaEffectDamage * (info.AmmoDef.Const.AreaEffectSize * 0.5f)) * areaDmgGlobal : 0;
            var scaledDamage = (float)((((info.BaseDamagePool * damageScale) * directDmgGlobal) + areaDamage) * info.ShieldResistMod);

            var distTraveled = info.AmmoDef.Const.IsBeamWeapon ? hitEnt.HitDist ?? info.DistanceTraveled : info.DistanceTraveled;

            var fallOff = info.AmmoDef.Const.FallOffScaling && distTraveled > info.AmmoDef.Const.FallOffDistance;
            if (fallOff)
            {
                var fallOffMultipler = (float)MathHelperD.Clamp(1.0 - ((distTraveled - info.AmmoDef.Const.FallOffDistance) / (info.AmmoDef.Const.MaxTrajectory - info.AmmoDef.Const.FallOffDistance)), info.AmmoDef.DamageScales.FallOff.MinMultipler, 1);
                scaledDamage *= fallOffMultipler;
            }

            if (scaledDamage < objHp) info.BaseDamagePool = 0;
            else
            {
                var damageLeft = scaledDamage - objHp;
                var reduction = scaledDamage / damageLeft;

                info.BaseDamagePool *= reduction;
            }

            if (info.DoDamage)
                destObj.DoDamage(scaledDamage, !info.ShieldBypassed ? MyDamageType.Bullet : MyDamageType.Drill, sync, null, attackerId);
            if (info.AmmoDef.Mass > 0)
            {
                var speed = !info.AmmoDef.Const.IsBeamWeapon && info.AmmoDef.Const.DesiredProjectileSpeed > 0 ? info.AmmoDef.Const.DesiredProjectileSpeed : 1;
                if (Session.IsServer) ApplyProjectileForce(entity, entity.PositionComp.WorldAABB.Center, hitEnt.Intersection.Direction, (info.AmmoDef.Mass * speed));
            }
        }

        private static void DamageProjectile(HitEntity hitEnt, ProInfo attacker)
        {
            var pTarget = hitEnt.Projectile;
            if (pTarget == null) return;

            attacker.ObjectsHit++;
            var objHp = pTarget.Info.BaseHealthPool;
            var integrityCheck = attacker.AmmoDef.DamageScales.MaxIntegrity > 0;
            if (integrityCheck && objHp > attacker.AmmoDef.DamageScales.MaxIntegrity) return;

            var damageScale = (float)attacker.AmmoDef.Const.HealthHitModifier;
            if (attacker.AmmoDef.Const.VirtualBeams) damageScale *= attacker.WeaponCache.Hits;
            var scaledDamage = 1 * damageScale;

            var distTraveled = attacker.AmmoDef.Const.IsBeamWeapon ? hitEnt.HitDist ?? attacker.DistanceTraveled : attacker.DistanceTraveled;

            var fallOff = attacker.AmmoDef.Const.FallOffScaling && distTraveled > attacker.AmmoDef.Const.FallOffDistance;
            if (fallOff)
            {
                var fallOffMultipler = (float)MathHelperD.Clamp(1.0 - ((distTraveled - attacker.AmmoDef.Const.FallOffDistance) / (attacker.AmmoDef.Const.MaxTrajectory - attacker.AmmoDef.Const.FallOffDistance)), attacker.AmmoDef.DamageScales.FallOff.MinMultipler, 1);
                scaledDamage *= fallOffMultipler;
            }

            if (scaledDamage >= objHp)
            {

                var safeObjHp = objHp <= 0 ? 0.0000001f : objHp;
                var remaining = (scaledDamage / safeObjHp) / damageScale;
                attacker.BaseDamagePool -= remaining;
                pTarget.Info.BaseHealthPool = 0;
                pTarget.State = Projectile.ProjectileState.Destroy;
                if (attacker.AmmoDef.Const.DetonationDamage > 0 && attacker.AmmoDef.AreaOfDamage.EndOfLife.Enable && attacker.Age >= attacker.AmmoDef.AreaOfDamage.EndOfLife.MinArmingTime)
                    DetonateProjectile(hitEnt, attacker);
            }
            else
            {
                attacker.BaseDamagePool = 0;
                pTarget.Info.BaseHealthPool -= scaledDamage;
                DetonateProjectile(hitEnt, attacker);
            }
        }

        private static void DetonateProjectile(HitEntity hitEnt, ProInfo attacker)
        {
            if (attacker.AmmoDef.Const.DetonationDamage > 0 && attacker.AmmoDef.AreaOfDamage.EndOfLife.Enable && attacker.Age >= attacker.AmmoDef.AreaOfDamage.EndOfLife.MinArmingTime)
            {
                var areaSphere = new BoundingSphereD(hitEnt.Projectile.Position, attacker.AmmoDef.Const.DetonationRadius);
                foreach (var sTarget in attacker.Ai.LiveProjectile)
                {

                    if (areaSphere.Contains(sTarget.Position) != ContainmentType.Disjoint)
                    {

                        var objHp = sTarget.Info.BaseHealthPool;
                        var integrityCheck = attacker.AmmoDef.DamageScales.MaxIntegrity > 0;
                        if (integrityCheck && objHp > attacker.AmmoDef.DamageScales.MaxIntegrity) continue;

                        var damageScale = (float)attacker.AmmoDef.Const.HealthHitModifier;
                        if (attacker.AmmoDef.Const.VirtualBeams) damageScale *= attacker.WeaponCache.Hits;
                        var scaledDamage = 1 * damageScale;

                        if (scaledDamage >= objHp)
                        {
                            sTarget.Info.BaseHealthPool = 0;
                            sTarget.State = Projectile.ProjectileState.Destroy;
                        }
                        else sTarget.Info.BaseHealthPool -= attacker.AmmoDef.Const.Health;
                    }
                }
            }
        }

        private void DamageVoxel(HitEntity hitEnt, ProInfo info)
        {
            var entity = hitEnt.Entity;
            var destObj = hitEnt.Entity as MyVoxelBase;
            if (destObj == null || entity == null || !hitEnt.HitPos.HasValue) return;
            var shieldHeal = info.AmmoDef.DamageScales.Shields.Type == ShieldDef.ShieldType.Heal;
            if (!info.AmmoDef.Const.VoxelDamage || shieldHeal)
            {
                info.BaseDamagePool = 0;
                return;
            }

            var directDmgGlobal = Settings.Enforcement.DirectDamageModifer;
            var detDmgGlobal = Settings.Enforcement.AreaDamageModifer;

            using (destObj.Pin())
            {
                var detonateOnEnd = info.AmmoDef.AreaOfDamage.EndOfLife.Enable && info.Age >= info.AmmoDef.AreaOfDamage.EndOfLife.MinArmingTime;

                info.ObjectsHit++;
                float damageScale = 1 * directDmgGlobal;
                if (info.AmmoDef.Const.VirtualBeams) damageScale *= info.WeaponCache.Hits;

                var scaledDamage = info.BaseDamagePool * damageScale;

                var distTraveled = info.AmmoDef.Const.IsBeamWeapon ? hitEnt.HitDist ?? info.DistanceTraveled : info.DistanceTraveled;
                var fallOff = info.AmmoDef.Const.FallOffScaling && distTraveled > info.AmmoDef.Const.FallOffDistance;

                if (fallOff)
                {
                    var fallOffMultipler = (float)MathHelperD.Clamp(1.0 - ((distTraveled - info.AmmoDef.Const.FallOffDistance) / (info.AmmoDef.Const.MaxTrajectory - info.AmmoDef.Const.FallOffDistance)), info.AmmoDef.DamageScales.FallOff.MinMultipler, 1);
                    scaledDamage *= fallOffMultipler;
                }

                var oRadius = info.AmmoDef.Const.AreaEffectSize;
                var minTestRadius = distTraveled - info.PrevDistanceTraveled;
                var tRadius = oRadius < minTestRadius && !info.AmmoDef.Const.IsBeamWeapon ? minTestRadius : oRadius;
                var objHp = (int)MathHelper.Clamp(MathFuncs.VolumeCube(MathFuncs.LargestCubeInSphere(tRadius)), 5000, double.MaxValue);


                if (tRadius > 5) objHp *= 5;

                if (scaledDamage < objHp)
                {
                    var reduceBy = objHp / scaledDamage;
                    oRadius /= reduceBy;
                    if (oRadius < 1) oRadius = 1;

                    info.BaseDamagePool = 0;
                }
                else
                {
                    info.BaseDamagePool -= objHp;
                    if (oRadius < minTestRadius) oRadius = minTestRadius;
                }
                destObj.PerformCutOutSphereFast(hitEnt.HitPos.Value, (float)(oRadius * info.AmmoDef.Const.VoxelHitModifier), false);

                if (detonateOnEnd && info.BaseDamagePool <= 0)
                {
                    var dRadius = info.AmmoDef.Const.DetonationRadius;
                    var dDamage = info.AmmoDef.Const.DetonationDamage * detDmgGlobal;

                    if (dRadius < 1.5) dRadius = 1.5f;

                    if (info.DoDamage)
                        SUtils.CreateVoxelExplosion(this, dDamage, dRadius, hitEnt.HitPos.Value, hitEnt.Intersection.Direction, info.Target.CoreEntity, destObj, info.AmmoDef, true);
                }
            }
        }

        public void RadiantAoe(IMySlimBlock root, Vector3I localpos, MyCubeGrid grid, double radius, double depth, Vector3D direction, ref int maxDbc, out bool foundSomething) //added depth and angle
        {

            var rootPos = root.Position; //local cube grid
            if (root.Min != root.Max) rootPos = localpos;

            radius *= grid.GridSizeR;  //GridSizeR is 0.4 for LG
            depth *= grid.GridSizeR;
            
            int maxradius = (int)Math.Floor(radius);  //changed to floor, experiment for precision/rounding bias
            int i, j, k;
            int maxdepth = (int)Math.Ceiling(depth*grid.GridSizeR); //Meters to cube conversion.  Round up or down?
            Vector3I min2 = Vector3I.Max(rootPos - maxradius, grid.Min);
            Vector3I max2 = Vector3I.Min(rootPos + maxradius, grid.Max);

            foundSomething = false;

            if (maxdepth < maxradius)
            {
                switch (direction.AbsMaxComponent())//sort out which "face" was hit and coming/going along that axis
                {                   
                    case 0://hit face perp to y
                        if (direction.Y <= 0f)
                        { 
                            min2.Y = rootPos.Y - maxdepth + 1;
                            max2.Y = rootPos.Y + maxdepth - 1;
                        }
                        else
                        { 
                            min2.Y = rootPos.Y + maxdepth - 1;
                            max2.Y = rootPos.Y - maxdepth + 1;
                        }
                        break;

                    case 1://hit face perp to x
                        if (direction.X <= 0f)
                        { 
                            min2.X = rootPos.X - maxdepth + 1;
                            max2.X = rootPos.X + maxdepth - 1;        
                        }
                        else
                        { 
                            min2.X = rootPos.X + maxdepth -1;
                            max2.X = rootPos.X - maxdepth +1;
                        }
                        break;

                    case 2://Hit face is perp to z
                        if (direction.Z <= 0f)
                        {
                            min2.Z = rootPos.Z - maxdepth + 1;
                            max2.Z = rootPos.Z + maxdepth - 1;
                        }
                        else
                        { 
                            min2.Z = rootPos.Z + maxdepth - 1;
                            max2.Z = rootPos.Z - maxdepth + 1;
                        }
                        break;
                }

            }


            var damageBlockCache = DamageBlockCache;

            for (i = min2.X; i <= max2.X; ++i)
            {
                for (j = min2.Y; j <= max2.Y; ++j)
                {
                    for (k = min2.Z; k <= max2.Z; ++k)
                    {
                        var vector3I = new Vector3I(i, j, k);
                        MyCube cube;
                        if (grid.TryGetCube(vector3I, out cube))  
                        {

                            int hitdist = Vector3I.DistanceManhattan(rootPos, vector3I);

                            if (hitdist <= maxradius)
                            {
                                var slim = (IMySlimBlock)cube.CubeBlock;
                                if (slim.IsDestroyed)
                                    continue;

                                var distArray = damageBlockCache[hitdist];

                                var slimmin = slim.Min;
                                var slimmax = slim.Max;
                                if (slimmax != slimmin)//Block larger than 1x1x1
                                {
                                    var hitblkbound = new BoundingBoxI(slimmin, slimmax);
                                    var rootposbound = new BoundingBoxI(rootPos, rootPos);
                                    rootposbound.IntersectWith(ref hitblkbound);
                                    rootposbound.Inflate(1);
                                    if (rootposbound.Contains(vector3I) == ContainmentType.Contains)
                                    {
                                        distArray.Add(slim);
                                        foundSomething = true;
                                        if (hitdist > maxDbc) maxDbc = hitdist;
                                        //slim.Dithering = 0.5f;//temp debug to make "hits" go clear, including the root block
                                    }
   
                                   
                                }
                                else//Happy normal 1x1x1
                                {
                                    distArray.Add(slim);
                                    foundSomething = true;
                                    if (hitdist > maxDbc) maxDbc = hitdist;
                                    //slim.Dithering = 0.5f;//temp debug to make "hits" go clear, including the root block
                                }
                            }
                        }
                    }
                }
            }
        }

        public static void GetBlocksInsideSphereFast(MyCubeGrid grid, ref BoundingSphereD sphere, bool checkDestroyed, List<IMySlimBlock> blocks)
        {
            var radius = sphere.Radius;
            radius *= grid.GridSizeR;
            var center = grid.WorldToGridInteger(sphere.Center);
            var gridMin = grid.Min;
            var gridMax = grid.Max;
            double radiusSq = radius * radius;
            int radiusCeil = (int)Math.Ceiling(radius);
            int i, j, k;
            Vector3I max2 = Vector3I.Min(Vector3I.One * radiusCeil, gridMax - center);
            Vector3I min2 = Vector3I.Max(Vector3I.One * -radiusCeil, gridMin - center);
            for (i = min2.X; i <= max2.X; ++i)
            {
                for (j = min2.Y; j <= max2.Y; ++j)
                {
                    for (k = min2.Z; k <= max2.Z; ++k)
                    {
                        if (i * i + j * j + k * k < radiusSq)
                        {
                            MyCube cube;
                            var vector3I = center + new Vector3I(i, j, k);

                            if (grid.TryGetCube(vector3I, out cube))
                            {
                                var slim = (IMySlimBlock)cube.CubeBlock;
                                if (slim.Position == vector3I)
                                {
                                    if (checkDestroyed && slim.IsDestroyed)
                                        continue;

                                    blocks.Add(slim);

                                }
                            }
                        }
                    }
                }
            }
        }

        public static void ApplyProjectileForce(MyEntity entity, Vector3D intersectionPosition, Vector3 normalizedDirection, float impulse)
        {
            if (entity.Physics == null || !entity.Physics.Enabled || entity.Physics.IsStatic || entity.Physics.Mass / impulse > 500)
                return;
            entity.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_IMPULSE_AND_WORLD_ANGULAR_IMPULSE, normalizedDirection * impulse, intersectionPosition, Vector3.Zero);
        }

        private bool HitDoor(HitEntity hitEnt, MyDoorBase door)
        {
            var ray = new RayD(ref hitEnt.Intersection.From, ref hitEnt.Intersection.Direction);
            var rayHit = ray.Intersects(door.PositionComp.WorldVolume);
            if (rayHit != null)
            {
                var hitPos = hitEnt.Intersection.From + (hitEnt.Intersection.Direction * (rayHit.Value + 0.25f));
                IHitInfo hitInfo;
                if (Physics.CastRay(hitPos, hitEnt.Intersection.To, out hitInfo, 15))
                {
                    var obb = new MyOrientedBoundingBoxD(door.PositionComp.LocalAABB, door.PositionComp.WorldMatrixRef);

                    var sphere = new BoundingSphereD(hitInfo.Position + (hitEnt.Intersection.Direction * 0.15f), 0.01f);
                    if (obb.Intersects(ref sphere))
                        return true;
                }
            }
            return false;
        }

        private bool RayAccuracyCheck(HitEntity hitEnt, IMySlimBlock block)
        {
            BoundingBoxD box;
            block.GetWorldBoundingBox(out box);
            var ray = new RayD(ref hitEnt.Intersection.From, ref hitEnt.Intersection.Direction);
            var rayHit = ray.Intersects(box);
            if (rayHit != null)
            {
                var hitPos = hitEnt.Intersection.From + (hitEnt.Intersection.Direction * (rayHit.Value - 0.1f));
                IHitInfo hitInfo;
                if (Physics.CastRay(hitPos, hitEnt.Intersection.To, out hitInfo, 15))
                {
                    var hit = (MyEntity)hitInfo.HitEntity;
                    var hitPoint = hitInfo.Position + (hitEnt.Intersection.Direction * 0.1f);
                    var rayHitTarget = box.Contains(hitPoint) != ContainmentType.Disjoint && hit == block.CubeGrid;
                    return rayHitTarget;
                }
            }
            return false;
        }
    }
}
