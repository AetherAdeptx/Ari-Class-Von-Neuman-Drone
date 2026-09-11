using Sandbox.ModAPI.Ingame;

namespace IngameScript
{
    public partial class Program
    {
        sealed class SeatMonitor
        {
            readonly ShipState _state;
            long _lastOccupiedId;

            public SeatMonitor(ShipState state)
            {
                _state = state;
            }

            public IMyShipController PollEnteredSeat()
            {
                IMyShipController occupied = FindOccupiedSeat();
                long occupiedId = occupied == null ? 0 : occupied.EntityId;
                bool entered = occupiedId != 0 && occupiedId != _lastOccupiedId;
                _lastOccupiedId = occupiedId;
                return entered ? occupied : null;
            }

            public IMyShipController FindOccupiedSeat()
            {
                for (int i = 0; i < _state.Controllers.Count; i++)
                {
                    if (_state.Controllers[i].IsUnderControl)
                        return _state.Controllers[i];
                }

                return null;
            }

            public IMyShipController SelectReference(
                IMyShipController preferredController)
            {
                if (preferredController != null)
                    return preferredController;

                IMyShipController occupied = FindOccupiedSeat();
                if (occupied != null)
                    return occupied;

                for (int i = 0; i < _state.Controllers.Count; i++)
                {
                    if (_state.Controllers[i].IsMainCockpit)
                        return _state.Controllers[i];
                }

                return _state.Controllers.Count == 0
                    ? null
                    : _state.Controllers[0];
            }

            public void ResetEdgeState()
            {
                IMyShipController occupied = FindOccupiedSeat();
                _lastOccupiedId = occupied == null ? 0 : occupied.EntityId;
            }
        }
    }
}
