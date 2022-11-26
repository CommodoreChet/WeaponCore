﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using CoreSystems.Platform;
using CoreSystems.Projectiles;
using Sandbox.Game.Entities;
using Sandbox.ModAPI.Ingame;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;
using static CoreSystems.Support.WeaponDefinition;
using static CoreSystems.Support.WeaponDefinition.TargetingDef;
using static CoreSystems.Support.WeaponDefinition.TargetingDef.BlockTypes;
using static CoreSystems.Support.WeaponDefinition.AmmoDef;
using static CoreSystems.Platform.Weapon.ApiShootRequest;
using IMyWarhead = Sandbox.ModAPI.IMyWarhead;

namespace CoreSystems.Support
{
    public partial class Ai
    {
        internal static void AcquireTarget(Weapon w, bool forceFocus, MyEntity targetEntity = null)
        {
            var foundTarget = false;

            if (w.PosChangedTick != w.Comp.Session.SimulationCount) 
                w.UpdatePivotPos();

            var comp = w.Comp;
            var masterAi = w.Comp.MasterAi;
            var mOverrides = comp.MasterOverrides;
            var cMode = mOverrides.Control;

            FakeTarget.FakeWorldTargetInfo fakeInfo = null;
            if (cMode == ProtoWeaponOverrides.ControlModes.Auto || cMode == ProtoWeaponOverrides.ControlModes.Painter && !w.Comp.PainterMode)
            {
                w.AimCone.ConeDir = w.MyPivotFwd;
                w.AimCone.ConeTip = w.BarrelOrigin + (w.MyPivotFwd * w.MuzzleDistToBarrelCenter);
                var request = w.ShootRequest;
                var projectileRequest = request.Type == TargetType.Projectile;
                var pCount = masterAi.LiveProjectile.Count;
                var shootProjectile = pCount > 0 && (w.System.TrackProjectile || projectileRequest || w.Comp.Ai.ControlComp != null) && mOverrides.Projectiles;
                var projectilesFirst = !forceFocus && shootProjectile && w.System.Values.Targeting.Threats.Length > 0 && w.System.Values.Targeting.Threats[0] == Threat.Projectiles;
                var projectilesOnly =  projectileRequest || w.ProjectilesNear && !w.Target.TargetChanged && w.Comp.Session.Count != w.Acquire.SlotId && !forceFocus;
                var checkObstructions = w.System.ScanNonThreats && masterAi.Obstructions.Count > 0;
                
                if (!projectilesFirst && w.System.TrackTopMostEntities && !projectilesOnly && !w.System.NonThreatsOnly)
                    foundTarget = AcquireTopMostEntity(w, mOverrides, forceFocus, targetEntity);
                else if (!forceFocus && shootProjectile)
                    foundTarget = AcquireProjectile(w, request.ProjectileId);

                if (projectilesFirst && !foundTarget && !projectilesOnly && !w.System.NonThreatsOnly)
                    foundTarget = AcquireTopMostEntity(w, mOverrides, false, targetEntity);

                if (!foundTarget && checkObstructions)
                {
                    foundTarget = AcquireObstruction(w, mOverrides);
                }
            }
            else if (!w.System.ScanTrackOnly && w.ValidFakeTargetInfo(w.Comp.Data.Repo.Values.State.PlayerId, out fakeInfo))
            {
                Vector3D predictedPos;
                if (Weapon.CanShootTarget(w, ref fakeInfo.WorldPosition, fakeInfo.LinearVelocity, fakeInfo.Acceleration, out predictedPos, false, null, MathFuncs.DebugCaller.CanShootTarget1))
                {
                    w.Target.SetFake(w.Comp.Session.Tick, predictedPos, w.MyPivotPos);
                    if (w.ActiveAmmoDef.AmmoDef.Trajectory.Guidance != TrajectoryDef.GuidanceType.None || !w.MuzzleHitSelf())
                        foundTarget = true;
                }
            }

            if (!foundTarget)
            {
                if (w.Target.CurrentState == Target.States.Acquired && w.Acquire.IsSleeping && w.Acquire.Monitoring && w.System.Session.AcqManager.MonitorState.Remove(w.Acquire))
                    w.Acquire.Monitoring = false;

                if (w.NewTarget.CurrentState != Target.States.NoTargetsSeen) 
                    w.NewTarget.Reset(w.Comp.Session.Tick, Target.States.NoTargetsSeen);
                
                if (w.Target.CurrentState != Target.States.NoTargetsSeen)
                    w.Target.Reset(w.Comp.Session.Tick, Target.States.NoTargetsSeen, fakeInfo == null);

                w.LastBlockCount = masterAi.BlockCount;
            }
            else 
                w.WakeTargets();
        }

        private static bool AcquireTopMostEntity(Weapon w, ProtoWeaponOverrides overRides, bool attemptReset = false, MyEntity targetEntity = null)
        {
            var s = w.System;
            var comp = w.Comp;
            var ai = comp.MasterAi;
            TargetInfo gridInfo = null;
            var forceTarget = false;
            if (targetEntity != null)
            {
                if (ai.Targets.TryGetValue(targetEntity, out gridInfo))
                {
                    forceTarget = true;
                }
            }

            var ammoDef = w.ActiveAmmoDef.AmmoDef;
            var aConst = ammoDef.Const;
            var attackNeutrals = overRides.Neutrals || w.System.ScanTrackOnly;
            var attackFriends = overRides.Friendly || w.System.ScanTrackOnly;
            var attackNoOwner = overRides.Unowned || w.System.ScanTrackOnly;
            var forceFoci = overRides.FocusTargets || w.System.ScanTrackOnly;
            var session = comp.Session;
            session.TargetRequests++;
            var weaponPos = w.BarrelOrigin + (w.MyPivotFwd * w.MuzzleDistToBarrelCenter);

            var target = w.NewTarget;
            var accelPrediction = (int)s.Values.HardPoint.AimLeadingPrediction > 1;
            var minRadius = overRides.MinSize * 0.5f;
            var maxRadius = overRides.MaxSize * 0.5f;
            var minTargetRadius = minRadius > 0 ? minRadius : s.MinTargetRadius;
            var maxTargetRadius = maxRadius < s.MaxTargetRadius ? maxRadius : s.MaxTargetRadius;

            var moveMode = overRides.MoveMode;
            var movingMode = moveMode == ProtoWeaponOverrides.MoveModes.Moving;
            var fireOnStation = moveMode == ProtoWeaponOverrides.MoveModes.Any || moveMode == ProtoWeaponOverrides.MoveModes.Moored;
            var stationOnly = moveMode == ProtoWeaponOverrides.MoveModes.Moored;
            BoundingSphereD waterSphere = new BoundingSphereD(Vector3D.Zero, 1f);
            WaterData water = null;
            if (session.WaterApiLoaded && !ammoDef.IgnoreWater && ai.InPlanetGravity && ai.MyPlanet != null && session.WaterMap.TryGetValue(ai.MyPlanet.EntityId, out water))
                waterSphere = new BoundingSphereD(ai.MyPlanet.PositionComp.WorldAABB.Center, water.MinRadius);

            int offset = 0;
            w.FoundTopMostTarget = false;

            var rootConstruct = ai.Construct.RootAi.Construct;
            var focusGrid = rootConstruct.LastFocusEntity as MyCubeGrid;
            var lastFocusGrid = rootConstruct.LastFocusEntityChecked as MyCubeGrid;

            var predefinedThreatCollection = rootConstruct.RootAi.ThreatCollection;
            if (rootConstruct.HadFocus && !w.System.ScanTrackOnly) {
                if (focusGrid != null) {
                    if (predefinedThreatCollection.Count == 0 || session.Tick - rootConstruct.LastFocusConstructTick > 180 || lastFocusGrid == null || !focusGrid.IsSameConstructAs(lastFocusGrid)) {
                        rootConstruct.LastFocusEntityChecked = focusGrid;
                        rootConstruct.LastFocusConstructTick = session.Tick;
                        session.GetSortedConstructCollection(ai, focusGrid);
                    }
                    offset = predefinedThreatCollection.Count;
                }
                else if (rootConstruct.LastFocusEntity != null)
                    offset = 1;
            }
            else if (w.System.SlaveToScanner && rootConstruct.GetExportedCollection(w, Constructs.ScanType.Threats))
            {
                Log.Line($"collection was not populated");
                offset = predefinedThreatCollection.Count;
            }

            var numOfTargets = ai.SortedTargets.Count;
            var adjTargetCount = forceFoci && offset > 0 ? offset : numOfTargets + offset;

            var deck = GetDeck(ref session.TargetDeck, 0, numOfTargets, w.System.Values.Targeting.TopTargets, ref w.TargetData.WeaponRandom.AcquireRandom);
            for (int x = 0; x < adjTargetCount; x++)
            {
                var focusTarget = offset > 0 && x < offset;
                var lastOffset = offset - 1;
                if (!focusTarget && (attemptReset || aConst.SkipAimChecks)) 
                    break;

                TargetInfo info;
                if (focusTarget && predefinedThreatCollection.Count > 0)
                    info = predefinedThreatCollection[x];
                else if (focusTarget)
                    ai.Targets.TryGetValue(rootConstruct.LastFocusEntity, out info);
                else 
                    info = ai.SortedTargets[deck[x - offset]];

                if (info?.Target == null || info.Target.MarkedForClose)
                    continue;
                    

                if (forceTarget && !focusTarget) 
                    info = gridInfo;
                else if (focusTarget && !attackFriends && info.EntInfo.Relationship == MyRelationsBetweenPlayerAndBlock.Friends)
                    continue;

                var grid = info.Target as MyCubeGrid;

                if (offset > 0 && x > lastOffset && (grid != null && focusGrid != null && grid.IsSameConstructAs(focusGrid)) || !attackNeutrals && info.EntInfo.Relationship == MyRelationsBetweenPlayerAndBlock.Neutral || !attackNoOwner && info.EntInfo.Relationship == MyRelationsBetweenPlayerAndBlock.NoOwnership)
                    continue;

                Weapon.TargetOwner tOwner;
                if (w.System.UniqueTargetPerWeapon && w.Comp.ActiveTargets.TryGetValue(info.Target, out tOwner) && tOwner.Weapon != w)
                    continue;

                if (w.System.ScanTrackOnly && !ValidScanEntity(w, info.EntInfo, info.Target, true))
                    continue;

                if (movingMode && info.VelLenSqr < 1 || !fireOnStation && info.IsStatic || stationOnly && !info.IsStatic)
                    continue;

                var character = info.Target as IMyCharacter;

                var targetRadius = character != null ? info.TargetRadius * 5 : info.TargetRadius;
                if (targetRadius < minTargetRadius || info.TargetRadius > maxTargetRadius && maxTargetRadius < 8192 || !focusTarget && info.OffenseRating <= 0) continue;

                var targetCenter = info.Target.PositionComp.WorldAABB.Center;
                var targetDistSqr = Vector3D.DistanceSquared(targetCenter, weaponPos);

                if (targetDistSqr > (w.MaxTargetDistance + info.TargetRadius) * (w.MaxTargetDistance + info.TargetRadius) || targetDistSqr < w.MinTargetDistanceSqr) continue;

                if (water != null) {
                    if (new BoundingSphereD(ai.MyPlanet.PositionComp.WorldAABB.Center, water.MinRadius).Contains(new BoundingSphereD(targetCenter, targetRadius)) == ContainmentType.Contains)
                        continue;
                }

                session.TargetChecks++;
                Vector3D targetLinVel = info.Target.Physics?.LinearVelocity ?? Vector3D.Zero;
                Vector3D targetAccel = accelPrediction ? info.Target.Physics?.LinearAcceleration ?? Vector3D.Zero : Vector3.Zero;
                Vector3D predictedPos;
                if (w.System.TargetGridCenter)
                {
                    if (!Weapon.CanShootTarget(w, ref targetCenter, targetLinVel, targetAccel, out predictedPos, false, null, MathFuncs.DebugCaller.CanShootTarget2)) continue;
                    double rayDist;
                    Vector3D.Distance(ref weaponPos, ref targetCenter, out rayDist);
                    var shortDist = rayDist;
                    var origDist = rayDist;
                    var topEntId = info.Target.GetTopMostParent().EntityId;
                    target.Set(info.Target, targetCenter, shortDist, origDist, topEntId);
                    target.TransferTo(w.Target, comp.Session.Tick);
                    if (w.Target.TargetState == Target.TargetStates.IsEntity)
                        ai.Session.NewThreat(w);

                    return true;
                }

                if (info.IsGrid)
                {
                    if (!s.TrackGrids || !overRides.Grids || (!overRides.LargeGrid && info.LargeGrid) || (!overRides.SmallGrid && !info.LargeGrid) || !focusTarget && info.FatCount < 2) continue;
                    session.CanShoot++;
                    Vector3D newCenter;
                    if (!w.TurretController && !w.RotorTurretTracking)
                    {

                        var validEstimate = true;
                        newCenter = w.System.Prediction != HardPointDef.Prediction.Off && (!aConst.IsBeamWeapon && aConst.DesiredProjectileSpeed > 0) ? Weapon.TrajectoryEstimation(w, targetCenter, targetLinVel, targetAccel, Vector3D.Zero, out validEstimate) : targetCenter;
                        var targetSphere = info.Target.PositionComp.WorldVolume;
                        targetSphere.Center = newCenter;

                        if (!validEstimate || (!aConst.SkipAimChecks || w.System.LockOnFocus) && !MathFuncs.TargetSphereInCone(ref targetSphere, ref w.AimCone)) continue;
                    }
                    else if (!Weapon.CanShootTargetObb(w, info.Target, targetLinVel, targetAccel, out newCenter)) continue;

                    if (ai.FriendlyShieldNear)
                    {
                        var targetDir = newCenter - weaponPos;
                        if (w.HitFriendlyShield(weaponPos, newCenter, targetDir))
                            continue;
                    }

                    w.FoundTopMostTarget = true;

                    if (!AcquireBlock(w, target, info,  ref waterSphere, ref w.XorRnd, null, !focusTarget)) continue;
                    
                    target.TransferTo(w.Target, comp.Session.Tick);
                    if (w.Target.TargetState == Target.TargetStates.IsEntity)
                        ai.Session.NewThreat(w);

                    return true;
                }

                var meteor = info.Target as MyMeteor;
                if (meteor != null && (!s.TrackMeteors || !overRides.Meteors)) continue;
                
                if (character != null && (!overRides.Biologicals || character.IsDead || character.Integrity <= 0 || session.AdminMap.ContainsKey(character))) continue;

                
                if (!Weapon.CanShootTarget(w, ref targetCenter, targetLinVel, targetAccel, out predictedPos, true, info.Target, MathFuncs.DebugCaller.CanShootTarget3)) continue;
                
                if (ai.FriendlyShieldNear)
                {
                    var targetDir = predictedPos - weaponPos;
                    if (w.HitFriendlyShield(weaponPos, predictedPos, targetDir))
                        continue;
                }
                
                session.TopRayCasts++;

                if (w.LastHitInfo?.HitEntity != null && (!w.System.Values.HardPoint.Other.MuzzleCheck || !w.MuzzleHitSelf()))
                {
                    TargetInfo hitInfo;
                    if (w.LastHitInfo.HitEntity == info.Target || ai.Targets.TryGetValue((MyEntity)w.LastHitInfo.HitEntity, out hitInfo) && (hitInfo.EntInfo.Relationship == MyRelationsBetweenPlayerAndBlock.Enemies || hitInfo.EntInfo.Relationship == MyRelationsBetweenPlayerAndBlock.Neutral || hitInfo.EntInfo.Relationship == MyRelationsBetweenPlayerAndBlock.NoOwnership))
                    {
                        double rayDist;
                        Vector3D.Distance(ref weaponPos, ref targetCenter, out rayDist);
                        var shortDist = rayDist * (1 - w.LastHitInfo.Fraction);
                        var origDist = rayDist * w.LastHitInfo.Fraction;
                        var topEntId = info.Target.GetTopMostParent().EntityId;
                        target.Set(info.Target, w.LastHitInfo.Position, shortDist, origDist, topEntId);
                        target.TransferTo(w.Target, comp.Session.Tick);
                        
                        w.FoundTopMostTarget = true;

                        if (w.Target.TargetState == Target.TargetStates.IsEntity)
                            ai.Session.NewThreat(w);

                        return true;
                    }
                }
                if (forceTarget) break;
            }

            return attemptReset && w.Target.HasTarget;
        }

        private static bool AcquireObstruction(Weapon w, ProtoWeaponOverrides overRides)
        {
            var s = w.System;
            var comp = w.Comp;
            var ai = comp.MasterAi;
            var ammoDef = w.ActiveAmmoDef.AmmoDef;
            var aConst = ammoDef.Const;
            var session = comp.Session;
            session.TargetRequests++;

            var weaponPos = w.BarrelOrigin + (w.MyPivotFwd * w.MuzzleDistToBarrelCenter);

            var target = w.NewTarget;
            var accelPrediction = (int)s.Values.HardPoint.AimLeadingPrediction > 1;
            var minRadius = overRides.MinSize * 0.5f;
            var maxRadius = overRides.MaxSize * 0.5f;
            var minTargetRadius = minRadius > 0 ? minRadius : s.MinTargetRadius;
            var maxTargetRadius = maxRadius < s.MaxTargetRadius ? maxRadius : s.MaxTargetRadius;

            var moveMode = overRides.MoveMode;
            var movingMode = moveMode == ProtoWeaponOverrides.MoveModes.Moving;
            var fireOnStation = moveMode == ProtoWeaponOverrides.MoveModes.Any || moveMode == ProtoWeaponOverrides.MoveModes.Moored;
            var stationOnly = moveMode == ProtoWeaponOverrides.MoveModes.Moored;

            w.FoundTopMostTarget = false;

            if (w.System.SlaveToScanner)
            {
                if (!w.Comp.Ai.Construct.RootAi.Construct.GetExportedCollection(w, Constructs.ScanType.NonThreats))
                    Log.Line($"couldnt export nonthreat collection");
            }

            var collection = !w.System.SlaveToScanner ? ai.Obstructions : ai.NonThreatCollection;
            var numOfTargets = collection.Count;

            var deck = GetDeck(ref session.TargetDeck, 0, numOfTargets, w.System.Values.Targeting.TopTargets, ref w.TargetData.WeaponRandom.AcquireRandom);
            for (int x = 0; x < numOfTargets; x++)
            {
                if (aConst.SkipAimChecks)
                    break;

                var info = collection[deck[x]];


                if (info.Target?.Physics == null || info.Target.MarkedForClose)
                    continue;

                if (!ValidScanEntity(w, info.EntInfo, info.Target))
                    continue;

                var grid = info.Target as MyCubeGrid;
                var character = info.Target as IMyCharacter;

                if (movingMode && !info.Target.Physics.IsMoving || !fireOnStation && info.Target.Physics.IsStatic || stationOnly && !info.Target.Physics.IsStatic)
                    continue;


                var targetRadius = character != null ? info.Target.PositionComp.LocalVolume.Radius * 5 : info.Target.PositionComp.LocalVolume.Radius;
                if (targetRadius < minTargetRadius || targetRadius > maxTargetRadius && maxTargetRadius < 8192) continue;

                var targetCenter = info.Target.PositionComp.WorldAABB.Center;
                var targetDistSqr = Vector3D.DistanceSquared(targetCenter, weaponPos);

                if (targetDistSqr > (w.MaxTargetDistance + targetRadius) * (w.MaxTargetDistance + targetRadius) || targetDistSqr < w.MinTargetDistanceSqr) continue;

                session.TargetChecks++;
                Vector3D targetLinVel = info.Target.Physics?.LinearVelocity ?? Vector3D.Zero;
                Vector3D targetAccel = accelPrediction ? info.Target.Physics?.LinearAcceleration ?? Vector3D.Zero : Vector3.Zero;
                double rayDist;

                if (grid != null)
                {
                    if (!overRides.Grids || (!overRides.LargeGrid && info.LargeGrid) || (!overRides.SmallGrid && !info.LargeGrid) || grid.CubeBlocks.Count == 0) continue;
                    session.CanShoot++;
                    Vector3D newCenter;

                    if (!w.TurretController && !w.RotorTurretTracking)
                    {

                        var validEstimate = true;
                        newCenter = w.System.Prediction != HardPointDef.Prediction.Off && (!aConst.IsBeamWeapon && aConst.DesiredProjectileSpeed > 0) ? Weapon.TrajectoryEstimation(w, targetCenter, targetLinVel, targetAccel, Vector3D.Zero, out validEstimate) : targetCenter;
                        var targetSphere = info.Target.PositionComp.WorldVolume;
                        targetSphere.Center = newCenter;

                        if (!validEstimate || (!aConst.SkipAimChecks || w.System.LockOnFocus) && !MathFuncs.TargetSphereInCone(ref targetSphere, ref w.AimCone)) continue;
                    }
                    else if (!Weapon.CanShootTargetObb(w, info.Target, targetLinVel, targetAccel, out newCenter)) continue;

                    w.FoundTopMostTarget = true;
                    var pos = grid.PositionComp.WorldVolume.Center;
                    Vector3D.Distance(ref weaponPos, ref pos, out rayDist);
                    target.Set(grid, pos, rayDist, rayDist, grid.EntityId);

                    target.TransferTo(w.Target, comp.Session.Tick);
                    if (w.Target.TargetState == Target.TargetStates.IsEntity)
                        ai.Session.NewThreat(w);

                    return true;
                }

                var meteor = info.Target as MyMeteor;
                if (meteor != null && (!s.TrackMeteors || !overRides.Meteors)) continue;

                if (character != null && (false && !overRides.Biologicals || character.IsDead || character.Integrity <= 0)) continue;

                Vector3D predictedPos;
                if (!Weapon.CanShootTarget(w, ref targetCenter, targetLinVel, targetAccel, out predictedPos, true, info.Target, MathFuncs.DebugCaller.CanShootTarget4)) continue;

                session.TopRayCasts++;

                Vector3D.Distance(ref weaponPos, ref targetCenter, out rayDist);
                var shortDist = rayDist;
                var origDist = rayDist;
                var topEntId = info.Target.GetTopMostParent().EntityId;
                target.Set(info.Target, targetCenter, shortDist, origDist, topEntId);
                target.TransferTo(w.Target, comp.Session.Tick);

                w.FoundTopMostTarget = true;

                if (w.Target.TargetState == Target.TargetStates.IsEntity)
                    ai.Session.NewThreat(w);

                return true;
            }

            return w.Target.HasTarget;
        }

        internal static bool AcquireProjectile(Weapon w, ulong id = ulong.MaxValue)
        {
            var ai = w.Comp.MasterAi;
            var system = w.System;
            var s = ai.Session;
            var physics = system.Session.Physics;
            var target = w.NewTarget;
            var weaponPos = w.BarrelOrigin;

            var collection = ai.GetProCache(w);

            var lockedOnly = w.System.Values.Targeting.LockedSmartOnly;
            var smartOnly = w.System.Values.Targeting.IgnoreDumbProjectiles;

            int index = int.MinValue;
            if (id != ulong.MaxValue) {
                if (!GetProjectileIndex(collection, id, out index)) 
                    return false;
            }
            else if (system.ClosestFirst)
            {
                int length = collection.Count;
                for (int h = length / 2; h > 0; h /= 2)
                {
                    for (int i = h; i < length; i += 1)
                    {
                        var tempValue = collection[i];
                        double temp;
                        Vector3D.DistanceSquared(ref collection[i].Position, ref weaponPos, out temp);

                        int j;
                        for (j = i; j >= h && Vector3D.DistanceSquared(collection[j - h].Position, weaponPos) > temp; j -= h)
                            collection[j] = collection[j - h];

                        collection[j] = tempValue;
                    }
                }
            }

            var numOfTargets = index < -1 ? collection.Count : index < 0 ? 0 : 1;

            int[] deck = null;
            if (index < -1)
            {
                var numToRandomize = system.ClosestFirst ? w.System.Values.Targeting.TopTargets : numOfTargets;
                deck = GetDeck(ref s.TargetDeck, 0, numOfTargets, numToRandomize, ref w.TargetData.WeaponRandom.AcquireRandom);
            }

            for (int x = 0; x < numOfTargets; x++)
            {
                var card = index < -1 ? deck[x] : index;
                var lp = collection[card];
                var lpaConst = lp.Info.AmmoDef.Const;
                var smart = lpaConst.IsDrone || lpaConst.IsSmart;
                var cube = lp.Info.Target.TargetEntity as MyCubeBlock;
                Weapon.TargetOwner tOwner;
                if (smartOnly && !smart || lockedOnly && (!smart || cube != null && w.Comp.IsBlock && cube.CubeGrid.IsSameConstructAs(w.Comp.Ai.GridEntity)) || lp.MaxSpeed > system.MaxTargetSpeed || lp.MaxSpeed <= 0 || lp.State != Projectile.ProjectileState.Alive || Vector3D.DistanceSquared(lp.Position, weaponPos) > w.MaxTargetDistanceSqr || Vector3D.DistanceSquared(lp.Position, weaponPos) < w.MinTargetDistanceBufferSqr || w.System.UniqueTargetPerWeapon && w.Comp.ActiveTargets.TryGetValue(lp, out tOwner) && tOwner.Weapon != w) continue;


                var lpAccel = lp.Velocity - lp.PrevVelocity;
                if (s.DebugMod && double.IsNaN(lpAccel.X))
                {
                    Log.Line($"projectile was NaN: {lp.Info.AmmoDef.AmmoRound}");
                    continue;
                }
                Vector3D predictedPos;
                if (Weapon.CanShootTarget(w, ref lp.Position, lp.Velocity, lpAccel, out predictedPos, false, null, MathFuncs.DebugCaller.CanShootTarget5))
                {
                    var needsCast = false;
                    for (int i = 0; i < ai.Obstructions.Count; i++)
                    {
                        var ent = ai.Obstructions[i].Target;

                        var obsSphere = ent.PositionComp.WorldVolume;

                        var dir = lp.Position - weaponPos;
                        var beam = new RayD(ref weaponPos, ref dir);

                        if (beam.Intersects(obsSphere) != null)
                        {
                            var transform = ent.PositionComp.WorldMatrixRef;
                            var box = ent.PositionComp.LocalAABB;
                            var obb = new MyOrientedBoundingBoxD(box, transform);
                            if (obb.Intersects(ref beam) != null)
                            {
                                needsCast = true;
                                break;
                            }
                        }
                    }

                    if (needsCast)
                    {
                        IHitInfo hitInfo;
                        physics.CastRay(weaponPos, lp.Position, out hitInfo, 15);
                        if (hitInfo?.HitEntity == null && (!w.System.Values.HardPoint.Other.MuzzleCheck || !w.MuzzleHitSelf()))
                        {
                            double hitDist;
                            Vector3D.Distance(ref weaponPos, ref lp.Position, out hitDist);
                            var shortDist = hitDist;
                            var origDist = hitDist;
                            target.Set(null, lp.Position, shortDist, origDist, long.MaxValue, lp);
                            target.TransferTo(w.Target, w.Comp.Session.Tick);
                            return true;
                        }
                    }
                    else
                    {
                        Vector3D? hitInfo;
                        if (ai.AiType == AiTypes.Grid && GridIntersection.BresenhamGridIntersection(ai.GridEntity, ref weaponPos, ref lp.Position, out hitInfo, w.Comp.Cube, ai))
                            continue;

                        double hitDist;
                        Vector3D.Distance(ref weaponPos, ref lp.Position, out hitDist);
                        var shortDist = hitDist;
                        var origDist = hitDist;
                        target.Set(null, lp.Position, shortDist, origDist, long.MaxValue, lp);
                        target.TransferTo(w.Target, w.Comp.Session.Tick);
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool GetProjectileIndex(List<Projectile> collection, ulong id, out int index)
        {
            if (id != ulong.MaxValue && collection.Count > 0)
            {
                for (int i = 0; i < collection.Count; i++)
                {
                    if (collection[i].Info.Id == id)
                    {
                        index = i;
                        return true;
                    }
                }
            }

            index = -1;
            return false;
        }

        internal static bool ReacquireTarget(Projectile p)
        {
            var info = p.Info;
            if (info.CompSceneVersion != info.Weapon.Comp.SceneVersion)
                return false;

            var w = info.Weapon;
            var s = w.System;
            var target = info.Target;
            info.Storage.ChaseAge = info.Age;
            var ai = info.Ai;
            var session = ai.Session;

            var aConst = info.AmmoDef.Const;
            var overRides = w.Comp.Data.Repo.Values.Set.Overrides;
            var attackNeutrals = overRides.Neutrals;
            var attackFriends = overRides.Friendly;
            var attackNoOwner = overRides.Unowned;
            var forceFoci = overRides.FocusTargets;
            var minRadius = overRides.MinSize * 0.5f;
            var maxRadius = overRides.MaxSize * 0.5f;
            var minTargetRadius = minRadius > 0 ? minRadius : s.MinTargetRadius;
            var maxTargetRadius = maxRadius < s.MaxTargetRadius ? maxRadius : s.MaxTargetRadius;
            var moveMode = overRides.MoveMode;
            var movingMode = moveMode == ProtoWeaponOverrides.MoveModes.Moving;
            var fireOnStation = moveMode == ProtoWeaponOverrides.MoveModes.Any || moveMode == ProtoWeaponOverrides.MoveModes.Moored;
            var stationOnly = moveMode == ProtoWeaponOverrides.MoveModes.Moored;
            var acquired = false;
            var lockedToTarget = info.LockOnFireState;
            BoundingSphereD waterSphere = new BoundingSphereD(Vector3D.Zero, 1f);
            WaterData water = null;
            if (s.Session.WaterApiLoaded && !info.AmmoDef.IgnoreWater && ai.InPlanetGravity && ai.MyPlanet != null && s.Session.WaterMap.TryGetValue(ai.MyPlanet.EntityId, out water))
                waterSphere = new BoundingSphereD(ai.MyPlanet.PositionComp.WorldAABB.Center, water.MinRadius);
            TargetInfo alphaInfo = null;
            int offset = 0;
            MyEntity fTarget;
            if (!aConst.OverrideTarget && ai.Construct.Data.Repo.FocusData.Target > 0 && MyEntities.TryGetEntityById(ai.Construct.Data.Repo.FocusData.Target, out fTarget) && ai.Targets.TryGetValue(fTarget, out alphaInfo))
                offset++;


            MyEntity topTarget = null;
            if (lockedToTarget && !aConst.OverrideTarget && target.TargetState == Target.TargetStates.IsEntity)
            {
                topTarget = target.TargetEntity.GetTopMostParent() ?? alphaInfo?.Target;
                if (topTarget != null && topTarget.MarkedForClose)
                    topTarget = null;
            }

            var numOfTargets = ai.SortedTargets.Count;
            var hasOffset = offset > 0;
            var adjTargetCount = forceFoci && hasOffset ? offset : numOfTargets + offset;
            var deck = GetDeck(ref session.TargetDeck, 0, numOfTargets, w.System.Values.Targeting.TopTargets, ref p.Info.Random);

            for (int i = 0; i < adjTargetCount; i++)
            {
                var focusTarget = hasOffset && i < offset;
                var lastOffset = offset - 1;

                TargetInfo tInfo;
                if (i == 0 && alphaInfo != null) tInfo = alphaInfo;
                else tInfo = ai.SortedTargets[deck[i - offset]];

                if (!focusTarget && tInfo.OffenseRating <= 0 || focusTarget && !attackFriends && tInfo.EntInfo.Relationship == MyRelationsBetweenPlayerAndBlock.Friends || tInfo.Target == null || tInfo.Target.MarkedForClose || hasOffset && i > lastOffset && (tInfo.Target == alphaInfo?.Target)) { continue; }

                if (!attackNeutrals && tInfo.EntInfo.Relationship == MyRelationsBetweenPlayerAndBlock.Neutral || !attackNoOwner && tInfo.EntInfo.Relationship == MyRelationsBetweenPlayerAndBlock.NoOwnership) continue;

                if (movingMode && tInfo.VelLenSqr < 1 || !fireOnStation && tInfo.IsStatic || stationOnly && !tInfo.IsStatic)
                    continue;

                var character = tInfo.Target as IMyCharacter;
                if (character != null && (!s.TrackCharacters || !overRides.Biologicals)) continue;

                var meteor = tInfo.Target as MyMeteor;
                if (meteor != null && (!s.TrackMeteors || !overRides.Meteors)) continue;

                var targetPos = tInfo.Target.PositionComp.WorldAABB.Center;

                double distSqr;
                Vector3D.DistanceSquared(ref targetPos, ref p.Position, out distSqr);

                if (distSqr > p.DistanceToTravelSqr)
                    continue;

                var targetRadius = tInfo.Target.PositionComp.LocalVolume.Radius;
                if (targetRadius < minTargetRadius || targetRadius > maxTargetRadius && maxTargetRadius < 8192 || topTarget != null && tInfo.Target != topTarget) continue;
                if (water != null)
                {
                    if (new BoundingSphereD(ai.MyPlanet.PositionComp.WorldAABB.Center, water.MinRadius).Contains(new BoundingSphereD(targetPos, targetRadius)) == ContainmentType.Contains)
                        continue;
                }

                if (tInfo.IsGrid)
                {

                    if (!s.TrackGrids || !overRides.Grids || !focusTarget && tInfo.FatCount < 2 || Obstruction(ref tInfo, ref targetPos, p) || (!overRides.LargeGrid && tInfo.LargeGrid) || (!overRides.SmallGrid && !tInfo.LargeGrid)) continue;

                    if (!AcquireBlock(w, target, tInfo, ref waterSphere, ref info.Random, p, !focusTarget)) continue;
                    acquired = true;
                    break;
                }

                if (Obstruction(ref tInfo, ref targetPos, p))
                    continue;

                var topEntId = tInfo.Target.GetTopMostParent().EntityId;
                target.Set(tInfo.Target, targetPos, 0, 0, topEntId);
                acquired = true;
                break;
            }
            if (!acquired && !lockedToTarget) target.Reset(s.Session.Tick, Target.States.NoTargetsSeen);
            return acquired;
        }

        internal static bool ReAcquireProjectile(Projectile p)
        {
            var info = p.Info;
            var w = info.Weapon;
            var comp = w.Comp;
            var s = w.System;
            var target = info.Target;
            info.Storage.ChaseAge = info.Age;
            var ai = info.Ai;
            var session = ai.Session;
            var physics = s.Session.Physics;
            var weaponPos = p.Position;

            var collection = ai.GetProCache(w);
            var numOfTargets = collection.Count;
            var lockedOnly = s.Values.Targeting.LockedSmartOnly;
            var smartOnly = s.Values.Targeting.IgnoreDumbProjectiles;
            var found = false;
            if (s.ClosestFirst)
            {
                int length = collection.Count;
                for (int h = length / 2; h > 0; h /= 2)
                {
                    for (int i = h; i < length; i += 1)
                    {
                        var tempValue = collection[i];
                        double temp;
                        Vector3D.DistanceSquared(ref collection[i].Position, ref weaponPos, out temp);

                        int j;
                        for (j = i; j >= h && Vector3D.DistanceSquared(collection[j - h].Position, weaponPos) > temp; j -= h)
                            collection[j] = collection[j - h];

                        collection[j] = tempValue;
                    }
                }
            }

            var numToRandomize = s.ClosestFirst ? s.Values.Targeting.TopTargets : numOfTargets;
            if (session.TargetDeck.Length < numOfTargets)
            {
                session.TargetDeck = new int[numOfTargets];
            }

            for (int i = 0; i < numOfTargets; i++)
            {
                var j = i < numToRandomize ? info.Random.Range(0, i + 1) : i;
                session.TargetDeck[i] = session.TargetDeck[j];
                session.TargetDeck[j] = 0 + i;
            }
            var deck = session.TargetDeck;
            for (int x = 0; x < numOfTargets; x++)
            {
                var card = deck[x];
                var lp = collection[card];
                var lpaConst = lp.Info.AmmoDef.Const;
                var smart = lpaConst.IsDrone || lpaConst.IsSmart;

                if (smartOnly && !smart || lockedOnly && !smart || lp.MaxSpeed > s.MaxTargetSpeed || lp.MaxSpeed <= 0 || lp.State != Projectile.ProjectileState.Alive) continue;

                var needsCast = false;
                for (int i = 0; i < ai.Obstructions.Count; i++)
                {
                    var ent = ai.Obstructions[i].Target;

                    var obsSphere = ent.PositionComp.WorldVolume;

                    var dir = lp.Position - weaponPos;
                    var ray = new RayD(ref weaponPos, ref dir);

                    if (ray.Intersects(obsSphere) != null)
                    {
                        var transform = ent.PositionComp.WorldMatrixRef;
                        var box = ent.PositionComp.LocalAABB;
                        var obb = new MyOrientedBoundingBoxD(box, transform);
                        if (obb.Intersects(ref ray) != null)
                        {
                            needsCast = true;
                            break;
                        }
                    }
                }

                if (needsCast)
                {
                    IHitInfo hitInfo;
                    physics.CastRay(weaponPos, lp.Position, out hitInfo, 15);
                    if (hitInfo?.HitEntity == null)
                    {
                        target.Set(null, lp.Position,  0, 0, long.MaxValue, lp);
                        p.TargetPosition = target.Projectile.Position;
                        target.Projectile.Seekers.Add(p);
                        found = true;
                        break;
                    }
                }
                else
                {
                    Vector3D? hitInfo;
                    if (ai.AiType == AiTypes.Grid && GridIntersection.BresenhamGridIntersection(ai.GridEntity, ref weaponPos, ref lp.Position, out hitInfo, comp.CoreEntity, ai))
                        continue;

                    target.Set(null, lp.Position, 0, 0, long.MaxValue, lp);
                    p.TargetPosition = target.Projectile.Position;
                    target.Projectile.Seekers.Add(p);
                    found = true;
                    break;
                }
            }

            return found;
        }
        private static bool AcquireBlock(Weapon w, Target target, TargetInfo info, ref BoundingSphereD waterSphere, ref XorShiftRandomStruct xRnd, Projectile p, bool checkPower = true)
        {
            var system = w.System;
            var s = system.Session;
            if (system.TargetSubSystems)
            {
                var overRides = w.Comp.Data.Repo.Values.Set.Overrides;
                var subSystems = system.Values.Targeting.SubSystems;
                var focusSubSystem = overRides.FocusSubSystem || overRides.FocusSubSystem;

                var targetLinVel = info.Target.Physics?.LinearVelocity ?? Vector3D.Zero;
                var targetAccel = (int)system.Values.HardPoint.AimLeadingPrediction > 1 ? info.Target.Physics?.LinearAcceleration ?? Vector3D.Zero : Vector3.Zero;
                var subSystem = overRides.SubSystem;

                foreach (var blockType in subSystems)
                {
                    var bt = focusSubSystem ? subSystem : blockType;

                    ConcurrentDictionary<BlockTypes, ConcurrentCachingList<MyCubeBlock>> blockTypeMap;
                    system.Session.GridToBlockTypeMap.TryGetValue((MyCubeGrid)info.Target, out blockTypeMap);
                    if (bt != Any && blockTypeMap != null && blockTypeMap[bt].Count > 0)
                    {
                        var subSystemList = blockTypeMap[bt];
                        if (system.ClosestFirst)
                        {
                            if (w.Top5.Count > 0 && (bt != w.LastTop5BlockType || w.Top5[0].CubeGrid != subSystemList[0].CubeGrid))
                                w.Top5.Clear();

                            w.LastTop5BlockType = bt;
                            if (GetClosestHitableBlockOfType(w, subSystemList, target, info, targetLinVel, targetAccel, ref waterSphere, p, checkPower))
                                return true;
                        }
                        else if (FindRandomBlock(w, target, info, subSystemList, ref waterSphere, ref xRnd, p, checkPower)) return true;
                    }

                    if (focusSubSystem) break;
                }

                if (system.OnlySubSystems || focusSubSystem && subSystem != Any) return false;
            }
            TopMap topMap;
            return system.Session.TopEntityToInfoMap.TryGetValue((MyCubeGrid)info.Target, out topMap) && topMap.MyCubeBocks != null && FindRandomBlock(w, target, info, topMap.MyCubeBocks, ref waterSphere, ref xRnd, p, checkPower);
        }

        private static bool FindRandomBlock(Weapon w, Target target, TargetInfo info, ConcurrentCachingList<MyCubeBlock> subSystemList, ref BoundingSphereD waterSphere, ref XorShiftRandomStruct xRnd, Projectile p, bool checkPower = true)
        {
            var totalBlocks = subSystemList.Count;
            var system = w.System;
            var ai = w.Comp.MasterAi;
            var s = ai.Session;

            Vector3D weaponPos;
            if (p != null)
                weaponPos = p.Position;
            else
            {
                var barrelPos = w.BarrelOrigin;
                var targetNormDir = Vector3D.Normalize(info.Target.PositionComp.WorldAABB.Center - barrelPos);
                weaponPos = barrelPos + (targetNormDir * w.MuzzleDistToBarrelCenter);
            }

            var topEnt = info.Target.GetTopMostParent();

            var entSphere = topEnt.PositionComp.WorldVolume;
            var distToEnt = MyUtils.GetSmallestDistanceToSphere(ref weaponPos, ref entSphere);
            var weaponCheck = p == null && !w.ActiveAmmoDef.AmmoDef.Const.SkipAimChecks;
            var topBlocks = system.Values.Targeting.TopBlocks;
            var lastBlocks = topBlocks > 10 && distToEnt < 1000 ? topBlocks : 10;
            var isPriroity = false;
            if (lastBlocks < 250)
            {
                TargetInfo priorityInfo;
                MyEntity fTarget;
                if (ai.Construct.Data.Repo.FocusData.Target > 0 && MyEntities.TryGetEntityById(ai.Construct.Data.Repo.FocusData.Target, out fTarget) && ai.Targets.TryGetValue(fTarget, out priorityInfo) && priorityInfo.Target?.GetTopMostParent() == topEnt)
                {
                    isPriroity = true;
                    lastBlocks = totalBlocks < 250 ? totalBlocks : 250;
                }

            }

            if (totalBlocks < lastBlocks) lastBlocks = totalBlocks;

            var deck = GetDeck(ref s.BlockDeck, 0, totalBlocks, topBlocks, ref xRnd);

            var physics = s.Physics;
            var iGrid = topEnt as IMyCubeGrid;
            var gridPhysics = iGrid?.Physics;
            Vector3D targetLinVel = gridPhysics?.LinearVelocity ?? Vector3D.Zero;
            Vector3D targetAccel = (int)system.Values.HardPoint.AimLeadingPrediction > 1 ? info.Target.Physics?.LinearAcceleration ?? Vector3D.Zero : Vector3.Zero;
            var foundBlock = false;
            var blocksChecked = 0;
            var blocksSighted = 0;
            var hitTmpList = s.HitInfoTmpList;
            for (int i = 0; i < totalBlocks; i++)
            {
                if (weaponCheck && (blocksChecked > lastBlocks || isPriroity && (blocksSighted > 100 || blocksChecked > 50 && s.RandomRayCasts > 500 || blocksChecked > 25 && s.RandomRayCasts > 1000)))
                    break;

                var card = deck[i];
                var block = subSystemList[card];

                if (block.MarkedForClose || checkPower && !(block is IMyWarhead) && !block.IsWorking) continue;

                s.BlockChecks++;

                var blockPos = block.CubeGrid.GridIntegerToWorld(block.Position);

                double rayDist;
                if (weaponCheck)
                {
                    double distSqr;
                    Vector3D.DistanceSquared(ref blockPos, ref weaponPos, out distSqr);
                    if (distSqr > w.MaxTargetDistanceSqr || distSqr < w.MinTargetDistanceSqr)
                        continue;

                    blocksChecked++;
                    ai.Session.CanShoot++;

                    Vector3D predictedPos;
                    if (!Weapon.CanShootTarget(w, ref blockPos, targetLinVel, targetAccel, out predictedPos, w.RotorTurretTracking, null, MathFuncs.DebugCaller.CanShootTarget6)) continue;

                    if (s.WaterApiLoaded && waterSphere.Radius > 2 && waterSphere.Contains(predictedPos) != ContainmentType.Disjoint)
                        continue;

                    blocksSighted++;

                    s.RandomRayCasts++;

                    var targetDir = blockPos - w.BarrelOrigin;
                    Vector3D targetDirNorm;
                    Vector3D.Normalize(ref targetDir, out targetDirNorm);
                    var testPos = w.BarrelOrigin + (targetDirNorm * w.MuzzleDistToBarrelCenter);

                    IHitInfo iHitInfo = null;
                    physics.CastRay(testPos, blockPos, hitTmpList, 15);
                    var skip = false;
                    for (int j = 0; j < hitTmpList.Count; j++)
                    {
                        var hitInfo = hitTmpList[j];

                        var entity = hitInfo.HitEntity as MyEntity;
                        var character = entity as IMyCharacter;

                        TargetInfo otherInfo;
                        var enemyCharacter = character != null && (!ai.Targets.TryGetValue(entity, out otherInfo) || !(otherInfo.EntInfo.Relationship == MyRelationsBetweenPlayerAndBlock.Enemies || otherInfo.EntInfo.Relationship == MyRelationsBetweenPlayerAndBlock.Neutral || otherInfo.EntInfo.Relationship == MyRelationsBetweenPlayerAndBlock.NoOwnership));

                        if (entity == null || entity is MyVoxelBase || character != null && !enemyCharacter)
                        {
                            skip = true;
                            break;
                        }

                        var topHitEntity = entity.GetTopMostParent();
                        var hitGrid = topHitEntity as MyCubeGrid;

                        if (hitGrid != null)
                        {
                            if (hitGrid.MarkedForClose || (hitGrid != block.CubeGrid && ai.AiType == AiTypes.Grid && hitGrid.IsSameConstructAs(ai.GridEntity)) || !hitGrid.DestructibleBlocks || hitGrid.Immune || hitGrid.GridGeneralDamageModifier <= 0) continue;
                            var isTarget = hitGrid == block.CubeGrid || hitGrid.IsSameConstructAs(block.CubeGrid);

                            var bigOwners = hitGrid.BigOwners;
                            var noOwner = bigOwners.Count == 0;

                            var validTarget = isTarget || noOwner || ai.Targets.TryGetValue(topHitEntity, out otherInfo) && (otherInfo.EntInfo.Relationship == MyRelationsBetweenPlayerAndBlock.Enemies || otherInfo.EntInfo.Relationship == MyRelationsBetweenPlayerAndBlock.Neutral || otherInfo.EntInfo.Relationship == MyRelationsBetweenPlayerAndBlock.NoOwnership);

                            skip = !validTarget;

                            if (isTarget)
                                iHitInfo = hitInfo;

                            if (isTarget || skip)
                                break;
                        }
                    }

                    if (skip || iHitInfo == null)
                        continue;

                    Vector3D.Distance(ref weaponPos, ref blockPos, out rayDist);
                    var shortDist = rayDist * (1 - iHitInfo.Fraction);
                    var origDist = rayDist * iHitInfo.Fraction;
                    target.Set(block, iHitInfo.Position, shortDist, origDist, block.GetTopMostParent().EntityId);
                    foundBlock = true;
                    break;
                }

                Vector3D.Distance(ref weaponPos, ref blockPos, out rayDist);
                target.Set(block, block.PositionComp.WorldAABB.Center, rayDist, rayDist, block.GetTopMostParent().EntityId);
                foundBlock = true;
                break;
            }
            return foundBlock;
        }

        internal static bool GetClosestHitableBlockOfType(Weapon w, ConcurrentCachingList<MyCubeBlock> cubes, Target target, TargetInfo info, Vector3D targetLinVel, Vector3D targetAccel, ref BoundingSphereD waterSphere, Projectile p, bool checkPower = true)
        {
            var minValue = double.MaxValue;
            var minValue0 = double.MaxValue;
            var minValue1 = double.MaxValue;
            var minValue2 = double.MaxValue;
            var minValue3 = double.MaxValue;

            MyCubeBlock newEntity = null;
            MyCubeBlock newEntity0 = null;
            MyCubeBlock newEntity1 = null;
            MyCubeBlock newEntity2 = null;
            MyCubeBlock newEntity3 = null;

            var aimOrigin = p?.Position ?? w.MyPivotPos;

            Vector3D weaponPos;
            if (p != null)
                weaponPos = p.Position;
            else
            {
                var barrelPos = w.BarrelOrigin;
                var targetNormDir = Vector3D.Normalize(info.Target.PositionComp.WorldAABB.Center - barrelPos);
                weaponPos = barrelPos + (targetNormDir * w.MuzzleDistToBarrelCenter);
            }
            var ai = w.Comp.MasterAi;
            var s = ai.Session;
            var bestCubePos = Vector3D.Zero;
            var top5Count = w.Top5.Count;
            var top5 = w.Top5;
            var physics = ai.Session.Physics;
            var hitTmpList = ai.Session.HitInfoTmpList;
            var weaponCheck = p == null && !w.ActiveAmmoDef.AmmoDef.Const.SkipAimChecks;
            IHitInfo iHitInfo = null;

            for (int i = 0; i < cubes.Count + top5Count; i++)
            {

                ai.Session.BlockChecks++;
                var index = i < top5Count ? i : i - top5Count;
                var cube = i < top5Count ? top5[index] : cubes[index];

                var grid = cube.CubeGrid;
                if (grid == null || grid.MarkedForClose) continue;
                if (cube.MarkedForClose || cube == newEntity || cube == newEntity0 || cube == newEntity1 || cube == newEntity2 || cube == newEntity3 || checkPower && !(cube is IMyWarhead) && !cube.IsWorking)
                    continue;

                var cubePos = grid.GridIntegerToWorld(cube.Position);
                var range = cubePos - weaponPos;
                var test = (range.X * range.X) + (range.Y * range.Y) + (range.Z * range.Z);

                if (ai.Session.WaterApiLoaded && waterSphere.Radius > 2 && waterSphere.Contains(cubePos) != ContainmentType.Disjoint)
                    continue;

                if (test < minValue3)
                {

                    IHitInfo hit = null;

                    var best = test < minValue;
                    var bestTest = false;
                    if (best)
                    {

                        if (weaponCheck)
                        {
                            ai.Session.CanShoot++;
                            Vector3D predictedPos;
                            if (Weapon.CanShootTarget(w, ref cubePos, targetLinVel, targetAccel, out predictedPos, false, null, MathFuncs.DebugCaller.CanShootTarget7))
                            {

                                ai.Session.ClosestRayCasts++;

                                var targetDir = cubePos - w.BarrelOrigin;
                                Vector3D targetDirNorm;
                                Vector3D.Normalize(ref targetDir, out targetDirNorm);

                                var rayStart = w.BarrelOrigin + (targetDirNorm * w.MuzzleDistToBarrelCenter);

                                physics.CastRay(rayStart, cubePos, hitTmpList, 15);

                                var skip = false;
                                for (int j = 0; j < hitTmpList.Count; j++)
                                {
                                    var hitInfo = hitTmpList[j];

                                    var entity = hitInfo.HitEntity as MyEntity;
                                    var character = entity as IMyCharacter;

                                    TargetInfo otherInfo;
                                    var enemyCharacter = character != null && (!ai.Targets.TryGetValue(entity, out otherInfo) || !(otherInfo.EntInfo.Relationship == MyRelationsBetweenPlayerAndBlock.Enemies || otherInfo.EntInfo.Relationship == MyRelationsBetweenPlayerAndBlock.Neutral || otherInfo.EntInfo.Relationship == MyRelationsBetweenPlayerAndBlock.NoOwnership));

                                    if (entity == null || entity is MyVoxelBase || character != null && !enemyCharacter)
                                    {
                                        skip = true;
                                        break;
                                    }


                                    var topHitEntity = entity.GetTopMostParent();
                                    var hitGrid = topHitEntity as MyCubeGrid;

                                    if (hitGrid != null)
                                    {
                                        if (hitGrid.MarkedForClose || (hitGrid != grid && ai.AiType == AiTypes.Grid && hitGrid.IsSameConstructAs(ai.GridEntity)) || !hitGrid.DestructibleBlocks || hitGrid.Immune || hitGrid.GridGeneralDamageModifier <= 0) continue;

                                        var isTarget = hitGrid == grid || hitGrid.IsSameConstructAs(grid);

                                        var bigOwners = hitGrid.BigOwners;
                                        var noOwner = bigOwners.Count == 0;

                                        var validTarget = isTarget || noOwner || ai.Targets.TryGetValue(topHitEntity, out otherInfo) && (otherInfo.EntInfo.Relationship == MyRelationsBetweenPlayerAndBlock.Enemies || otherInfo.EntInfo.Relationship == MyRelationsBetweenPlayerAndBlock.Neutral || otherInfo.EntInfo.Relationship == MyRelationsBetweenPlayerAndBlock.NoOwnership);

                                        skip = !validTarget;

                                        if (isTarget)
                                        {
                                            bestTest = true;
                                            hit = hitInfo;
                                        }

                                        if (isTarget || skip)
                                            break;
                                    }
                                }

                                if (skip || hit == null)
                                    continue;
                            }
                        }
                        else bestTest = true;
                    }

                    if (best && bestTest)
                    {
                        minValue3 = minValue2;
                        newEntity3 = newEntity2;
                        minValue2 = minValue1;
                        newEntity2 = newEntity1;
                        minValue1 = minValue0;
                        newEntity1 = newEntity0;
                        minValue0 = minValue;
                        newEntity0 = newEntity;
                        minValue = test;

                        newEntity = cube;
                        bestCubePos = cubePos;
                        iHitInfo = hit;
                    }
                    else if (test < minValue0)
                    {
                        minValue3 = minValue2;
                        newEntity3 = newEntity2;
                        minValue2 = minValue1;
                        newEntity2 = newEntity1;
                        minValue1 = minValue0;
                        newEntity1 = newEntity0;
                        minValue0 = test;

                        newEntity0 = cube;
                    }
                    else if (test < minValue1)
                    {
                        minValue3 = minValue2;
                        newEntity3 = newEntity2;
                        minValue2 = minValue1;
                        newEntity2 = newEntity1;
                        minValue1 = test;

                        newEntity1 = cube;
                    }
                    else if (test < minValue2)
                    {
                        minValue3 = minValue2;
                        newEntity3 = newEntity2;
                        minValue2 = test;

                        newEntity2 = cube;
                    }
                    else
                    {
                        minValue3 = test;
                        newEntity3 = cube;
                    }
                }

            }
            top5.Clear();
            if (newEntity != null && iHitInfo != null)
            {

                double rayDist;
                Vector3D.Distance(ref weaponPos, ref bestCubePos, out rayDist);
                var shortDist = rayDist * (1 - iHitInfo.Fraction);
                var origDist = rayDist * iHitInfo.Fraction;
                target.Set(newEntity, iHitInfo.Position, shortDist, origDist, newEntity.GetTopMostParent().EntityId);
                top5.Add(newEntity);
            }
            else if (newEntity != null)
            {

                double rayDist;
                Vector3D.Distance(ref weaponPos, ref bestCubePos, out rayDist);
                var shortDist = rayDist;
                var origDist = rayDist;
                target.Set(newEntity, bestCubePos, shortDist, origDist, newEntity.GetTopMostParent().EntityId);
                top5.Add(newEntity);
            }
            else target.Reset(ai.Session.Tick, Target.States.NoTargetsSeen, w == null);

            if (newEntity0 != null) top5.Add(newEntity0);
            if (newEntity1 != null) top5.Add(newEntity1);
            if (newEntity2 != null) top5.Add(newEntity2);
            if (newEntity3 != null) top5.Add(newEntity3);

            return top5.Count > 0;
        }
        private static bool Obstruction(ref TargetInfo info, ref Vector3D targetPos, Projectile p)
        {
            var ai = p.Info.Ai;
            var obstruction = false;
            var topEntity = p.Info.Weapon.Comp.TopEntity;
            for (int j = 0; j < ai.Obstructions.Count; j++)
            {
                var ent = ai.Obstructions[j].Target;

                var voxel = ent as MyVoxelBase;
                var dir = (targetPos - p.Position);
                var entWorldVolume = ent.PositionComp.WorldVolume;
                if (voxel != null)
                {

                    if (!ai.PlanetSurfaceInRange && (entWorldVolume.Contains(p.Position) != ContainmentType.Disjoint || new RayD(ref p.Position, ref dir).Intersects(entWorldVolume) != null))
                    {
                        var dirNorm = Vector3D.Normalize(dir);
                        var targetDist = Vector3D.Distance(p.Position, targetPos);
                        var tRadius = info.Target.PositionComp.LocalVolume.Radius;
                        var testPos = p.Position + (dirNorm * (targetDist - tRadius));
                        var lineTest = new LineD(p.Position, testPos);
                        Vector3D? voxelHit;
                        using (voxel.Pin())
                            voxel.RootVoxel.GetIntersectionWithLine(ref lineTest, out voxelHit);

                        obstruction = voxelHit.HasValue;
                        if (obstruction)
                            break;
                    }
                }
                else
                {
                    if (new RayD(ref p.Position, ref dir).Intersects(entWorldVolume) != null)
                    {
                        var transform = ent.PositionComp.WorldMatrixRef;
                        var box = ent.PositionComp.LocalAABB;
                        var obb = new MyOrientedBoundingBoxD(box, transform);
                        var lineTest = new LineD(p.Position, targetPos);
                        if (obb.Intersects(ref lineTest) != null)
                        {
                            obstruction = true;
                            break;
                        }
                    }
                }
            }

            if (!obstruction)
            {
                var dir = (targetPos - p.Position);
                var ray = new RayD(ref p.Position, ref dir);
                foreach (var sub in ai.SubGridCache)
                {
                    var subDist = sub.PositionComp.WorldVolume.Intersects(ray);
                    if (subDist.HasValue)
                    {
                        var transform = topEntity.PositionComp.WorldMatrixRef;
                        var box = topEntity.PositionComp.LocalAABB;
                        var obb = new MyOrientedBoundingBoxD(box, transform);
                        if (obb.Intersects(ref ray) != null)
                            obstruction = sub.RayCastBlocks(p.Position, targetPos) != null;
                    }

                    if (obstruction) break;
                }

                if (!obstruction && ai.PlanetSurfaceInRange && ai.MyPlanet != null)
                {
                    double targetDist;
                    Vector3D.Distance(ref p.Position, ref targetPos, out targetDist);
                    var dirNorm = dir / targetDist;

                    var tRadius = info.Target.PositionComp.LocalVolume.Radius;
                    targetDist = targetDist > tRadius ? (targetDist - tRadius) : targetDist;

                    var targetEdgePos = targetPos + (-dirNorm * tRadius);

                    if (targetDist > 300)
                    {
                        var lineTest1 = new LineD(p.Position, p.Position + (dirNorm * 150), 150);
                        var lineTest2 = new LineD(targetEdgePos, targetEdgePos + (-dirNorm * 150), 150);
                        obstruction = VoxelIntersect.CheckSurfacePointsOnLine(ai.MyPlanet, ref lineTest1, 3);
                        if (!obstruction)
                            obstruction = VoxelIntersect.CheckSurfacePointsOnLine(ai.MyPlanet, ref lineTest2, 3);
                    }
                    else
                    {
                        var lineTest = new LineD(p.Position, targetEdgePos, targetDist);
                        obstruction = VoxelIntersect.CheckSurfacePointsOnLine(ai.MyPlanet, ref lineTest, 3);
                    }
                }
            }
            return obstruction;
        }

        internal static bool ValidScanEntity(Weapon w, MyDetectedEntityInfo info, MyEntity target, bool skipUnique = false)
        {
            Weapon.TargetOwner tOwner;
            if (!skipUnique && w.System.UniqueTargetPerWeapon && w.Comp.ActiveTargets.TryGetValue(target, out tOwner) && tOwner.Weapon != w)
                return false;

            var character = target as IMyCharacter;

            if (character != null)
            {
                switch (info.Relationship)
                {
                    case MyRelationsBetweenPlayerAndBlock.FactionShare:
                    case MyRelationsBetweenPlayerAndBlock.Friends:
                    case MyRelationsBetweenPlayerAndBlock.Owner:
                        if (!w.System.Threats.Contains((int) Threat.ScanFriendlyCharacter))
                            return false;
                        break;
                    case MyRelationsBetweenPlayerAndBlock.Neutral:
                        if (!w.System.Threats.Contains((int) Threat.ScanNeutralCharacter))
                            return false;
                        break;
                    case MyRelationsBetweenPlayerAndBlock.Enemies:
                        if (!w.System.Threats.Contains((int) Threat.ScanEnemyCharacter))
                            return false;
                        break;
                    default:
                        return false;
                }
            }

            var voxel = target as MyVoxelBase;
            if (voxel != null)
            {
                var planet = voxel as MyPlanet;
                if (planet != null && !w.System.Threats.Contains((int) Threat.ScanPlanet))
                    return false;
                if (!w.System.Threats.Contains((int) Threat.ScanRoid))
                    return false;
            }

            var grid = target as MyCubeGrid;
            if (grid != null)
            {
                switch (info.Relationship)
                {
                    case MyRelationsBetweenPlayerAndBlock.FactionShare:
                    case MyRelationsBetweenPlayerAndBlock.Friends:
                        if (!w.System.Threats.Contains((int) Threat.ScanFriendlyGrid))
                            return false;
                        break;
                    case MyRelationsBetweenPlayerAndBlock.Neutral:
                        if (!w.System.Threats.Contains((int) Threat.ScanNeutralGrid))
                            return false;
                        break;
                    case MyRelationsBetweenPlayerAndBlock.Enemies:
                        if (!w.System.Threats.Contains((int) Threat.ScanEnemyGrid))
                            return false;
                        break;
                    case MyRelationsBetweenPlayerAndBlock.NoOwnership:
                        if (!w.System.Threats.Contains((int) Threat.ScanUnOwnedGrid))
                            return false;
                        break;
                    case MyRelationsBetweenPlayerAndBlock.Owner:
                        if (!w.System.Threats.Contains((int) Threat.ScanOwnersGrid))
                            return false;
                        break;
                    default:
                        return false;
                }
            }

            return true;
        }

        internal static bool SwitchToDrone(Weapon w)
        {
            w.AimCone.ConeDir = w.MyPivotFwd;
            w.AimCone.ConeTip = w.BarrelOrigin + (w.MyPivotFwd * w.MuzzleDistToBarrelCenter);

            var comp = w.Comp;
            var overRides = comp.Data.Repo.Values.Set.Overrides;
            var attackNeutrals = overRides.Neutrals;
            var attackNoOwner = overRides.Unowned;
            var session = w.Comp.Session;
            var ai = comp.MasterAi;
            session.TargetRequests++;
            var ammoDef = w.ActiveAmmoDef.AmmoDef;
            var aConst = ammoDef.Const;
            var weaponPos = w.BarrelOrigin + (w.MyPivotFwd * w.MuzzleDistToBarrelCenter);
            var target = w.NewTarget;
            var s = w.System;
            var accelPrediction = (int)s.Values.HardPoint.AimLeadingPrediction > 1;
            var minRadius = overRides.MinSize * 0.5f;
            var maxRadius = overRides.MaxSize * 0.5f;
            var minTargetRadius = minRadius > 0 ? minRadius : s.MinTargetRadius;
            var maxTargetRadius = maxRadius < s.MaxTargetRadius ? maxRadius : s.MaxTargetRadius;

            var moveMode = overRides.MoveMode;
            var movingMode = moveMode == ProtoWeaponOverrides.MoveModes.Moving;
            var fireOnStation = moveMode == ProtoWeaponOverrides.MoveModes.Any || moveMode == ProtoWeaponOverrides.MoveModes.Moored;
            var stationOnly = moveMode == ProtoWeaponOverrides.MoveModes.Moored;
            BoundingSphereD waterSphere = new BoundingSphereD(Vector3D.Zero, 1f);
            WaterData water = null;
            if (session.WaterApiLoaded && !ammoDef.IgnoreWater && ai.InPlanetGravity && ai.MyPlanet != null && session.WaterMap.TryGetValue(ai.MyPlanet.EntityId, out water))
                waterSphere = new BoundingSphereD(ai.MyPlanet.PositionComp.WorldAABB.Center, water.MinRadius);
            var numOfTargets = ai.SortedTargets.Count;
            var deck = GetDeck(ref session.TargetDeck, 0, numOfTargets, ai.DetectionInfo.DroneCount, ref w.TargetData.WeaponRandom.AcquireRandom);

            for (int i = 0; i < numOfTargets; i++)
            {
                var info = ai.SortedTargets[deck[i]];

                if (!info.Drone)
                    break;

                if (info.Target == null || info.Target.MarkedForClose || !attackNeutrals && info.EntInfo.Relationship == MyRelationsBetweenPlayerAndBlock.Neutral || !attackNoOwner && info.EntInfo.Relationship == MyRelationsBetweenPlayerAndBlock.NoOwnership) continue;

                if (movingMode && info.VelLenSqr < 1 || !fireOnStation && info.IsStatic || stationOnly && !info.IsStatic)
                    continue;

                var character = info.Target as IMyCharacter;
                var targetRadius = character != null ? info.TargetRadius * 5 : info.TargetRadius;
                if (targetRadius < minTargetRadius || info.TargetRadius > maxTargetRadius && maxTargetRadius < 8192) continue;

                var targetCenter = info.Target.PositionComp.WorldAABB.Center;
                var targetDistSqr = Vector3D.DistanceSquared(targetCenter, weaponPos);
                if (targetDistSqr > (w.MaxTargetDistance + info.TargetRadius) * (w.MaxTargetDistance + info.TargetRadius) || targetDistSqr < w.MinTargetDistanceSqr) continue;
                if (water != null)
                {
                    if (new BoundingSphereD(ai.MyPlanet.PositionComp.WorldAABB.Center, water.MinRadius).Contains(new BoundingSphereD(targetCenter, targetRadius)) == ContainmentType.Contains)
                        continue;
                }
                session.TargetChecks++;
                Vector3D targetLinVel = info.Target.Physics?.LinearVelocity ?? Vector3D.Zero;
                Vector3D targetAccel = accelPrediction ? info.Target.Physics?.LinearAcceleration ?? Vector3D.Zero : Vector3.Zero;

                if (info.IsGrid)
                {

                    if (!s.TrackGrids || !overRides.Grids || info.FatCount < 2 || (!overRides.LargeGrid && info.LargeGrid) || (!overRides.SmallGrid && !info.LargeGrid)) continue;
                    session.CanShoot++;
                    Vector3D newCenter;
                    if (!w.TurretController)
                    {

                        var validEstimate = true;
                        newCenter = w.System.Prediction != HardPointDef.Prediction.Off && (!aConst.IsBeamWeapon && aConst.DesiredProjectileSpeed > 0) ? Weapon.TrajectoryEstimation(w, targetCenter, targetLinVel, targetAccel, Vector3D.Zero, out validEstimate) : targetCenter;
                        var targetSphere = info.Target.PositionComp.WorldVolume;
                        targetSphere.Center = newCenter;
                        if (!validEstimate || !aConst.SkipAimChecks && !MathFuncs.TargetSphereInCone(ref targetSphere, ref w.AimCone)) continue;
                    }
                    else if (!Weapon.CanShootTargetObb(w, info.Target, targetLinVel, targetAccel, out newCenter)) continue;

                    if (w.Comp.MasterAi.FriendlyShieldNear)
                    {
                        var targetDir = newCenter - weaponPos;
                        if (w.HitFriendlyShield(weaponPos, newCenter, targetDir))
                            continue;
                    }

                    if (!AcquireBlock(w, target, info, ref waterSphere, ref w.XorRnd, null, true)) continue;
                    target.TransferTo(w.Target, w.Comp.Session.Tick, true);

                    var validTarget = w.Target.TargetState == Target.TargetStates.IsEntity;

                    if (validTarget)
                    {

                        ai.Session.NewThreat(w);

                        if (ai.Session.MpActive && ai.Session.IsServer)
                            w.Target.PushTargetToClient(w);
                    }

                    return validTarget;
                }
            }

            return false;
        }

    }
}
