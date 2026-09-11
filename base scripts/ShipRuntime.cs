using Sandbox.ModAPI.Ingame;
using System;
using VRage.Game.ModAPI.Ingame;
using VRageMath;

namespace IngameScript
{
    public partial class Program
    {
        sealed class ShipRuntime
        {
            readonly Program _program;
            readonly ShipState _state;
            readonly BlockCatalog _blocks;
            readonly SeatMonitor _seats;
            readonly ThrusterClassifier _thrusters;
            readonly GyroArray _gyros;
            readonly TelemetryState _telemetry;
            readonly ResourceTelemetry _resources;
            readonly FuelTelemetry _fuels;
            readonly ShipStatsTelemetry _shipStats;
            readonly TelemetryScheduler _scheduler;
            readonly ResourceTargets _resourceTargets;
            readonly BspSpatialMap _spatialMap;
            readonly GlobalTemporalBsp _globalMap;
            readonly DistanceSensorArray _distanceSensors;
            readonly SpatialBufferMonitor _spatialBuffer;
            readonly RadioNetwork _radio;
            readonly SolarTracker _solar;
            readonly Supervisor _supervisor;
            readonly RuntimeRegistry _registry;
            readonly PersistenceStore _persistence;
            readonly Navigator _navigator;
            readonly NavigationMemory _navigationMemory;
            readonly TaskInterpreter _taskInterpreter;
            readonly DiscoveryManager _discovery;
            readonly ProspectingManager _prospecting;
            readonly OreMemory _oreMemory;
            readonly DestinationBook _destinations;
            bool _isRunning;
            double _elapsedSeconds;
            double _heartbeatTimer;

            public void SetAvoidanceDistancePerSpeed(double value) { _state.Avoidance_Distance_Per_Speed = Math.Max(0.1, Math.Min(50, value)); }
            public double GetAvoidanceDistancePerSpeed() { return _state.Avoidance_Distance_Per_Speed; }
            public string DumpRegistry() { _registry.UpdateSettings(); return _registry.EncodePersistent(); }
            public string DumpDebug() { return "state=" + _state.Supervisor_State + ";need=" + _state.Need_Level + ";avoid=" + _state.Needs_Avoidance + ";speed=" + _state.Target_Speed.ToString("0.0") + ";blocks=" + _state.Blocks.Count; }

            public ShipRuntime(Program program)
            {
                _program = program;
                _state = new ShipState();
                _blocks = new BlockCatalog(program, _state);
                _seats = new SeatMonitor(_state);
                _thrusters = new ThrusterClassifier(_state);
                _gyros = new GyroArray(_state);
                _telemetry = new TelemetryState();
                _oreMemory = new OreMemory();
                _destinations = new DestinationBook();
                _resources = new ResourceTelemetry(_state, _telemetry,
                    _oreMemory);
                _fuels = new FuelTelemetry(_state, _telemetry);
                _shipStats = new ShipStatsTelemetry(_state, _telemetry);
                _resourceTargets = new ResourceTargets(program, _telemetry);
                _spatialMap = new BspSpatialMap(
                    _state.Spatial_Cache_Distance);
                _globalMap = new GlobalTemporalBsp();
                _distanceSensors = new DistanceSensorArray(
                    _state,
                    _spatialMap,
                    _globalMap);
                _spatialBuffer = new SpatialBufferMonitor(
                    program,
                    _state,
                    _spatialMap,
                    _telemetry);
                _radio = new RadioNetwork(
                    program, _state, _spatialMap, _globalMap);
                _solar = new SolarTracker(_state);
                _supervisor = new Supervisor(
                    _state, _telemetry, _solar);
                _registry = new RuntimeRegistry(
                    _state, _radio, _globalMap);
                _registry.AttachMemory(_oreMemory, _destinations);
                _persistence = new PersistenceStore(
                    _spatialMap, _globalMap, _radio, _registry, _oreMemory,
                    _destinations);
                _navigator = new Navigator(
                    _state, _spatialMap, _globalMap);
                _navigationMemory = new NavigationMemory();
                _taskInterpreter = new TaskInterpreter(
                    _state, _supervisor, _radio, _registry, _destinations,
                    _oreMemory);
                _radio.TaskReceived = ReceiveTask;
                _discovery = new DiscoveryManager(program, _state, _radio);
                _prospecting = new ProspectingManager(_state, _oreMemory);
                _scheduler = new TelemetryScheduler(
                    _telemetry,
                    _shipStats,
                    _fuels,
                    _resources,
                    _registry);
                _supervisor.Enqueue(
                    UpdateDistanceTask, true, _distanceSensors);
                _supervisor.Enqueue(UpdateRadioTask, true, _radio);
                _supervisor.Enqueue(UpdateProspectingTask, true, _prospecting);
            }

            public void Init()
            {
                _isRunning = true;
                _program.Runtime.UpdateFrequency = UpdateFrequency.Update1;
                Reinit(null, "Initialized");
                string stored = _program.Storage ?? "";
                if (stored.StartsWith("VN2|"))
                {
                    if (!_persistence.Decode(stored))
                        _program.Echo("Persistent state could not be decoded");
                }
                else
                {
                    int radioStart = stored.IndexOf('\n');
                    string spatialData = radioStart < 0 ? stored :
                        stored.Substring(0, radioStart);
                    if (!_spatialMap.Decode(spatialData))
                        _program.Echo("Stored BSP map could not be decoded");
                    if (radioStart >= 0 && !_radio.Decode(
                        stored.Substring(radioStart + 1)))
                        _program.Echo("Stored radio network could not decode");
                }
                _state.SetSpatialCacheDistance(
                    _spatialMap.SpatialCacheDistance);
                _registry.UpdateSettings();
            }

            public void Reinit(
                IMyShipController preferredController,
                string reason)
            {
                _supervisor.ResetBlockBindings();
                _blocks.ScanOnce();
                _state.ReferenceController =
                    _seats.SelectReference(preferredController);
                if (_state.ReferenceController != null)
                {
                    _spatialMap.Configure(
                        _state.ReferenceController.CubeGrid.GridSize,
                        _state.Spatial_Cache_Distance,
                        _program.Me.GetPosition());
                }
                else
                {
                    _spatialMap.Clear();
                }
                _resourceTargets.Reload();
                _thrusters.RebuildGroups();
                _gyros.SortInnerToOuter();
                _distanceSensors.RebuildGroups();
                _radio.Rebuild();
                _registry.UpdateSettings();
                _seats.ResetEdgeState();
                _blocks.CrawlNextBlock();
                _scheduler.RestartCycle();
                PrintStatus(reason);
            }

            public void Main(string argument, UpdateType updateSource)
            {
                if (string.Equals(argument, "init",
                    StringComparison.OrdinalIgnoreCase))
                {
                    Init();
                    return;
                }

                if (string.Equals(argument, "close",
                    StringComparison.OrdinalIgnoreCase))
                {
                    Close();
                    return;
                }

                if (!_isRunning)
                    return;

                _elapsedSeconds += _program.Runtime.TimeSinceLastRun.TotalSeconds;
                _spatialMap.Recenter(_program.Me.GetPosition());
                _spatialBuffer.UpdateEveryFrame();
                _globalMap.MarkScanned(_program.Me.GetPosition());
                _blocks.CrawlNextBlock();

                IMyShipController enteredSeat = _seats.PollEnteredSeat();
                if (enteredSeat != null)
                {
                    Reinit(enteredSeat, "Seat entered; reinitialized");
                    return;
                }

                if (string.Equals(argument, "reinit",
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(argument, "rescan",
                        StringComparison.OrdinalIgnoreCase))
                {
                    Reinit(_seats.FindOccupiedSeat(), "Manual reinitialize");
                    return;
                }

                if (string.Equals(argument, "status",
                    StringComparison.OrdinalIgnoreCase))
                {
                    PrintStatus("Current telemetry");
                    return;
                }

                if (string.Equals(argument, "save-data",
                    StringComparison.OrdinalIgnoreCase))
                {
                    Save();
                    _program.Echo("Persistent state saved");
                    return;
                }

                if (string.Equals(argument, "load-data",
                    StringComparison.OrdinalIgnoreCase))
                {
                    if (!_persistence.Decode(_program.Storage ?? ""))
                        _program.Echo("Persistent state could not be loaded");
                    else
                        _program.Echo("Persistent state loaded");
                    return;
                }

                if (TrySetSpatialCacheDistance(argument) ||
                    TrySetCoordinates(argument, "home-base ", true) ||
                    TrySetCoordinates(argument, "assigned-dock ", false) ||
                    TrySetAutoTrackingLevel(argument) ||
                    TrySetAutoTracking(argument) ||
                    TrySetIdentity(argument) ||
                    TrySetRadioKey(argument) ||
                    TrySetRadioSlot(argument) ||
                    TrySetRadioColor(argument) ||
                    TrySetRadioKind(argument) ||
                    TrySetDiscoveryMode(argument) ||
                    TrySetDroneMode(argument) ||
                    TrySetNamedDestination(argument) ||
                    TrySetBeacon(argument) ||
                    TryInterpreterCommand(argument) ||
                    TryNavigationCommand(argument) ||
                    TryRadioFreeMode(argument) ||
                    TrySendTask(argument) ||
                    TrySetMotherShip(argument) ||
                    TrySyncMaps(argument) ||
                    TrySetTasksPerFrame(argument) ||
                    TrySetSupervisorState(argument))
                    return;

                _supervisor.Update(
                    _program.Runtime.TimeSinceLastRun.TotalSeconds);
                _taskInterpreter.Update(
                    _program.Runtime.TimeSinceLastRun.TotalSeconds);
                _navigator.Update();
                _navigationMemory.Update(_state,
                    _program.Runtime.TimeSinceLastRun.TotalSeconds);
                _scheduler.Tick(_elapsedSeconds);
                if (_state.Drone_Mode != 0)
                    _discovery.Update();
                _heartbeatTimer += _program.Runtime.TimeSinceLastRun.TotalSeconds;
                if (_state.Drone_Mode != 0 && _heartbeatTimer >= 15)
                {
                    _heartbeatTimer -= 15;
                    _radio.BroadcastHeartbeat();
                }
            }

            bool TryInterpreterCommand(string argument)
            {
                if (argument == null) return false;
                string line = null;
                if (argument.StartsWith("task ", StringComparison.OrdinalIgnoreCase))
                    line = argument.Substring(5);
                else if (argument.Equals("dump-debug", StringComparison.OrdinalIgnoreCase))
                {
                    _program.Echo(DumpDebug());
                    return true;
                }
                else if (argument.Equals("dump-registry", StringComparison.OrdinalIgnoreCase))
                {
                    string registry = DumpRegistry();
                    _program.Echo(registry.Substring(0,
                        Math.Min(3500, registry.Length)));
                    return true;
                }
                else if (argument.StartsWith("set ", StringComparison.OrdinalIgnoreCase))
                    line = argument;
                if (line == null) return false;
                _taskInterpreter.Enqueue(line, 0);
                return true;
            }

            bool TryVector(string text, out Vector3D value)
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

            public void Save()
            {
                _program.Storage = _persistence.Encode();
            }

            public void Close()
            {
                _isRunning = false;
                _program.Runtime.UpdateFrequency = UpdateFrequency.None;
                _program.Echo("Closed; run with argument 'init' to restart");
            }

            public int GetHardStop()
            {
                return _state.Hard_Stop;
            }

            public void SetSpatialCacheDistance(int distance)
            {
                _state.SetSpatialCacheDistance(distance);
                _spatialMap.SetSpatialCacheDistance(
                    _state.Spatial_Cache_Distance);
                _registry.UpdateSettings();
                string span = GetSpatialCacheSpanKilometers().ToString("0.###");
                _program.Echo("Spatial cache: " + span + "x" + span +
                    "x" + span + " km (" + GetSpatialCacheDistance() +
                    " trees/axis)");
            }

            public int GetSpatialCacheDistance()
            {
                return _state.Spatial_Cache_Distance;
            }

            public double GetSpatialCacheSpanKilometers()
            {
                return _spatialMap.CacheSpanMeters / 1000.0;
            }

            public byte GetSpatialValue(Vector3D localPositionMeters)
            {
                if (_state.ReferenceController == null)
                    return 0;
                return _spatialMap.GetValue(Vector3D.Transform(
                    localPositionMeters,
                    _state.ReferenceController.CubeGrid.WorldMatrix));
            }

            public byte GetSpatialValueAtWorld(Vector3D worldPositionMeters)
            {
                return _spatialMap.GetValue(worldPositionMeters);
            }

            public string EncodeSpatialMap()
            {
                return _spatialMap.Encode();
            }

            public bool DecodeSpatialMap(string encoded)
            {
                bool decoded = _spatialMap.Decode(encoded);
                if (decoded)
                {
                    _state.SetSpatialCacheDistance(
                        _spatialMap.SpatialCacheDistance);
                    _registry.UpdateSettings();
                }
                return decoded;
            }

            bool TrySetSpatialCacheDistance(string argument)
            {
                const string command = "spatial-cache ";
                if (argument == null || !argument.StartsWith(
                    command,
                    StringComparison.OrdinalIgnoreCase))
                    return false;

                int distance;
                if (int.TryParse(
                    argument.Substring(command.Length).Trim(),
                    out distance))
                    SetSpatialCacheDistance(distance);
                else
                    _program.Echo("Use: spatial-cache 1..16");
                return true;
            }

            bool TrySetCoordinates(
                string argument,
                string command,
                bool homeBase)
            {
                if (argument == null || !argument.StartsWith(
                    command,
                    StringComparison.OrdinalIgnoreCase))
                    return false;
                string[] values = argument.Substring(command.Length).Split(',');
                double x;
                double y;
                double z;
                if (values.Length == 3 &&
                    double.TryParse(values[0], out x) &&
                    double.TryParse(values[1], out y) &&
                    double.TryParse(values[2], out z))
                {
                    if (homeBase)
                        _state.Home_Base_Coordinates = new Vector3D(x, y, z);
                    else
                        _state.Assigned_Dock_Coordinates =
                            new Vector3D(x, y, z);
                    _registry.UpdateSettings();
                    _program.Echo(command.Trim() + " coordinates updated");
                }
                else
                {
                    _program.Echo("Use: " + command + "X,Y,Z");
                }
                return true;
            }

            bool TrySetTasksPerFrame(string argument)
            {
                const string command = "tasks-per-frame ";
                if (argument == null || !argument.StartsWith(
                    command, StringComparison.OrdinalIgnoreCase))
                    return false;
                int count;
                if (int.TryParse(
                    argument.Substring(command.Length), out count))
                {
                    _supervisor.SetTasksPerFrame(count);
                    _registry.UpdateSettings();
                }
                else
                    _program.Echo("Use: tasks-per-frame 1..32");
                return true;
            }

            bool TrySetAutoTracking(string argument)
            {
                const string command = "auto-tracking ";
                if (argument == null || !argument.StartsWith(
                    command, StringComparison.OrdinalIgnoreCase))
                    return false;
                string value = argument.Substring(command.Length).Trim();
                if (value.Equals("on", StringComparison.OrdinalIgnoreCase))
                    _state.Auto_Tracking_Flag = 1;
                else if (value.Equals(
                    "off", StringComparison.OrdinalIgnoreCase))
                    _state.Auto_Tracking_Flag = 0;
                else
                {
                    _program.Echo("Use: auto-tracking on|off");
                    return true;
                }
                _registry.UpdateSettings();
                return true;
            }

            bool TrySetAutoTrackingLevel(string argument)
            {
                const string command = "auto-tracking-level ";
                if (argument == null || !argument.StartsWith(
                    command, StringComparison.OrdinalIgnoreCase))
                    return false;
                double level;
                if (double.TryParse(
                    argument.Substring(command.Length), out level))
                {
                    _state.Auto_Tracking_Enable_Level =
                        Math.Max(0, Math.Min(100, level));
                    _registry.UpdateSettings();
                }
                else
                    _program.Echo("Use: auto-tracking-level 0..100");
                return true;
            }

            bool TrySetMotherShip(string argument)
            {
                const string command = "mother-ship ";
                if (argument == null || !argument.StartsWith(command,
                    StringComparison.OrdinalIgnoreCase))
                    return false;
                long address;
                if (long.TryParse(argument.Substring(command.Length).Trim(),
                    out address))
                {
                    _state.Mother_Ship_Address = address;
                    _registry.UpdateSettings();
                }
                else
                    _program.Echo("Use: mother-ship ENTITY_ID");
                return true;
            }

            bool TrySetIdentity(string argument)
            {
                const string command = "identity ";
                if (argument == null || !argument.StartsWith(command,
                    StringComparison.OrdinalIgnoreCase))
                    return false;
                string identity = argument.Substring(command.Length).Trim();
                if (identity.Length == 0 || identity.Length > 32 ||
                    identity.IndexOfAny(new char[] { '|', ';', '=', ':' }) >= 0)
                    _program.Echo("Identity: 1..32 chars, no |;=:");
                else
                {
                    _state.Node_Identity = identity;
                    _registry.UpdateSettings();
                    _program.Echo("Identity: " + identity);
                }
                return true;
            }

            bool TrySetRadioKey(string argument)
            {
                const string command = "radio-key ";
                if (argument == null || !argument.StartsWith(command,
                    StringComparison.OrdinalIgnoreCase))
                    return false;
                string key = argument.Substring(command.Length).Trim();
                if (key.Length < 8 || key.Length > 64 ||
                    key.IndexOfAny(new char[] { '|', ';', '=', '\n', '\r' }) >= 0)
                    _program.Echo("Radio key: 8..64 chars, no |;= or newline");
                else
                {
                    _state.Radio_Encryption_Key = key;
                    _registry.UpdateSettings();
                    _program.Echo("Radio encryption key updated");
                }
                return true;
            }

            bool TryRadioFreeMode(string argument)
            {
                const string command = "radio-free-mode ";
                if (argument == null || !argument.StartsWith(command,
                    StringComparison.OrdinalIgnoreCase))
                    return false;
                string body = argument.Substring(command.Length);
                int split = body.IndexOf(' ');
                long address;
                if (split < 1 || !long.TryParse(body.Substring(0, split),
                    out address))
                {
                    _program.Echo("Use: radio-free-mode ADDRESS on|off");
                    return true;
                }
                string value = body.Substring(split + 1).Trim();
                bool valid = value.Equals("on",
                    StringComparison.OrdinalIgnoreCase) ||
                    value.Equals("off", StringComparison.OrdinalIgnoreCase);
                if (!valid || !_radio.SendFreeMode(address,
                    value.Equals("on", StringComparison.OrdinalIgnoreCase)))
                    _program.Echo("Encrypted Free_Mode command not sent");
                return true;
            }

            bool TrySetRadioSlot(string argument)
            {
                const string command = "radio-slot ";
                if (argument == null || !argument.StartsWith(command,
                    StringComparison.OrdinalIgnoreCase))
                    return false;
                int slot;
                if (int.TryParse(argument.Substring(command.Length).Trim(),
                    out slot))
                {
                    int max = _state.Radio_Node_Kind == 0 ? 24 : 64;
                    _state.Radio_Slot = Math.Max(1, Math.Min(max, slot));
                    _registry.UpdateSettings();
                }
                else
                    _program.Echo("Use: radio-slot 1..24 (drone) or 1..64 (relay)");
                return true;
            }

            bool TrySetRadioColor(string argument)
            {
                const string command = "radio-color ";
                if (argument == null || !argument.StartsWith(command,
                    StringComparison.OrdinalIgnoreCase))
                    return false;
                string value = argument.Substring(command.Length).Trim();
                string[] names = { "red", "orange", "yellow", "green",
                    "blue", "indigo", "violet" };
                int color = Array.IndexOf(names, value.ToLowerInvariant());
                if (color < 0)
                {
                    int.TryParse(value, out color);
                }
                if (color >= 0 && color < 7)
                {
                    _state.Radio_Color = color;
                    _registry.UpdateSettings();
                }
                else
                    _program.Echo("Use: radio-color red|orange|yellow|green|blue|indigo|violet");
                return true;
            }

            bool TrySetRadioKind(string argument)
            {
                const string command = "radio-kind ";
                if (argument == null || !argument.StartsWith(command,
                    StringComparison.OrdinalIgnoreCase))
                    return false;
                string value = argument.Substring(command.Length).Trim();
                if (value.Equals("drone", StringComparison.OrdinalIgnoreCase))
                    _state.Radio_Node_Kind = 0;
                else if (value.Equals("relay", StringComparison.OrdinalIgnoreCase))
                    _state.Radio_Node_Kind = 1;
                else
                {
                    _program.Echo("Use: radio-kind drone|relay");
                    return true;
                }
                int max = _state.Radio_Node_Kind == 0 ? 24 : 64;
                _state.Radio_Slot = Math.Min(max, _state.Radio_Slot);
                _registry.UpdateSettings();
                return true;
            }

            bool TrySetDiscoveryMode(string argument)
            {
                const string command = "mode ";
                if (argument == null || !argument.StartsWith(command,
                    StringComparison.OrdinalIgnoreCase))
                    return false;
                string value = argument.Substring(command.Length).Trim();
                _state.Discovery_Mode = value.Equals("discovery",
                    StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                _state.Broadcast_Mode = value.Equals("broadcast",
                    StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                _state.Monitor_Mode = value.Equals("monitor",
                    StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                if (!value.Equals("discovery", StringComparison.OrdinalIgnoreCase) &&
                    !value.Equals("broadcast", StringComparison.OrdinalIgnoreCase) &&
                    !value.Equals("monitor", StringComparison.OrdinalIgnoreCase) &&
                    !value.Equals("off", StringComparison.OrdinalIgnoreCase))
                    _program.Echo("Use: mode discovery|broadcast|monitor|off");
                _registry.UpdateSettings();
                return true;
            }

            bool TrySetDroneMode(string argument)
            {
                const string command = "drone-mode ";
                if (argument == null || !argument.StartsWith(command,
                    StringComparison.OrdinalIgnoreCase))
                    return false;
                int mode;
                if (int.TryParse(argument.Substring(command.Length).Trim(),
                    out mode))
                {
                    _state.Drone_Mode = Math.Max(0, Math.Min(2, mode));
                    if (_state.Drone_Mode < 2)
                        _state.Free_Move = false;
                    _registry.UpdateSettings();
                    _program.Echo("Drone_Mode: " + _state.Drone_Mode);
                }
                else
                    _program.Echo("Use: drone-mode 0|1|2");
                return true;
            }

            bool TrySetNamedDestination(string argument)
            {
                const string command = "dest ";
                if (argument == null || !argument.StartsWith(command,
                    StringComparison.OrdinalIgnoreCase))
                    return false;
                string body = argument.Substring(command.Length);
                int split = body.IndexOf(' ');
                Vector3D position;
                if (split > 0 && TryVector(body.Substring(split + 1),
                    out position) && _destinations.Set(body.Substring(0, split),
                    position))
                {
                    _registry.UpdateSettings();
                    _program.Echo("Destination saved");
                }
                else
                    _program.Echo("Use: dest NAME X,Y,Z");
                return true;
            }

            bool TrySetBeacon(string argument)
            {
                const string command = "beacon ";
                if (argument == null || !argument.StartsWith(command,
                    StringComparison.OrdinalIgnoreCase))
                    return false;
                string value = argument.Substring(command.Length).Trim();
                if (value.Equals("on", StringComparison.OrdinalIgnoreCase))
                    _state.Beacon_Enabled = 1;
                else if (value.Equals("off", StringComparison.OrdinalIgnoreCase))
                    _state.Beacon_Enabled = 0;
                else
                    _program.Echo("Use: beacon on|off");
                _registry.UpdateSettings();
                return true;
            }

            bool TrySyncMaps(string argument)
            {
                if (!string.Equals(argument, "sync-maps",
                    StringComparison.OrdinalIgnoreCase))
                    return false;
                _radio.RequestMapSync(_elapsedSeconds);
                _program.Echo("Spatial-map sync requested");
                return true;
            }

            bool TryNavigationCommand(string argument)
            {
                string line = null;
                if (argument != null && argument.StartsWith("target ",
                    StringComparison.OrdinalIgnoreCase))
                    line = "SET_DESTINATION " + argument.Substring(7);
                else if (argument != null && argument.StartsWith("goto ",
                    StringComparison.OrdinalIgnoreCase))
                    line = "GOTO " + argument.Substring(5);
                else if (argument != null && argument.StartsWith("free-move ",
                    StringComparison.OrdinalIgnoreCase))
                    line = "FREE_MOVE " + argument.Substring(10);
                else if (argument != null && argument.StartsWith("free-mode ",
                    StringComparison.OrdinalIgnoreCase))
                    line = "FREE_MODE " + argument.Substring(10);
                else if (argument != null && argument.StartsWith("cruise-speed ",
                    StringComparison.OrdinalIgnoreCase))
                    line = "CRUISE_SPEED " + argument.Substring(13);
                else if (argument != null && argument.StartsWith("travel-speed ",
                    StringComparison.OrdinalIgnoreCase))
                    line = "TRAVEL_SPEED " + argument.Substring(13);
                else if (argument != null && argument.StartsWith("task-lines ",
                    StringComparison.OrdinalIgnoreCase))
                {
                    int count;
                    if (int.TryParse(argument.Substring(11), out count))
                        _state.Task_Object_Lines_Per_Frame = Math.Max(1,
                            Math.Min(32, count));
                    return true;
                }
                if (line == null)
                    return false;
                _taskInterpreter.Enqueue(line, _program.Me.EntityId);
                return true;
            }

            bool TrySendTask(string argument)
            {
                const string command = "send-task ";
                if (argument == null || !argument.StartsWith(command,
                    StringComparison.OrdinalIgnoreCase))
                    return false;
                string body = argument.Substring(command.Length);
                int split = body.IndexOf('|');
                long address;
                if (split < 1 || !long.TryParse(body.Substring(0, split),
                    out address) || !_radio.SendTask(address,
                        body.Substring(split + 1)))
                    _program.Echo("Use: send-task ADDRESS|COMMAND;COMMAND");
                return true;
            }

            void ReceiveTask(string phrase, long source)
            {
                _taskInterpreter.Enqueue(phrase, source);
            }

            bool TrySetSupervisorState(string argument)
            {
                const string command = "state ";
                if (argument == null || !argument.StartsWith(
                    command, StringComparison.OrdinalIgnoreCase))
                    return false;
                ShipState.SupervisorState state;
                string name = argument.Substring(command.Length)
                    .Trim().Replace('-', '_');
                if (Enum.TryParse(name, true, out state))
                {
                    _supervisor.SetState(state);
                    _registry.UpdateSettings();
                }
                else
                    _program.Echo("Unknown supervisor state");
                return true;
            }

            void UpdateDistanceTask(object[] parameters)
            {
                if (_state.Drone_Mode == 0)
                    return;
                ((DistanceSensorArray)parameters[0]).Update(
                    _program.Runtime.TimeSinceLastRun.TotalSeconds);
            }

            void UpdateRadioTask(object[] parameters)
            {
                if (_state.Drone_Mode == 0)
                    return;
                ((RadioNetwork)parameters[0]).Update(_elapsedSeconds);
            }

            void UpdateProspectingTask(object[] parameters)
            {
                ((ProspectingManager)parameters[0]).Update(
                    _program.Runtime.TimeSinceLastRun.TotalSeconds);
            }

            void PrintStatus(string reason)
            {
                _program.Echo(reason);
                _program.Echo("Reference: " +
                    (_state.ReferenceController == null
                        ? "none"
                        : _state.ReferenceController.CustomName));
                _program.Echo("Blocks: " + _state.Blocks.Count);
                _program.Echo("Thrusters: " + _state.Thrusters.Count);
                _program.Echo("Move F/B/U/D/L/R: " +
                    _state.Forward.Count + "/" +
                    _state.Backward.Count + "/" +
                    _state.Up.Count + "/" +
                    _state.Down.Count + "/" +
                    _state.Left.Count + "/" +
                    _state.Right.Count);
                _program.Echo("Telemetry jobs: " +
                    _telemetry.UpdateNames.Count + " (2/tick)");
                _program.Echo("Cameras F/B/U/D/L/R: " +
                    _state.SensorsForward.Count + "/" +
                    _state.SensorsBackward.Count + "/" +
                    _state.SensorsUp.Count + "/" +
                    _state.SensorsDown.Count + "/" +
                    _state.SensorsLeft.Count + "/" +
                    _state.SensorsRight.Count);
                _program.Echo("Gyros: " + _state.Gyroscopes.Count);
                _program.Echo("BSP trees/region meters: " +
                    _spatialMap.Trees.Count + "/" +
                    _spatialMap.RegionSizeMeters.ToString("0.0"));
                _program.Echo("BSP span/tree meters: " +
                    _spatialMap.CacheSpanMeters.ToString("0.0") + "/" +
                    _spatialMap.TreeSizeMeters.ToString("0.0"));
                _program.Echo("BSP empty regions: " +
                    _spatialMap.EmptyRegionCount);
                _program.Echo("BSP estimated memory: " +
                    (_spatialMap.EstimatedMemoryBytes /
                        (1024.0 * 1024.0)).ToString("0.000") + " MiB");
                _program.Echo("Identity: " + _state.Node_Identity);
                _program.Echo("Spatial cache trees/axis: " +
                    _registry.Values["Spatial_Cache_Distance"]);
                _program.Echo("Hard stop: " + _state.Hard_Stop);
                _program.Echo("Velocity encounter: " +
                    _state.Velocity_Vector_Encounter_Detected);
                _program.Echo("Radio drones/relays: " +
                    _radio.AssignedDroneCount + "/" +
                    _radio.AssignedRelayCount);
                _program.Echo("Supervisor: " +
                    _state.Supervisor_State + " (" +
                    _state.Supervisor_Queued_Tasks + " queued)");

                double powerUse;
                if (_telemetry.ShipStats.TryGetValue(
                    "PowerUseMW", out powerUse))
                {
                    _program.Echo("Power use: " +
                        powerUse.ToString("0.000") + " MW");
                }

                _program.Echo("Fuel B/H/O/U/I: " +
                    Fuel("Battery") + "/" +
                    Fuel("Hydrogen") + "/" +
                    Fuel("Oxygen") + "/" +
                    Fuel("Uranium") + "/" +
                    Fuel("Ice"));
            }

            string Fuel(string name)
            {
                double value;
                if (!_telemetry.FuelPercent.TryGetValue(name, out value))
                    return "0%";
                return value.ToString("0.0") + "%";
            }
        }
    }
}
