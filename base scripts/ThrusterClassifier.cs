using Sandbox.ModAPI.Ingame;
using System.Collections.Generic;
using VRageMath;

namespace IngameScript
{
    public partial class Program
    {
        sealed class ThrusterClassifier
        {
            const double TorqueEpsilon = 0.05;
            readonly ShipState _state;

            public ThrusterClassifier(ShipState state)
            {
                _state = state;
            }

            public void RebuildGroups()
            {
                ClearGroups();
                if (_state.ReferenceController == null)
                    return;

                for (int i = 0; i < _state.Thrusters.Count; i++)
                {
                    IMyThrust thruster = _state.Thrusters[i];
                    ClassifyTranslation(thruster);
                    ClassifyRotation(thruster);
                }

                SortRotationGroupsOuterToInner();
            }

            void ClassifyTranslation(IMyThrust thruster)
            {
                // WorldMatrix.Backward is the force direction, away from the
                // exhaust. The nearest controller axis wins.
                Vector3D force = thruster.WorldMatrix.Backward;
                MatrixD frame = _state.ReferenceController.WorldMatrix;
                double bestDot = double.MinValue;
                List<IMyThrust> bestGroup = null;

                Select(force, frame.Forward, _state.Forward,
                    ref bestDot, ref bestGroup);
                Select(force, frame.Backward, _state.Backward,
                    ref bestDot, ref bestGroup);
                Select(force, frame.Up, _state.Up,
                    ref bestDot, ref bestGroup);
                Select(force, frame.Down, _state.Down,
                    ref bestDot, ref bestGroup);
                Select(force, frame.Left, _state.Left,
                    ref bestDot, ref bestGroup);
                Select(force, frame.Right, _state.Right,
                    ref bestDot, ref bestGroup);

                if (bestGroup != null)
                    bestGroup.Add(thruster);
            }

            void Select(
                Vector3D force,
                Vector3D direction,
                List<IMyThrust> group,
                ref double bestDot,
                ref List<IMyThrust> bestGroup)
            {
                double dot = Vector3D.Dot(force, direction);
                if (dot <= bestDot)
                    return;

                bestDot = dot;
                bestGroup = group;
            }

            void ClassifyRotation(IMyThrust thruster)
            {
                Vector3D lever = thruster.GetPosition() -
                    _state.ReferenceController.CenterOfMass;
                Vector3D force = thruster.WorldMatrix.Backward;
                Vector3D torque = Vector3D.Cross(lever, force);
                MatrixD frame = _state.ReferenceController.WorldMatrix;

                double pitch = Vector3D.Dot(torque, frame.Right);
                double yaw = Vector3D.Dot(torque, frame.Up);
                double roll = Vector3D.Dot(torque, frame.Forward);

                AddSigned(pitch, _state.PitchUp, _state.PitchDown, thruster);
                AddSigned(yaw, _state.YawLeft, _state.YawRight, thruster);
                AddSigned(roll, _state.RollRight, _state.RollLeft, thruster);
            }

            void AddSigned(
                double torque,
                List<IMyThrust> positive,
                List<IMyThrust> negative,
                IMyThrust thruster)
            {
                if (torque > TorqueEpsilon)
                    positive.Add(thruster);
                else if (torque < -TorqueEpsilon)
                    negative.Add(thruster);
            }

            void ClearGroups()
            {
                _state.Forward.Clear();
                _state.Backward.Clear();
                _state.Up.Clear();
                _state.Down.Clear();
                _state.Left.Clear();
                _state.Right.Clear();
                _state.PitchUp.Clear();
                _state.PitchDown.Clear();
                _state.YawLeft.Clear();
                _state.YawRight.Clear();
                _state.RollLeft.Clear();
                _state.RollRight.Clear();
            }

            void SortRotationGroupsOuterToInner()
            {
                MatrixD frame = _state.ReferenceController.WorldMatrix;
                SortByTorque(_state.PitchUp, frame.Right);
                SortByTorque(_state.PitchDown, frame.Right);
                SortByTorque(_state.YawLeft, frame.Up);
                SortByTorque(_state.YawRight, frame.Up);
                SortByTorque(_state.RollLeft, frame.Forward);
                SortByTorque(_state.RollRight, frame.Forward);
            }

            void SortByTorque(List<IMyThrust> group, Vector3D rotationAxis)
            {
                group.Sort((left, right) =>
                    TorqueScore(right, rotationAxis).CompareTo(
                        TorqueScore(left, rotationAxis)));
            }

            double TorqueScore(IMyThrust thruster, Vector3D rotationAxis)
            {
                Vector3D lever = thruster.GetPosition() -
                    _state.ReferenceController.CenterOfMass;
                Vector3D torque = Vector3D.Cross(
                    lever,
                    thruster.WorldMatrix.Backward);
                return System.Math.Abs(Vector3D.Dot(torque, rotationAxis));
            }
        }
    }
}
