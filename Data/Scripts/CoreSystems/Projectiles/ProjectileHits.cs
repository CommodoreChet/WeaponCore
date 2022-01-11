﻿using System;
using System.Collections.Generic;
using CoreSystems.Support;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Interfaces;
using VRage.Utils;
using VRageMath;
using static CoreSystems.Support.HitEntity.Type;
using static CoreSystems.Support.WeaponDefinition.AmmoDef.EwarDef.EwarType;
using static CoreSystems.Support.DeferedVoxels;
using CollisionLayers = Sandbox.Engine.Physics.MyPhysics.CollisionLayers;
namespace CoreSystems.Projectiles
{
    public partial class Projectiles
    {
        internal void InitialHitCheck()
        {
            var vhCount = ValidateHits.Count;
            var minCount = Session.Settings.Enforcement.BaseOptimizations ? 96 : 99999;
            var targetStride = vhCount / 20;
            var stride = vhCount < minCount ? 100000 : targetStride > 48 ? targetStride : 48;

            MyAPIGateway.Parallel.For(0, ValidateHits.Count, x => {

                var p = ValidateHits[x];
                var info = p.Info;
                var target = info.Target;
                var ai = info.Ai;
                var aDef = info.AmmoDef;
                var aConst = info.AmmoDef.Const;
                var shieldByPass = aConst.ShieldDamageBypassMod > 0;
                var genericFields = info.EwarActive && (aConst.EwarType == Dot || aConst.EwarType == Push || aConst.EwarType == Pull || aConst.EwarType == Tractor);

                p.FinalizeIntersection = false;
                p.Info.ShieldInLine = false;

                var isBeam = aConst.IsBeamWeapon;
                var lineCheck = aConst.CollisionIsLine && !info.EwarAreaPulse;
                var offensiveEwar = (info.EwarActive && aConst.NonAntiSmartEwar);

                bool projetileInShield = false;
                var tick = Session.Tick;
                var useEntityCollection = p.CheckType != Projectile.CheckTypes.Ray;
                var entityCollection = p.MyEntityList;
                var collectionCount = !useEntityCollection ? p.MySegmentList.Count : entityCollection.Count;
                var ray = new RayD(ref p.Beam.From, ref p.Beam.Direction);
                var firingCube = target.CoreCube;
                var goCritical = aConst.IsCriticalReaction;
                var selfDamage = aConst.SelfDamage;
                var ignoreVoxels = aDef.IgnoreVoxels;
                var isGrid = ai.AiType == Ai.AiTypes.Grid;
                WaterData water = null;
                if (Session.WaterApiLoaded && info.MyPlanet != null)
                    Session.WaterMap.TryGetValue(info.MyPlanet.EntityId, out water);

                for (int i = 0; i < collectionCount; i++) {
                    var ent = !useEntityCollection ? p.MySegmentList[i].Element : entityCollection[i];

                    var grid = ent as MyCubeGrid;
                    var entIsSelf = grid != null && firingCube != null && (grid == firingCube.CubeGrid || firingCube.CubeGrid.IsSameConstructAs(grid));

                    if (entIsSelf && p.IsSmart || ent.MarkedForClose || !ent.InScene || ent == info.MyShield || !isGrid && ent == ai.TopEntity) continue;

                    var character = ent as IMyCharacter;
                    if (info.EwarActive && character != null && !genericFields) continue;

                    var entSphere = ent.PositionComp.WorldVolume;
                    if (useEntityCollection)
                    {

                        if (p.CheckType == Projectile.CheckTypes.CachedRay)
                        {
                            var dist = ray.Intersects(entSphere);
                            if (!dist.HasValue || dist > p.Beam.Length)
                                continue;
                        }
                        else if (p.CheckType == Projectile.CheckTypes.CachedSphere && p.PruneSphere.Contains(entSphere) == ContainmentType.Disjoint)
                            continue;
                    }

                    if (grid != null || character != null)
                    {
                        var extBeam = new LineD(p.Beam.From - p.Beam.Direction * (entSphere.Radius * 2), p.Beam.To);
                        var transform = ent.PositionComp.WorldMatrixRef;
                        var box = ent.PositionComp.LocalAABB;
                        var obb = new MyOrientedBoundingBoxD(box, transform);

                        if (lineCheck && obb.Intersects(ref extBeam) == null || !lineCheck && !obb.Intersects(ref p.PruneSphere)) continue;
                    }

                    var safeZone = ent as MySafeZone;
                    if (safeZone != null && safeZone.Enabled)
                    {

                        var action = (Session.SafeZoneAction)safeZone.AllowedActions;
                        if ((action & Session.SafeZoneAction.Damage) == 0)
                        {

                            bool intersects;
                            if (safeZone.Shape == MySafeZoneShape.Sphere)
                            {
                                var sphere = new BoundingSphereD(safeZone.PositionComp.WorldVolume.Center, safeZone.Radius);
                                var dist = ray.Intersects(sphere);
                                intersects = dist != null && dist <= p.Beam.Length;
                            }
                            else
                                intersects = new MyOrientedBoundingBoxD(safeZone.PositionComp.LocalAABB, safeZone.PositionComp.WorldMatrixRef).Intersects(ref p.Beam) != null;

                            if (intersects)
                            {

                                p.State = Projectile.ProjectileState.Depleted;
                                p.EarlyEnd = true;

                                if (p.EnableAv)
                                    info.AvShot.ForceHitParticle = true;
                                break;
                            }
                        }
                    }

                    HitEntity hitEntity = null;
                    var checkShield = Session.ShieldApiLoaded && Session.ShieldHash == ent.DefinitionId?.SubtypeId && ent.Render.Visible;
                    MyTuple<IMyTerminalBlock, MyTuple<bool, bool, float, float, float, int>, MyTuple<MatrixD, MatrixD>>? shieldInfo = null;

                    if (checkShield && !info.EwarActive || info.EwarActive && (aConst.EwarType == Dot || aConst.EwarType == Emp))
                    {
                        shieldInfo = Session.SApi.MatchEntToShieldFastExt(ent, true);
                        if (shieldInfo != null && (firingCube == null || !firingCube.CubeGrid.IsSameConstructAs(shieldInfo.Value.Item1.CubeGrid) && !goCritical))
                        {

                            var shrapnelSpawn = p.Info.IsShrapnel && p.Info.Age < 1;
                            if (Vector3D.Transform(!shrapnelSpawn ? info.Origin : target.CoreEntity.PositionComp.WorldMatrixRef.Translation, shieldInfo.Value.Item3.Item1).LengthSquared() > 1)
                            {

                                p.EntitiesNear = true;
                                var dist = MathFuncs.IntersectEllipsoid(shieldInfo.Value.Item3.Item1, shieldInfo.Value.Item3.Item2, new RayD(p.Beam.From, p.Beam.Direction));
                                if (target.IsProjectile && Vector3D.Transform(target.Projectile.Position, shieldInfo.Value.Item3.Item1).LengthSquared() <= 1)
                                    projetileInShield = true;

                                var shieldIntersect = dist != null && (dist.Value < p.Beam.Length || info.EwarActive);
                                info.ShieldKeepBypass = shieldIntersect;
                                if (shieldIntersect && !info.ShieldBypassed)
                                {

                                    hitEntity = HitEntityPool.Get();
                                    hitEntity.EventType = Shield;
                                    var hitPos = p.Beam.From + (p.Beam.Direction * dist.Value);
                                    hitEntity.HitPos = p.Beam.From + (p.Beam.Direction * dist.Value);
                                    hitEntity.HitDist = dist;
                                    if (shieldInfo.Value.Item2.Item2)
                                    {

                                        var faceInfo = Session.SApi.GetFaceInfo(shieldInfo.Value.Item1, hitPos);
                                        var modifiedBypassMod = ((1 - aConst.ShieldDamageBypassMod) + faceInfo.Item5);
                                        var validRange = modifiedBypassMod >= 0 && modifiedBypassMod <= 1 || faceInfo.Item1;
                                        var notSupressed = validRange && modifiedBypassMod < 1 && faceInfo.Item5 < 1;
                                        var bypassAmmo = shieldByPass && notSupressed;
                                        var bypass = bypassAmmo || faceInfo.Item1;

                                        info.ShieldResistMod = faceInfo.Item4;

                                        if (bypass)
                                        {
                                            info.ShieldBypassed = true;
                                            modifiedBypassMod = bypassAmmo && faceInfo.Item1 ? 0f : modifiedBypassMod;
                                            info.ShieldBypassMod = bypassAmmo ? modifiedBypassMod : 0.15f;
                                        }
                                        else p.Info.ShieldBypassMod = 1f;
                                    }
                                    else if (shieldByPass)
                                    {
                                        info.ShieldBypassed = true;
                                        info.ShieldResistMod = 1f;
                                        info.ShieldBypassMod = aConst.ShieldDamageBypassMod;
                                    }
                                }
                                else continue;
                            }
                        }
                    }

                    var destroyable = ent as IMyDestroyableObject;
                    var voxel = ent as MyVoxelBase;
                    if (voxel != null && voxel == voxel?.RootVoxel && !ignoreVoxels)
                    {

                        if ((ent == info.MyPlanet && !(p.LinePlanetCheck || p.DynamicGuidance)) || !p.LinePlanetCheck && isBeam)
                            continue;

                        VoxelIntersectBranch voxelState = VoxelIntersectBranch.None;
                        Vector3D? voxelHit = null;
                        if (tick - info.VoxelCache.HitRefreshed < 60)
                        {
                            var cacheDist = ray.Intersects(info.VoxelCache.HitSphere);
                            if (cacheDist.HasValue && cacheDist.Value <= p.Beam.Length)
                            {
                                voxelHit = p.Beam.From + (p.Beam.Direction * cacheDist.Value);
                                voxelState = VoxelIntersectBranch.PseudoHit1;
                            }
                            else if (cacheDist.HasValue)
                                info.VoxelCache.MissSphere.Center = p.Beam.To;
                        }

                        if (voxelState != VoxelIntersectBranch.PseudoHit1)
                        {

                            if (voxel == info.MyPlanet && info.VoxelCache.MissSphere.Contains(p.Beam.To) == ContainmentType.Disjoint)
                            {

                                if (p.LinePlanetCheck)
                                {
                                    if (water != null && !aDef.IgnoreWater)
                                    {
                                        var waterSphere = new BoundingSphereD(info.MyPlanet.PositionComp.WorldAABB.Center, water.MinRadius);
                                        var estiamtedSurfaceDistance = ray.Intersects(waterSphere);

                                        if (estiamtedSurfaceDistance.HasValue && estiamtedSurfaceDistance.Value <= p.Beam.Length)
                                        {
                                            var estimatedHit = ray.Position + (ray.Direction * estiamtedSurfaceDistance.Value);
                                            voxelHit = estimatedHit;
                                            voxelState = VoxelIntersectBranch.PseudoHit2;
                                        }
                                    }
                                    if (voxelState != VoxelIntersectBranch.PseudoHit2)
                                    {

                                        var surfacePos = info.MyPlanet.GetClosestSurfacePointGlobal(ref p.Position);
                                        var planetCenter = info.MyPlanet.PositionComp.WorldAABB.Center;
                                        double surfaceToCenter;
                                        Vector3D.DistanceSquared(ref surfacePos, ref planetCenter, out surfaceToCenter);
                                        double endPointToCenter;
                                        Vector3D.DistanceSquared(ref p.Position, ref planetCenter, out endPointToCenter);
                                        double startPointToCenter;
                                        Vector3D.DistanceSquared(ref info.Origin, ref planetCenter, out startPointToCenter);

                                        var prevEndPointToCenter = p.PrevEndPointToCenterSqr;
                                        Vector3D.DistanceSquared(ref surfacePos, ref p.Position, out p.PrevEndPointToCenterSqr);
                                        if (surfaceToCenter > endPointToCenter || p.PrevEndPointToCenterSqr <= (p.Beam.Length * p.Beam.Length) || endPointToCenter > startPointToCenter && prevEndPointToCenter > p.DistanceToTravelSqr || surfaceToCenter > Vector3D.DistanceSquared(planetCenter, p.LastPosition))
                                        {

                                            var estiamtedSurfaceDistance = ray.Intersects(info.VoxelCache.PlanetSphere);
                                            var fullCheck = info.VoxelCache.PlanetSphere.Contains(p.Info.Origin) != ContainmentType.Disjoint || !estiamtedSurfaceDistance.HasValue;

                                            if (!fullCheck && estiamtedSurfaceDistance.HasValue && (estiamtedSurfaceDistance.Value <= p.Beam.Length || info.VoxelCache.PlanetSphere.Radius < 1))
                                            {

                                                double distSqr;
                                                var estimatedHit = ray.Position + (ray.Direction * estiamtedSurfaceDistance.Value);
                                                Vector3D.DistanceSquared(ref info.VoxelCache.FirstPlanetHit, ref estimatedHit, out distSqr);

                                                if (distSqr > 625) fullCheck = true;
                                                else
                                                {
                                                    voxelHit = estimatedHit;
                                                    voxelState = VoxelIntersectBranch.PseudoHit2;
                                                }
                                            }

                                            if (fullCheck)
                                                voxelState = VoxelIntersectBranch.DeferFullCheck;

                                            if (voxelHit.HasValue && Vector3D.DistanceSquared(voxelHit.Value, info.VoxelCache.PlanetSphere.Center) > info.VoxelCache.PlanetSphere.Radius * info.VoxelCache.PlanetSphere.Radius)
                                                info.VoxelCache.GrowPlanetCache(voxelHit.Value);
                                        }
                                    }
                                }
                            }
                            else if (voxelHit == null && info.VoxelCache.MissSphere.Contains(p.Beam.To) == ContainmentType.Disjoint)
                                voxelState = VoxelIntersectBranch.DeferedMissUpdate;
                        }

                        if (voxelState == VoxelIntersectBranch.PseudoHit1 || voxelState == VoxelIntersectBranch.PseudoHit2)
                        {

                            if (!voxelHit.HasValue)
                            {

                                if (info.VoxelCache.MissSphere.Contains(p.Beam.To) == ContainmentType.Disjoint)
                                    info.VoxelCache.MissSphere.Center = p.Beam.To;
                                continue;
                            }

                            hitEntity = HitEntityPool.Get();

                            var hitPos = voxelHit.Value;
                            hitEntity.HitPos = hitPos;

                            double dist;
                            Vector3D.Distance(ref p.Beam.From, ref hitPos, out dist);
                            hitEntity.HitDist = dist;

                            hitEntity.EventType = Voxel;
                        }
                        else if (voxelState == VoxelIntersectBranch.DeferedMissUpdate || voxelState == VoxelIntersectBranch.DeferFullCheck) {
                            lock (DeferedVoxels)
                            {
                                DeferedVoxels.Add(new DeferedVoxels { Projectile = p, Branch = voxelState, Voxel = voxel });
                            }
                        }
                    }
                    else if (ent.Physics != null && !ent.Physics.IsPhantom && !ent.IsPreview && grid != null)
                    {

                        if (grid != null)
                        {
                            hitEntity = HitEntityPool.Get();
                            if (entIsSelf && !selfDamage)
                            {

                                if (!isBeam && p.Beam.Length <= grid.GridSize * 2 && !goCritical)
                                {
                                    MyCube cube;
                                    if (!(grid.TryGetCube(grid.WorldToGridInteger(p.Position), out cube) && cube.CubeBlock != target.CoreCube.SlimBlock || grid.TryGetCube(grid.WorldToGridInteger(p.LastPosition), out cube) && cube.CubeBlock != target.CoreCube.SlimBlock))
                                    {
                                        HitEntityPool.Return(hitEntity);
                                        continue;
                                    }
                                }

                                if (!p.Info.EwarAreaPulse)
                                {

                                    var forwardPos = p.Info.Age != 1 ? p.Beam.From : p.Beam.From + (p.Beam.Direction * Math.Min(grid.GridSizeHalf, info.DistanceTraveled - info.PrevDistanceTraveled));
                                    grid.RayCastCells(forwardPos, p.Beam.To, hitEntity.Vector3ICache, null, true, true);

                                    if (hitEntity.Vector3ICache.Count > 0)
                                    {

                                        bool hitself = false;
                                        for (int j = 0; j < hitEntity.Vector3ICache.Count; j++)
                                        {

                                            MyCube myCube;
                                            if (grid.TryGetCube(hitEntity.Vector3ICache[j], out myCube))
                                            {

                                                if (goCritical || ((IMySlimBlock)myCube.CubeBlock).Position != target.CoreCube.Position)
                                                {

                                                    hitself = true;
                                                    break;
                                                }
                                            }
                                        }

                                        if (!hitself)
                                        {
                                            HitEntityPool.Return(hitEntity);
                                            continue;
                                        }
                                        IHitInfo hitInfo = null;
                                        if (!goCritical)
                                        {
                                            Session.Physics.CastRay(forwardPos, p.Beam.To, out hitInfo, CollisionLayers.DefaultCollisionLayer);
                                            var hitGrid = hitInfo?.HitEntity?.GetTopMostParent() as MyCubeGrid;
                                            if (hitGrid == null || firingCube == null || !firingCube.CubeGrid.IsSameConstructAs(hitGrid))
                                            {
                                                HitEntityPool.Return(hitEntity);
                                                continue;
                                            }
                                        }

                                        hitEntity.HitPos = hitInfo?.Position ?? p.Beam.From;
                                        var posI = hitEntity.Vector3ICache[0];
                                        hitEntity.Blocks.Add(new HitEntity.RootBlocks { Block = grid.GetCubeBlock(hitEntity.Vector3ICache[0]), QueryPos = posI});
                                    }
                                }
                            }
                            else
                                grid.RayCastCells(p.Beam.From, p.Beam.To, hitEntity.Vector3ICache, null, true, true);

                            if (!offensiveEwar)
                                hitEntity.EventType = Grid;
                            else if (!info.EwarAreaPulse)
                                hitEntity.EventType = Effect;
                            else
                                hitEntity.EventType = Field;

                            p.EntitiesNear = true;
                        }
                    }
                    else if (destroyable != null)
                    {

                        hitEntity = HitEntityPool.Get();
                        hitEntity.EventType = Destroyable;
                    }

                    if (hitEntity != null)
                    {
                        p.FinalizeIntersection = true;
                        hitEntity.Info = info;
                        hitEntity.Entity = hitEntity.EventType != Shield ? ent : (MyEntity)shieldInfo.Value.Item1;
                        hitEntity.Intersection = p.Beam;
                        hitEntity.SphereCheck = !lineCheck;
                        hitEntity.PruneSphere = p.PruneSphere;
                        hitEntity.SelfHit = entIsSelf;
                        hitEntity.DamageOverTime = aConst.EwarType == Dot;
                        info.HitList.Add(hitEntity);
                    }
                }
                                
                if (target.IsProjectile && aConst.NonAntiSmartEwar && !projetileInShield)
                {
                    var detonate = p.State == Projectile.ProjectileState.Detonate;
                    var hitTolerance = detonate ? aConst.EndOfLifeRadius : aConst.ByBlockHitRadius > aConst.CollisionSize ? aConst.ByBlockHitRadius : aConst.CollisionSize;
                    var useLine = aConst.CollisionIsLine && !detonate && aConst.ByBlockHitRadius <= 0;

                    var sphere = new BoundingSphereD(target.Projectile.Position, aConst.CollisionSize);
                    sphere.Include(new BoundingSphereD(target.Projectile.LastPosition, 1));

                    bool rayCheck = false;
                    if (useLine)
                    {
                        var dist = sphere.Intersects(new RayD(p.LastPosition, info.Direction));
                        if (dist <= hitTolerance || isBeam && dist <= p.Beam.Length)
                            rayCheck = true;
                    }

                    var testSphere = p.PruneSphere;
                    testSphere.Radius = hitTolerance;
                    /*
                    var targetCapsule = new CapsuleD(p.Position, p.LastPosition, (float) p.Info.Target.Projectile.Info.AmmoDef.Const.CollisionSize / 2);
                    var dVec = Vector3D.Zero;
                    var eVec = Vector3.Zero;
                    */
                    if (rayCheck || sphere.Intersects(testSphere))
                    {
                        /*
                        var dir = p.Info.Target.Projectile.Position - p.Info.Target.Projectile.LastPosition;
                        var delta = dir.Normalize();
                        var radius = p.Info.Target.Projectile.Info.AmmoDef.Const.CollisionSize;
                        var size = p.Info.Target.Projectile.Info.AmmoDef.Const.CollisionSize;
                        var obb = new MyOrientedBoundingBoxD((p.Info.Target.Projectile.Position + p.Info.Target.Projectile.LastPosition) / 2, new Vector3(size, size, delta / 2 + radius), Quaternion.CreateFromForwardUp(dir, Vector3D.CalculatePerpendicularVector(dir)));
                        if (obb.Intersects(ref testSphere))
                        */
                        ProjectileHit(p, target.Projectile, lineCheck, ref p.Beam);
                    }
                }

                if (!useEntityCollection)
                    p.MySegmentList.Clear();
                else if (p.CheckType == Projectile.CheckTypes.Sphere)
                    entityCollection.Clear();

                if (p.FinalizeIntersection) {
                    lock (FinalHitCheck)
                        FinalHitCheck.Add(p);
                }

            }, stride);
            ValidateHits.Clear();
        }

        internal void DeferedVoxelCheck()
        {
            for (int i = 0; i < DeferedVoxels.Count; i++)
            {

                var p = DeferedVoxels[i].Projectile;
                var branch = DeferedVoxels[i].Branch;
                var voxel = DeferedVoxels[i].Voxel;
                Vector3D? voxelHit = null;

                if (branch == VoxelIntersectBranch.DeferFullCheck)
                {

                    if (p.Beam.Length > 85)
                    {
                        IHitInfo hit;
                        if (p.Info.System.Session.Physics.CastRay(p.Beam.From, p.Beam.To, out hit, CollisionLayers.VoxelCollisionLayer, false) && hit != null)
                            voxelHit = hit.Position;
                    }
                    else
                    {

                        using (voxel.Pin())
                        {
                            if (!voxel.GetIntersectionWithLine(ref p.Beam, out voxelHit, true, IntersectionFlags.DIRECT_TRIANGLES) && VoxelIntersect.PointInsideVoxel(voxel, p.Info.System.Session.TmpStorage, p.Beam.From))
                                voxelHit = p.Beam.From;
                        }
                    }

                    if (voxelHit.HasValue && p.Info.IsShrapnel && p.Info.Age == 0)
                    {
                        if (!VoxelIntersect.PointInsideVoxel(voxel, p.Info.System.Session.TmpStorage, voxelHit.Value + (p.Beam.Direction * 1.25f)))
                            voxelHit = null;
                    }
                }
                else if (branch == VoxelIntersectBranch.DeferedMissUpdate)
                {

                    using (voxel.Pin())
                    {

                        if (p.Info.AmmoDef.Const.IsBeamWeapon && p.Info.AmmoDef.Const.RealShotsPerMin < 10)
                        {
                            IHitInfo hit;
                            if (p.Info.System.Session.Physics.CastRay(p.Beam.From, p.Beam.To, out hit, CollisionLayers.VoxelCollisionLayer, false) && hit != null)
                                voxelHit = hit.Position;
                        }
                        else if (!voxel.GetIntersectionWithLine(ref p.Beam, out voxelHit, true, IntersectionFlags.DIRECT_TRIANGLES) && VoxelIntersect.PointInsideVoxel(voxel, p.Info.System.Session.TmpStorage, p.Beam.From))
                            voxelHit = p.Beam.From;
                    }
                }

                if (!voxelHit.HasValue)
                {

                    if (p.Info.VoxelCache.MissSphere.Contains(p.Beam.To) == ContainmentType.Disjoint)
                        p.Info.VoxelCache.MissSphere.Center = p.Beam.To;
                    continue;
                }

                p.Info.VoxelCache.Update(voxel, ref voxelHit, Session.Tick);

                if (voxelHit == null)
                    continue;
                if (!p.FinalizeIntersection)
                {
                    p.FinalizeIntersection = true;
                    FinalHitCheck.Add(p);
                }
                var hitEntity = HitEntityPool.Get();
                var lineCheck = p.Info.AmmoDef.Const.CollisionIsLine && !p.Info.EwarAreaPulse;
                hitEntity.Info = p.Info;
                hitEntity.Entity = voxel;
                hitEntity.Intersection = p.Beam;
                hitEntity.SphereCheck = !lineCheck;
                hitEntity.PruneSphere = p.PruneSphere;
                hitEntity.DamageOverTime = p.Info.AmmoDef.Const.EwarType == Dot;

                var hitPos = voxelHit.Value;
                hitEntity.HitPos = hitPos;

                double dist;
                Vector3D.Distance(ref p.Beam.From, ref hitPos, out dist);
                hitEntity.HitDist = dist;

                hitEntity.EventType = Voxel;
                p.Info.HitList.Add(hitEntity);
            }
            DeferedVoxels.Clear();
        }
        internal void FinalizeHits()
        {
            var vhCount = FinalHitCheck.Count;
            var minCount = Session.Settings.Enforcement.BaseOptimizations ? 96 : 99999;
            var targetStride = vhCount / 20;
            var stride = vhCount < minCount ? 100000 : targetStride > 48 ? targetStride : 48;

            MyAPIGateway.Parallel.For(0, FinalHitCheck.Count, x =>
            {
                var p = FinalHitCheck[x];

                p.Intersecting = GenerateHitInfo(p);

                var info = p.Info;
                if (p.Intersecting)
                {
                    var aConst = info.AmmoDef.Const;
                    if (aConst.VirtualBeams)
                    {

                        info.WeaponCache.VirtualHit = true;
                        info.WeaponCache.HitEntity.Entity = info.Hit.Entity;
                        info.WeaponCache.HitEntity.HitPos = info.Hit.SurfaceHit;
                        info.WeaponCache.Hits = p.VrPros.Count;
                        info.WeaponCache.HitDistance = Vector3D.Distance(p.LastPosition, info.Hit.SurfaceHit);

                        if (info.Hit.Entity is MyCubeGrid) info.WeaponCache.HitBlock = info.Hit.Block;
                    }

                    if (Session.IsClient && info.AimedShot && aConst.ClientPredictedAmmo && !info.IsShrapnel)
                    {
                        var isBeam = aConst.IsBeamWeapon;
                        var vel = isBeam ? Vector3D.Zero : !MyUtils.IsZero(p.Velocity) ? p.Velocity : p.PrevVelocity;

                        var firstHitEntity = info.HitList[0];
                        var hitDist = firstHitEntity.HitDist ?? info.MaxTrajectory;
                        var distToTarget = aConst.IsBeamWeapon ? hitDist : info.MaxTrajectory - info.DistanceTraveled;

                        var intersectOrigin = isBeam ? new Vector3D(p.Beam.From + (info.Direction * distToTarget)) : p.LastPosition;

                        Session.SendFixedGunHitEvent(info.Target.CoreEntity, info.Hit.Entity, intersectOrigin, vel, info.OriginUp, info.MuzzleId, info.System.WeaponIdHash, aConst.AmmoIdxPos, (float) (isBeam ? info.MaxTrajectory : distToTarget));
                        info.AimedShot = false; //to prevent hits on another grid from triggering again
                    }
                    lock(Session.Hits)
                        Session.Hits.Add(p);
                    return;
                }

                info.HitList.Clear();
            },stride);
            FinalHitCheck.Clear();
        }

        internal void ProjectileHit(Projectile attacker, Projectile target, bool lineCheck, ref LineD beam)
        {
            var hitEntity = HitEntityPool.Get();
            hitEntity.Info = attacker.Info;
            hitEntity.EventType = HitEntity.Type.Projectile;
            hitEntity.Hit = true;
            hitEntity.Projectile = target;
            hitEntity.SphereCheck = !lineCheck;
            hitEntity.PruneSphere = attacker.PruneSphere;
            double dist;
            Vector3D.Distance(ref beam.From, ref target.Position, out dist);
            hitEntity.HitDist = dist;

            hitEntity.Intersection = new LineD(attacker.LastPosition, attacker.LastPosition + (attacker.Info.Direction * dist));
            hitEntity.HitPos = hitEntity.Intersection.To;

            lock (attacker.Info.HitList)
                attacker.Info.HitList.Add(hitEntity);

            attacker.FinalizeIntersection = true;
        }

        internal bool GenerateHitInfo(Projectile p)
        {
            var info = p.Info;
            var count = info.HitList.Count;
            if (count > 1)
            {
                try { info.HitList.Sort((x, y) => GetEntityCompareDist(x, y, info)); } // Unable to sort because the IComparer.Compare() method returns inconsistent results
                catch (Exception ex) { Log.Line($"p.Info.HitList.Sort failed: {ex} - weapon:{info.System.PartName} - ammo:{info.AmmoDef.AmmoRound} - hitCount:{info.HitList.Count}", null, true); } 
            }
            else GetEntityCompareDist(info.HitList[0], null, info);
            var pulseTrigger = false;
            
            var voxelFound = false;

            for (int i = info.HitList.Count - 1; i >= 0; i--)
            {
                var ent = info.HitList[i];
                if (ent.EventType == Voxel)
                    voxelFound = true;

                if (!ent.Hit)
                {

                    if (ent.PulseTrigger) pulseTrigger = true;
                    info.HitList.RemoveAtFast(i);
                    HitEntityPool.Return(ent);
                }
                else break;
            }

            if (pulseTrigger)
            {

                info.EwarAreaPulse = true;
                p.DistanceToTravelSqr = info.DistanceTraveled * info.DistanceTraveled;
                p.PrevVelocity = p.Velocity;
                p.Velocity = Vector3D.Zero;
                info.Hit.SurfaceHit = p.Position + info.Direction * info.AmmoDef.Const.EwarTriggerRange;
                info.Hit.LastHit = info.Hit.SurfaceHit;
                info.HitList.Clear();
                return false;
            }

            var finalCount = info.HitList.Count;
            if (finalCount > 0)
            {
                var aConst = info.AmmoDef.Const;
                if (voxelFound && info.HitList[0].EventType != Voxel && aConst.IsBeamWeapon)
                    info.VoxelCache.HitRefreshed = 0;

                var checkHit = (!aConst.IsBeamWeapon || !info.ShieldBypassed || finalCount > 1); ;

                var blockingEnt = !info.ShieldBypassed || finalCount == 1 ? 0 : 1;
                var hitEntity = info.HitList[blockingEnt];

                if (!checkHit)
                    hitEntity.HitPos = p.Beam.To;

                if (hitEntity.EventType == Shield)
                {
                    var cube = hitEntity.Entity as MyCubeBlock;
                    if (cube?.CubeGrid?.Physics != null)
                        p.LastHitEntVel = cube.CubeGrid.Physics.LinearVelocity;
                }
                else if (hitEntity.Projectile != null)
                    p.LastHitEntVel = hitEntity.Projectile?.Velocity;
                else if (hitEntity.Entity?.Physics != null)
                    p.LastHitEntVel = hitEntity.Entity?.Physics.LinearVelocity;
                else p.LastHitEntVel = Vector3.Zero;

                var grid = hitEntity.Entity as MyCubeGrid;

                IMySlimBlock hitBlock = null;
                Vector3D? visualHitPos;
                if (grid != null)
                {
                    if (aConst.VirtualBeams)
                        hitBlock = hitEntity.Blocks[0].Block;

                    IHitInfo hitInfo = null;
                    if (Session.HandlesInput && hitEntity.HitPos.HasValue && Vector3D.DistanceSquared(hitEntity.HitPos.Value, Session.CameraPos) < 22500 && Session.CameraFrustrum.Contains(hitEntity.HitPos.Value) != ContainmentType.Disjoint)
                    {
                        var entSphere = hitEntity.Entity.PositionComp.WorldVolume;
                        var from = hitEntity.Intersection.From + (hitEntity.Intersection.Direction * MyUtils.GetSmallestDistanceToSphereAlwaysPositive(ref hitEntity.Intersection.From, ref entSphere));
                        var to = hitEntity.HitPos.Value + (hitEntity.Intersection.Direction * 3f);
                        Session.Physics.CastRay(from, to, out hitInfo, 15);
                    }
                    visualHitPos = hitInfo?.HitEntity != null ? hitInfo.Position : hitEntity.HitPos;
                }
                else visualHitPos = hitEntity.HitPos;

                info.Hit = new Hit { Block = hitBlock, Entity = hitEntity.Entity, LastHit = visualHitPos ?? Vector3D.Zero, SurfaceHit = visualHitPos ?? Vector3D.Zero, HitVelocity = p.LastHitEntVel ?? Vector3D.Zero, HitTick = Session.Tick};
                if (p.EnableAv)
                {
                    info.AvShot.LastHitShield = hitEntity.EventType == Shield;
                    info.AvShot.Hit = info.Hit;
                }

                return true;
            }
            return false;
        }

        internal int GetEntityCompareDist(HitEntity x, HitEntity y, ProInfo info)
        {
            var xDist = double.MaxValue;
            var yDist = double.MaxValue;
            var beam = x.Intersection;
            var count = y != null ? 2 : 1;
            var eWarPulse = info.AmmoDef.Const.Ewar && info.AmmoDef.Const.Pulse;
            var triggerEvent = eWarPulse && !info.EwarAreaPulse && info.AmmoDef.Const.EwarTriggerRange > 0;
            for (int i = 0; i < count; i++)
            {
                var isX = i == 0;

                MyEntity ent;
                HitEntity hitEnt;
                if (isX)
                {
                    hitEnt = x;
                    ent = hitEnt.Entity;
                }
                else
                {
                    hitEnt = y;
                    ent = hitEnt.Entity;
                }

                var dist = double.MaxValue;
                var shield = ent as IMyTerminalBlock;
                var grid = ent as MyCubeGrid;
                var voxel = ent as MyVoxelBase;

                if (triggerEvent && (info.Ai.Targets.ContainsKey(ent) || shield != null))
                    hitEnt.PulseTrigger = true;
                else if (hitEnt.Projectile != null)
                    dist = hitEnt.HitDist.Value;
                else if (shield != null)
                {
                    hitEnt.Hit = true;
                    dist = hitEnt.HitDist.Value;
                    info.ShieldInLine = true;
                }
                else if (grid != null)
                {
                    if (hitEnt.Hit)
                    {
                        dist = Vector3D.Distance(hitEnt.Intersection.From, hitEnt.HitPos.Value);
                        hitEnt.HitDist = dist;
                    }
                    else if (hitEnt.HitPos != null)
                    {
                        dist = Vector3D.Distance(hitEnt.Intersection.From, hitEnt.HitPos.Value);
                        hitEnt.HitDist = dist;
                        hitEnt.Hit = true;
                    }
                    else
                    {
                        if (hitEnt.SphereCheck || info.EwarActive && eWarPulse)
                        {
                            var ewarActive = hitEnt.EventType == Field || hitEnt.EventType == Effect;

                            var hitPos = !ewarActive ? hitEnt.PruneSphere.Center + (hitEnt.Intersection.Direction * hitEnt.PruneSphere.Radius) : hitEnt.PruneSphere.Center;
                            if (hitEnt.SelfHit && Vector3D.DistanceSquared(hitPos, hitEnt.Info.Origin) <= grid.GridSize * grid.GridSize)
                                continue;

                            if (!ewarActive)
                                GetAndSortBlocksInSphere(hitEnt.Info.AmmoDef, hitEnt.Info.System, grid, hitEnt.PruneSphere, false, hitEnt.Blocks);

                            if (hitEnt.Blocks.Count > 0 || ewarActive)
                            {
                                dist = 0;
                                hitEnt.HitDist = dist;
                                hitEnt.Hit = true;
                                hitEnt.HitPos = hitPos;
                            }
                        }
                        else
                        {

                            var closestBlockFound = false;
                            IMySlimBlock lastBlockHit = null;
                            for (int j = 0; j < hitEnt.Vector3ICache.Count; j++)
                            {
                                var posI = hitEnt.Vector3ICache[j];
                                var firstBlock = grid.GetCubeBlock(posI) as IMySlimBlock;
                                MatrixD transform = grid.WorldMatrix;
                                if (firstBlock != null && firstBlock != lastBlockHit && !firstBlock.IsDestroyed && (hitEnt.Info.Target.CoreCube == null || firstBlock != hitEnt.Info.Target.CoreCube.SlimBlock))
                                {
                                    lastBlockHit = firstBlock;
                                    hitEnt.Blocks.Add(new HitEntity.RootBlocks {Block = firstBlock, QueryPos = posI});
                                    if (closestBlockFound) continue;
                                    MyOrientedBoundingBoxD obb;
                                    var fat = firstBlock.FatBlock;
                                    if (fat != null)
                                        obb = new MyOrientedBoundingBoxD(fat.Model.BoundingBox, fat.PositionComp.WorldMatrixRef);
                                    else
                                    {
                                        Vector3 halfExt;
                                        firstBlock.ComputeScaledHalfExtents(out halfExt);
                                        var blockBox = new BoundingBoxD(-halfExt, halfExt);
                                        transform.Translation = grid.GridIntegerToWorld(firstBlock.Position);
                                        obb = new MyOrientedBoundingBoxD(blockBox, transform);
                                    }

                                    var hitDist = obb.Intersects(ref beam) ?? Vector3D.Distance(beam.From, obb.Center);
                                    var hitPos = beam.From + (beam.Direction * hitDist);

                                    if (hitEnt.SelfHit)
                                    {
                                        if (Vector3D.DistanceSquared(hitPos, hitEnt.Info.Origin) <= grid.GridSize * 3)
                                        {
                                            hitEnt.Blocks.Clear();
                                        }
                                        else
                                        {
                                            dist = hitDist;
                                            hitEnt.HitDist = dist;
                                            hitEnt.Hit = true;
                                            hitEnt.HitPos = hitPos;
                                        }
                                        break;
                                    }

                                    dist = hitDist;
                                    hitEnt.HitDist = dist;
                                    hitEnt.Hit = true;
                                    hitEnt.HitPos = hitPos;
                                    closestBlockFound = true;
                                }
                            }
                        }
                    }
                }
                else if (voxel != null)
                {
                    hitEnt.Hit = true;
                    dist = hitEnt.HitDist.Value;
                    hitEnt.HitDist = dist;
                }
                else if (ent is IMyDestroyableObject)
                {

                    if (hitEnt.Hit) dist = Vector3D.Distance(hitEnt.Intersection.From, hitEnt.HitPos.Value);
                    else
                    {

                        if (hitEnt.SphereCheck || info.EwarActive && eWarPulse)
                        {

                            var ewarActive = hitEnt.EventType == Field || hitEnt.EventType == Effect;
                            dist = 0;
                            hitEnt.HitDist = dist;
                            hitEnt.Hit = true;
                            var hitPos = !ewarActive ? hitEnt.PruneSphere.Center + (hitEnt.Intersection.Direction * hitEnt.PruneSphere.Radius) : hitEnt.PruneSphere.Center;
                            hitEnt.HitPos = hitPos;
                        }
                        else
                        {

                            var transform = ent.PositionComp.WorldMatrixRef;
                            var box = ent.PositionComp.LocalAABB;
                            var obb = new MyOrientedBoundingBoxD(box, transform);
                            dist = obb.Intersects(ref beam) ?? double.MaxValue;
                            if (dist < double.MaxValue)
                            {
                                hitEnt.Hit = true;
                                hitEnt.HitPos = beam.From + (beam.Direction * dist);
                                hitEnt.HitDist = dist;
                            }
                        }
                    }
                }

                if (isX) xDist = dist;
                else yDist = dist;
            }
            return xDist.CompareTo(yDist);
        }

        //TODO: In order to fix SphereShapes collisions with grids, this needs to be adjusted to take into account the Beam of the projectile
        internal static void GetAndSortBlocksInSphere(WeaponDefinition.AmmoDef ammoDef, WeaponSystem system, MyCubeGrid grid, BoundingSphereD sphere, bool fatOnly, List<HitEntity.RootBlocks> blocks)
        {
            var matrixNormalizedInv = grid.PositionComp.WorldMatrixNormalizedInv;
            Vector3D result;
            Vector3D.Transform(ref sphere.Center, ref matrixNormalizedInv, out result);
            var localSphere = new BoundingSphere(result, (float)sphere.Radius);
            var fieldType = ammoDef.Ewar.Type;
            var hitPos = sphere.Center;
            if (fatOnly)
            {
                GridMap map;
                if (system.Session.GridToInfoMap.TryGetValue(grid, out map))
                {
                    foreach (var cube in map.MyCubeBocks)
                    {
                        switch (fieldType)
                        {
                            case JumpNull:
                                if (!(cube is MyJumpDrive)) continue;
                                break;
                            case EnergySink:
                                if (!(cube is IMyPowerProducer)) continue;
                                break;
                            case Anchor:
                                if (!(cube is MyThrust)) continue;
                                break;
                            case Nav:
                                if (!(cube is MyGyro)) continue;
                                break;
                            case Offense:
                                if (!(cube is IMyGunBaseUser)) continue;
                                break;
                            case Emp:
                            case Dot:
                                if (fieldType == Emp && cube is IMyUpgradeModule && system.Session.CoreShieldBlockTypes.Contains(cube.BlockDefinition))
                                    continue;
                                break;
                            default: continue;
                        }
                        var block = cube.SlimBlock as IMySlimBlock;
                        if (!new BoundingBox(block.Min * grid.GridSize - grid.GridSizeHalf, block.Max * grid.GridSize + grid.GridSizeHalf).Intersects(localSphere))
                            continue;
                        blocks.Add(new HitEntity.RootBlocks {Block = block, QueryPos = block.Position});
                    }
                }
            }
            else
            {
                //usage:
                //var dict = (Dictionary<Vector3I, IMySlimBlock>)GetHackDict((IMySlimBlock) null);
                var tmpList = system.Session.SlimPool.Get();
                Session.GetBlocksInsideSphereFast(grid, ref sphere, true, tmpList);

                for (int i = 0; i < tmpList.Count; i++)
                {
                    var block = tmpList[i];
                    blocks.Add(new HitEntity.RootBlocks { Block = block, QueryPos = block.Position});
                }

                system.Session.SlimPool.Return(tmpList);
            }

            blocks.Sort((a, b) =>
            {
                var aPos = grid.GridIntegerToWorld(a.Block.Position);
                var bPos = grid.GridIntegerToWorld(b.Block.Position);
                return Vector3D.DistanceSquared(aPos, hitPos).CompareTo(Vector3D.DistanceSquared(bPos, hitPos));
            });
        }
        public static object GetHackDict<TVal>(TVal valueType) => new Dictionary<Vector3I, TVal>();

    }
}
