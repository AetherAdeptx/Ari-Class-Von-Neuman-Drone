using System;
using VRageMath;

namespace IngameScript
{
    public partial class Program
    {
        struct NavigationSnapshot
        {
            public ulong Timestamp;
            public Vector3D Position;
            public Vector3D Destination;
            public string State;
        }

        sealed class NavigationMemory
        {
            public const int Capacity = 12 * 60 * 60 / 15;
            public const long RawBytes = Capacity * 64L;
            readonly NavigationSnapshot[] _entries =
                new NavigationSnapshot[Capacity];
            readonly string[] _stateNames = Enum.GetNames(
                typeof(ShipState.SupervisorState));
            int _next;
            double _timer = 15;

            public int Count { get; private set; }

            public void Update(ShipState ship, double elapsedSeconds)
            {
                _timer += elapsedSeconds;
                if (_timer < 15)
                    return;
                _timer -= 15;
                _entries[_next] = new NavigationSnapshot
                {
                    Timestamp = (ulong)Math.Max(1,
                        (DateTime.UtcNow.Ticks - 621355968000000000L) /
                            10000000L),
                    Position = ship.CurrentShipPosition,
                    Destination = ship.Target_Destination,
                    State = _stateNames[(int)ship.Supervisor_State]
                };
                _next = (_next + 1) % Capacity;
                if (Count < Capacity)
                    Count++;
                ship.Navigation_History_Count = Count;
            }
        }
    }
}
