using VRageMath;

namespace IngameScript
{
    public partial class Program
    {
        sealed class SpatialBufferMonitor
        {
            const long Megabyte = 1024L * 1024L;
            const long PurgeThresholdBytes = 64L * Megabyte;
            const long EmergencyThresholdBytes = 64L * Megabyte;
            const double MovementEpsilonMeters = 0.001;

            readonly Program _program;
            readonly ShipState _ship;
            readonly BspSpatialMap _map;
            readonly TelemetryState _telemetry;
            Vector3D _lastProgrammableBlockPosition;
            bool _hasPositionSample;
            long _purgeCount;
            int _lastPurgeLevel;
            int _lastPurgedTrees;
            long _lastFreedBytes;

            public SpatialBufferMonitor(
                Program program,
                ShipState ship,
                BspSpatialMap map,
                TelemetryState telemetry)
            {
                _program = program;
                _ship = ship;
                _map = map;
                _telemetry = telemetry;
            }

            public void UpdateEveryFrame()
            {
                UpdateDirectionHistory(
                    CalculateMovementDirection(
                        _program.Me.GetPosition()));
                PublishStats();

                // Enforce the per-script spatial-map budget before more work.
                long size = _map.EstimatedMemoryBytes;
                if (size > EmergencyThresholdBytes)
                    RunEmergencyPurge();
                else if (size > PurgeThresholdBytes)
                    PurgeTrailingSlab(1);
            }

            Vector3D CalculateMovementDirection(Vector3D currentPosition)
            {
                Vector3D direction = Vector3D.Zero;
                if (_hasPositionSample)
                {
                    Vector3D movement =
                        currentPosition - _lastProgrammableBlockPosition;
                    double distance = movement.Length();
                    if (distance > MovementEpsilonMeters)
                        direction = movement / distance;
                }

                _lastProgrammableBlockPosition = currentPosition;
                _hasPositionSample = true;
                return direction;
            }

            void UpdateDirectionHistory(Vector3D direction)
            {
                _ship.Spatial_Second_Last_Direction_Vector =
                    _ship.Spatial_Last_Direction_Vector;
                _ship.Spatial_Last_Direction_Vector =
                    _ship.Spatial_Direction_Vector;
                _ship.Spatial_Direction_Vector = direction;
            }

            void PurgeTrailingSlab(int limitLevel)
            {
                Vector3D localDirection;
                Vector3D currentLocalPosition;
                if (!TryGetLocalPurgeFrame(
                    out localDirection,
                    out currentLocalPosition))
                {
                    _lastPurgeLevel = limitLevel;
                    _lastPurgedTrees = 0;
                    _lastFreedBytes = 0;
                    PublishStats();
                    return;
                }

                long before = _map.EstimatedMemoryBytes;
                int purgedTrees =
                    _map.PurgeOppositeDirection(localDirection);
                long bytesFreed = before - _map.EstimatedMemoryBytes;
                if (purgedTrees > 0)
                    _purgeCount++;
                _lastPurgeLevel = limitLevel;
                _lastPurgedTrees = purgedTrees;
                _lastFreedBytes = bytesFreed;
                PublishStats();
            }

            void RunEmergencyPurge()
            {
                Vector3D localDirection;
                Vector3D currentLocalPosition;
                if (!TryGetLocalPurgeFrame(
                    out localDirection,
                    out currentLocalPosition))
                {
                    _ship.Hard_Stop = 1;
                    _lastPurgeLevel = 2;
                    _lastPurgedTrees = 0;
                    _lastFreedBytes = 0;
                    PublishStats();
                    return;
                }

                long before = _map.EstimatedMemoryBytes;
                int totalPurgedTrees =
                    _map.PurgeOppositeDirection(localDirection);

                while (_map.EstimatedMemoryBytes >
                    EmergencyThresholdBytes)
                {
                    int purgedTrees = _map.PurgeNextEmergencyLayer(
                        localDirection,
                        currentLocalPosition);
                    if (purgedTrees == 0)
                        break;
                    totalPurgedTrees += purgedTrees;
                }

                if (_map.EstimatedMemoryBytes > EmergencyThresholdBytes)
                    _ship.Hard_Stop = 1;

                if (totalPurgedTrees > 0)
                    _purgeCount++;
                _lastPurgeLevel = 2;
                _lastPurgedTrees = totalPurgedTrees;
                _lastFreedBytes = before - _map.EstimatedMemoryBytes;
                PublishStats();
            }

            bool TryGetLocalPurgeFrame(
                out Vector3D localDirection,
                out Vector3D currentLocalPosition)
            {
                Vector3D worldDirection = FirstNonZeroDirection(
                    _ship.Spatial_Direction_Vector,
                    _ship.Spatial_Last_Direction_Vector,
                    _ship.Spatial_Second_Last_Direction_Vector);
                if (worldDirection.LengthSquared() < 0.000001)
                {
                    localDirection = Vector3D.Zero;
                    currentLocalPosition = Vector3D.Zero;
                    return false;
                }

                localDirection = worldDirection;
                currentLocalPosition = _program.Me.GetPosition();
                return true;
            }

            Vector3D FirstNonZeroDirection(
                Vector3D current,
                Vector3D previous,
                Vector3D secondPrevious)
            {
                if (current.LengthSquared() >= 0.000001)
                    return current;
                if (previous.LengthSquared() >= 0.000001)
                    return previous;
                return secondPrevious;
            }

            void PublishStats()
            {
                long bytes = _map.EstimatedMemoryBytes;
                _telemetry.ShipStats["SpatialBufferEstimatedBytes"] = bytes;
                _telemetry.ShipStats["SpatialBufferEstimatedMB"] =
                    bytes / (double)Megabyte;
                _telemetry.ShipStats["SpatialBufferAllocatedNodes"] =
                    _map.AllocatedNodeCount;
                _telemetry.ShipStats["SpatialBufferOver64MB"] =
                    bytes > PurgeThresholdBytes ? 1 : 0;
                _telemetry.ShipStats["SpatialBufferAtHardLimit"] =
                    bytes > EmergencyThresholdBytes ? 1 : 0;
                _telemetry.ShipStats["SpatialBufferLastPurgeLevel"] =
                    _lastPurgeLevel;
                _telemetry.ShipStats["SpatialBufferLastPurgedTrees"] =
                    _lastPurgedTrees;
                _telemetry.ShipStats["SpatialBufferLastFreedBytes"] =
                    _lastFreedBytes;
                _telemetry.ShipStats["SpatialBufferPurgeCount"] = _purgeCount;
                _telemetry.ShipStats["Hard_Stop"] = _ship.Hard_Stop;
                PublishVector(
                    "SpatialDirection",
                    _ship.Spatial_Direction_Vector);
                PublishVector(
                    "SpatialLastDirection",
                    _ship.Spatial_Last_Direction_Vector);
                PublishVector(
                    "SpatialSecondLastDirection",
                    _ship.Spatial_Second_Last_Direction_Vector);
            }

            void PublishVector(string name, Vector3D value)
            {
                _telemetry.ShipStats[name + "X"] = value.X;
                _telemetry.ShipStats[name + "Y"] = value.Y;
                _telemetry.ShipStats[name + "Z"] = value.Z;
            }
        }
    }
}
