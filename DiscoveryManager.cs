using Sandbox.ModAPI.Ingame;
using System;
using VRageMath;

namespace IngameScript
{
    public partial class Program
    {
        sealed class DiscoveryManager
        {
            const int FrequencyCount = 8;
            readonly Program _program;
            readonly ShipState _ship;
            readonly RadioNetwork _radio;
            readonly IMyBroadcastListener[] _listeners =
                new IMyBroadcastListener[FrequencyCount];
            int _frame;
            int _frequency;
            int _listenFrames;

            public DiscoveryManager(Program program, ShipState ship,
                RadioNetwork radio)
            {
                _program = program;
                _ship = ship;
                _radio = radio;
                for (int i = 0; i < FrequencyCount; i++)
                    _listeners[i] = program.IGC.RegisterBroadcastListener(
                        RadioNetwork.DiscoveryTagPrefix + i);
            }

            public void Update()
            {
                _frame++;
                if (_ship.Discovery_Mode != 0 && _frame % 900 == 0)
                {
                    _radio.BroadcastHeartbeat(_frequency++ % FrequencyCount);
                    _listenFrames = 60;
                }
                if (_ship.Broadcast_Mode != 0 && _frame % 4 == 0)
                    _radio.BroadcastHeartbeat(_frequency++ % FrequencyCount);
                if (_ship.Monitor_Mode != 0 && _frame % 24 == 0)
                    _listenFrames = 60;
                if (_listenFrames > 0)
                {
                    Listen();
                    _listenFrames--;
                }
                bool beaconOn = _ship.Beacon_Enabled != 0 &&
                    (_ship.Enemy_Exposure_Time > 0 ||
                     _ship.Supervisor_State ==
                        ShipState.SupervisorState.Emergency);
                for (int i = 0; i < _ship.Beacons.Count; i++)
                    _ship.Beacons[i].Enabled = beaconOn;
            }

            void Listen()
            {
                for (int frequency = 0; frequency < FrequencyCount; frequency++)
                    for (int messageCount = 0;
                        messageCount < 4 && _listeners[frequency].HasPendingMessage;
                        messageCount++)
                    {
                        MyIGCMessage message = _listeners[frequency].AcceptMessage();
                        string text;
                        if (!_radio.TryDecode(message.Data as string, out text) ||
                            !text.StartsWith("H1|") ||
                            _radio.IsKnownNode(message.Source))
                            continue;
                        _ship.Enemy_Exposure_Time = (DateTime.UtcNow.Ticks -
                            621355968000000000L) / 10000000.0;
                        _ship.Beacon_Enabled = 1;
                    }
            }
        }
    }
}
