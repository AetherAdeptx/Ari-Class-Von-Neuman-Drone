using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRageMath;

namespace IngameScript
{
    public partial class Program
    {
        sealed class SolarTracker
        {
            const float TrackingSpeedRpm = 0.1f;
            const double SampleSeconds = 2;
            readonly ShipState _ship;
            int _rotorIndex;
            int _samplesOnRotor;
            float _direction = 1;
            double _sampleTimer;
            double _previousOutput = -1;
            bool _active;

            public SolarTracker(ShipState ship)
            {
                _ship = ship;
            }

            public void Update(bool active, double elapsedSeconds)
            {
                double output = 0;
                IMySolarPanel strongest = null;
                for (int i = 0; i < _ship.SolarPanels.Count; i++)
                {
                    output += _ship.SolarPanels[i].CurrentOutput;
                    if (strongest == null ||
                        _ship.SolarPanels[i].CurrentOutput >
                            strongest.CurrentOutput)
                        strongest = _ship.SolarPanels[i];
                }
                _ship.Solar_Current_Output_MW = output;
                UpdateReceivingSide(strongest);
                if (output > _ship.Solar_Best_Output_MW)
                    _ship.Solar_Best_Output_MW = output;

                if (!active || _ship.SolarRotors.Count == 0)
                {
                    if (_active)
                        StopAll();
                    _active = false;
                    _previousOutput = -1;
                    return;
                }

                if (!_active)
                {
                    _active = true;
                    _rotorIndex = 0;
                    _samplesOnRotor = 0;
                    _sampleTimer = SampleSeconds;
                    StartCurrentRotor();
                }
                else if (_rotorIndex >= _ship.SolarRotors.Count)
                    _rotorIndex = 0;

                _sampleTimer += elapsedSeconds;
                if (_sampleTimer < SampleSeconds)
                    return;
                _sampleTimer = 0;
                if (_previousOutput >= 0 &&
                    output + 0.000001 < _previousOutput)
                {
                    _direction = -_direction;
                    StartCurrentRotor();
                }
                _previousOutput = output;
                _samplesOnRotor++;
                if (_samplesOnRotor < 4)
                    return;
                _ship.SolarRotors[_rotorIndex].TargetVelocityRPM = 0;
                _rotorIndex = (_rotorIndex + 1) %
                    _ship.SolarRotors.Count;
                _samplesOnRotor = 0;
                StartCurrentRotor();
            }

            void StartCurrentRotor()
            {
                IMyMotorStator rotor = _ship.SolarRotors[_rotorIndex];
                rotor.RotorLock = false;
                rotor.TargetVelocityRPM = TrackingSpeedRpm * _direction;
            }

            void StopAll()
            {
                for (int i = 0; i < _ship.SolarRotors.Count; i++)
                {
                    _ship.SolarRotors[i].TargetVelocityRPM = 0;
                    _ship.SolarRotors[i].RotorLock = true;
                }
            }

            void UpdateReceivingSide(IMySolarPanel panel)
            {
                if (panel == null || _ship.ReferenceController == null)
                {
                    _ship.Auto_Tracking_Receicing_Side = -1;
                    return;
                }
                Vector3D offset = panel.GetPosition() -
                    _ship.ReferenceController.GetPosition();
                if (offset.LengthSquared() < 0.000001)
                {
                    _ship.Auto_Tracking_Receicing_Side = -1;
                    return;
                }
                MatrixD frame = _ship.ReferenceController.WorldMatrix;
                Vector3D[] axes =
                {
                    frame.Forward, frame.Backward, frame.Up,
                    frame.Down, frame.Left, frame.Right
                };
                int side = 0;
                double best = Vector3D.Dot(offset, axes[0]);
                for (int i = 1; i < axes.Length; i++)
                {
                    double score = Vector3D.Dot(offset, axes[i]);
                    if (score > best)
                    {
                        best = score;
                        side = i;
                    }
                }
                _ship.Auto_Tracking_Receicing_Side = side;
            }
        }
    }
}
