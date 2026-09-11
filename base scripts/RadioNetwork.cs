using Sandbox.ModAPI.Ingame;
using System;
using System.Text;
using System.Collections.Generic;
using VRage;
using VRageMath;

namespace IngameScript
{
    public partial class Program
    {
        enum RadioColor
        {
            Red, Orange, Yellow, Green, Blue, Indigo, Violet
        }

        sealed class RadioNode
        {
            public readonly int Slot;
            public string Identity;
            public bool Assigned;
            public RadioColor Color;
            public long Address;
            public double AntennaRangeMeters;
            public double LastContactSeconds;
            public Vector3D LastKnownLocation;
            public Vector3D Predicted15Minutes;
            public Vector3D Predicted30Minutes;
            public Vector3D Predicted1Hour;
            public Vector3D Predicted1Day;
            public Vector3D UltimateLocation;
            public double LastSyncSeconds;
            public bool OutOfSync = true;

            public RadioNode(int slot)
            {
                Slot = slot;
            }
        }

        sealed class RadioNetwork
        {
            public const string NetworkTag = "VON.NEUMAN.NET";
            public const string MapTag = "VON.NEUMAN.MAP";
            public const string TaskTag = "VON.NEUMAN.TASK";
            public const string DiscoveryTagPrefix = "VON.NEUMAN.DISC.";
            const string EncodingHeader = "RAD1|";
            const int MessagesPerTick = 8;

            readonly ShipState _ship;
            readonly IMyBroadcastListener _listener;
            readonly IMyBroadcastListener _mapListener;
            readonly IMyUnicastListener _unicastListener;
            readonly IMyIntergridCommunicationSystem _igc;
            readonly GlobalTemporalBsp _globalMap;
            readonly BspSpatialMap _localMap;
            readonly RadioCipher _cipher;
            readonly long _ownAddress;
            readonly Queue<string> _outgoingMap = new Queue<string>();
            readonly Dictionary<long, StringBuilder> _incomingMap =
                new Dictionary<long, StringBuilder>();
            readonly Dictionary<long, int> _incomingPart =
                new Dictionary<long, int>();
            readonly Dictionary<long, int> _incomingSequence =
                new Dictionary<long, int>();
            double _lastDailySync = -86400;
            double _motherRequestSeconds;
            bool _waitingForMother;
            bool _fallbackSent;
            int _syncSequence;

            public readonly RadioColor[] Colors =
            {
                RadioColor.Red, RadioColor.Orange, RadioColor.Yellow,
                RadioColor.Green, RadioColor.Blue, RadioColor.Indigo,
                RadioColor.Violet
            };
            public readonly RadioNode[] Drones = CreateNodes(24);
            public readonly RadioNode[] Relays = CreateNodes(64);
            public int AssignedDroneCount { get; private set; }
            public int AssignedRelayCount { get; private set; }
            public double MaximumAntennaRangeMeters { get; private set; }
            public int OutOfSyncNodeCount
            {
                get
                {
                    int count = 0;
                    for (int kind = 0; kind < 2; kind++)
                    {
                        RadioNode[] nodes = kind == 0 ? Drones : Relays;
                        for (int i = 0; i < nodes.Length; i++)
                            if (nodes[i].Assigned && nodes[i].OutOfSync)
                                count++;
                    }
                    return count;
                }
            }
            public Action<string, long> TaskReceived;

            public RadioNetwork(Program program, ShipState ship,
                BspSpatialMap localMap, GlobalTemporalBsp globalMap)
            {
                _ship = ship;
                _globalMap = globalMap;
                _localMap = localMap;
                _igc = program.IGC;
                _ownAddress = program.Me.EntityId;
                _cipher = new RadioCipher(ship, program.Me.EntityId);
                _listener = _igc.RegisterBroadcastListener(NetworkTag);
                _mapListener = _igc.RegisterBroadcastListener(MapTag);
                _unicastListener = _igc.UnicastListener;
            }

            public void Rebuild()
            {
                MaximumAntennaRangeMeters = 0;
                for (int i = 0; i < _ship.RadioAntennas.Count; i++)
                {
                    IMyRadioAntenna antenna = _ship.RadioAntennas[i];
                    if (antenna.IsWorking)
                        MaximumAntennaRangeMeters = Math.Max(
                            MaximumAntennaRangeMeters, antenna.Radius);
                }
            }

            public void Update(double elapsedSeconds)
            {
                for (int i = 0;
                    i < MessagesPerTick && _listener.HasPendingMessage;
                    i++)
                {
                    MyIGCMessage message = _listener.AcceptMessage();
                    if (message.Tag != NetworkTag)
                        continue;
                    if (message.Data is string)
                    {
                        string presence;
                        if (_cipher.Unprotect(message.Data as string,
                            out presence) && presence.StartsWith("H1|"))
                            ReceivePresence(presence, message.Source);
                    }
                    else if (message.Data is
                        MyTuple<int, int, int, double, Vector3D>)
                        ReceiveHeartbeat(
                            (MyTuple<int, int, int, double, Vector3D>)
                                message.Data,
                            message.Source,
                            elapsedSeconds);
                    else if (message.Data is MyTuple<int, int,
                        MyTuple<Vector3D, Vector3D, Vector3D,
                            Vector3D, Vector3D>>)
                        ReceivePredictions((MyTuple<int, int,
                            MyTuple<Vector3D, Vector3D, Vector3D,
                                Vector3D, Vector3D>>)message.Data);
                }
                for (int i = 0;
                    i < MessagesPerTick && _mapListener.HasPendingMessage;
                    i++)
                    ReceiveMapMessage(_mapListener.AcceptMessage(),
                        elapsedSeconds);
                for (int i = 0;
                    i < MessagesPerTick && _unicastListener.HasPendingMessage;
                    i++)
                {
                    MyIGCMessage message = _unicastListener.AcceptMessage();
                    if (message.Tag == MapTag)
                        ReceiveMapMessage(message, elapsedSeconds);
                    else if (message.Tag == TaskTag &&
                        TaskReceived != null && IsTrusted(message.Source))
                    {
                        string task;
                        if (_cipher.Unprotect(message.Data as string, out task) &&
                            task.StartsWith("T1|"))
                            TaskReceived(task.Substring(3), message.Source);
                    }
                }
                if (_outgoingMap.Count > 0)
                    SendEncryptedBroadcast(MapTag, _outgoingMap.Dequeue());
                if (_waitingForMother && !_fallbackSent &&
                    elapsedSeconds - _motherRequestSeconds >= 60)
                {
                    SendEncryptedBroadcast(MapTag, "Q");
                    _fallbackSent = true;
                }
                if ((_ship.Supervisor_State ==
                        ShipState.SupervisorState.Docking ||
                     _ship.Supervisor_State ==
                        ShipState.SupervisorState.Docked) &&
                    elapsedSeconds - _lastDailySync >= 86400)
                    RequestMapSync(elapsedSeconds);
            }

            public bool BroadcastHeartbeat()
            {
                return BroadcastHeartbeat(-1);
            }

            public bool BroadcastHeartbeat(int frequency)
            {
                string plain = "H1|" + _ship.Node_Identity + "|" +
                    _ship.Radio_Node_Kind + "|" + _ship.Radio_Slot + "|" +
                    _ship.Radio_Color + "|" + MaximumAntennaRangeMeters + "|" +
                    _ship.CurrentShipPosition.X + "," +
                    _ship.CurrentShipPosition.Y + "," +
                    _ship.CurrentShipPosition.Z;
                return SendEncryptedBroadcast(frequency < 0 ? NetworkTag :
                    DiscoveryTagPrefix + frequency, plain);
            }

            public bool TryDecode(string packet, out string plain)
            {
                return _cipher.Unprotect(packet, out plain);
            }

            public bool IsKnownNode(long address)
            {
                return address == _ownAddress ||
                    address == _ship.Mother_Ship_Address ||
                    FindByAddress(address) != null;
            }

            void ReceivePresence(string text, long source)
            {
                string[] values = text.Split('|');
                int kind, slot, color;
                double range;
                Vector3D position;
                if (values.Length != 7 || !int.TryParse(values[2], out kind) ||
                    !int.TryParse(values[3], out slot) ||
                    !int.TryParse(values[4], out color) ||
                    !double.TryParse(values[5], out range) ||
                    !TryVector(values[6], out position) ||
                    (kind != 0 && kind != 1) || color < 0 || color > 6 ||
                    source == _ownAddress)
                    return;
                RadioNode node = GetNode(kind, slot);
                if (node == null)
                    return;
                Assign(node, kind);
                node.Identity = values[1];
                node.Color = Colors[color];
                node.Address = source;
                node.AntennaRangeMeters = Math.Max(0, range);
                node.LastKnownLocation = position;
                if (source == _ship.Mother_Ship_Address)
                    _ship.Mother_Ship_Last_Position = position;
                node.LastContactSeconds = (DateTime.UtcNow.Ticks -
                    621355968000000000L) / 10000000.0;
            }

            public void RequestMapSync(double elapsedSeconds)
            {
                _lastDailySync = elapsedSeconds;
                if (_ship.Mother_Ship_Address != 0)
                {
                    SendEncryptedUnicast(_ship.Mother_Ship_Address,
                        MapTag, "Q");
                    _motherRequestSeconds = elapsedSeconds;
                    _waitingForMother = true;
                    _fallbackSent = false;
                }
                else
                    SendEncryptedBroadcast(MapTag, "Q");
            }

            public bool SendTask(long address, string phrase)
            {
                return address != 0 && !string.IsNullOrWhiteSpace(phrase) &&
                    phrase.Length <= 4096 && SendEncryptedUnicast(address,
                        TaskTag, "T1|" + phrase);
            }

            public bool SendFreeMode(long address, bool enabled)
            {
                return SendTask(address,
                    "FREE_MODE " + (enabled ? "ON" : "OFF"));
            }

            bool SendEncryptedUnicast(long address, string tag, string plain)
            {
                string packet;
                return _cipher.Protect(plain, out packet) &&
                    _igc.SendUnicastMessage(address, tag, packet);
            }

            bool SendEncryptedBroadcast(string tag, string plain)
            {
                string packet;
                if (!_cipher.Protect(plain, out packet))
                    return false;
                _igc.SendBroadcastMessage(tag, packet,
                    TransmissionDistance.TransmissionDistanceMax);
                return true;
            }

            bool IsTrusted(long address)
            {
                return address == _ship.Mother_Ship_Address ||
                    FindByAddress(address) != null;
            }

            void ReceiveMapMessage(MyIGCMessage message, double elapsedSeconds)
            {
                string packet;
                if (!_cipher.Unprotect(message.Data as string, out packet))
                    return;
                if (packet == "Q")
                {
                    if (IsTrusted(message.Source))
                        QueueMapSnapshot();
                    return;
                }
                string[] parts = packet.Split(new char[] { '|' }, 5);
                int sequence;
                int part;
                int total;
                if (parts.Length != 5 || parts[0] != "M" ||
                    !int.TryParse(parts[1], out sequence) ||
                    !int.TryParse(parts[2], out part) ||
                    !int.TryParse(parts[3], out total) || total < 1 ||
                    total > 40)
                    return;
                StringBuilder buffer;
                int expected;
                if (part == 0)
                {
                    buffer = new StringBuilder();
                    _incomingMap[message.Source] = buffer;
                    _incomingPart[message.Source] = 0;
                    _incomingSequence[message.Source] = sequence;
                }
                if (!_incomingMap.TryGetValue(message.Source, out buffer) ||
                    !_incomingPart.TryGetValue(message.Source, out expected) ||
                    !_incomingSequence.ContainsKey(message.Source) ||
                    _incomingSequence[message.Source] != sequence ||
                    part != expected)
                    return;
                if (buffer.Length + parts[4].Length > 65000)
                {
                    _incomingMap.Remove(message.Source);
                    _incomingPart.Remove(message.Source);
                    _incomingSequence.Remove(message.Source);
                    return;
                }
                buffer.Append(parts[4]);
                _incomingPart[message.Source] = expected + 1;
                if (part + 1 == total)
                {
                    string merged = buffer.ToString();
                    int separator = merged.IndexOf('|', 4);
                    int localLength;
                    if (merged.StartsWith("SM1|") && separator > 4 &&
                        int.TryParse(merged.Substring(4, separator - 4),
                            out localLength) && localLength >= 0 &&
                        separator + 1 + localLength <= merged.Length)
                    {
                        _localMap.Merge(merged.Substring(
                            separator + 1, localLength));
                        _globalMap.Merge(merged.Substring(
                            separator + 1 + localLength));
                    }
                    _waitingForMother = false;
                    _incomingMap.Remove(message.Source);
                    _incomingPart.Remove(message.Source);
                    _incomingSequence.Remove(message.Source);
                    RadioNode node = FindByAddress(message.Source);
                    if (node != null)
                    {
                        node.LastSyncSeconds = (DateTime.UtcNow.Ticks -
                            621355968000000000L) / 10000000.0;
                        node.OutOfSync = false;
                    }
                }
            }

            void QueueMapSnapshot()
            {
                if (_outgoingMap.Count > 0)
                    return;
                const int chunkSize = 1800;
                string local = _localMap.Encode(30000);
                string map = "SM1|" + local.Length + "|" + local +
                    _globalMap.Encode(30000);
                int total = Math.Max(1,
                    (map.Length + chunkSize - 1) / chunkSize);
                int sequence = ++_syncSequence;
                for (int part = 0; part < total; part++)
                {
                    int start = part * chunkSize;
                    int count = Math.Min(chunkSize, map.Length - start);
                    _outgoingMap.Enqueue("M|" + sequence + "|" + part +
                        "|" + total + "|" + map.Substring(start, count));
                }
            }

            RadioNode FindByAddress(long address)
            {
                for (int kind = 0; kind < 2; kind++)
                {
                    RadioNode[] nodes = kind == 0 ? Drones : Relays;
                    for (int i = 0; i < nodes.Length; i++)
                        if (nodes[i].Address == address)
                            return nodes[i];
                }
                return null;
            }

            void ReceiveHeartbeat(
                MyTuple<int, int, int, double, Vector3D> packet,
                long source,
                double elapsedSeconds)
            {
                RadioNode node = GetNode(packet.Item1, packet.Item2);
                if (node == null || packet.Item3 < 0 ||
                    packet.Item3 >= Colors.Length)
                    return;
                Assign(node, packet.Item1);
                node.Color = Colors[packet.Item3];
                node.Address = source;
                node.AntennaRangeMeters = Math.Max(0, packet.Item4);
                node.LastKnownLocation = packet.Item5;
                node.LastContactSeconds = (DateTime.UtcNow.Ticks -
                    621355968000000000L) / 10000000.0;
            }

            void ReceivePredictions(MyTuple<int, int,
                MyTuple<Vector3D, Vector3D, Vector3D,
                    Vector3D, Vector3D>> packet)
            {
                RadioNode node = GetNode(packet.Item1, packet.Item2);
                if (node == null)
                    return;
                Assign(node, packet.Item1);
                node.Predicted15Minutes = packet.Item3.Item1;
                node.Predicted30Minutes = packet.Item3.Item2;
                node.Predicted1Hour = packet.Item3.Item3;
                node.Predicted1Day = packet.Item3.Item4;
                node.UltimateLocation = packet.Item3.Item5;
            }

            void Assign(RadioNode node, int kind)
            {
                if (node.Assigned)
                    return;
                node.Assigned = true;
                if (kind == 0)
                    AssignedDroneCount++;
                else
                    AssignedRelayCount++;
            }

            RadioNode GetNode(int kind, int slot)
            {
                RadioNode[] nodes = kind == 0
                    ? Drones
                    : kind == 1 ? Relays : null;
                return nodes == null || slot < 1 || slot > nodes.Length
                    ? null
                    : nodes[slot - 1];
            }

            static RadioNode[] CreateNodes(int count)
            {
                RadioNode[] nodes = new RadioNode[count];
                for (int i = 0; i < count; i++)
                    nodes[i] = new RadioNode(i + 1);
                return nodes;
            }

            public string Encode()
            {
                StringBuilder text = new StringBuilder(EncodingHeader);
                AppendVector(text, _ship.Home_Base_Coordinates);
                text.Append('|');
                AppendVector(text, _ship.Assigned_Dock_Coordinates);
                for (int kind = 0; kind < 2; kind++)
                {
                    RadioNode[] nodes = kind == 0 ? Drones : Relays;
                    for (int i = 0; i < nodes.Length; i++)
                    {
                        RadioNode node = nodes[i];
                        if (!node.Assigned)
                            continue;
                        text.Append('|').Append(kind).Append(':')
                            .Append(node.Slot).Append(':')
                            .Append((int)node.Color).Append(':')
                            .Append(node.Address).Append(':')
                            .Append(node.AntennaRangeMeters).Append(':')
                            .Append(node.Identity ?? "").Append(':')
                            .Append(node.LastContactSeconds).Append(':')
                            .Append(node.LastSyncSeconds).Append(':')
                            .Append(node.OutOfSync ? 1 : 0);
                        AppendNodeVectors(text, node);
                    }
                }
                return text.ToString();
            }

            public bool Decode(string encoded)
            {
                if (string.IsNullOrWhiteSpace(encoded))
                    return true;
                if (!encoded.StartsWith(EncodingHeader))
                    return false;
                string[] records = encoded.Substring(
                    EncodingHeader.Length).Split('|');
                Vector3D position;
                if (records.Length < 2 ||
                    !TryVector(records[0], out position))
                    return false;
                _ship.Home_Base_Coordinates = position;
                if (!TryVector(records[1], out position))
                    return false;
                _ship.Assigned_Dock_Coordinates = position;
                for (int i = 2; i < records.Length; i++)
                {
                    string[] values = records[i].Split(':');
                    int kind;
                    int slot;
                    int color;
                    long address;
                    double range;
                    if ((values.Length != 11 && values.Length != 14 &&
                        values.Length != 15) ||
                        !int.TryParse(values[0], out kind) ||
                        !int.TryParse(values[1], out slot) ||
                        !int.TryParse(values[2], out color) ||
                        !long.TryParse(values[3], out address) ||
                        !double.TryParse(values[4], out range) ||
                        color < 0 || color >= Colors.Length)
                        return false;
                    RadioNode node = GetNode(kind, slot);
                    if (node == null ||
                        !ReadNodeVectors(values, node))
                        return false;
                    Assign(node, kind);
                    node.Color = Colors[color];
                    node.Address = address;
                    node.AntennaRangeMeters = Math.Max(0, range);
                    if (values.Length == 15)
                    {
                        node.Identity = values[5];
                        double.TryParse(values[6], out node.LastContactSeconds);
                        double.TryParse(values[7], out node.LastSyncSeconds);
                        node.OutOfSync = values[8] != "0";
                    }
                }
                return true;
            }

            void AppendNodeVectors(StringBuilder text, RadioNode node)
            {
                text.Append(':');
                AppendVector(text, node.LastKnownLocation);
                text.Append(':');
                AppendVector(text, node.Predicted15Minutes);
                text.Append(':');
                AppendVector(text, node.Predicted30Minutes);
                text.Append(':');
                AppendVector(text, node.Predicted1Hour);
                text.Append(':');
                AppendVector(text, node.Predicted1Day);
                text.Append(':');
                AppendVector(text, node.UltimateLocation);
            }

            bool ReadNodeVectors(string[] values, RadioNode node)
            {
                int first = values.Length == 15 ? 9 :
                    values.Length == 14 ? 8 : 5;
                return TryVector(values[first], out node.LastKnownLocation) &&
                    TryVector(values[first + 1], out node.Predicted15Minutes) &&
                    TryVector(values[first + 2], out node.Predicted30Minutes) &&
                    TryVector(values[first + 3], out node.Predicted1Hour) &&
                    TryVector(values[first + 4], out node.Predicted1Day) &&
                    TryVector(values[first + 5], out node.UltimateLocation);
            }

            static void AppendVector(StringBuilder text, Vector3D value)
            {
                text.Append(value.X).Append(',')
                    .Append(value.Y).Append(',')
                    .Append(value.Z);
            }

            static bool TryVector(string text, out Vector3D value)
            {
                string[] parts = text.Split(',');
                double x;
                double y;
                double z;
                if (parts.Length == 3 &&
                    double.TryParse(parts[0], out x) &&
                    double.TryParse(parts[1], out y) &&
                    double.TryParse(parts[2], out z))
                {
                    value = new Vector3D(x, y, z);
                    return true;
                }
                value = Vector3D.Zero;
                return false;
            }
        }
    }
}
