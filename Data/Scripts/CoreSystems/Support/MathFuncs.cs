﻿using System;
using CoreSystems.Platform;
using VRage.Utils;
using VRageMath;

namespace CoreSystems.Support
{
    internal class ProNavGuidance : RelNavGuidance
    {
        public ProNavGuidance(double updatesPerSecond, double missileAcceleration, double navConstant, double navAccelConstant = 0) : base(updatesPerSecond, missileAcceleration, navConstant, navAccelConstant) { }

        protected override Vector3D GetLatax(Vector3D missileToTarget, Vector3D missileToTargetNorm, Vector3D relativeVelocity, Vector3D lateralTargetAcceleration, Vector3D gravityCompensationTerm)
        {
            Vector3D omega = Vector3D.Cross(missileToTarget, relativeVelocity) / Math.Max(missileToTarget.LengthSquared(), 1); //to combat instability at close range
            return NavConstant * relativeVelocity.Length() * Vector3D.Cross(omega, missileToTargetNorm)
                   + NavAccelConstant * lateralTargetAcceleration
                   + gravityCompensationTerm; //normal to LOS
        }
    }

    internal abstract class RelNavGuidance : MissileGuidanceBase
    {
        public double NavConstant;
        public double NavAccelConstant;

        protected RelNavGuidance(double updatesPerSecond, double missileAcceleration, double navConstant, double navAccelConstant = 0) : base(updatesPerSecond, missileAcceleration)
        {
            NavConstant = navConstant;
            NavAccelConstant = navAccelConstant;
        }

        protected abstract Vector3D GetLatax(Vector3D missileToTarget, Vector3D missileToTargetNorm, Vector3D relativeVelocity, Vector3D lateralTargetAcceleration, Vector3D gravityCompensationTerm);

        protected override Vector3D GetPointingVector(Vector3D missilePosition, Vector3D missileVelocity, Vector3D targetPosition, Vector3D targetVelocity, Vector3D targetAcceleration, Vector3D gravity)
        {
            Vector3D missileToTarget = targetPosition - missilePosition;
            Vector3D missileToTargetNorm = Vector3D.Normalize(missileToTarget);
            Vector3D relativeVelocity = targetVelocity - missileVelocity;
            Vector3D lateralTargetAcceleration = (targetAcceleration - Vector3D.Dot(targetAcceleration, missileToTargetNorm) * missileToTargetNorm);
            Vector3D gravityCompensationTerm = 1.1 * -(gravity - Vector3D.Dot(gravity, missileToTargetNorm) * missileToTargetNorm);

            Vector3D lateralAcceleration = GetLatax(missileToTarget, missileToTargetNorm, relativeVelocity, lateralTargetAcceleration, gravityCompensationTerm);

            if (Vector3D.IsZero(lateralAcceleration))
                return missileToTarget;

            double diff = MissileAcceleration * MissileAcceleration - lateralAcceleration.LengthSquared();
            if (diff < 0)
                return lateralAcceleration; //fly parallel to the target
            return lateralAcceleration + Math.Sqrt(diff) * missileToTargetNorm;
        }
    }

    internal abstract class MissileGuidanceBase
    {
        public double MissileAcceleration;
        protected double _deltaTime;
        protected double _updatesPerSecond;

        Vector3D _lastVelocity;
        bool _lastVelocitySet;

        protected MissileGuidanceBase(double updatesPerSecond, double missileAcceleration)
        {
            MissileAcceleration = missileAcceleration;
            _updatesPerSecond = updatesPerSecond;
            _deltaTime = 1.0 / _updatesPerSecond;
        }

        public void ClearAcceleration()
        {
            _lastVelocitySet = false;
        }

        public Vector3D Update(Vector3D missilePosition, Vector3D missileVelocity, Vector3D targetPosition, Vector3D targetVelocity, Vector3D? gravity = null)
        {
            Vector3D targetAcceleration = Vector3D.Zero;
            if (!_lastVelocitySet)
                _lastVelocitySet = true;
            else
                targetAcceleration = (targetVelocity - _lastVelocity) * _updatesPerSecond;
            _lastVelocity = targetVelocity;

            return GetPointingVector(missilePosition, missileVelocity, targetPosition, targetVelocity, targetAcceleration, gravity == null ? Vector3D.Zero : gravity.Value);
        }

        protected abstract Vector3D GetPointingVector(Vector3D missilePosition, Vector3D missileVelocity, Vector3D targetPosition, Vector3D targetVelocity, Vector3D targetAcceleration, Vector3D gravity);
    }

    internal static class MathFuncs
    {
        internal struct Cone
        {
            internal Vector3D ConeDir;
            internal Vector3D ConeTip;
            internal double ConeAngle;
        }

        internal static bool TargetSphereInCone(ref BoundingSphereD targetSphere, ref Cone cone)
        {
            Vector3D toSphere = targetSphere.Center - cone.ConeTip;
            var angPos = AngleBetween(cone.ConeDir, toSphere);
            double angRad = Math.Asin(targetSphere.Radius / toSphere.Length());

            var ang1 = angPos + angRad;
            var ang2 = angPos - angRad;

            if (ang1 < -cone.ConeAngle)
                return false; 

            if (ang2 > cone.ConeAngle)
                return false; 

            return true;
        }

        internal static double? IntersectEllipsoid(MatrixD ellipsoidMatrixInv, MatrixD ellipsoidMatrix, RayD ray)
        {
            var normSphere = new BoundingSphereD(Vector3.Zero, 1f);
            var kRay = new RayD(Vector3D.Zero, Vector3D.Forward);

            Vector3D krayPos;
            Vector3D.Transform(ref ray.Position, ref ellipsoidMatrixInv, out krayPos);

            Vector3D nDir;
            Vector3D.TransformNormal(ref ray.Direction, ref ellipsoidMatrixInv, out nDir);

            Vector3D krayDir;
            Vector3D.Normalize(ref nDir, out krayDir);

            kRay.Direction = krayDir;
            kRay.Position = krayPos;
            var nullDist = normSphere.Intersects(kRay);
            if (!nullDist.HasValue) return null;

            var hitPos = krayPos + (krayDir * -nullDist.Value);
            Vector3D worldHitPos;
            Vector3D.Transform(ref hitPos, ref ellipsoidMatrix, out worldHitPos);

            double dist;
            Vector3D.Distance(ref worldHitPos, ref ray.Position, out dist);
            return (double.IsNaN(dist) ? (double?) null : dist);
        }

        public static bool PointInEllipsoid(Vector3D point, MatrixD ellipsoidMatrixInv)
        {
            return Vector3D.Transform(point, ellipsoidMatrixInv).LengthSquared() <= 1;
        }

        internal static bool IsDotProductWithinTolerance(ref Vector3D targetDir, ref Vector3D refDir, double tolerance)
        {
            double dot = Vector3D.Dot(targetDir, refDir);
            double num = targetDir.LengthSquared() * refDir.LengthSquared() * tolerance * Math.Abs(tolerance);
            return Math.Abs(dot) * dot > num;
        }

        /*
        This sausage of a method was made by combining my MissileGuidance class into a single call
        because... because Keen won't let us have nice things.

        This is derived using a lateral acceleration (latax) that is normal to the line of sight vector.
        This is a much more stable solution than using a latax that is normal to the velocity vector
        because I don't need to screw with the dozen or so edge cases you get with that geometry.

        - Whiplash141 the Salty
        */
        internal static Vector3D ProNavGuidanceButAllInOneMethod(
            double NavConstant, // Nominally: 3-5
            double NavAccelConstant, // Nominally: NavConstant / 2
            ref Vector3D targetPosition,
            ref Vector3D targetVelocity,
            ref Vector3D targetAcceleration,
            ref Vector3D missilePosition,
            ref Vector3D missileVelocity,
            ref Vector3D gravity, // Plug in Vector3D.Zero if you don't give a shit about gravity
            double missileAccelerationMagnitude, // m/s^2
            double updateIntervalInSeconds)
        {
            Vector3D missileToTarget = targetPosition - missilePosition;
            Vector3D missileToTargetNorm = Vector3D.Normalize(missileToTarget);
            Vector3D relativeVelocity = targetVelocity - missileVelocity;
            Vector3D lateralTargetAcceleration = (targetAcceleration - Vector3D.Dot(targetAcceleration, missileToTargetNorm) * missileToTargetNorm);
            Vector3D gravityCompensationTerm = 1.1 * -(gravity - Vector3D.Dot(gravity, missileToTargetNorm) * missileToTargetNorm);
            /*
            This is where the magic happens. Here we are using the angular rate of change as the "error" 
            that we are going to slap into an augmented PROPORTIONAL controller. That is where the name
            comes from.
            */
            Vector3D omega = Vector3D.Cross(missileToTarget, relativeVelocity) / Math.Max(missileToTarget.LengthSquared(), 1); // To combat instability at close range
            Vector3D lateralAcceleration = NavConstant * relativeVelocity.Length() * Vector3D.Cross(omega, missileToTargetNorm)
                                           + NavAccelConstant * lateralTargetAcceleration
                                           + gravityCompensationTerm; // Normal to LOS

            if (Vector3D.IsZero(lateralAcceleration))
                return missileToTarget;

            double diff = missileAccelerationMagnitude * missileAccelerationMagnitude - lateralAcceleration.LengthSquared();
            if (diff < 0)
                return lateralAcceleration; // Fly parallel to the target if we run out of acceleration budget
            return lateralAcceleration + Math.Sqrt(diff) * missileToTargetNorm;
        }

        //Relative velocity proportional navigation
        //aka: Whip-Nav
        internal static Vector3D CalculateMissileIntercept(Vector3D targetPosition, Vector3D targetVelocity, Vector3D missilePos, Vector3D missileVelocity, double missileAcceleration, double compensationFactor = 1, double maxLateralThrustProportion = 0.5)
        {
            var missileToTarget = Vector3D.Normalize(targetPosition - missilePos);
            var relativeVelocity = targetVelocity - missileVelocity;
            var parallelVelocity = relativeVelocity.Dot(missileToTarget) * missileToTarget;
            var normalVelocity = (relativeVelocity - parallelVelocity);

            var normalMissileAcceleration = normalVelocity * compensationFactor;

            if (Vector3D.IsZero(normalMissileAcceleration))
                return missileToTarget * missileAcceleration;

            double maxLateralThrust = missileAcceleration * Math.Min(1, Math.Max(0, maxLateralThrustProportion));
            if (normalMissileAcceleration.LengthSquared() > maxLateralThrust * maxLateralThrust)
            {
                Vector3D.Normalize(ref normalMissileAcceleration, out normalMissileAcceleration);
                normalMissileAcceleration *= maxLateralThrust;
            }
            double diff = missileAcceleration * missileAcceleration - normalMissileAcceleration.LengthSquared();
            var maxedDiff = Math.Max(0, diff);
            return Math.Sqrt(maxedDiff) * missileToTarget + normalMissileAcceleration;
        }

        internal static bool WeaponLookAt(Weapon weapon, ref Vector3D targetDir, double targetDistSqr, bool setWeapon, bool canSeeOnly, out bool isTracking)
        {
            var system = weapon.System;
            var target = weapon.Target;
            isTracking = false;
            //Get weapon direction and orientation
            Vector3D currentVector;
            Vector3D.CreateFromAzimuthAndElevation(weapon.Azimuth, weapon.Elevation, out currentVector);
            Vector3D.Rotate(ref currentVector, ref weapon.WeaponConstMatrix, out currentVector);

            var up = weapon.MyPivotUp;
            Vector3D left;
            Vector3D.Cross(ref up, ref currentVector, out left);
            if (!Vector3D.IsUnit(ref left) && !Vector3D.IsZero(left)) left.Normalize();
            Vector3D forward;
            Vector3D.Cross(ref left, ref up, out forward);
            var constraintMatrix = new MatrixD { Forward = forward, Left = left, Up = up, };

            // ugly as sin inlined compute GetRotationAngles + AngleBetween, returning the desired az/el doubles;
            var transposeMatrix = MatrixD.Transpose(constraintMatrix);
            Vector3D localTargetVector;
            Vector3D.TransformNormal(ref targetDir, ref transposeMatrix, out localTargetVector);

            var flattenedTargetVector = new Vector3D(localTargetVector.X, 0, localTargetVector.Z);
            var azVecIsZero = Vector3D.IsZero(flattenedTargetVector);
            var flatSqr = flattenedTargetVector.LengthSquared();

            var desiredAzimuth = azVecIsZero ? 0 : Math.Acos(MathHelperD.Clamp(-flattenedTargetVector.Z / Math.Sqrt(flatSqr), -1, 1)) * -Math.Sign(localTargetVector.X); //right is positive;

            if (Math.Abs(desiredAzimuth) < 1E-6 && localTargetVector.Z > 0) //check for straight back case
                desiredAzimuth = Math.PI;
            
            double desiredElevation;
            if (Vector3D.IsZero(flattenedTargetVector)) //check for straight up case
                desiredElevation = MathHelper.PiOver2 * Math.Sign(localTargetVector.Y);
            else {
                var elVecIsZero = Vector3D.IsZero(localTargetVector) || Vector3D.IsZero(flattenedTargetVector);
                desiredElevation = elVecIsZero ? 0 : Math.Acos(MathHelperD.Clamp(localTargetVector.Dot(flattenedTargetVector) / Math.Sqrt(localTargetVector.LengthSquared() * flatSqr), -1, 1)) * Math.Sign(localTargetVector.Y); //up is positive
            }

            // return result of desired values being in tolerances
            if (canSeeOnly)
            {
                if (weapon.Azimuth + desiredAzimuth > weapon.MaxAzToleranceRadians && weapon.MaxAzToleranceRadians < Math.PI)
                    return false;

                if (weapon.Azimuth + desiredAzimuth < weapon.MinAzToleranceRadians && weapon.MinAzToleranceRadians > -Math.PI)
                    return false;

                if (desiredElevation < weapon.MinElToleranceRadians || desiredElevation > weapon.MaxElToleranceRadians)
                    return false;

                return true;
            }

            // check for backAround constraint
            double azToTraverse;
            if (weapon.MaxAzToleranceRadians < Math.PI && weapon.MinAzToleranceRadians > -Math.PI) {
                
                var azAngle = weapon.Azimuth + desiredAzimuth;
                if (azAngle > Math.PI) {
                    azAngle -= MathHelperD.TwoPi;
                }
                else if (azAngle < -Math.PI) {
                    azAngle = MathHelperD.TwoPi + azAngle;
                }
                azToTraverse = azAngle - weapon.Azimuth;
            }
            else 
                azToTraverse = desiredAzimuth;

            // Clamp step within limits.
            var azStep = MathHelperD.Clamp(azToTraverse, -system.AzStep, system.AzStep);
            var elStep = MathHelperD.Clamp(desiredElevation - weapon.Elevation, -system.ElStep, system.ElStep);

            // epsilon based on target type and distance
            var epsilon = target.IsProjectile || system.Session.Tick120 ? 1E-06d : targetDistSqr <= 640000 ? 1E-03d : targetDistSqr <= 3240000 ? 1E-04d : 1E-05d;

            // check if step is within epsilon of zero;
            var azLocked = MyUtils.IsZero(azStep, (float)epsilon);
            var elLocked = MyUtils.IsZero(elStep, (float)epsilon);

            // are az and el both within tolerance of target
            var locked = azLocked && elLocked;

            // Compute actual angle to rotate subparts
            var az = weapon.Azimuth + azStep;
            var el = weapon.Elevation + elStep;

            // This is where we should clamp. az and el are measured relative the WorldMatrix.Forward.
            // desiredAzimuth is measured off of the CURRENT heading of the barrel. The limits are based off of
            // WorldMatrix.Forward as well.
            var azHitLimit = false;
            var elHitLimit = false;

            // Check azimuth angles
            if (az > weapon.MaxAzToleranceRadians && weapon.MaxAzToleranceRadians < Math.PI)
            {
                // Hit upper azimuth limit
                az = weapon.MaxAzToleranceRadians;
                azHitLimit = true;
            }
            else if (az < weapon.MinAzToleranceRadians && weapon.MinAzToleranceRadians > -Math.PI)
            {
                // Hit lower azimuth limit
                az = weapon.MinAzToleranceRadians;
                azHitLimit = true;
            }

            // Check elevation angles
            if (el > weapon.MaxElToleranceRadians)
            {
                // Hit upper elevation limit
                el = weapon.MaxElToleranceRadians;
                elHitLimit = true;
            }
            else if (el < weapon.MinElToleranceRadians)
            {
                // Hit lower elevation limit
                el = weapon.MinElToleranceRadians;
                elHitLimit = true;
            }


            // Weapon has a degree of freedom to move towards target
            var tracking = !azHitLimit && !elHitLimit;

            if (setWeapon)
            {
                isTracking = tracking;

                if (!azLocked) {
                    weapon.Azimuth = az;
                    weapon.AzimuthTick = system.Session.Tick;
                }

                if (!elLocked) {
                    weapon.Elevation = el;
                    weapon.ElevationTick = system.Session.Tick;
                }
            }


            return !locked;
        }

        /*
        /// Whip's Get Rotation Angles Method v14 - 9/25/18 ///
        Dependencies: AngleBetween
        */

        internal static double AngleBetween(Vector3D a, Vector3D b) //returns radians
        {
            if (Vector3D.IsZero(a) || Vector3D.IsZero(b))
                return 0;

            return Math.Acos(MathHelperD.Clamp(a.Dot(b) / Math.Sqrt(a.LengthSquared() * b.LengthSquared()), -1, 1));
        }

        internal static long UniqueId(int left, int right)
        {
            long uniqueId = left;
            uniqueId <<= 32;
            uniqueId += right;
            return uniqueId;
        }

        internal static void GetRotationAngles(ref Vector3D targetVector, ref MatrixD matrix, out double yaw, out double pitch)
        {
            var localTargetVector = Vector3D.TransformNormal(targetVector, MatrixD.Transpose(matrix));
            var flattenedTargetVector = new Vector3D(localTargetVector.X, 0, localTargetVector.Z);
            yaw = AngleBetween(Vector3D.Forward, flattenedTargetVector) * -Math.Sign(localTargetVector.X); //right is positive
            if (Math.Abs(yaw) < 1E-6 && localTargetVector.Z > 0) //check for straight back case
                yaw = Math.PI;
            if (Vector3D.IsZero(flattenedTargetVector)) //check for straight up case
                pitch = MathHelper.PiOver2 * Math.Sign(localTargetVector.Y);
            else
                pitch = AngleBetween(localTargetVector, flattenedTargetVector) * Math.Sign(localTargetVector.Y); //up is positive
        }

        internal static float NormalizeAngle(int angle)
        {
            int num = angle % 360;
            if (num == 0 && angle != 0)
                return 360f;
            return num;
        }

        internal static double Intercept(Vector3D deltaPos, Vector3D deltaVel, float projectileVel)
        {
            var num1 = Vector3D.Dot(deltaVel, deltaVel) - projectileVel * projectileVel;
            var num2 = 2.0 * Vector3D.Dot(deltaVel, deltaPos);
            var num3 = Vector3D.Dot(deltaPos, deltaPos);
            var d = num2 * num2 - 4.0 * num1 * num3;
            if (d <= 0.0)
                return -1.0;
            return 2.0 * num3 / (Math.Sqrt(d) - num2);
        }

        internal static void WrapAngleAroundPI(ref float angle)
        {
            angle %= MathHelper.TwoPi;
            if (angle > Math.PI)
                angle = -MathHelper.TwoPi + angle;
            else if (angle < -Math.PI)
                angle = MathHelper.TwoPi + angle;
        }

        internal static double CalculateRotorDeviationAngle(Vector3D forwardVector, MatrixD lastOrientation)
        {
            var flattenedForwardVector = Rejection(forwardVector, lastOrientation.Up);
            return AngleBetween(flattenedForwardVector, lastOrientation.Forward) * Math.Sign(flattenedForwardVector.Dot(lastOrientation.Left));
        }

        internal static void GetAzimuthAngle(ref Vector3D targetVector, ref MatrixD matrix, out double azimuth)
        {
            var localTargetVector = Vector3D.TransformNormal(targetVector, MatrixD.Transpose(matrix));
            var flattenedTargetVector = new Vector3D(localTargetVector.X, 0, localTargetVector.Z);
            azimuth = AngleBetween(Vector3D.Forward, flattenedTargetVector) * -Math.Sign(localTargetVector.X); //right is positive
            if (Math.Abs(azimuth) < 1E-6 && localTargetVector.Z > 0) //check for straight back case
                azimuth = Math.PI;
        }
        internal static void GetElevationAngle(ref Vector3D targetVector, ref MatrixD matrix, out double pitch)
        {
            var localTargetVector = Vector3D.TransformNormal(targetVector, MatrixD.Transpose(matrix));
            var flattenedTargetVector = new Vector3D(localTargetVector.X, 0, localTargetVector.Z);
            if (Vector3D.IsZero(flattenedTargetVector)) //check for straight up case
                pitch = MathHelper.PiOver2 * Math.Sign(localTargetVector.Y);
            else
                pitch = AngleBetween(localTargetVector, flattenedTargetVector) * Math.Sign(localTargetVector.Y); //up is positive
        }

        internal static Vector3D SafeNormalize(Vector3D a)
        {
            if (Vector3D.IsZero(a)) return Vector3D.Zero; 
            if (Vector3D.IsUnit(ref a)) return a; 
            return Vector3D.Normalize(a);
        }

        internal static Vector3D Reflection(Vector3D a, Vector3D b, double rejectionFactor = 1) //reflect a over b
        {
            Vector3D project_a = Projection(a, b); 
            Vector3D reject_a = a - project_a; 
            return project_a - reject_a * rejectionFactor;
        }

        internal static Vector3D Rejection(Vector3D a, Vector3D b) //reject a on b
        {
            if (Vector3D.IsZero(a) || Vector3D.IsZero(b)) 
                return Vector3D.Zero; 
            return a - a.Dot(b) / b.LengthSquared() * b;
        }

        internal static Vector3D Projection(Vector3D a, Vector3D b)
        {
            if (Vector3D.IsZero(a) || Vector3D.IsZero(b)) 
                return Vector3D.Zero; 
            if (Vector3D.IsUnit(ref b)) 
                return a.Dot(b) * b; 

            return a.Dot(b) / b.LengthSquared() * b;
        }

        internal static double ScalarProjection(Vector3D a, Vector3D b)
        {
            if (Vector3D.IsZero(a) || Vector3D.IsZero(b)) return 0; 
            if (Vector3D.IsUnit(ref b)) 
                return a.Dot(b); 
            return a.Dot(b) / b.Length();
        }



        public static double CosBetween(Vector3D a, Vector3D b)
        {
            if (Vector3D.IsZero(a) || Vector3D.IsZero(b)) 
                return 0;
            return MathHelper.Clamp(a.Dot(b) / Math.Sqrt(a.LengthSquared() * b.LengthSquared()), -1, 1);
        }


        public static Vector3D NearestPointOnLine(Vector3D start, Vector3D end, Vector3D pnt)
        {
            var line = (end - start);
            var len = line.Length();
            line.Normalize();

            var v = pnt - start;
            var d = Vector3.Dot(v, line);
            MathHelper.Clamp(d, 0f, len);
            return start + line * d;
        }

        /*
        ** Returns the point on the line formed by (point1 + dir1 * x) that is closest to the point
        ** on the line formed by line (point2 + dir2 * t)
        */

        public static Vector3D GetClosestPointOnLine1(Vector3D point1, Vector3D dir1, Vector3D point2, Vector3D dir2)
        {
            Vector3D axis = Vector3D.Cross(dir1, dir2);
            if (Vector3D.IsZero(axis))
                return point1;
            Vector3D perpDir2 = Vector3D.Cross(dir2, axis);
            Vector3D point1To2 = point2 - point1;
            return point1 + Vector3D.Dot(point1To2, perpDir2) / Vector3D.Dot(dir1, perpDir2) * dir1;
        }

        /*
        ** Returns the point on the line1 that is closest to the point on line2
        */

        public static Vector3D GetClosestPointOnLine2(Vector3D line1Start, Vector3D line1End, Vector3D line2Start, Vector3D line2End)
        {
            Vector3D dir1 = line1End - line1Start;
            Vector3D dir2 = line2End - line2Start;
            Vector3D axis = Vector3D.Cross(dir1, dir2);
            if (Vector3D.IsZero(axis))
                return line1Start;
            Vector3D perpDir2 = Vector3D.Cross(dir2, axis);
            Vector3D point1To2 = line2Start - line1Start;
            return line1Start + Vector3D.Dot(point1To2, perpDir2) / Vector3D.Dot(dir1, perpDir2) * dir1;
        }

        public static Vector3D VectorProjection(Vector3D a, Vector3D b)
        {
            if (Vector3D.IsZero(b))
                return Vector3D.Zero;

            return a.Dot(b) / b.LengthSquared() * b;
        }

        public static bool SameSign(float num1, double num2)
        {
            if (num1 > 0 && num2 < 0)
                return false;
            if (num1 < 0 && num2 > 0)
                return false;
            return true;
        }

        public static bool NearlyEqual(double f1, double f2)
        {
            // Equal if they are within 0.00001 of each other
            return Math.Abs(f1 - f2) < 0.00001;
        }


        public static double InverseSqrDist(Vector3D source, Vector3D target, double range)
        {
            var rangeSq = range * range;
            var distSq = (target - source).LengthSquared();
            if (distSq > rangeSq)
                return 0.0;
            return 1.0 - (distSq / rangeSq);
        }

        public static double GetIntersectingSurfaceArea(MatrixD matrix, Vector3D hitPosLocal)
        {
            var surfaceArea = -1d;

            var boxMax = matrix.Backward + matrix.Right + matrix.Up;
            var boxMin = -boxMax;
            var box = new BoundingBoxD(boxMin, boxMax);

            var maxWidth = box.Max.LengthSquared();
            var testLine = new LineD(Vector3D.Zero, Vector3D.Normalize(hitPosLocal) * maxWidth);
            LineD testIntersection;
            box.Intersect(ref testLine, out testIntersection);

            var intersection = testIntersection.To;

            var epsilon = 1e-6;
            var projFront = VectorProjection(intersection, matrix.Forward);
            if (Math.Abs(projFront.LengthSquared() - matrix.Forward.LengthSquared()) < epsilon)
            {
                var a = Vector3D.Distance(matrix.Left, matrix.Right);
                var b = Vector3D.Distance(matrix.Up, matrix.Down);
                surfaceArea = a * b;
            }

            var projLeft = VectorProjection(intersection, matrix.Left);
            if (Math.Abs(projLeft.LengthSquared() - matrix.Left.LengthSquared()) < epsilon)
            {
                var a = Vector3D.Distance(matrix.Forward, matrix.Backward);
                var b = Vector3D.Distance(matrix.Up, matrix.Down);
                surfaceArea = a * b;
            }

            var projUp = VectorProjection(intersection, matrix.Up);
            if (Math.Abs(projUp.LengthSquared() - matrix.Up.LengthSquared()) < epsilon)
            {
                var a = Vector3D.Distance(matrix.Forward, matrix.Backward);
                var b = Vector3D.Distance(matrix.Left, matrix.Right);
                surfaceArea = a * b;
            }
            return surfaceArea;
        }

        public static void FibonacciSeq(int magicNum)
        {
            var root5 = Math.Sqrt(5);
            var phi = (1 + root5) / 2;

            var n = 0;
            int Fn;
            do
            {
                Fn = (int)((Math.Pow(phi, n) - Math.Pow(-phi, -n)) / ((2 * phi) - 1));
                //Console.Write("{0} ", Fn);
                ++n;
            }
            while (Fn < magicNum);
        }

        public static double LargestCubeInSphere(double r)
        {

            // radius cannot be negative  
            if (r < 0)
                return -1;

            // side of the cube  
            var a = (2 * r) / Math.Sqrt(3);
            return a;
        }

        public static double AreaCube(double a)
        {
            return (a * a * a);
        }

        public static double SurfaceCube(double a)
        {
            return (6 * a * a);
        }

        public static double VolumeCube(double len)
        {
            return Math.Pow(len, 3);
        }

        public static double Percentile(double[] sequence, double excelPercentile)
        {
            Array.Sort(sequence);
            int N = sequence.Length;
            double n = (N - 1) * excelPercentile + 1;
            // Another method: double n = (N + 1) * excelPercentile;
            if (n == 1d) return sequence[0];
            if (n == N) return sequence[N - 1];
            int k = (int)n;
            double d = n - k;
            return sequence[k - 1] + d * (sequence[k] - sequence[k - 1]);
        }

        public static double GetMedian(int[] array)
        {
            int[] tempArray = array;
            int count = tempArray.Length;

            Array.Sort(tempArray);

            double medianValue = 0;

            if (count % 2 == 0)
            {
                // count is even, need to get the middle two elements, add them together, then divide by 2
                int middleElement1 = tempArray[(count / 2) - 1];
                int middleElement2 = tempArray[(count / 2)];
                medianValue = (middleElement1 + middleElement2) / 2;
            }
            else
            {
                // count is odd, simply get the middle element.
                medianValue = tempArray[(count / 2)];
            }

            return medianValue;
        }
    }

}
