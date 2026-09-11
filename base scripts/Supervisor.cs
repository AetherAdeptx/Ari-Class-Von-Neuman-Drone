using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using VRageMath;

namespace IngameScript
{
    public partial class Program
    {
        sealed class SupervisorTask
        {
            public readonly Action<object[]> Function;
            public readonly object[] Parameters;
            public readonly bool Repeat;

            public SupervisorTask(
                Action<object[]> function,
                bool repeat,
                params object[] parameters)
            {
                Function = function;
                Repeat = repeat;
                Parameters = parameters;
            }
        }

        sealed class Supervisor
        {
            readonly ShipState _ship;
            readonly TelemetryState _telemetry;
            readonly SolarTracker _solar;
            readonly Queue<SupervisorTask> _tasks =
                new Queue<SupervisorTask>();
            public readonly List<SupervisorTask> RecurringTasks =
                new List<SupervisorTask>();
            public readonly List<SupervisorTask> OneTimeTasks =
                new List<SupervisorTask>();
            double _runningSeconds;
            double _maintenanceSeconds = 5;
            ChargeMode _lastChargeMode = (ChargeMode)(-1);
            readonly Dictionary<IMyFunctionalBlock, bool> _dockEnabled =
                new Dictionary<IMyFunctionalBlock, bool>();
            readonly Dictionary<IMyGasTank, bool> _dockStockpile =
                new Dictionary<IMyGasTank, bool>();
            bool _dockSafetyApplied;

            public Supervisor(
                ShipState ship,
                TelemetryState telemetry,
                SolarTracker solar)
            {
                _ship = ship;
                _telemetry = telemetry;
                _solar = solar;
            }

            public void Enqueue(
                Action<object[]> function,
                bool repeat,
                params object[] parameters)
            {
                if (function != null)
                {
                    SupervisorTask task = new SupervisorTask(
                        function, repeat, parameters);
                    _tasks.Enqueue(task);
                    (repeat ? RecurringTasks : OneTimeTasks).Add(task);
                }
                _ship.Supervisor_Queued_Tasks = _tasks.Count;
            }

            public void Update(double elapsedSeconds)
            {
                _runningSeconds += elapsedSeconds;
                _maintenanceSeconds += elapsedSeconds;
                if (_maintenanceSeconds >= 5)
                { _maintenanceSeconds = 0; UpdateFiveSecondNeeds(); }
                State_Machine_Update();
                _solar.Update(
                    _ship.Auto_Tracking_Flag != 0 &&
                    (_ship.Supervisor_State ==
                        ShipState.SupervisorState.Solar_Charge ||
                    (_ship.Supervisor_State ==
                        ShipState.SupervisorState.Normal ||
                     _ship.Supervisor_State ==
                        ShipState.SupervisorState.Emergency) &&
                        _ship.SolarPanels.Count > 0 &&
                        (_ship.Supervisor_State ==
                            ShipState.SupervisorState.Emergency ||
                         BatteryPercent() <
                            _ship.Auto_Tracking_Enable_Level) &&
                        (_ship.ReferenceController == null ||
                            !_ship.ReferenceController.IsUnderControl)),
                    elapsedSeconds);
                ApplyBatteryMode();
                ApplyDockedSafety();
                _ship.Supervisor_Queued_Tasks = _tasks.Count;
            }

            public void State_Machine_Update()
            {
                _ship.State_Priority = 0;
                _ship.State_Blocker = "";
                if (_ship.Drone_Mode == 0)
                {
                    _ship.Free_Move = false;
                    _ship.Supervisor_State = ShipState.SupervisorState.In_Use;
                    _ship.State_Blocker = "player_mode";
                    return;
                }
                UpdateNeedHierarchy();
                if (!SurvivalReady()) return;
                _ship.State_Priority = 10;
                if (!NavigationReady()) return;
                _ship.State_Priority = 50;
                RunTasks();
                _ship.State_Priority = 100;
                if (_ship.Supervisor_State != ShipState.SupervisorState.Docked)
                    _ship.Supervisor_State = ShipState.SupervisorState.Normal;
            }

            bool SurvivalReady()
            {
                if (_ship.Hard_Stop != 0)
                { _ship.State_Blocker = "hard_stop"; _ship.Supervisor_State = ShipState.SupervisorState.Emergency; return false; }
                if (_ship.Mothership_Bound && !_ship.Mothership_In_Bounds)
                {
                    _ship.State_Blocker = _ship.Mother_Ship_Last_Position == Vector3D.Zero ? "mother_unknown" : "mother_range";
                    if (_ship.Mother_Ship_Last_Position != Vector3D.Zero)
                    { _ship.Target_Destination = _ship.Mother_Ship_Last_Position; _ship.Free_Move = true; }
                    _ship.Supervisor_State = ShipState.SupervisorState.Return_To_Base;
                    return false;
                }
                if (_runningSeconds >= 5 && ResourcesLow())
                { _ship.State_Blocker = "resources"; _ship.Supervisor_State = ShipState.SupervisorState.Emergency; return false; }
                if (_ship.Mother_Ship_Final_Destination != Vector3D.Zero && !HasReturnResources())
                { _ship.State_Blocker = "return_reserve"; _ship.Target_Destination = _ship.Mother_Ship_Final_Destination; _ship.Free_Move = true; _ship.Supervisor_State = ShipState.SupervisorState.Return_To_Base; return false; }
                return true;
            }

            void UpdateFiveSecondNeeds()
            {
                if (_ship.Mothership_Bound)
                {
                    if (_ship.Mother_Ship_Last_Position == Vector3D.Zero)
                        _ship.Mothership_In_Bounds = false;
                    else
                        _ship.Mothership_In_Bounds = Vector3D.DistanceSquared(
                            _ship.CurrentShipPosition, _ship.Mother_Ship_Last_Position) <=
                            _ship.Mothership_Bound_Range * _ship.Mothership_Bound_Range;
                }
                else _ship.Mothership_In_Bounds = true;
                bool detectorsOn = BatteryPercent() > _ship.Critical_Battery_Level;
                for (int i = 0; i < _ship.OreDetectors.Count; i++)
                    _ship.OreDetectors[i].Enabled = detectorsOn;
                bool mining = _ship.Drone_Mode >= 2 && detectorsOn &&
                    _ship.Supervisor_State ==
                    ShipState.SupervisorState.Mining;
                for (int i = 0; i < _ship.Drills.Count; i++)
                    _ship.Drills[i].Enabled = mining;
            }

            bool NavigationReady()
            {
                if (_ship.ReferenceController != null && _ship.ReferenceController.IsUnderControl)
                { _ship.State_Blocker = "pilot"; _ship.Supervisor_State = ShipState.SupervisorState.In_Use; return false; }
                if (_ship.Needs_Avoidance != 0)
                { _ship.State_Blocker = "collision"; _ship.Supervisor_State = ShipState.SupervisorState.Evasion; return false; }
                if (_ship.Supervisor_State == ShipState.SupervisorState.Docking && IsConnected())
                    _ship.Supervisor_State = ShipState.SupervisorState.Docked;
                if (_ship.Supervisor_State == ShipState.SupervisorState.Docked && !IsConnected())
                { _ship.State_Blocker = "dock_lost"; _ship.Supervisor_State = ShipState.SupervisorState.Docking; return false; }
                return true;
            }

            public void SetState(ShipState.SupervisorState state)
            {
                _ship.Supervisor_State = state;
            }

            public void SetTasksPerFrame(int count)
            {
                _ship.Tasks_Per_Frame = Math.Max(1, Math.Min(32, count));
            }

            public void ResetBlockBindings()
            {
                RestoreDockedBlocks();
            }

            void RunTasks()
            {
                int count = Math.Min(_ship.Tasks_Per_Frame, _tasks.Count);
                for (int i = 0; i < count; i++)
                {
                    SupervisorTask task = _tasks.Dequeue();
                    task.Function(task.Parameters);
                    if (task.Repeat)
                        _tasks.Enqueue(task);
                }
            }

            void UpdateNeedHierarchy()
            {
                _ship.Need_Level = 0;
                if (_ship.Mother_Ship_Final_Destination != Vector3D.Zero &&
                    !HasReturnResources())
                {
                    _ship.Need_Level = 1;
                    _ship.Supervisor_State = ShipState.SupervisorState.Return_To_Base;
                }
                bool mother = (_ship.Node_Identity ?? "").IndexOf("Mother", StringComparison.OrdinalIgnoreCase) >= 0;
                bool childrenPresent = true;
                // Radio presence is reconciled by RadioNetwork; an empty list
                // means there are no required children to wait for.
                childrenPresent = _ship.Mother_Ship_Children.Count > 0;
                _ship.Max_Speed = mother && childrenPresent ? 500 : mother ? 90 : 100;
            }

            bool HasReturnResources()
            {
                double distance = Vector3D.Distance(_ship.CurrentShipPosition,
                    _ship.Mother_Ship_Final_Destination);
                double fuel = 1;
                if (_ship.Batteries.Count > 0) fuel = BatteryPercent() / 100.0;
                return fuel > Math.Min(0.95, distance / 100000.0 + 0.1);
            }

            bool IsConnected()
            {
                for (int i = 0; i < _ship.Connectors.Count; i++)
                    if (_ship.Connectors[i].Status ==
                        MyShipConnectorStatus.Connected)
                        return true;
                return false;
            }

            void ApplyDockedSafety()
            {
                if (_ship.Supervisor_State !=
                    ShipState.SupervisorState.Docked)
                {
                    RestoreDockedBlocks();
                    return;
                }
                if (!_dockSafetyApplied)
                {
                    _dockEnabled.Clear();
                    _dockStockpile.Clear();
                    Remember(_ship.Thrusters);
                    Remember(_ship.Gyroscopes);
                    Remember(_ship.FuelSources);
                    Remember(_ship.GasGenerators);
                    for (int i = 0; i < _ship.GasTanks.Count; i++)
                        _dockStockpile[_ship.GasTanks[i]] =
                            _ship.GasTanks[i].Stockpile;
                    _dockSafetyApplied = true;
                }
                foreach (IMyFunctionalBlock block in _dockEnabled.Keys)
                    block.Enabled = false;
                for (int i = 0; i < _ship.GasTanks.Count; i++)
                    _ship.GasTanks[i].Stockpile = true;
            }

            void Remember<T>(List<T> blocks) where T : class, IMyFunctionalBlock
            {
                for (int i = 0; i < blocks.Count; i++)
                    if (!_dockEnabled.ContainsKey(blocks[i]))
                        _dockEnabled[blocks[i]] = blocks[i].Enabled;
            }

            void RestoreDockedBlocks()
            {
                if (!_dockSafetyApplied)
                    return;
                foreach (KeyValuePair<IMyFunctionalBlock, bool> item in
                    _dockEnabled)
                    item.Key.Enabled = item.Value;
                foreach (KeyValuePair<IMyGasTank, bool> item in _dockStockpile)
                    item.Key.Stockpile = item.Value;
                _dockEnabled.Clear();
                _dockStockpile.Clear();
                _dockSafetyApplied = false;
            }

            bool ResourcesLow()
            {
                if (_ship.Batteries.Count > 0 &&
                    BatteryPercent() < _ship.Low_Charge_Percent)
                    return true;
                double fuel;
                if (HasHydrogenTanks() &&
                    _telemetry.FuelPercent.TryGetValue(
                        "Hydrogen", out fuel) &&
                    fuel < _ship.Low_Fuel_Percent)
                    return true;
                return _ship.Reactors.Count > 0 &&
                    _telemetry.FuelPercent.TryGetValue(
                        "Uranium", out fuel) &&
                    fuel < _ship.Low_Fuel_Percent;
            }

            double BatteryPercent()
            {
                double charge = 0;
                double capacity = 0;
                for (int i = 0; i < _ship.Batteries.Count; i++)
                {
                    charge += _ship.Batteries[i].CurrentStoredPower;
                    capacity += _ship.Batteries[i].MaxStoredPower;
                }
                return capacity <= 0 ? 100 : charge / capacity * 100;
            }

            bool HasHydrogenTanks()
            {
                for (int i = 0; i < _ship.GasTanks.Count; i++)
                {
                    IMyGasTank tank = _ship.GasTanks[i];
                    string name = tank.BlockDefinition.SubtypeName + " " +
                        tank.DefinitionDisplayNameText;
                    if (name.IndexOf(
                        "Hydrogen", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
                return false;
            }

            bool HasSolarPower()
            {
                for (int i = 0; i < _ship.SolarPanels.Count; i++)
                    if (_ship.SolarPanels[i].IsWorking &&
                        _ship.SolarPanels[i].CurrentOutput > 0.0001)
                        return true;
                return false;
            }

            void ApplyBatteryMode()
            {
                ChargeMode mode =
                    _ship.Supervisor_State ==
                        ShipState.SupervisorState.Solar_Charge
                    ? ChargeMode.Recharge
                    : ChargeMode.Auto;
                if (mode == _lastChargeMode)
                    return;
                _lastChargeMode = mode;
                for (int i = 0; i < _ship.Batteries.Count; i++)
                    _ship.Batteries[i].ChargeMode = mode;
            }
        }
    }
}
