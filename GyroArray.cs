using System;
using VRageMath;

namespace IngameScript
{
    public partial class Program
    {
        sealed class GyroArray
        {
            readonly ShipState _ship;

            public GyroArray(ShipState ship)
            {
                _ship = ship;
            }

            public void SortInnerToOuter()
            {
                if (_ship.ReferenceController == null)
                    return;
                Vector3D center = _ship.ReferenceController.CenterOfMass;
                _ship.Gyroscopes.Sort((left, right) =>
                    Vector3D.DistanceSquared(left.GetPosition(), center)
                        .CompareTo(Vector3D.DistanceSquared(
                            right.GetPosition(), center)));
            }
        }
    }
}
