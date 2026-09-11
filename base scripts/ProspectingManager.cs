using System;
using VRageMath;

namespace IngameScript
{
    public partial class Program
    {
        sealed class ProspectingManager
        {
            readonly ShipState _ship;
            double _timer;
            public ProspectingManager(ShipState ship, OreMemory oreMemory)
            {
                _ship = ship;
            }

            public void Update(double elapsed)
            {
                _timer += elapsed;
                if (_timer < 5) return;
                _timer = 0;
                if (_ship.Drone_Mode < 2 || !_ship.Prospecting_Enabled ||
                    _ship.Need_Level < 10) return;
                if (Vector3D.DistanceSquared(_ship.CurrentShipPosition,
                    _ship.Target_Destination) > 100 * 100) return;
                int i = _ship.Prospecting_Sample++;
                double golden = 2.399963229728653;
                double y = 1 - 2 * ((i % 64) + .5) / 64.0;
                double radial = Math.Sqrt(Math.Max(0, 1 - y * y));
                double angle = i * golden;
                double shell = _ship.Prospecting_Radius * (.25 + .75 * ((i / 64) % 4 + 1) / 4.0);
                _ship.Target_Destination = _ship.Prospecting_Center +
                    new Vector3D(Math.Cos(angle) * radial, y,
                        Math.Sin(angle) * radial) * shell;
                _ship.Free_Move = true;
                _ship.Supervisor_State = ShipState.SupervisorState.Prospecting;
            }
        }
    }
}
