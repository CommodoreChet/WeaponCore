﻿using System;
using CoreSystems.Platform;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRageMath;
using static CoreSystems.Session;
using static CoreSystems.Support.Ai;

namespace CoreSystems.Support
{
    public partial class CoreComponent : MyEntityComponentBase
    {
        public override void OnAddedToContainer()
        {
            try
            {
                ++SceneVersion;
                base.OnAddedToContainer();
                if (Container.Entity.InScene) {
                    LastAddToScene = Session.I.Tick;
                    if (Platform.State == CorePlatform.PlatformState.Fresh)
                        PlatformInit();
                }
                else 
                    Log.Line($"Tried to add comp but it was already scene - {Platform.State} - AiNull:{Ai == null} ");
            }
            catch (Exception ex) { Log.Line($"Exception in OnAddedToContainer: {ex}", null, true); }
        }

        public override void OnAddedToScene()
        {
            try
            {

                ++SceneVersion;
                base.OnAddedToScene();

                if (Platform.State == CorePlatform.PlatformState.Inited || Platform.State == CorePlatform.PlatformState.Ready)
                    ReInit();
                else {

                    if (Platform.State == CorePlatform.PlatformState.Delay)
                        return;
                    
                    if (Platform.State != CorePlatform.PlatformState.Fresh)
                        Log.Line($"OnAddedToScene != Fresh, Inited or Ready: {Platform.State}");

                    PlatformInit();
                }
            }
            catch (Exception ex) { Log.Line($"Exception in OnAddedToScene: {ex}", null, true); }
        }
        
        public override void OnBeforeRemovedFromContainer()
        {
            base.OnBeforeRemovedFromContainer();
        }

        internal void PlatformInit()
        {
            switch (Platform.Init()) {

                case CorePlatform.PlatformState.Invalid:
                    Invalidate();
                    break;
                case CorePlatform.PlatformState.Valid:
                    Platform.PlatformCrash(this, false, true, $"Something went wrong with Platform PreInit: {SubtypeName}");
                    break;
                case CorePlatform.PlatformState.Delay:
                    Session.I.CompsDelayedInit.Add(this);
                    break;
                case CorePlatform.PlatformState.Inited:
                    Init();
                    break;
            }
        }

        internal void Init()
        {
            using (CoreEntity.Pin()) 
            {
                if (!CoreEntity.MarkedForClose && Entity != null) 
                {
                    Ai.FirstRun = true;

                    StorageSetup();

                    if (TypeSpecific != CompTypeSpecific.Phantom && TypeSpecific != CompTypeSpecific.Control && TypeSpecific != CompTypeSpecific.SearchLight) {
                        InventoryInit();

                        if (IsBlock)
                            PowerInit();
                    }

                    Entity.NeedsWorldMatrix = NeedsWorldMatrix;
                    WorldMatrixEnabled = NeedsWorldMatrix;

                    if (IsBlock && !Session.I.TopEntityToInfoMap.ContainsKey(Ai.TopEntity))
                    {
                        Log.Line($"WeaponComp Init did not have GridToInfoMap");
                        Session.I.CompReAdds.Add(new CompReAdd { Ai = Ai, AiVersion = Ai.Version, AddTick = Session.I.Tick, Comp = this });
                    }
                    else OnAddedToSceneTasks(true);

                    Platform.State = CorePlatform.PlatformState.Ready;



                } 
                else Log.Line("BaseComp Init() failed");
            }
        }

        internal void ReInit(bool checkMap = true)
        {
            using (CoreEntity.Pin())  {

                if (!CoreEntity.MarkedForClose && Entity != null)  {

                    if (IsBlock)
                    {
                        TopEntity = GetTopEntity();
                        TopMap topMap;
                        if (checkMap && (!Session.I.TopEntityToInfoMap.TryGetValue(TopEntity, out topMap) || topMap.GroupMap == null)) {
                            
                            if (!InReInit)
                                Session.I.CompsDelayedReInit.Add(this);

                            Session.I.ReInitTick = Session.I.Tick;
                            InReInit = true;
                            return;
                        }

                        if (InReInit)
                            RemoveFromReInit();
                    }

                    Ai ai;
                    if (!Session.I.EntityAIs.TryGetValue(TopEntity, out ai)) {

                        var newAi = Session.I.AiPool.Count > 0 ? Session.I.AiPool.Pop() : new Ai();
                        newAi.Init(TopEntity, TypeSpecific);
                        Session.I.EntityAIs[TopEntity] = newAi;
                        Ai = newAi;
                    }
                    else {
                        Ai = ai;
                    }

                    if (Ai != null) {

                        Ai.FirstRun = true;
                        if (Type == CompType.Weapon && Platform.State == CorePlatform.PlatformState.Inited)
                            Platform.ResetParts();

                        Entity.NeedsWorldMatrix = NeedsWorldMatrix; 
                        WorldMatrixEnabled = NeedsWorldMatrix;

                        // ReInit Counters
                        if (!Ai.PartCounting.ContainsKey(SubTypeId)) // Need to account for reinit case
                            Ai.PartCounting[SubTypeId] = Session.I.PartCountPool.Get();

                        var pCounter = Ai.PartCounting[SubTypeId];
                        pCounter.Max = Platform.Structure.ConstructPartCap;

                        pCounter.Current++;
                        if (IsBlock)
                            Constructs.BuildAiListAndCounters(Ai);
                        // end ReInit

                        OnAddedToSceneTasks(false);
                    }
                    else {
                        Log.Line("BaseComp ReInit() failed stage2!");
                    }
                }
                else {
                    Log.Line($"BaseComp ReInit() failed stage1! - marked:{CoreEntity.MarkedForClose} - Entity:{Entity != null} - hasAi:{Session.I.EntityAIs.ContainsKey(TopEntity)}");
                }
            }
        }

        internal void OnAddedToSceneTasks(bool firstRun)
        {
            try {

                if (Ai.MarkedForClose || CoreEntity.MarkedForClose)
                    Log.Line($"OnAddedToSceneTasks and AI/CoreEntity MarkedForClose - Subtype:{SubtypeName} - grid:{TopEntity.DebugName} - CubeMarked:{CoreEntity.MarkedForClose} - CubeClosed:{CoreEntity.Closed} - CubeInScene:{CoreEntity.InScene} - GridMarked:{TopEntity.MarkedForClose}({CoreEntity.GetTopMostParent()?.MarkedForClose ?? true}) - GridMatch:{TopEntity == Ai.TopEntity} - AiContainsMe:{Ai.CompBase.ContainsKey(CoreEntity)} - MyGridInAi:{Session.I.EntityToMasterAi.ContainsKey(TopEntity)}[{Session.I.EntityAIs.ContainsKey(TopEntity)}]");
                
                Ai.UpdatePowerSources = true;
                RegisterEvents();
                if (!Ai.AiInit) {

                    Ai.AiInit = true;
                    if (IsBlock)
                    {
                        var fatList = Session.I.TopEntityToInfoMap[TopEntity].MyCubeBocks;

                        for (int i = 0; i < fatList.Count; i++)
                        {

                            var cubeBlock = fatList[i];
                            var stator = cubeBlock as IMyMotorStator;
                            var tool = cubeBlock as IMyShipToolBase;
                            var offense = cubeBlock as IMyOffensiveCombatBlock;
                            var flight = cubeBlock as IMyFlightMovementBlock;

                            if (cubeBlock is MyBatteryBlock || cubeBlock.HasInventory || stator != null || tool != null || offense != null || flight != null)
                                Ai.FatBlockAdded(cubeBlock);
                        }
                        var bigOwners = Ai.GridEntity.BigOwners;
                        var oldOwner = Ai.AiOwner;
                        Ai.AiOwner = bigOwners.Count > 0 ? bigOwners[0] : 0;

                        if (oldOwner != Ai.AiOwner)
                            Ai.UpdateFactionColors();
                    }
                }

                if (Type == CompType.Control)
                    ((ControlSys.ControlComponent)this).Platform.Control.CleanControl();

                if (Type == CompType.Weapon)
                    ((Weapon.WeaponComponent)this).OnAddedToSceneWeaponTasks(firstRun);

                Ai.CompBase[CoreEntity] = this;

                Ai.CompChange(true, this);

                Ai.IsStatic = Ai.TopEntity.Physics?.IsStatic ?? false;

                if (IsBlock)
                {
                    if (Platform.Weapons.Count > 0)
                    {
                        MyOrientedBoundingBoxD obb;
                        SUtils.GetBlockOrientedBoundingBox(Cube, out obb);
                        foreach (var weapon in Platform.Weapons)
                        {
                            var scopeInfo = weapon.GetScope.Info;
                            if (!obb.Contains(ref scopeInfo.Position))
                            {
                                var rayBack = new RayD(scopeInfo.Position, -scopeInfo.Direction);
                                weapon.ScopeDistToCheckPos = obb.Intersects(ref rayBack) ?? 0;
                            }
                            Session.I.FutureEvents.Schedule(weapon.DelayedStart, FunctionalBlock.Enabled, 1);
                        }
                    }

                    if (Ai.AiSpawnTick > Ai.Construct.LastRefreshTick || Ai.Construct.LastRefreshTick == 0)
                        Ai.TopEntityMap.GroupMap.UpdateAis();
                }
                else if (TypeSpecific == CompTypeSpecific.Rifle)
                {
                    Ai.TopEntityMap.GroupMap.UpdateAis();
                    Session.I.OnPlayerControl(null, CoreEntity);
                }
                Status = !IsWorking ? Start.Starting : Start.ReInit;
            }

            catch (Exception ex) { Log.Line($"Exception in OnAddedToSceneTasks: {ex} AiNull:{Ai == null}  EntNull{Entity == null} MyCubeNull:{TopEntity == null}", null, true); }
        }

        public override void OnRemovedFromScene()
        {
            try
            {
                ++SceneVersion;
                base.OnRemovedFromScene();
                RemoveComp();
            }
            catch (Exception ex) { Log.Line($"Exception in OnRemovedFromScene: {ex}", null, true); }
        }

        public override bool IsSerialized()
        {
            if (Platform.State == CorePlatform.PlatformState.Ready) {

                if (CoreEntity?.Storage != null) {
                    BaseData.Save();
                }
            }
            return false;
        }

        public override string ComponentTypeDebugString => "CoreSystems";

        private void Invalidate()
        {
            Platform.PlatformCrash(this, false, false, $"Platform PreInit is in an invalid state: {SubtypeName}");
        }
    }
}
