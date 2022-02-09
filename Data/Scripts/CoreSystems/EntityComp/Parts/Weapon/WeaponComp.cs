﻿using System.Collections.Generic;
using CoreSystems.Support;
using Sandbox.Game.Entities;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Weapons;
using VRage.Game;
using VRage.Game.Entity;
using VRageMath;

namespace CoreSystems.Platform
{
    public partial class Weapon
    {
        public class WeaponComponent : CoreComponent
        {
            internal readonly IMyAutomaticRifleGun Rifle;
            internal readonly IMyHandheldGunObject<MyGunBase> GunBase;
            internal readonly IMyLargeTurretBase VanillaTurretBase;
            internal TriggerActions DefaultTrigger;
            internal readonly ShootManager ShootManager;
            internal readonly WeaponCompData Data;
            internal readonly WeaponStructure Structure;
            internal readonly List<Weapon> Collection;
            internal readonly int TotalWeapons;
            internal Weapon TrackingWeapon;
            internal int DefaultAmmoId;
            internal int DefaultReloads;
            internal int MaxAmmoCount;


            internal uint LastRayCastTick;
            internal float EffectiveDps;
            internal float PerfectDps;
            internal float PeakDps;
            internal float RealShotsPerSec;
            internal float ShotsPerSec;
            internal float BaseDps;
            internal float AreaDps;
            internal float DetDps;
            internal bool HasEnergyWeapon;
            internal bool HasGuidance;
            internal bool HasDisabledBurst;
            internal bool HasRofSlider;
            internal bool ShootSubmerged;
            internal bool HasTracking;
            internal bool HasRequireTarget;


            internal WeaponComponent(Session session, MyEntity coreEntity, MyDefinitionId id)
            {
                var cube = coreEntity as MyCubeBlock;

                MyEntity topEntity;
                if (cube != null) {

                    topEntity = cube.CubeGrid;

                    var turret = coreEntity as IMyLargeTurretBase;
                    if (turret != null)
                    {
                        VanillaTurretBase = turret;
                        VanillaTurretBase.EnableIdleRotation = false;
                    }
                }
                else {

                    var gun = coreEntity as IMyAutomaticRifleGun;

                    if (gun != null) {
                        Rifle = gun;
                        GunBase = gun;
                        topEntity = Rifle.Owner;
                    }
                    else
                        topEntity = coreEntity;
                }

                //Bellow order is important
                Data = new WeaponCompData(this);
                ShootManager = new ShootManager(this);
                Init(session, coreEntity, cube != null, Data, topEntity, id);
                Structure = (WeaponStructure)Platform.Structure;
                Collection = TypeSpecific != CompTypeSpecific.Phantom ? Platform.Weapons : Platform.Phantoms;
                TotalWeapons = Structure.HashToId.Count;
            }

            internal MyEntity GetTopEntity()
            {
                var cube = CoreEntity as MyCubeBlock;

                if (cube != null)
                    return cube.CubeGrid; 

                var gun = CoreEntity as IMyAutomaticRifleGun;
                return gun != null ? Rifle.Owner : CoreEntity;
            }



            internal void WeaponInit()
            {

                for (int i = 0; i < Collection.Count; i++)
                {
                    var w = Collection[i];
                        w.UpdatePivotPos();

                    if (Session.IsClient)
                        w.Target.ClientDirty = true;

                    if (w.ProtoWeaponAmmo.CurrentAmmo == 0 && !w.Loading)
                        w.EventTriggerStateChanged(WeaponDefinition.AnimationDef.PartAnimationSetDef.EventTriggers.EmptyOnGameLoad, true);

                    if (TypeSpecific == CompTypeSpecific.Rifle)
                    {
                        Ai.AiOwner = GunBase.OwnerIdentityId;
                        Ai.SmartHandheld = w.System.HasGuidedAmmo;
                    }

                    if (w.TurretAttached) {
                        w.Azimuth = w.System.HomeAzimuth;
                        w.Elevation = w.System.HomeElevation;
                        w.AimBarrel();
                    }
                }
            }

            internal void OnAddedToSceneWeaponTasks(bool firstRun)
            {
                var maxTrajectory1 = 0f;

                var weaponStructure = (WeaponStructure)Platform.Structure;
                if (weaponStructure.MaxLockRange > Ai.Construct.MaxLockRange)
                    Ai.Construct.MaxLockRange = weaponStructure.MaxLockRange;

                if (firstRun && TypeSpecific == CompTypeSpecific.Phantom)
                    Ai.AiOwner = CustomIdentity;
                
                for (int i = 0; i < Collection.Count; i++)
                {
                    var w = Collection[i];

                    if (Session.IsServer)
                        w.ChangeActiveAmmoServer();
                    else
                    {
                        w.ChangeActiveAmmoClient();
                        w.AmmoName = w.ActiveAmmoDef.AmmoName;
                    }

                    if (w.ActiveAmmoDef.AmmoDef == null || !w.ActiveAmmoDef.AmmoDef.Const.IsTurretSelectable && w.System.AmmoTypes.Length > 1)
                    {
                        Platform.PlatformCrash(this, false, true, $"[{w.System.PartName}] Your first ammoType is broken (isNull:{w.ActiveAmmoDef.AmmoDef == null}), I am crashing now Dave.");
                        return;
                    }

                    w.UpdateWeaponRange();
                    if (maxTrajectory1 < w.MaxTargetDistance)
                        maxTrajectory1 = (float)w.MaxTargetDistance;

                }

                if (Data.Repo.Values.Set.Range <= 0)
                    Data.Repo.Values.Set.Range = maxTrajectory1;

                var maxTrajectory2 = 0d;

                for (int i = 0; i < Collection.Count; i++)
                {

                    var weapon = Collection[i];
                    weapon.InitTracking();

                    double weaponMaxRange;
                    DpsAndHeatInit(weapon, out weaponMaxRange);

                    if (maxTrajectory2 < weaponMaxRange)
                        maxTrajectory2 = weaponMaxRange;

                    if (weapon.ProtoWeaponAmmo.CurrentAmmo > weapon.ActiveAmmoDef.AmmoDef.Const.MaxAmmo)
                        weapon.ProtoWeaponAmmo.CurrentAmmo = weapon.ActiveAmmoDef.AmmoDef.Const.MaxAmmo;

                    if (Session.IsServer && weapon.System.HasRequiresTarget)
                        Session.AcqManager.Monitor(weapon.Acquire);
                }

                var expandedMaxTrajectory2 = maxTrajectory2 + Ai.TopEntity.PositionComp.LocalVolume.Radius;
                if (expandedMaxTrajectory2 > Ai.MaxTargetingRange)
                {

                    Ai.MaxTargetingRange = MathHelperD.Min(expandedMaxTrajectory2, Session.Settings.Enforcement.MaxHudFocusDistance);
                    Ai.MaxTargetingRangeSqr = Ai.MaxTargetingRange * Ai.MaxTargetingRange;
                }

                Ai.OptimalDps += PeakDps;
                Ai.EffectiveDps += EffectiveDps;
                Ai.PerfectDps += PerfectDps;

                VanillaTurretBase?.SetTarget(Vector3D.MaxValue);

                if (firstRun)
                    WeaponInit();
            }

            private void DpsAndHeatInit(Weapon weapon, out double maxTrajectory)
            {
                MaxHeat += weapon.System.MaxHeat;

                weapon.RateOfFire = (int)(weapon.System.WConst.RateOfFire * Data.Repo.Values.Set.RofModifier);
                weapon.BarrelSpinRate = (int)(weapon.System.BarrelSpinRate * Data.Repo.Values.Set.RofModifier);
                HeatSinkRate += weapon.HsRate * 3f;

                if (weapon.System.HasBarrelRotation) weapon.UpdateBarrelRotation();

                if (weapon.RateOfFire < 1)
                    weapon.RateOfFire = 1;

                weapon.SetWeaponDps();

                if (!weapon.System.DesignatorWeapon)
                {
                    
                    if (!weapon.System.DesignatorWeapon)
                    {
                        var ammo = weapon.ActiveAmmoDef.AmmoDef;
                        weapon.Comp.PeakDps += ammo.Const.PeakDps;
                        weapon.Comp.EffectiveDps += ammo.Const.EffectiveDps;
                        weapon.Comp.PerfectDps += ammo.Const.PerfectDps;
                        weapon.Comp.RealShotsPerSec += ammo.Const.RealShotsPerSec;
                        weapon.Comp.ShotsPerSec += ammo.Const.ShotsPerSec;
                        weapon.Comp.BaseDps += ammo.Const.BaseDps;
                        weapon.Comp.AreaDps += ammo.Const.AreaDps;
                        weapon.Comp.DetDps += ammo.Const.DetDps;
                    }
                    
                }

                maxTrajectory = 0;
                if (weapon.ActiveAmmoDef.AmmoDef.Const.MaxTrajectory > maxTrajectory)
                    maxTrajectory = weapon.ActiveAmmoDef.AmmoDef.Const.MaxTrajectory;

                if (weapon.System.TrackProjectile)
                    Ai.PointDefense = true;
            }

            internal bool AllWeaponsOutOfAmmo()
            {
                var wCount = Collection.Count;
                var outCount = 0;

                for (int i = 0; i < wCount; i++) {
                    var w = Collection[i];
                    if (w.Reload.CurrentMags == 0 && w.ProtoWeaponAmmo.CurrentAmmo == 0)
                        ++outCount;
                }
                return outCount == wCount;
            }

            internal static void SetRange(Weapon.WeaponComponent comp)
            {
                foreach (var w in comp.Collection)
                {
                    w.UpdateWeaponRange();
                }
            }

            internal static void SetRof(Weapon.WeaponComponent comp)
            {
                for (int i = 0; i < comp.Collection.Count; i++)
                {
                    var w = comp.Collection[i];

                    w.UpdateRof();
                }

                SetDps(comp);
            }

            internal static void SetDps(Weapon.WeaponComponent comp, bool change = false)
            {
                if (comp == null || comp.Platform.State != CorePlatform.PlatformState.Ready) return;

                for (int i = 0; i < comp.Collection.Count; i++)
                {
                    var w = comp.Collection[i];
                    comp.Session.FutureEvents.Schedule(w.SetWeaponDps, null, 1);
                }
            }

            internal void ResetShootState(TriggerActions action, long playerId)
            {
                var cycleShootClick = Data.Repo.Values.State.TerminalAction == TriggerActions.TriggerClick && action == TriggerActions.TriggerClick;
                var cycleShootOn = Data.Repo.Values.State.TerminalAction == TriggerActions.TriggerOn && action == TriggerActions.TriggerOn;
                var cycleSomething = cycleShootOn || cycleShootClick;

                Data.Repo.Values.Set.Overrides.Control = ProtoWeaponOverrides.ControlModes.Auto;

                Data.Repo.Values.State.TerminalActionSetter(this, cycleSomething ? TriggerActions.TriggerOff : action);

                if (action == TriggerActions.TriggerClick || action == TriggerActions.TriggerOn)
                    Data.Repo.Values.State.Control = ProtoWeaponState.ControlMode.Ui;
                else
                    Data.Repo.Values.State.Control = ProtoWeaponState.ControlMode.None;

                playerId = Session.HandlesInput && playerId == -1 ? Session.PlayerId : playerId;
                var noReset = !Data.Repo.Values.State.TrackingReticle;
                var newId = action == TriggerActions.TriggerOff && noReset ? -1 : playerId;
                Data.Repo.Values.State.PlayerId = newId;
            }

            internal void RequestShootUpdate(TriggerActions action, long playerId)
            {
                if (IsDisabled) return;

                if (IsBlock && Session.HandlesInput)
                    Session.TerminalMon.HandleInputUpdate(this);

                if (Session.IsServer)
                {
                    ResetShootState(action, playerId);

                    if (Session.MpActive)
                    {
                        Session.SendComp(this);
                        if (action == TriggerActions.TriggerClick || action == TriggerActions.TriggerOn)
                        {
                            foreach (var w in Collection)
                            {
                                //Session.SendWeaponAmmoData(w);
                                Session.SendWeaponReload(w);
                            }
                        }
                    }

                }
                else Session.SendActionShootUpdate(this, action);
            }

            internal void DetectStateChanges()
            {
                if (Platform.State != CorePlatform.PlatformState.Ready)
                    return;

                if (Session.Tick - Ai.LastDetectEvent > 59)
                {
                    Ai.LastDetectEvent = Session.Tick;
                    Ai.SleepingComps = 0;
                    Ai.AwakeComps = 0;
                    Ai.DetectOtherSignals = false;
                }

                UpdatedState = true;

                var overRides = Data.Repo.Values.Set.Overrides;
                var attackNeutrals = overRides.Neutrals;
                var attackNoOwner = overRides.Unowned;
                var attackFriends = overRides.Friendly;
                var targetNonThreats = (attackNoOwner || attackNeutrals || attackFriends);

                DetectOtherSignals = targetNonThreats;
                if (DetectOtherSignals)
                    Ai.DetectOtherSignals = true;
                var wasAsleep = IsAsleep;
                IsAsleep = false;
                IsDisabled = Ai.TouchingWater && !ShootSubmerged && Ai.WaterVolume.Contains(CoreEntity.PositionComp.WorldAABB.Center) != ContainmentType.Disjoint;
                
                if (!Ai.Session.IsServer)
                    return;

                var otherRangeSqr = Ai.DetectionInfo.OtherRangeSqr;
                var threatRangeSqr = Ai.DetectionInfo.PriorityRangeSqr;
                var targetInrange = DetectOtherSignals ? otherRangeSqr <= MaxDetectDistanceSqr && otherRangeSqr >= MinDetectDistanceSqr || threatRangeSqr <= MaxDetectDistanceSqr && threatRangeSqr >= MinDetectDistanceSqr : threatRangeSqr <= MaxDetectDistanceSqr && threatRangeSqr >= MinDetectDistanceSqr;
                if (Ai.Session.Settings.Enforcement.ServerSleepSupport && !targetInrange && PartTracking == 0 && Ai.Construct.RootAi.Construct.ControllingPlayers.Count <= 0 && Session.TerminalMon.Comp != this && Data.Repo.Values.State.TerminalAction == TriggerActions.TriggerOff)
                {

                    IsAsleep = true;
                    Ai.SleepingComps++;
                }
                else if (wasAsleep)
                {

                    Ai.AwakeComps++;
                }
                else
                    Ai.AwakeComps++;
            }

            internal void ResetPlayerControl()
            {
                Data.Repo.Values.State.PlayerId = -1;
                Data.Repo.Values.State.Control = ProtoWeaponState.ControlMode.None;
                Data.Repo.Values.Set.Overrides.Control = ProtoWeaponOverrides.ControlModes.Auto;
                if (Data.Repo.Values.Set.Overrides.ShootMode == ShootManager.ShootModes.MouseControl)
                    Data.Repo.Values.Set.Overrides.ShootMode = ShootManager.ShootModes.Inactive;

                Data.Repo.Values.State.TrackingReticle = false;
                
                var tAction = Data.Repo.Values.State.TerminalAction;
                if (tAction == TriggerActions.TriggerClick)
                    Data.Repo.Values.State.TerminalActionSetter(this, TriggerActions.TriggerOff);
                if (Session.MpActive)
                    Session.SendComp(this);
            }

            internal static void RequestCountDown(WeaponComponent comp, bool value)
            {
                if (value != comp.Data.Repo.Values.State.CountingDown)
                {
                    comp.Session.SendCountingDownUpdate(comp, value);
                }
            }

            internal static void RequestCriticalReaction(WeaponComponent comp)
            {
                if (true != comp.Data.Repo.Values.State.CriticalReaction)
                {
                    comp.Session.SendTriggerCriticalReaction(comp);
                }
            }

            internal static void RequestSetValue(WeaponComponent comp, string setting, int value, long playerId)
            {
                if (comp.Session.IsServer)
                {
                    SetValue(comp, setting, value, playerId);
                }
                else if (comp.Session.IsClient)
                {
                    comp.Session.SendOverRidesClientComp(comp, setting, value);
                }
            }

            internal static void SetValue(WeaponComponent comp, string setting, int v, long playerId)
            {
                var o = comp.Data.Repo.Values.Set.Overrides;
                var enabled = v > 0;
                var clearTargets = false;
                var resetState = false;
                switch (setting)
                {
                    case "MaxSize":
                        o.MaxSize = v;
                        clearTargets = true;
                        break;
                    case "MinSize":
                        o.MinSize = v;
                        clearTargets = true;
                        break;
                    case "SubSystems":
                        o.SubSystem = (WeaponDefinition.TargetingDef.BlockTypes)v;
                        clearTargets = true;
                        break;
                    case "MovementModes":
                        o.MoveMode = (ProtoWeaponOverrides.MoveModes)v;
                        clearTargets = true;
                        break;
                    case "ControlModes":
                        o.Control = (ProtoWeaponOverrides.ControlModes)v;
                        clearTargets = true;
                        resetState = true;
                        break;
                    case "FocusSubSystem":
                        o.FocusSubSystem = enabled;
                        break;
                    case "FocusTargets":
                        o.FocusTargets = enabled;
                        clearTargets = true;
                        break;
                    case "Unowned":
                        o.Unowned = enabled;
                        clearTargets = true;
                        break;
                    case "Friendly":
                        o.Friendly = enabled;
                        clearTargets = true;
                        break;
                    case "Meteors":
                        o.Meteors = enabled;
                        break;
                    case "Grids":
                        o.Grids = enabled;
                        clearTargets = true;
                        break;
                    case "Biologicals":
                        o.Biologicals = enabled;
                        clearTargets = true;
                        break;
                    case "Projectiles":
                        o.Projectiles = enabled;
                        clearTargets = true;
                        break;
                    case "Neutrals":
                        o.Neutrals = enabled;
                        clearTargets = true;
                        break;
                    case "Repel":
                        o.Repel = enabled;
                        clearTargets = true;
                        break;
                    case "CameraChannel":
                        o.CameraChannel = v;
                        break;
                    case "BurstCount":
                        o.BurstCount = v;
                        break;
                    case "BurstDelay":
                        o.BurstDelay = v;
                        break;
                    case "SequenceId":
                        o.SequenceId = v;
                        break;
                    case "WeaponGroupId":
                        o.WeaponGroupId = v;
                        break;
                    case "ShootMode":
                        o.ShootMode = (ShootManager.ShootModes)v;
                        break;
                    case "LeadGroup":
                        o.LeadGroup = v;
                        break;
                    case "ArmedTimer":
                        o.ArmedTimer = v;
                        break;
                    case "Armed":
                        o.Armed = enabled;
                        break;
                    case "Debug":
                        o.Debug = enabled;
                        break;
                    case "Override":
                        o.Override = enabled;
                        break;
                }

                ResetCompState(comp, playerId, clearTargets, resetState);

                if (comp.Session.MpActive)
                    comp.Session.SendComp(comp);
            }

            internal static void ResetCompState(WeaponComponent comp, long playerId, bool resetTarget, bool resetState, Dictionary<string, int> settings = null)
            {
                var o = comp.Data.Repo.Values.Set.Overrides;
                var userControl = o.Control != ProtoWeaponOverrides.ControlModes.Auto;
                
                //comp.RequestShootBurstId = comp.Data.Repo.Values.State.ShootSyncStateId;
                //comp.WeaponsFired = 0;

                if (userControl)
                {
                    comp.Data.Repo.Values.State.PlayerId = playerId;
                    comp.Data.Repo.Values.State.Control = ProtoWeaponState.ControlMode.Ui;
                    if (settings != null) settings["ControlModes"] = (int)o.Control;
                    comp.Data.Repo.Values.State.TerminalActionSetter(comp, TriggerActions.TriggerOff);
                }
                else if (resetState)
                {
                    comp.Data.Repo.Values.State.Control = ProtoWeaponState.ControlMode.None;
                }
                else if (o.ShootMode != ShootManager.ShootModes.Inactive)
                {
                    comp.Data.Repo.Values.State.PlayerId = playerId;
                }

                if (resetTarget)
                    ClearTargets(comp);
            }

            private static void ClearTargets(Weapon.WeaponComponent comp)
            {
                for (int i = 0; i < comp.Collection.Count; i++)
                {
                    var weapon = comp.Collection[i];
                    if (weapon.Target.HasTarget)
                        comp.Collection[i].Target.Reset(comp.Session.Tick, Target.States.ControlReset);
                }
            }

            internal void NotFunctional()
            {
                for (int i = 0; i < Collection.Count; i++)
                {

                    var w = Collection[i];
                    PartAnimation[] partArray;
                    if (w.AnimationsSet.TryGetValue(WeaponDefinition.AnimationDef.PartAnimationSetDef.EventTriggers.TurnOff, out partArray))
                    {
                        for (int j = 0; j < partArray.Length; j++)
                            w.PlayEmissives(partArray[j]);
                    }
                    if (!Session.IsClient && !IsWorking)
                        w.Target.Reset(Session.Tick, Target.States.Offline);
                }
            }

            internal void PowerLoss()
            {
                Session.SendComp(this);
                if (IsWorking)
                {
                    foreach (var w in Collection)
                    {
                        Session.SendWeaponAmmoData(w);
                        Session.SendWeaponReload(w);
                    }
                }
            }


            internal void GeneralWeaponCleanUp()
            {
                if (Platform?.State == CorePlatform.PlatformState.Ready)
                {
                    foreach (var w in Collection)
                    {

                        w.RayCallBackClean();

                        w.Comp.Session.AcqManager.Asleep.Remove(w.Acquire);
                        w.Comp.Session.AcqManager.MonitorState.Remove(w.Acquire);
                        w.Acquire.Monitoring = false;
                        w.Acquire.IsSleeping = false;

                    }
                }
            }

            public void CleanCompParticles()
            {
                if (Platform?.State == CorePlatform.PlatformState.Ready)
                {
                    foreach (var w in Collection)
                    {
                        for (int i = 0; i < w.System.Values.Assignments.Muzzles.Length; i++)
                        {
                            if (w.HitEffects?[i] != null)
                            {
                                Log.Line($"[Clean CompHitPartice] Weapon:{w.System.PartName} - Particle:{w.HitEffects[i].GetName()}");
                                w.HitEffects[i].Stop();
                                w.HitEffects[i] = null;
                            }
                            if (w.Effects1?[i] != null)
                            {
                                Log.Line($"[Clean Effects1] Weapon:{w.System.PartName} - Particle:{w.Effects1[i].GetName()}");
                                w.Effects1[i].Stop();
                                w.Effects1[i] = null;
                            }
                            if (w.Effects2?[i] != null)
                            {
                                Log.Line($"[Clean Effects2] Weapon:{w.System.PartName} - Particle:{ w.Effects2[i].GetName()}");
                                w.Effects2[i].Stop();
                                w.Effects2[i] = null;
                            }
                        }
                    }
                }
            }

            public void CleanCompSounds()
            {
                if (Platform?.State == CorePlatform.PlatformState.Ready)
                {

                    foreach (var w in Collection)
                    {

                        if (w.AvCapable && w.System.FiringSound == WeaponSystem.FiringSoundState.WhenDone)
                            Session.SoundsToClean.Add(new Session.CleanSound { Force = true, Emitter = w.FiringEmitter, EmitterPool = Session.Emitters, SpawnTick = Session.Tick });

                        if (w.AvCapable && w.System.PreFireSound)
                            Session.SoundsToClean.Add(new Session.CleanSound { Force = true, Emitter = w.PreFiringEmitter, EmitterPool = Session.Emitters, SpawnTick = Session.Tick });

                        if (w.AvCapable && w.System.WeaponReloadSound)
                            Session.SoundsToClean.Add(new Session.CleanSound { Force = true, Emitter = w.ReloadEmitter, EmitterPool = Session.Emitters, SpawnTick = Session.Tick });

                        if (w.AvCapable && w.System.BarrelRotateSound)
                            Session.SoundsToClean.Add(new Session.CleanSound { Emitter = w.BarrelRotateEmitter, EmitterPool = Session.Emitters, SpawnTick = Session.Tick });
                    }

                    if (Session.PurgedAll)
                    {
                        Session.CleanSounds();
                        Log.Line("purged already called");
                    }
                }
            }

            public void StopAllSounds()
            {
                foreach (var w in Collection)
                {
                    w.StopReloadSound();
                    w.StopBarrelRotateSound();
                    w.StopShootingAv(false);
                    //w.IsShooting = false;
                }
            }
        }
    }
}
