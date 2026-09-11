using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using VRage.Game;
using VRage.Game.Components;
using VRageMath;

namespace IngameScript
{
    public partial class Program
    {
        sealed class ShipStatsTelemetry
        {
            static readonly MyDefinitionId Electricity =
                MyDefinitionId.Parse("MyObjectBuilder_GasProperties/Electricity");

            readonly ShipState _ship;
            readonly TelemetryState _telemetry;
            Vector3D _lastVelocity;
            double _lastMotionSampleSeconds;
            bool _hasMotionSample;
            bool _hasAverage;
            bool _hasEconomyAverage;

            public ShipStatsTelemetry(ShipState ship, TelemetryState telemetry)
            {
                _ship = ship;
                _telemetry = telemetry;
            }

            public void UpdatePower()
            {
                double currentUseMw = 0;
                double currentGenerationMw = 0;
                double generationCapacityMw = 0;
                double batteryOutputCapacityMw = 0;

                for (int i = 0; i < _ship.Blocks.Count; i++)
                {
                    MyResourceSinkComponent sink =
                        _ship.Blocks[i].Components.Get<MyResourceSinkComponent>();
                    if (sink != null)
                    {
                        currentUseMw += sink.CurrentInputByType(
                            Electricity);
                    }
                }

                for (int i = 0; i < _ship.PowerProducers.Count; i++)
                {
                    IMyPowerProducer producer = _ship.PowerProducers[i];
                    currentGenerationMw += producer.CurrentOutput;

                    if (producer is IMyBatteryBlock)
                        batteryOutputCapacityMw += producer.MaxOutput;
                    else
                        generationCapacityMw += producer.MaxOutput;
                }

                _telemetry.ShipStats["PowerUseMW"] = currentUseMw;
                _telemetry.ShipStats["PowerGenerationMW"] =
                    currentGenerationMw;
                _telemetry.ShipStats["PowerGenerationCapacityMW"] =
                    generationCapacityMw;
                _telemetry.ShipStats["BatteryOutputCapacityMW"] =
                    batteryOutputCapacityMw;

                double average = currentUseMw;
                double oldAverage;
                if (_hasAverage && _telemetry.ShipStats.TryGetValue(
                    "AveragePowerUseMW", out oldAverage))
                {
                    average = oldAverage + (currentUseMw - oldAverage) * 0.05;
                }

                _hasAverage = true;
                _telemetry.ShipStats["AveragePowerUseMW"] = average;
                UpdateEconomy(currentUseMw);
            }

            public void UpdateMotion(double elapsedSeconds)
            {
                if (_ship.ReferenceController == null)
                    return;

                Vector3D velocity = _ship.ReferenceController
                    .GetShipVelocities().LinearVelocity;
                double speed = velocity.Length();
                double acceleration = 0;

                if (_hasMotionSample)
                {
                    double duration = elapsedSeconds - _lastMotionSampleSeconds;
                    if (duration > 0.0001)
                        acceleration = (velocity - _lastVelocity).Length() / duration;
                }

                _lastVelocity = velocity;
                _lastMotionSampleSeconds = elapsedSeconds;
                _hasMotionSample = true;

                _telemetry.ShipStats["SpeedMetersPerSecond"] = speed;
                _telemetry.ShipStats["AccelerationMetersPerSecondSquared"] =
                    acceleration;
                _telemetry.ShipStats["ShipMassKg"] =
                    _ship.ReferenceController.CalculateShipMass().PhysicalMass;

                double powerUse;
                if (_telemetry.ShipStats.TryGetValue("PowerUseMW", out powerUse))
                    UpdateEconomy(powerUse);
            }

            void UpdateEconomy(double powerUseMw)
            {
                double speed;
                double acceleration;
                _telemetry.ShipStats.TryGetValue(
                    "SpeedMetersPerSecond", out speed);
                _telemetry.ShipStats.TryGetValue(
                    "AccelerationMetersPerSecondSquared", out acceleration);

                double joulesPerMeter = speed > 0.5
                    ? powerUseMw * 1000000.0 / speed
                    : 0;
                double accelerationPerMw = powerUseMw > 0.000001
                    ? acceleration / powerUseMw
                    : 0;
                double joulesPerMeterPerAcceleration = acceleration > 0.0001
                    ? joulesPerMeter / acceleration
                    : 0;

                _telemetry.ShipStats["JoulesPerMeter"] = joulesPerMeter;
                _telemetry.ShipStats["AccelerationPerMW"] = accelerationPerMw;
                _telemetry.ShipStats["JoulesPerMeterPerAcceleration"] =
                    joulesPerMeterPerAcceleration;

                double averageJoulesPerMeter = joulesPerMeter;
                double averageAccelerationPerMw = accelerationPerMw;
                double averageJoulesPerAcceleration =
                    joulesPerMeterPerAcceleration;
                if (_hasEconomyAverage)
                {
                    averageJoulesPerMeter = Smooth(
                        "AverageJoulesPerMeter", joulesPerMeter);
                    averageAccelerationPerMw = Smooth(
                        "AverageAccelerationPerMW", accelerationPerMw);
                    averageJoulesPerAcceleration = Smooth(
                        "AverageJoulesPerMeterPerAcceleration",
                        joulesPerMeterPerAcceleration);
                }

                _hasEconomyAverage = true;
                _telemetry.ShipStats["AverageJoulesPerMeter"] =
                    averageJoulesPerMeter;
                _telemetry.ShipStats["AverageAccelerationPerMW"] =
                    averageAccelerationPerMw;
                _telemetry.ShipStats["AverageEfficiency"] =
                    averageAccelerationPerMw;
                _telemetry.ShipStats[
                    "AverageJoulesPerMeterPerAcceleration"] =
                    averageJoulesPerAcceleration;
            }

            double Smooth(string key, double current)
            {
                double previous;
                if (!_telemetry.ShipStats.TryGetValue(key, out previous))
                    return current;
                return previous + (current - previous) * 0.05;
            }
        }
    }
}
