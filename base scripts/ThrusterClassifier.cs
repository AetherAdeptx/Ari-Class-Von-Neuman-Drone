using Sandbox.ModAPI.Ingame;
using System.Collections.Generic;
using VRageMath;

namespace IngameScript
{
    public partial class Program
    {
        sealed class ThrusterClassifier
        {
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
                }
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

            void ClearGroups()
            {
                _state.Forward.Clear();
                _state.Backward.Clear();
                _state.Up.Clear();
                _state.Down.Clear();
                _state.Left.Clear();
                _state.Right.Clear();
            }
        }
    }
}
