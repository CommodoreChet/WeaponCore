﻿using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using VRage.Game;
using VRage.Game.ObjectBuilders;
using VRage.Utils;
using VRageMath;
using static CoreSystems.Support.PartAnimation;
using static CoreSystems.Support.ValueProcessors;
using static CoreSystems.Support.WeaponDefinition;
using static CoreSystems.Support.WeaponDefinition.AnimationDef.PartAnimationSetDef;
using static CoreSystems.Support.WeaponDefinition.HardPointDef;
using static CoreSystems.Support.WeaponDefinition.AmmoDef.EwarDef;
using static CoreSystems.Support.WeaponDefinition.AmmoDef.AreaOfDamageDef;
namespace CoreSystems.Support
{
    internal class CoreSystem
    {
        public const string Arc = "Arc";
        public HardwareDef.HardwareType PartType;
        public MyStringHash PartNameIdHash;
        public int PartIdHash;
        public int PartId;
        public bool StayCharged;
        public string PartName;
        public Session Session;

        public Dictionary<EventTriggers, PartAnimation[]> WeaponAnimationSet;
        public Dictionary<EventTriggers, uint> PartAnimationLengths;
        public Dictionary<EventTriggers, ParticleEvent[]> ParticleEvents;
        public HashSet<string> AnimationIdLookup;
        public Dictionary<string, EmissiveState> PartEmissiveSet;
        public Dictionary<string, Matrix[]> PartLinearMoveSet;
        public string[] HeatingSubparts;

    }

    internal class UpgradeSystem : CoreSystem
    {
        public readonly UpgradeDefinition Values;

        public float IdlePower;

        public bool AnimationsInited;

        public UpgradeSystem(Session session, MyStringHash partNameIdHash, UpgradeDefinition values, string partName, int partIdHash, int partId)
        {
            Session = session;

            PartNameIdHash = partNameIdHash;

            Values = values;
            PartIdHash = partIdHash;
            PartId = partId;
            PartName = partName;
            PartType = (HardwareDef.HardwareType)Values.HardPoint.HardWare.Type;
            StayCharged = values.HardPoint.Other.StayCharged;
            IdlePower = values.HardPoint.HardWare.IdlePower > 0 ? values.HardPoint.HardWare.IdlePower : 0.001f;

            Session.CreateAnimationSets(Values.Animations, this, out WeaponAnimationSet, out PartEmissiveSet, out PartLinearMoveSet, out AnimationIdLookup, out PartAnimationLengths, out HeatingSubparts, out ParticleEvents);

        }
    }

    internal class SupportSystem : CoreSystem
    {
        public readonly SupportDefinition Values;

        public float IdlePower;

        public bool AnimationsInited;

        public SupportSystem(Session session, MyStringHash partNameIdHash, SupportDefinition values, string partName, int partIdHash, int partId)
        {
            Session = session;

            PartNameIdHash = partNameIdHash;

            Values = values;
            PartIdHash = partIdHash;
            PartId = partId;
            PartName = partName;
            StayCharged = values.HardPoint.Other.StayCharged;
            IdlePower = values.HardPoint.HardWare.IdlePower > 0 ? values.HardPoint.HardWare.IdlePower : 0.001f;

            Session.CreateAnimationSets(Values.Animations, this, out WeaponAnimationSet, out PartEmissiveSet, out PartLinearMoveSet, out AnimationIdLookup, out PartAnimationLengths, out HeatingSubparts, out ParticleEvents);

        }
    }

    internal class WeaponSystem : CoreSystem
    {
        internal class AmmoType
        {
            public MyDefinitionId AmmoDefinitionId;
            public MyDefinitionId EjectionDefinitionId;
            public AmmoDef AmmoDef;
            public string AmmoName;
            public string AmmoNameQueued;
            public bool IsShrapnel;
        }

        internal WeaponConstants WConst;

        public readonly MyStringHash MuzzlePartName;
        public readonly MyStringHash AzimuthPartName;
        public readonly MyStringHash ElevationPartName;
        public readonly MyStringHash SpinPartName;
        public readonly WeaponDefinition Values;
        public readonly AmmoType[] AmmoTypes;
        public readonly MySoundPair PreFireSoundPair;
        public readonly MySoundPair HardPointSoundPair;
        public readonly MySoundPair ReloadSoundPairs;
        public readonly MySoundPair BarrelRotateSoundPair;
        public readonly MySoundPair NoSoundPair;

        public readonly Prediction Prediction;
        public readonly TurretType TurretMovement;
        public readonly FiringSoundState FiringSound;

        public readonly string AltScopeName;
        public readonly string AltEjectorName;
        public readonly string ShortName;
        public readonly string[] Muzzles;

        public readonly int DelayToFire;
        public readonly int CeaseFireDelay;
        public readonly int MinAzimuth;
        public readonly int MaxAzimuth;
        public readonly int MinElevation;
        public readonly int MaxElevation;
        public readonly int MaxHeat;
        public readonly int WeaponIdHash;
        public readonly int WeaponId;
        public readonly int BarrelsPerShot;
        public readonly int BarrelSpinRate;
        public readonly int ShotsPerBurst;

        public readonly bool HasAntiSmart;
        public readonly bool HasAmmoSelection;
        public readonly bool HasEjector;
        public readonly bool HasScope;
        public readonly bool HasBarrelRotation;
        public readonly bool BarrelEffect1;
        public readonly bool BarrelEffect2;
        public readonly bool HasBarrelShootAv;
        public readonly bool TargetSubSystems;
        public readonly bool OnlySubSystems;
        public readonly bool ClosestFirst;
        public readonly bool DegRof;
        public readonly bool TrackProjectile;
        public readonly bool TrackTopMostEntities;
        public readonly bool TrackGrids;
        public readonly bool TrackCharacters;
        public readonly bool TrackMeteors;
        public readonly bool TrackNeutrals;
        public readonly bool TrackTargets;
        public readonly bool HasRequiresTarget;
        public readonly bool DesignatorWeapon;
        public readonly bool DelayCeaseFire;
        public readonly bool AlwaysFireFull;
        public readonly bool WeaponReloadSound;
        public readonly bool NoAmmoSound;
        public readonly bool HardPointRotationSound;
        public readonly bool BarrelRotateSound;
        public readonly bool PreFireSound;
        public readonly bool LockOnFocus;
        public readonly bool HasGuidedAmmo;
        public readonly bool SuppressFire;
        public readonly bool NoSubParts;
        public readonly bool HasSpinPart;
        public readonly bool DebugMode;
        public readonly bool HasProjectileSync;
        public readonly double MaxTargetSpeed;
        public readonly double AzStep;
        public readonly double ElStep;
        public readonly double HomeAzimuth;
        public readonly double HomeElevation;

        public readonly float Barrel1AvTicks;
        public readonly float Barrel2AvTicks;
        public readonly float WepCoolDown;
        public readonly float MinTargetRadius;
        public readonly float MaxTargetRadius;
        public readonly float MaxAmmoVolume;
        public readonly float FullAmmoVolume;
        public readonly float FiringSoundDistSqr;
        public readonly float ReloadSoundDistSqr;
        public readonly float BarrelSoundDistSqr;
        public readonly float HardPointSoundDistSqr;
        public readonly float NoAmmoSoundDistSqr;
        public readonly float HardPointAvMaxDistSqr;
        public readonly float ApproximatePeakPower;

        public bool AnimationsInited;

        public enum FiringSoundState
        {
            None,
            PerShot,
            WhenDone
        }

        public enum TurretType
        {
            Full,
            AzimuthOnly,
            ElevationOnly,
            Fixed //not used yet
        }

        public WeaponSystem(Session session, MyStringHash partNameIdHash, MyStringHash muzzlePartName, MyStringHash azimuthPartName, MyStringHash elevationPartName, MyStringHash spinPartName, WeaponDefinition values, string partName, AmmoType[] weaponAmmoTypes, int weaponIdHash, int weaponId)
        {
            Session = session;

            WConst = new WeaponConstants(session, values);
            
            DebugMode = values.HardPoint.Other.Debug;
            PartNameIdHash = partNameIdHash;
            MuzzlePartName = muzzlePartName;
            DesignatorWeapon = muzzlePartName.String == "Designator";
            AzimuthPartName = azimuthPartName;
            ElevationPartName = elevationPartName;
            SpinPartName = spinPartName;
            NoSubParts = (muzzlePartName.String == "None" || string.IsNullOrEmpty(muzzlePartName.String)) && (AzimuthPartName.String == "None" || string.IsNullOrEmpty(AzimuthPartName.String)) && (ElevationPartName.String == "None" || string.IsNullOrEmpty(ElevationPartName.String));
            HasSpinPart = !string.IsNullOrEmpty(SpinPartName.String) && SpinPartName.String != "None";
            Values = values;
            Muzzles = values.Assignments.Muzzles;
            WeaponIdHash = weaponIdHash;
            WeaponId = weaponId;
            PartName = partName;
            AmmoTypes = weaponAmmoTypes;
            MaxAmmoVolume = Values.HardPoint.HardWare.InventorySize;
            FullAmmoVolume = MaxAmmoVolume * 0.75f;
            CeaseFireDelay = values.HardPoint.DelayCeaseFire;
            DelayCeaseFire = CeaseFireDelay > 0;
            DelayToFire = values.HardPoint.Loading.DelayUntilFire;
            StayCharged = values.HardPoint.Loading.StayCharged || WConst.ReloadTime == 0;
            MaxTargetSpeed = values.Targeting.StopTrackingSpeed > 0 ? values.Targeting.StopTrackingSpeed : double.MaxValue;
            ClosestFirst = values.Targeting.ClosestFirst;
            AlwaysFireFull = values.HardPoint.Loading.FireFull;
            Prediction = Values.HardPoint.AimLeadingPrediction;
            LockOnFocus = Values.HardPoint.Ai.LockOnFocus && !Values.HardPoint.Ai.TrackTargets;
            SuppressFire = Values.HardPoint.Ai.SuppressFire;
            PartType = Values.HardPoint.HardWare.Type;
            HasEjector = !string.IsNullOrEmpty(Values.Assignments.Ejector);
            AltEjectorName = HasEjector ? "subpart_" + Values.Assignments.Ejector : string.Empty;
            HasScope = !string.IsNullOrEmpty(Values.Assignments.Scope);
            AltScopeName = HasScope ? "subpart_" + Values.Assignments.Scope : string.Empty;
            TurretMovements(out AzStep, out ElStep, out MinAzimuth, out MaxAzimuth, out MinElevation, out MaxElevation, out HomeAzimuth, out HomeElevation, out TurretMovement);
            Heat(out DegRof, out MaxHeat, out WepCoolDown);
            BarrelValues(out BarrelsPerShot, out ShotsPerBurst);
            BarrelsAv(out BarrelEffect1, out BarrelEffect2, out Barrel1AvTicks, out Barrel2AvTicks, out BarrelSpinRate, out HasBarrelRotation);
            Track(out TrackProjectile, out TrackGrids, out TrackCharacters, out TrackMeteors, out TrackNeutrals, out TrackTopMostEntities);
            SubSystems(out TargetSubSystems, out OnlySubSystems);
            ValidTargetSize(out MinTargetRadius, out MaxTargetRadius);
            Session.CreateAnimationSets(Values.Animations, this, out WeaponAnimationSet, out PartEmissiveSet, out PartLinearMoveSet, out AnimationIdLookup, out PartAnimationLengths, out HeatingSubparts, out ParticleEvents);

           // CheckForBadAnimations();

            ApproximatePeakPower = WConst.IdlePower;

            var ammoSelections = 0;
            for (int i = 0; i < AmmoTypes.Length; i++) // remap old configs
                RemapLegacy(AmmoTypes[i].AmmoDef);

            TrackTargets = Values.HardPoint.Ai.TrackTargets;

            var requiresTarget = TrackTargets;
            for (int i = 0; i < AmmoTypes.Length; i++)
            {

                var ammo = AmmoTypes[i];
                ammo.AmmoDef.Const = new AmmoConstants(ammo, Values, Session, this, i);
                if (ammo.AmmoDef.Const.GuidedAmmoDetected)
                    HasGuidedAmmo = true;

                if (ammo.AmmoDef.Const.ProjectileSync)
                    HasProjectileSync = true;

                if (ammo.AmmoDef.Const.AntiSmartDetected)
                    HasAntiSmart = true;

                if (ammo.AmmoDef.Const.IsTurretSelectable)
                    ++ammoSelections;

                if (ammo.AmmoDef.Const.ChargSize > ApproximatePeakPower)
                    ApproximatePeakPower = ammo.AmmoDef.Const.ChargSize;

                if (ammo.AmmoDef.Const.RequiresTarget)
                    requiresTarget = true;
            }

            HasRequiresTarget = requiresTarget;

            HasAmmoSelection = ammoSelections > 1;
            HardPointSoundSetup(out WeaponReloadSound, out ReloadSoundPairs, out HardPointRotationSound, out HardPointSoundPair, out BarrelRotateSound, out BarrelRotateSoundPair, out NoAmmoSound, out NoSoundPair, out PreFireSound, out PreFireSoundPair, out HardPointAvMaxDistSqr, out FiringSound);
            HardPointSoundDistMaxSqr(AmmoTypes, out FiringSoundDistSqr, out ReloadSoundDistSqr, out BarrelSoundDistSqr, out HardPointSoundDistSqr, out NoAmmoSoundDistSqr, out HardPointAvMaxDistSqr);

            HasBarrelShootAv = BarrelEffect1 || BarrelEffect2 || HardPointRotationSound || FiringSound != FiringSoundState.None;

            var nameLen = partName.Length;
            if (nameLen > 21)
                ShortName = partName.Remove(21, nameLen - 21);
            else
                ShortName = PartName;
        }


        private void RemapLegacy(AmmoDef ammoDef)
        {
            var oldDetDetected = ammoDef.AreaEffect.Detonation.DetonateOnEnd;
            var oldType = ammoDef.AreaEffect.AreaEffect;
            var oldDamageType = oldType == AmmoDef.AreaDamageDef.AreaEffectType.Explosive || oldType == AmmoDef.AreaDamageDef.AreaEffectType.Radiant;
            if (oldDamageType)
            {
                var checkold = Math.Max(ammoDef.AreaEffect.Base.EffectStrength, ammoDef.AreaEffect.AreaEffectDamage);
                var currentDamage = checkold <= 0 ? ammoDef.BaseDamage : checkold;
                var currentRadius = Math.Max(ammoDef.AreaEffect.Base.Radius, ammoDef.AreaEffect.AreaEffectRadius);
                if (currentDamage > 0 && currentRadius > 0)
                {
                    ammoDef.AreaOfDamage.ByBlockHit.Enable = true;
                    ammoDef.AreaOfDamage.ByBlockHit.Damage = currentDamage;
                    ammoDef.AreaOfDamage.ByBlockHit.Radius = currentRadius;
                    ammoDef.AreaOfDamage.ByBlockHit.Depth = 1;
                    ammoDef.AreaOfDamage.ByBlockHit.Falloff = Falloff.Exponential;
                }
            }

            if (oldDetDetected)
            {
                ammoDef.AreaOfDamage.EndOfLife.Enable = true;
                ammoDef.AreaOfDamage.EndOfLife.Damage = ammoDef.AreaEffect.Detonation.DetonationDamage;
                ammoDef.AreaOfDamage.EndOfLife.Radius = ammoDef.AreaEffect.Detonation.DetonationRadius;
                ammoDef.AreaOfDamage.EndOfLife.Depth = 1;
                ammoDef.AreaOfDamage.EndOfLife.MinArmingTime = ammoDef.AreaEffect.Detonation.MinArmingTime;
                ammoDef.AreaOfDamage.EndOfLife.ArmOnlyOnHit = ammoDef.AreaEffect.Detonation.ArmOnlyOnHit;
                ammoDef.AreaOfDamage.EndOfLife.CustomParticle = ammoDef.AreaEffect.Explosions.CustomParticle;
                ammoDef.AreaOfDamage.EndOfLife.CustomSound = ammoDef.AreaEffect.Explosions.CustomSound;
                ammoDef.AreaOfDamage.EndOfLife.ParticleScale = ammoDef.AreaEffect.Explosions.Scale;
                ammoDef.AreaOfDamage.EndOfLife.NoVisuals = ammoDef.AreaEffect.Explosions.NoVisuals;
                ammoDef.AreaOfDamage.EndOfLife.NoSound = ammoDef.AreaEffect.Explosions.NoSound;
                ammoDef.AreaOfDamage.EndOfLife.Falloff = Falloff.Exponential;
            }

            if (!oldDamageType && oldType != AmmoDef.AreaDamageDef.AreaEffectType.Disabled)
            {
                ammoDef.Ewar.Enable = true;

                ammoDef.Ewar.Radius = ammoDef.AreaEffect.Base.Radius <= 0 ? ammoDef.AreaEffect.AreaEffectDamage : ammoDef.AreaEffect.Base.Radius;
                ammoDef.Ewar.Strength = ammoDef.AreaEffect.Base.EffectStrength <= 0 ? ammoDef.AreaEffect.AreaEffectDamage : ammoDef.AreaEffect.Base.EffectStrength;

                ammoDef.Ewar.Depletable = ammoDef.AreaEffect.EwarFields.Depletable;
                ammoDef.Ewar.NoHitParticle = ammoDef.AreaEffect.EwarFields.DisableParticleEffect;
                ammoDef.Ewar.StackDuration = ammoDef.AreaEffect.EwarFields.StackDuration;
                ammoDef.Ewar.Duration = ammoDef.AreaEffect.EwarFields.Duration;
                ammoDef.Ewar.MaxStacks = ammoDef.AreaEffect.EwarFields.MaxStacks;

                var field = ammoDef.AreaEffect.Pulse.PulseChance > 0 && ammoDef.AreaEffect.EwarFields.TriggerRange > 0;
                ammoDef.Ewar.Mode = field ? EwarMode.Field : EwarMode.Effect;
                ammoDef.Ewar.Type = PickEwarType(ammoDef);

                ammoDef.Ewar.Field.Particle = ammoDef.AreaEffect.Pulse.Particle;

                ammoDef.Ewar.Field.ShowParticle = ammoDef.AreaEffect.Pulse.ShowParticle;
                ammoDef.Ewar.Field.HideModel = ammoDef.AreaEffect.Pulse.HideModel;
                ammoDef.Ewar.Field.GrowTime = ammoDef.AreaEffect.Pulse.GrowTime;
                ammoDef.Ewar.Field.Interval = ammoDef.AreaEffect.Pulse.Interval;
                ammoDef.Ewar.Field.PulseChance = ammoDef.AreaEffect.Pulse.PulseChance;

                ammoDef.Ewar.Field.TriggerRange = ammoDef.AreaEffect.EwarFields.TriggerRange;

                ammoDef.Ewar.Force.DisableRelativeMass = ammoDef.AreaEffect.EwarFields.Force.DisableRelativeMass;
                ammoDef.Ewar.Force.ShooterFeelsForce = ammoDef.AreaEffect.EwarFields.Force.ShooterFeelsForce;
                ammoDef.Ewar.Force.TractorRange = ammoDef.AreaEffect.EwarFields.Force.TractorRange;
                ammoDef.Ewar.Force.ForceFrom = (PushPullDef.Force)ammoDef.AreaEffect.EwarFields.Force.ForceFrom;
                ammoDef.Ewar.Force.ForceTo = (PushPullDef.Force)ammoDef.AreaEffect.EwarFields.Force.ForceTo;
                ammoDef.Ewar.Force.Position = (PushPullDef.Force)ammoDef.AreaEffect.EwarFields.Force.Position;

            }
        }

        private EwarType PickEwarType(AmmoDef def)
        {
            switch (def.AreaEffect.AreaEffect)
            {
                case AmmoDef.AreaDamageDef.AreaEffectType.AntiSmart:
                    return EwarType.AntiSmart;
                case AmmoDef.AreaDamageDef.AreaEffectType.AnchorField:
                    return EwarType.Anchor;
                case AmmoDef.AreaDamageDef.AreaEffectType.TractorField:
                    return EwarType.Tractor;
                case AmmoDef.AreaDamageDef.AreaEffectType.PushField:
                    return EwarType.Push;
                case AmmoDef.AreaDamageDef.AreaEffectType.PullField:
                    return EwarType.Pull;
                case AmmoDef.AreaDamageDef.AreaEffectType.DotField:
                    return EwarType.Dot;
                case AmmoDef.AreaDamageDef.AreaEffectType.EmpField:
                    return EwarType.Emp;
                case AmmoDef.AreaDamageDef.AreaEffectType.EnergySinkField:
                    return EwarType.EnergySink;
                case AmmoDef.AreaDamageDef.AreaEffectType.NavField:
                    return EwarType.Nav;
                case AmmoDef.AreaDamageDef.AreaEffectType.OffenseField:
                    return EwarType.Offense;
                case AmmoDef.AreaDamageDef.AreaEffectType.JumpNullField:
                    return EwarType.JumpNull;
                default:
                    return EwarType.Offense;
            }
        }

        private void CheckForBadAnimations()
        {
            uint delay;
            if (PartAnimationLengths.TryGetValue(EventTriggers.PreFire, out delay) && delay > DelayToFire)
            {
                var message1 = $"This mod uses animation PreFire delay ({delay}) instead of DelayToFire ({DelayToFire}), this will break multiplayer... please report to mod author -- Weapon:{PartName}";
                var message2 = $"ModPath:{Values.ModPath}";
                Log.Line(message1);
                Log.Line(message2);
            }


            if (Values.HardPoint.Loading.ShotsInBurst > 0 && PartAnimationLengths.TryGetValue(EventTriggers.Firing, out delay) && delay > Values.HardPoint.Loading.DelayAfterBurst)
            {
                var message1 = $"This mod uses animation to delay ({delay}) shooting instead of DelayAfterBurst ({Values.HardPoint.Loading.DelayAfterBurst}), this will break multiplayer... please report to mod author -- Weapon:{PartName}";
                var message2 = $"ModPath:{Values.ModPath}";
                Log.Line(message1);
                Log.Line(message2);
            }
        }
        private void Heat(out bool degRof, out int maxHeat, out float wepCoolDown)
        {
            degRof = Values.HardPoint.Loading.DegradeRof;
            maxHeat = Values.HardPoint.Loading.MaxHeat;
            wepCoolDown = Values.HardPoint.Loading.Cooldown;
            if (wepCoolDown < 0) wepCoolDown = 0;
            if (wepCoolDown > .95f) wepCoolDown = .95f;
        }

        private void BarrelsAv(out bool barrelEffect1, out bool barrelEffect2, out float barrel1AvTicks, out float barrel2AvTicks, out int barrelSpinRate, out bool hasBarrelRotation)
        {
            barrelEffect1 = Values.HardPoint.Graphics.Effect1.Name != string.Empty;
            barrelEffect2 = Values.HardPoint.Graphics.Effect2.Name != string.Empty;
            barrel1AvTicks = Values.HardPoint.Graphics.Effect1.Extras.MaxDuration;
            barrel2AvTicks = Values.HardPoint.Graphics.Effect2.Extras.MaxDuration;

            barrelSpinRate = 0;
            if (Values.HardPoint.Other.RotateBarrelAxis != 0)
            {
                if (Values.HardPoint.Loading.BarrelSpinRate > 0) barrelSpinRate = Values.HardPoint.Loading.BarrelSpinRate < 3600 ? Values.HardPoint.Loading.BarrelSpinRate : 3599;
                else barrelSpinRate = WConst.RateOfFire < 3699 ? WConst.RateOfFire : 3599;
            }
            hasBarrelRotation = barrelSpinRate > 0 && (NoSubParts || (MuzzlePartName.String != "None" && !string.IsNullOrEmpty(MuzzlePartName.String)));
        }

        private void BarrelValues(out int barrelsPerShot, out int shotsPerBurst)
        {
            barrelsPerShot = Values.HardPoint.Loading.BarrelsPerShot;
            shotsPerBurst = Values.HardPoint.Loading.ShotsInBurst;
        }

        private void TurretMovements(out double azStep, out double elStep, out int minAzimuth, out int maxAzimuth, out int minElevation, out int maxElevation, out double homeAzimuth, out double homeElevation, out TurretType turretMove)
        {
            azStep = Values.HardPoint.HardWare.RotateRate;
            elStep = Values.HardPoint.HardWare.ElevateRate;
            minAzimuth = Values.HardPoint.HardWare.MinAzimuth;
            maxAzimuth = Values.HardPoint.HardWare.MaxAzimuth;
            minElevation = Values.HardPoint.HardWare.MinElevation;
            maxElevation = Values.HardPoint.HardWare.MaxElevation;

            homeAzimuth = MathHelperD.ToRadians((((Values.HardPoint.HardWare.HomeAzimuth + 180) % 360) - 180));
            homeElevation = MathHelperD.ToRadians((((Values.HardPoint.HardWare.HomeElevation + 180) % 360) - 180));

            turretMove = TurretType.Full;

            if (minAzimuth == maxAzimuth)
                turretMove = TurretType.ElevationOnly;
            if (minElevation == maxElevation && TurretMovement != TurretType.Full)
                turretMove = TurretType.Fixed;
            else if (minElevation == maxElevation)
                turretMove = TurretType.AzimuthOnly;
            else if (NoSubParts)
                turretMove = TurretType.Fixed;
        }

        private void Track(out bool trackProjectile, out bool trackGrids, out bool trackCharacters, out bool trackMeteors, out bool trackNeutrals, out bool trackTopMostEntities)
        {
            trackProjectile = false;
            trackGrids = false;
            trackCharacters = false;
            trackMeteors = false;
            trackNeutrals = false;
            trackTopMostEntities = false;

            var threats = Values.Targeting.Threats;
            foreach (var threat in threats)
            {
                if (threat == TargetingDef.Threat.Projectiles)
                    trackProjectile = true;
                else if (threat == TargetingDef.Threat.Grids)
                {
                    trackGrids = true;
                    trackTopMostEntities = true;
                }
                else if (threat == TargetingDef.Threat.Characters)
                {
                    trackCharacters = true;
                    trackTopMostEntities = true;
                }
                else if (threat == TargetingDef.Threat.Meteors)
                {
                    trackMeteors = true;
                    trackTopMostEntities = true;
                }
                else if (threat == TargetingDef.Threat.Neutrals)
                {
                    trackNeutrals = true;
                    trackTopMostEntities = true;
                }
            }
        }

        private void SubSystems(out bool targetSubSystems, out bool onlySubSystems)
        {
            targetSubSystems = false;
            var anySystemDetected = false;
            if (Values.Targeting.SubSystems.Length > 0)
            {
                foreach (var system in Values.Targeting.SubSystems)
                {
                    if (system != TargetingDef.BlockTypes.Any) targetSubSystems = true;
                    else anySystemDetected = true;
                }
            }
            if (TargetSubSystems && anySystemDetected) onlySubSystems = false;
            else onlySubSystems = true;
        }

        private void ValidTargetSize(out float minTargetRadius, out float maxTargetRadius)
        {
            var minDiameter = Values.Targeting.MinimumDiameter;
            var maxDiameter = Values.Targeting.MaximumDiameter;

            minTargetRadius = (float)(minDiameter > 0 ? minDiameter * 0.5d : 0);
            maxTargetRadius = (float)(maxDiameter > 0 ? maxDiameter * 0.5d : 8192);
        }

        private void HardPointSoundSetup(out bool weaponReloadSound, out MySoundPair reloadSoundPair, out bool hardPointRotationSound, out MySoundPair hardPointSoundPair, out bool barrelRotationSound, out MySoundPair barrelSoundPair, out bool noAmmoSound, out MySoundPair noAmmoSoundPair, out bool preFireSound, out MySoundPair preFireSoundPair, out float hardPointAvMaxDistSqr, out FiringSoundState firingSound)
        {
            weaponReloadSound = Values.HardPoint.Audio.ReloadSound != string.Empty;
            reloadSoundPair = weaponReloadSound ? new MySoundPair(Values.HardPoint.Audio.ReloadSound, false) : null;

            hardPointRotationSound = Values.HardPoint.Audio.HardPointRotationSound != string.Empty;
            hardPointSoundPair = hardPointRotationSound  ? new MySoundPair(Values.HardPoint.Audio.HardPointRotationSound, false) : null;

            barrelRotationSound = Values.HardPoint.Audio.BarrelRotationSound != string.Empty;
            barrelSoundPair = barrelRotationSound ? new MySoundPair(Values.HardPoint.Audio.BarrelRotationSound, false) : null;

            noAmmoSound = Values.HardPoint.Audio.NoAmmoSound != string.Empty;
            noAmmoSoundPair = noAmmoSound ? new MySoundPair(Values.HardPoint.Audio.NoAmmoSound, false) : null;

            preFireSound = Values.HardPoint.Audio.PreFiringSound != string.Empty;
            preFireSoundPair = preFireSound ? new MySoundPair(Values.HardPoint.Audio.PreFiringSound, false) : null;

            var fSoundStart = Values.HardPoint.Audio.FiringSound;
            if (fSoundStart != string.Empty && Values.HardPoint.Audio.FiringSoundPerShot)
                firingSound = FiringSoundState.PerShot;
            else if (fSoundStart != string.Empty && !Values.HardPoint.Audio.FiringSoundPerShot)
                firingSound = FiringSoundState.WhenDone;
            else firingSound = FiringSoundState.None;

            hardPointAvMaxDistSqr = 0;
            if (Values.HardPoint.Graphics.Effect1.Extras.MaxDistance * Values.HardPoint.Graphics.Effect1.Extras.MaxDistance > HardPointAvMaxDistSqr)
                hardPointAvMaxDistSqr = Values.HardPoint.Graphics.Effect1.Extras.MaxDistance * Values.HardPoint.Graphics.Effect1.Extras.MaxDistance;

            if (Values.HardPoint.Graphics.Effect2.Extras.MaxDistance * Values.HardPoint.Graphics.Effect2.Extras.MaxDistance > HardPointAvMaxDistSqr)
                hardPointAvMaxDistSqr = Values.HardPoint.Graphics.Effect2.Extras.MaxDistance * Values.HardPoint.Graphics.Effect2.Extras.MaxDistance;
        }

        private void HardPointSoundDistMaxSqr(AmmoType[] weaponAmmo, out float firingSoundDistSqr, out float reloadSoundDistSqr, out float barrelSoundDistSqr, out float hardPointSoundDistSqr, out float noAmmoSoundDistSqr, out float hardPointAvMaxDistSqr)
        {
            var fireSound = string.Concat(Arc, Values.HardPoint.Audio.FiringSound);
            var reloadSound = string.Concat(Arc, Values.HardPoint.Audio.ReloadSound);
            var barrelSound = string.Concat(Arc, Values.HardPoint.Audio.BarrelRotationSound);
            var hardPointSound = string.Concat(Arc, Values.HardPoint.Audio.HardPointRotationSound);
            var noAmmoSound = string.Concat(Arc, Values.HardPoint.Audio.NoAmmoSound);

            firingSoundDistSqr = 0f;
            reloadSoundDistSqr = 0f;
            barrelSoundDistSqr = 0f;
            hardPointSoundDistSqr = 0f;
            noAmmoSoundDistSqr = 0f;
            hardPointAvMaxDistSqr = HardPointAvMaxDistSqr;

            foreach (var def in Session.SoundDefinitions)
            {
                var id = def.Id.SubtypeId.String;

                if (FiringSound != FiringSoundState.None && id == fireSound)
                {
                    var ob = def.GetObjectBuilder() as MyObjectBuilder_AudioDefinition;
                    if (ob != null) firingSoundDistSqr = ob.MaxDistance * ob.MaxDistance;
                    if (firingSoundDistSqr > hardPointAvMaxDistSqr) hardPointAvMaxDistSqr = FiringSoundDistSqr;
                }
                if (WeaponReloadSound && id == reloadSound)
                {
                    var ob = def.GetObjectBuilder() as MyObjectBuilder_AudioDefinition;
                    if (ob != null) reloadSoundDistSqr = ob.MaxDistance * ob.MaxDistance;
                    if (reloadSoundDistSqr > hardPointAvMaxDistSqr) hardPointAvMaxDistSqr = ReloadSoundDistSqr;

                }
                if (BarrelRotateSound && id == barrelSound)
                {
                    var ob = def.GetObjectBuilder() as MyObjectBuilder_AudioDefinition;
                    if (ob != null) barrelSoundDistSqr = ob.MaxDistance * ob.MaxDistance;
                    if (barrelSoundDistSqr > hardPointAvMaxDistSqr) hardPointAvMaxDistSqr = BarrelSoundDistSqr;
                }
                if (HardPointRotationSound && id == hardPointSound)
                {
                    var ob = def.GetObjectBuilder() as MyObjectBuilder_AudioDefinition;
                    if (ob != null) hardPointSoundDistSqr = ob.MaxDistance * ob.MaxDistance;
                    if (hardPointSoundDistSqr > hardPointAvMaxDistSqr) hardPointAvMaxDistSqr = HardPointSoundDistSqr;
                }
                if (NoAmmoSound && id == noAmmoSound)
                {
                    var ob = def.GetObjectBuilder() as MyObjectBuilder_AudioDefinition;
                    if (ob != null) noAmmoSoundDistSqr = ob.MaxDistance * ob.MaxDistance;
                    if (noAmmoSoundDistSqr > hardPointAvMaxDistSqr) hardPointAvMaxDistSqr = NoAmmoSoundDistSqr;
                }
            }

            if (firingSoundDistSqr <= 0)
                foreach (var ammoType in weaponAmmo)
                    if (ammoType.AmmoDef.Trajectory.MaxTrajectory * ammoType.AmmoDef.Trajectory.MaxTrajectory > firingSoundDistSqr)
                        firingSoundDistSqr = ammoType.AmmoDef.Trajectory.MaxTrajectory * ammoType.AmmoDef.Trajectory.MaxTrajectory;
        }
    
    }

    internal class WeaponConstants
    {
        private const string MaxTargetStr = "MaxTargetDistance";
        private const string MinTargetStr = "MinTargetDistance";
        private const string ROFStr = "RateOfFire";
        private const string ReloadStr = "ReloadTime";
        private const string DeviateStr = "DeviateShotAngle";
        private const string AimTolStr = "AimingTolerance";
        private const string InvSizeStr = "InventorySize";
        private const string HeatPerStr = "HeatPerShot";
        private const string MaxHeatStr = "MaxHeat";
        private const string HeatSinkStr = "HeatSinkRate";
        private const string HeatCDStr = "Cooldown";
        private const string PartCapStr = "ConstructPartCap";
        private const string RestrictRadStr = "RestrictionRadius";
        private const string CheckBoxStr = "CheckInflatedBox";
        private const string CheckAnyStr = "CheckForAnyWeapon";
        private const string MuzzleCheckStr = "MuzzleCheck";
        private const string IdlePowerStr = "IdlePower";

        private readonly Dictionary<string, BaseProcessor> modifierMap = new Dictionary<string, BaseProcessor>()
        {
            {MaxTargetStr, new FloatProcessor() },
            {MinTargetStr, new FloatProcessor() },
            {ROFStr, new IntProcessor() },
            {ReloadStr, new IntProcessor() },
            {DeviateStr, new FloatProcessor() },
            {AimTolStr, new DoubleProcessor() },
            {InvSizeStr, new NonZeroFloatProcessor() },
            {HeatPerStr, new IntProcessor() },
            {MaxHeatStr, new IntProcessor() },
            {HeatSinkStr, new FloatProcessor() },
            {HeatCDStr, new FloatProcessor() },
            {PartCapStr, new NonZeroIntProcessor() },
            {RestrictRadStr, new DoubleProcessor() },
            {CheckBoxStr, new BoolProcessor() },
            {CheckAnyStr, new BoolProcessor() },
            {MuzzleCheckStr, new BoolProcessor() },
            {IdlePowerStr, new FloatProcessor() },
        };

        internal readonly double MaxTargetDistance;
        internal readonly double AimingToleranceRads;

        internal readonly float MinTargetDistance;
        internal readonly float DeviateShotAngleRads;
        internal readonly float IdlePower;
        internal readonly float HeatSinkRate;
        internal readonly int ReloadTime;
        internal readonly int RateOfFire;
        internal readonly int HeatPerShot;
        public readonly int DelayAfterBurst;

        internal readonly uint FireSoundEndDelay;

        public readonly bool GiveUpAfter;
        internal bool SpinFree;
        internal bool DebugMode;
        internal bool HasServerOverrides;
        internal bool FireSoundNoBurst;

        internal WeaponConstants(Session session, WeaponDefinition values)
        {
            FireSoundNoBurst = values.HardPoint.Audio.FireSoundNoBurst;
            FireSoundEndDelay = values.HardPoint.Audio.FireSoundEndDelay;
            DelayAfterBurst = values.HardPoint.Loading.DelayAfterBurst;
            GiveUpAfter = values.HardPoint.Loading.GiveUpAfter;

            SpinFree = values.HardPoint.Loading.SpinFree;
            LoadModifiers(session, values, out HasServerOverrides);
            GetModifiableValues(values, out MaxTargetDistance, out MinTargetDistance, out RateOfFire, out ReloadTime, out DeviateShotAngleRads, out AimingToleranceRads, out IdlePower, out HeatSinkRate, out HeatPerShot, out DebugMode);
        }

        private void LoadModifiers(Session session, WeaponDefinition weaponDef, out bool modsFound)
        {
            modsFound = false;
            Dictionary<string, string> weaponMods;
            if (session.WeaponValuesMap.TryGetValue(weaponDef, out weaponMods) && weaponMods != null)
            {
                foreach (var mod in weaponMods)
                {
                    BaseProcessor processor;
                    if (modifierMap.TryGetValue(mod.Key, out processor))
                        processor.WriteData(mod.Value);
                }
                modsFound = true;
            }
        }

        private void GetModifiableValues(WeaponDefinition weaponDef, out double maxTargetDistance, out float minTargetDistance, out int rateOfFire, out int reloadTime, out float deviateShotAngleRads, out double aimingToleranceRads, out float idlePower, out float heatSinkRate, out int heatPerShot, out bool debugMode)
        {
            var givenMaxDist = HasServerOverrides && modifierMap[MaxTargetStr].HasData() ? modifierMap[MaxTargetStr].GetAsFloat : weaponDef.Targeting.MaxTargetDistance;
            maxTargetDistance = givenMaxDist > 0 ? givenMaxDist : double.MaxValue;

            debugMode = weaponDef.HardPoint.Other.Debug;
            
            minTargetDistance = HasServerOverrides && modifierMap[MinTargetStr].HasData() ? modifierMap[MinTargetStr].GetAsFloat : weaponDef.Targeting.MinTargetDistance;

            rateOfFire = HasServerOverrides && modifierMap[ROFStr].HasData() ? modifierMap[ROFStr].GetAsInt : weaponDef.HardPoint.Loading.RateOfFire;

            reloadTime = HasServerOverrides && modifierMap[ReloadStr].HasData() ? modifierMap[ReloadStr].GetAsInt : weaponDef.HardPoint.Loading.ReloadTime;

            var givenShotAngle = HasServerOverrides && modifierMap[DeviateStr].HasData() ? modifierMap[DeviateStr].GetAsFloat : weaponDef.HardPoint.DeviateShotAngle;
            deviateShotAngleRads = MathHelper.ToRadians(givenShotAngle);

            var givenAimingTolerance = HasServerOverrides && modifierMap[AimTolStr].HasData() ? modifierMap[AimTolStr].GetAsDouble : weaponDef.HardPoint.AimingTolerance;
            aimingToleranceRads = MathHelperD.ToRadians(givenAimingTolerance <= 0 ? 180 : givenAimingTolerance);

            var givenIdlePower = HasServerOverrides && modifierMap[IdlePowerStr].HasData() ? modifierMap[IdlePowerStr].GetAsFloat : weaponDef.HardPoint.HardWare.IdlePower;
            idlePower = givenIdlePower > 0 ? givenIdlePower : 0.001f;

            heatSinkRate = HasServerOverrides && modifierMap[HeatSinkStr].HasData() ? modifierMap[HeatSinkStr].GetAsFloat : weaponDef.HardPoint.Loading.HeatSinkRate;

            heatPerShot = HasServerOverrides && modifierMap[HeatPerStr].HasData() ? modifierMap[HeatPerStr].GetAsInt : weaponDef.HardPoint.Loading.HeatPerShot;
        }
    }
}
