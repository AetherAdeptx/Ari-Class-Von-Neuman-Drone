using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using VRageMath;

namespace IngameScript
{
    public partial class Program
    {
        enum DistanceDirection
        {
            Forward,
            Backward,
            Up,
            Down,
            Left,
            Right
        }

        sealed class DistanceSensorArray
        {
            const double VelocityScanDistanceMeters = 2000;
            const double FastVelocityScanDistanceMeters = 15000;
            const double FastScanSpeedMetersPerSecond = 25;
            const double OtherScanDistanceMeters = 1000;
            const double OtherScanIntervalSeconds = 4;

            readonly ShipState _ship;
            readonly BspSpatialMap _map;
            readonly GlobalTemporalBsp _globalMap;
            readonly int[] _nextCamera = new int[6];
            int _nextVelocityCamera;
            int _velocitySample;
            double _otherScanTimer = OtherScanIntervalSeconds;

            public DistanceSensorArray(
                ShipState ship,
                BspSpatialMap map,
                GlobalTemporalBsp globalMap)
            {
                _ship = ship;
                _map = map;
                _globalMap = globalMap;
            }

            public void RebuildGroups()
            {
                _ship.SensorsForward.Clear();
                _ship.SensorsBackward.Clear();
                _ship.SensorsUp.Clear();
                _ship.SensorsDown.Clear();
                _ship.SensorsLeft.Clear();
                _ship.SensorsRight.Clear();
                if (_ship.ReferenceController == null)
                    return;

                for (int i = 0; i < _ship.DistanceSensors.Count; i++)
                {
                    IMyCameraBlock camera = _ship.DistanceSensors[i];
                    camera.EnableRaycast = true;
                    GetCameraGroup(
                        FindDirection(camera.WorldMatrix.Forward)).Add(camera);
                }

                SortAllFrontTopLeft();
                for (int i = 0; i < _nextCamera.Length; i++)
                    _nextCamera[i] = 0;
                _nextVelocityCamera = 0;
                _velocitySample = 0;
                _otherScanTimer = OtherScanIntervalSeconds;
            }

            public void Update(double elapsedSeconds)
            {
                if (_ship.Velocity_Vector_Scan_Valid != 0)
                    _ship.Velocity_Vector_Scan_Age_Seconds += elapsedSeconds;
                Vector3D velocityDirection = GetVelocityDirection();
                DistanceDirection travelDirection;
                bool moving = _ship.ReferenceController != null &&
                    velocityDirection.LengthSquared() > 0.000001;
                if (moving)
                {
                    double speed = _ship.ReferenceController == null
                        ? 0
                        : _ship.ReferenceController.GetShipSpeed();
                    _ship.Velocity_Vector_Scan_Distance_Meters =
                        speed > FastScanSpeedMetersPerSecond
                            ? FastVelocityScanDistanceMeters
                            : VelocityScanDistanceMeters;
                    TryVelocityRaycast(
                        velocityDirection,
                        _ship.Velocity_Vector_Scan_Distance_Meters);
                    travelDirection = FindDirection(velocityDirection);
                }
                else
                {
                    travelDirection = DistanceDirection.Forward;
                }

                _otherScanTimer += elapsedSeconds;
                if (_otherScanTimer < OtherScanIntervalSeconds)
                    return;
                _otherScanTimer = 0;

                for (int i = 0; i < 6; i++)
                {
                    DistanceDirection direction = (DistanceDirection)i;
                    if (!moving || direction != travelDirection)
                        TryDirectionRaycast(direction, OtherScanDistanceMeters);
                }
            }

            void TryVelocityRaycast(Vector3D direction, double distance)
            {
                List<IMyCameraBlock> cameras = _ship.DistanceSensors;
                for (int attempt = 0; attempt < cameras.Count; attempt++)
                {
                    int index = _nextVelocityCamera % cameras.Count;
                    _nextVelocityCamera = (index + 1) % cameras.Count;
                    IMyCameraBlock camera = cameras[index];
                    if (!camera.IsWorking)
                        continue;
                    Vector3D target = camera.GetPosition() +
                        direction * distance + GetSampleOffset(direction);
                    if (camera.CanScan(target))
                    {
                        MyDetectedEntityInfo hit = RecordRaycast(camera, target);
                        _ship.Velocity_Vector_Encounter_Detected =
                            hit.HitPosition.HasValue ? 1 : 0;
                        _ship.Velocity_Vector_Encounter_Distance_Meters =
                            hit.HitPosition.HasValue
                                ? Vector3D.Distance(
                                    camera.GetPosition(), hit.HitPosition.Value)
                                : 0;
                        _ship.Velocity_Vector_Scan_Valid = 1;
                        _ship.Velocity_Vector_Scan_Age_Seconds = 0;
                        _velocitySample++;
                        return;
                    }
                }
            }

            Vector3D GetSampleOffset(Vector3D direction)
            {
                if ((_velocitySample & 1) == 0 ||
                    _ship.ReferenceController == null)
                    return Vector3D.Zero;
                MatrixD frame = _ship.ReferenceController.WorldMatrix;
                Vector3D axis = Math.Abs(Vector3D.Dot(direction, frame.Up)) < 0.9
                    ? frame.Up
                    : frame.Right;
                axis = Vector3D.Normalize(
                    axis - direction * Vector3D.Dot(axis, direction));
                Vector3D second = Vector3D.Cross(direction, axis);
                switch ((_velocitySample >> 1) & 3)
                {
                    case 0: return axis * _map.RegionSizeMeters;
                    case 1: return second * _map.RegionSizeMeters;
                    case 2: return -axis * _map.RegionSizeMeters;
                    default: return -second * _map.RegionSizeMeters;
                }
            }

            void TryDirectionRaycast(
                DistanceDirection direction,
                double distance)
            {
                List<IMyCameraBlock> cameras = GetCameraGroup(direction);
                int directionIndex = (int)direction;
                for (int attempt = 0; attempt < cameras.Count; attempt++)
                {
                    int index = _nextCamera[directionIndex] % cameras.Count;
                    _nextCamera[directionIndex] =
                        (index + 1) % cameras.Count;
                    IMyCameraBlock camera = cameras[index];
                    if (!camera.IsWorking || !camera.CanScan(distance))
                        continue;
                    RecordRaycast(camera,
                        camera.GetPosition() + camera.WorldMatrix.Forward * distance);
                    return;
                }
            }

            MyDetectedEntityInfo RecordRaycast(
                IMyCameraBlock camera,
                Vector3D target)
            {
                MyDetectedEntityInfo hit = camera.Raycast(target);
                Vector3D observedPosition = hit.HitPosition.HasValue
                    ? hit.HitPosition.Value
                    : target;
                Vector3D start = camera.GetPosition();
                double distance = Vector3D.Distance(start, observedPosition);
                int samples = Math.Max(1, (int)Math.Ceiling(distance / 100));
                int emptySamples = hit.HitPosition.HasValue ? samples - 1 : samples;
                for (int i = 1; i <= emptySamples; i++)
                    SetWorldRegion(Vector3D.Lerp(start, observedPosition,
                        i / (double)samples), 1);
                if (hit.HitPosition.HasValue)
                    SetWorldRegion(observedPosition, 2);
                _globalMap.MarkScanned(observedPosition);
                return hit;
            }

            Vector3D GetVelocityDirection()
            {
                Vector3D direction = _ship.Spatial_Direction_Vector;
                if (direction.LengthSquared() < 0.000001)
                    direction = _ship.Spatial_Last_Direction_Vector;
                if (direction.LengthSquared() < 0.000001)
                    direction = _ship.Spatial_Second_Last_Direction_Vector;
                return direction;
            }

            void SetWorldRegion(Vector3D worldPosition, byte value)
            {
                _map.SetValue(worldPosition, value);
            }

            DistanceDirection FindDirection(Vector3D facing)
            {
                MatrixD frame = _ship.ReferenceController.WorldMatrix;
                DistanceDirection bestDirection = DistanceDirection.Forward;
                double bestDot = Vector3D.Dot(facing, frame.Forward);
                Choose(facing, frame.Backward, DistanceDirection.Backward,
                    ref bestDot, ref bestDirection);
                Choose(facing, frame.Up, DistanceDirection.Up,
                    ref bestDot, ref bestDirection);
                Choose(facing, frame.Down, DistanceDirection.Down,
                    ref bestDot, ref bestDirection);
                Choose(facing, frame.Left, DistanceDirection.Left,
                    ref bestDot, ref bestDirection);
                Choose(facing, frame.Right, DistanceDirection.Right,
                    ref bestDot, ref bestDirection);
                return bestDirection;
            }

            void Choose(
                Vector3D facing,
                Vector3D axis,
                DistanceDirection candidate,
                ref double bestDot,
                ref DistanceDirection bestDirection)
            {
                double dot = Vector3D.Dot(facing, axis);
                if (dot <= bestDot)
                    return;
                bestDot = dot;
                bestDirection = candidate;
            }

            void SortAllFrontTopLeft()
            {
                SortBlocks(_ship.SensorsForward);
                SortBlocks(_ship.SensorsBackward);
                SortBlocks(_ship.SensorsUp);
                SortBlocks(_ship.SensorsDown);
                SortBlocks(_ship.SensorsLeft);
                SortBlocks(_ship.SensorsRight);
            }

            void SortBlocks<T>(List<T> blocks) where T : IMyTerminalBlock
            {
                MatrixD frame = _ship.ReferenceController.WorldMatrix;
                Vector3D origin = _ship.ReferenceController.GetPosition();
                blocks.Sort((left, right) => ComparePosition(
                    left.GetPosition() - origin,
                    right.GetPosition() - origin,
                    frame));
            }

            int ComparePosition(Vector3D left, Vector3D right, MatrixD frame)
            {
                int comparison = DescendingProjection(
                    left, right, frame.Forward);
                if (comparison != 0)
                    return comparison;
                comparison = DescendingProjection(left, right, frame.Up);
                if (comparison != 0)
                    return comparison;
                return DescendingProjection(left, right, frame.Left);
            }

            int DescendingProjection(
                Vector3D left,
                Vector3D right,
                Vector3D axis)
            {
                return Vector3D.Dot(right, axis).CompareTo(
                    Vector3D.Dot(left, axis));
            }

            List<IMyCameraBlock> GetCameraGroup(DistanceDirection direction)
            {
                switch (direction)
                {
                    case DistanceDirection.Forward:
                        return _ship.SensorsForward;
                    case DistanceDirection.Backward:
                        return _ship.SensorsBackward;
                    case DistanceDirection.Up:
                        return _ship.SensorsUp;
                    case DistanceDirection.Down:
                        return _ship.SensorsDown;
                    case DistanceDirection.Left:
                        return _ship.SensorsLeft;
                    default:
                        return _ship.SensorsRight;
                }
            }

        }
    }
}
