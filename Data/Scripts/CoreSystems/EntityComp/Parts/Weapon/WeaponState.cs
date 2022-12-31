﻿using System;
using CoreSystems.Projectiles;
using System.Collections.Generic;
using CoreSystems.Support;
using Sandbox.Game.Entities;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRageMath;
using WeaponCore.Data.Scripts.CoreSystems.Ui.Hud;
using static CoreSystems.Support.WeaponDefinition.AnimationDef.PartAnimationSetDef;

namespace CoreSystems.Platform
{
    public partial class Weapon 
    {

        internal void PositionChanged(MyPositionComponentBase pComp)
        {
            try
            {
                if (PosChangedTick != Comp.Session.SimulationCount)
                    UpdatePivotPos();

                if (Comp.UserControlled) {
                    ReturingHome = false;
                    IsHome = false;
                }
            }
            catch (Exception ex) { Log.Line($"Exception in PositionChanged: {ex}", null, true); }
        }

        internal void TargetChanged()
        {

            if (!Target.HasTarget)
            {
                EventTriggerStateChanged(EventTriggers.Tracking, false);
                if (InCharger) 
                    ExitCharger = true;

                if (Comp.Session.MpActive && Comp.Session.IsServer)  {
                    TargetData.ClearTarget();
                    Target.PushTargetToClient(this);
                } 
            }
            else if (Comp.Session.IsClient)
                EventTriggerStateChanged(EventTriggers.Tracking, true);

            Target.TargetChanged = false;
        }

        internal bool ValidFakeTargetInfo(long playerId, out Ai.FakeTarget.FakeWorldTargetInfo fakeTargetInfo, bool preferPainted = true)
        {
            fakeTargetInfo = null;
            Ai.FakeTargets fakeTargets;
            if (Comp.Session.PlayerDummyTargets.TryGetValue(playerId, out fakeTargets))
            {
                var validManual = Comp.Data.Repo.Values.Set.Overrides.Control == ProtoWeaponOverrides.ControlModes.Manual && Comp.Data.Repo.Values.State.TrackingReticle && fakeTargets.ManualTarget.FakeInfo.WorldPosition != Vector3D.Zero;
                var validPainter = Comp.Data.Repo.Values.Set.Overrides.Control == ProtoWeaponOverrides.ControlModes.Painter && fakeTargets.PaintedTarget.LocalPosition != Vector3D.Zero;
                var fakeTarget = validPainter && preferPainted ? fakeTargets.PaintedTarget : validManual ? fakeTargets.ManualTarget : null;
                if (fakeTarget == null)
                {
                    return false;
                }

                fakeTargetInfo = fakeTarget.LastInfoTick != System.Session.Tick ? fakeTarget.GetFakeTargetInfo(Comp.MasterAi) : fakeTarget.FakeInfo;
            }

            return fakeTargetInfo != null;
        }

        internal void EntPartClose(MyEntity obj)
        {
            obj.PositionComp.OnPositionChanged -= PositionChanged;
            obj.OnMarkForClose -= EntPartClose;
            if (Comp.FakeIsWorking)
                Comp.Status = CoreComponent.Start.ReInit;
        }

        internal void UpdateDesiredPower()
        {
            if (ActiveAmmoDef.AmmoDef.Const.MustCharge)
            {
                var rofPerSecond = RateOfFire / MyEngineConstants.UPDATE_STEPS_PER_SECOND;
                DesiredPower = ((ShotEnergyCost * (rofPerSecond * MyEngineConstants.PHYSICS_STEP_SIZE_IN_SECONDS)) * System.Values.HardPoint.Loading.BarrelsPerShot) * System.Values.HardPoint.Loading.TrajectilesPerBarrel;
            }
            else
                DesiredPower = System.WConst.IdlePower;
        }

        internal void UpdateShotEnergy()
        {
            ShotEnergyCost = (float) (ActiveAmmoDef.AmmoDef.Ewar.Enable ? ActiveAmmoDef.AmmoDef.Const.EnergyCost * ActiveAmmoDef.AmmoDef.Const.EwarStrength : ActiveAmmoDef.AmmoDef.Const.EnergyCost * ActiveAmmoDef.AmmoDef.Const.BaseDamage);
        }

        internal void UpdateBarrelRotation()
        {
            const int loopCnt = 10;
            var interval = System.Values.HardPoint.Loading.DeterministicSpin ? (3600f / System.BarrelSpinRate) * (1f / _numModelBarrels) : (3600f / System.BarrelSpinRate) * ((float)Math.PI / _numModelBarrels);
            var steps = (360f / _numModelBarrels) / interval;
            _ticksBeforeSpinUp = (uint)interval / loopCnt;
            for (int i = 0; i < loopCnt; i++) {

                var multi = (float)(i + 1) / loopCnt;
                var angle = MathHelper.ToRadians(steps * multi);
                switch (System.Values.HardPoint.Other.RotateBarrelAxis) {

                    case 1:
                        BarrelRotationPerShot[i] = SpinPart.ToTransformation * Matrix.CreateRotationX(angle) * SpinPart.FromTransformation;
                        break;
                    case 2:
                        BarrelRotationPerShot[i] = SpinPart.ToTransformation * Matrix.CreateRotationY(angle) * SpinPart.FromTransformation;
                        break;
                    case 3:
                        BarrelRotationPerShot[i] = SpinPart.ToTransformation * Matrix.CreateRotationZ(angle) * SpinPart.FromTransformation;
                        break;
                }
            }
        }

        public void StartShooting()
        {
            if (FiringEmitter != null && !BurstAvDelay) StartFiringSound();
            if (!IsShooting)
            {
                EventTriggerStateChanged(EventTriggers.StopFiring, false);
                if (!ActiveAmmoDef.AmmoDef.Const.Reloadable && !Comp.ModOverride && !ExitCharger)
                    ChargeReload();
            }
            IsShooting = true;
        }

        public void StopShooting(bool burst = false)
        {
            if (IsShooting || PreFired)
            {
                EventTriggerStateChanged(EventTriggers.Firing, false);
                EventTriggerStateChanged(EventTriggers.StopFiring, true, _muzzlesFiring);
            }

            if (System.Session.HandlesInput)
                StopShootingAv(burst);

            ResetShotState();

            //var resetBlock = System.Session.IsServer && Comp.IsBlock && (!Comp.Cube.IsWorking || !Comp.Ai.HasPower) && Comp.Data.Repo.Values.State.TerminalAction != CoreComponent.TriggerActions.TriggerOff;
            //if (resetBlock)
            //    Comp.ResetPlayerControl(false);
        }

        private void ResetShotState()
        {
            FireCounter = 0;
            CeaseFireDelayTick = uint.MaxValue / 2;
            _ticksUntilShoot = 0;
            FinishShots = false;

            if (PreFired)
                UnSetPreFire();

            IsShooting = false;
        }

        internal double GetMaxWeaponRange()
        {
            var ammoMax = ActiveAmmoDef.AmmoDef.Const.MaxTrajectory;
            var hardPointMax = System.WConst.MaxTargetDistance;
            return Math.Min(hardPointMax, ammoMax);
        }

        internal void UpdateWeaponRange()
        {
            var hardPointMax = System.WConst.MaxTargetDistance;
            var range = Comp.Data.Repo.Values.Set.Range < 0 ? hardPointMax : Comp.Data.Repo.Values.Set.Range;
            var ammoMax = ActiveAmmoDef.AmmoDef.Const.MaxTrajectory;
            var weaponRange = Math.Min(hardPointMax, ammoMax);
            MaxTargetDistance = Math.Min(range, weaponRange);
            MaxTargetDistanceSqr = MaxTargetDistance * MaxTargetDistance;
            MaxTargetDistance75Sqr = (MaxTargetDistance * 0.75d) * (MaxTargetDistance * 0.75d);
            MaxTargetDistance50Sqr = (MaxTargetDistance * 0.5d) * (MaxTargetDistance * 0.5d);
            MaxTargetDistance25Sqr = (MaxTargetDistance * 0.25d) * (MaxTargetDistance * 0.25d);
            MinTargetDistance = System.WConst.MinTargetDistance;
            MinTargetDistanceSqr = MinTargetDistance * MinTargetDistance;

            var minBuffer = MinTargetDistance * 0.50;
            var minBufferSqr = (MinTargetDistance + minBuffer) * (MinTargetDistance + minBuffer);
            MinTargetDistanceBufferSqr = minBufferSqr;

            if (Comp.MaxDetectDistance < MaxTargetDistance)
            {
                Comp.MaxDetectDistance = MaxTargetDistance;
                Comp.MaxDetectDistanceSqr = MaxTargetDistanceSqr;
            }

            if (Comp.MinDetectDistance > MinTargetDistance)
            {
                Comp.MinDetectDistance = MinTargetDistance;
                Comp.MinDetectDistanceSqr = MinTargetDistanceSqr;
            }
        }

        internal void RayCallBackClean()
        {
            RayCallBack.Weapon = null;
            RayCallBack = null;
        }

        internal void WakeTargets()
        {
            LastTargetTick = Comp.Session.Tick;
            if (System.Session.IsServer && System.HasRequiresTarget)
            {
                if (Acquire.Monitoring)
                    System.Session.AcqManager.Refresh(Acquire);
                else
                    System.Session.AcqManager.Monitor(Acquire);
            }

            ShortLoadId = Comp.Session.ShortLoadAssigner();
        }

        public void CriticalMonitor()
        {
            var cState = Comp.Data.Repo.Values.State;
            var cSet = Comp.Data.Repo.Values.Set;

            if (cState.CriticalReaction && !Comp.CloseCondition)
                CriticalOnDestruction(true);
            else if (cState.CountingDown && cSet.Overrides.ArmedTimer - 1 >= 0)
            {
                if (--cSet.Overrides.ArmedTimer == 0)
                {
                    CriticalOnDestruction();
                }
            }
        }

        public void CriticalOnDestruction(bool force = false)
        {
            if ((force || Comp.Data.Repo.Values.Set.Overrides.Armed) && !Comp.CloseCondition)
            {
                Comp.CloseCondition = true;
                //Temporarily set Comp.Entity.Parent as a destroyed block does not properly spawn a phantom
                Comp.Session.CreatePhantomEntity(Comp.SubtypeName, 3600, true, 1, System.Values.HardPoint.HardWare.CriticalReaction.AmmoRound, CoreComponent.Trigger.Once, null, Comp.CoreEntity, false, false, Comp.Ai.AiOwner);
            }
        }


        internal void AddTargetToBlock(bool setTarget)
        {
            var entity = Target.TargetObject as MyEntity;
            var targetObj = entity != null ? entity.GetTopMostParent() : Target.TargetObject;

            if (targetObj != null)
            {
                if (setTarget)
                {
                    var grid = targetObj as MyCubeGrid;
                    TopMap map;
                    if (grid != null && System.Session.TopEntityToInfoMap.TryGetValue(grid, out map)) {
                        foreach (var target in map.GroupMap.Construct.Keys)
                        {
                            Comp.AddActiveTarget(this, target);
                        }
                    }
                    else
                    {
                        Comp.AddActiveTarget(this, targetObj);
                    }
                }
                else
                {
                    var grid = targetObj as MyCubeGrid;
                    TopMap map;
                    if (grid != null && System.Session.TopEntityToInfoMap.TryGetValue(grid, out map)) {
                        foreach (var target in map.GroupMap.Construct.Keys)
                        {
                            Comp.RemoveActiveTarget(this, target);
                        }
                    }
                    else
                    {
                        Comp.RemoveActiveTarget(this, targetObj);
                    }
                }
            }
        }

        internal void StoreTargetOnConstruct(bool setTarget)
        {
            var entity = Target.TargetObject as MyEntity;
            var targetObj = entity != null ? entity.GetTopMostParent() : Target.TargetObject;

            var rootConstruct = Comp.Ai.Construct.RootAi.Construct;
            var changedSomeThing = false;
            if (targetObj != null)
            {
                if (setTarget)
                {
                    var grid = targetObj as MyCubeGrid;
                    TopMap map;
                    if (grid != null && System.Session.TopEntityToInfoMap.TryGetValue(grid, out map))
                    {
                        foreach (var target in map.GroupMap.Construct.Keys)
                        {
                            if (rootConstruct.TryAddOrUpdateTrackedTarget(this, target))
                            {
                                changedSomeThing = true;
                                if (System.UniqueTargetPerWeapon)
                                    Comp.AddActiveTarget(this, target);
                            }
                            else
                                Log.Line($"couldn't add target to construct database - {System.ShortName} - {System.RadioType}");
                        }

                    }
                    else
                    {
                        if (rootConstruct.TryAddOrUpdateTrackedTarget(this, targetObj))
                        {
                            changedSomeThing = true;
                            if (System.UniqueTargetPerWeapon)
                                Comp.AddActiveTarget(this, targetObj);
                        }
                        else
                            Log.Line($"couldn't add target to target database - {System.ShortName} - {System.RadioType}");
                    }

                    if (changedSomeThing)
                        Comp.StoredTargets++;
                }
                else
                {
                    var grid = targetObj as MyCubeGrid;
                    TopMap map;
                    if (grid != null && System.Session.TopEntityToInfoMap.TryGetValue(grid, out map))
                    {
                        foreach (var target in map.GroupMap.Construct.Keys)
                        {
                            if (rootConstruct.TryRemoveTrackedTarget(this, target))
                            {
                                changedSomeThing = true;
                                if (System.UniqueTargetPerWeapon)
                                    Comp.RemoveActiveTarget(this, targetObj);
                            }
                            else
                                Log.Line($"couldn't remove target to construct database - {System.ShortName} - {System.RadioType}");
                        }
                    }
                    else
                    {
                        if (rootConstruct.TryRemoveTrackedTarget(this, targetObj))
                        {
                            changedSomeThing = true;
                            if (System.UniqueTargetPerWeapon)
                                Comp.RemoveActiveTarget(this, targetObj);
                        }
                        else
                            Log.Line($"couldn't remove target to target database - {System.ShortName} - {System.RadioType}");
                    }

                    if (changedSomeThing)
                        Comp.StoredTargets--;
                }
            }
        }

        internal void CheckForOverLimit()
        {
            var entity = Target.TargetObject as MyEntity;
            var targetObj = entity != null ? entity.GetTopMostParent() : Target.TargetObject;

            if (targetObj != null)
            {
                var rootConstruct = Comp.Ai.Construct.RootAi.Construct;
                if (rootConstruct.OverLimit(this))
                    StoreTargetOnConstruct(false);
            }

        }

        internal bool DelayedAcquire(Ai.TargetInfo tInfo)
        {
            const int minQueueTime = 5;
            const int requestInterval = 60;
            const int tenMetersSec = 100;
            const int oneMetersSec = 1;
            var s = Comp.Session;
            
            var collisionRisk = tInfo.VelLenSqr > tenMetersSec && tInfo.Approaching;
            var highDamage = tInfo.OffenseRating >= 1;
            var moving = tInfo.VelLenSqr >= oneMetersSec || Comp.Ai.TopEntityVel.LengthSquared() > oneMetersSec;

            var updateRate = collisionRisk ? minQueueTime : highDamage && moving ? 10 : !moving && highDamage ? 20 : 30;

            var queueTime = Math.Min(Math.Max(minQueueTime, HiddenTargets.Count), updateRate);

            HiddenInfo hInfo;
            if (!HiddenTargets.TryGetValue(tInfo.Target, out hInfo))
            {
                HiddenTargets[tInfo.Target] = new HiddenInfo {SlotId = XorRnd.Range(0, queueTime - 1), TickAdded = s.Tick};
            }
            else if (((FailedAcquires + hInfo.SlotId) % queueTime != 0) && s.Tick - hInfo.TickAdded > queueTime * requestInterval)
            {
                AcquiredBlock = true;
                return false;
            }
            return true;
        }

        internal void RecordConnection(bool setTarget)
        {
            var entity = Target.TargetObject as MyEntity;
            var targetObj = entity != null ? entity.GetTopMostParent() : Target.TargetObject;

            var rootConstruct = Comp.Ai.Construct.RootAi.Construct;

            Dictionary<object, Weapon> dict;
            if (targetObj == null || !rootConstruct.TrackedTargets.TryGetValue(System.StorageLocation, out dict))
            {
                if (targetObj != null)
                    Log.Line($"RecordConnection fail1");
                return;
            }

            Weapon master;
            if (dict.TryGetValue(targetObj, out master))
            {
                if (setTarget)
                    master.Connections.Add(this);
                else
                    master.Connections.Remove(this);
            }
        }

        internal enum FriendlyNames
        {
            Normal,
            NoAmmo,
            NoSubSystems,
            NoTarget,
        }

        internal string UpdateAndGetFriendlyName(FriendlyNames type)
        {

            string weaponName;
            var update = LastFriendlyNameTick == 0;
            LastFriendlyNameTick = Comp.Session.Tick;

            if (Comp.Ai.AiType == Ai.AiTypes.Grid && Comp.Collection.Count == 1)
            {
                weaponName = Comp.FunctionalBlock.CustomName;
                update = !weaponName.Equals(FriendlyName);
            }
            else
            {
                weaponName = System.ShortName;
            }

            if (update)
            {
                FriendlyName = weaponName;
                FriendlyNameNoTarget = weaponName + Hud.NoTargetStr;
                FriendlyNameNoAmmo = weaponName + Hud.NoAmmoStr;
                FriendlyNameNoSubsystem = weaponName + Hud.NoSubSystemStr;
            }

            switch (type)
            {
                case FriendlyNames.NoAmmo:
                    return FriendlyNameNoAmmo;
                case FriendlyNames.NoTarget:
                    return FriendlyNameNoTarget;
                case FriendlyNames.NoSubSystems:
                    return FriendlyNameNoSubsystem;
                default:
                    return FriendlyName;
            }
        }
    }
}
