using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using VRageMath;

namespace IngameScript
{
    public partial class Program
    {
        sealed class Navigator
        {
            const double CellMeters = 100;
            const double PlanningHorizonMeters = 1500;
            const double ArrivalMeters = 20;
            const int MaximumSearchNodes = 4096;

            sealed class SearchNode
            {
                public Vector3I Cell;
                public SearchNode Parent;
                public double G;
                public double F;
            }

            readonly ShipState _ship;
            readonly BspSpatialMap _map;
            readonly GlobalTemporalBsp _globalMap;
            readonly List<Vector3D> _waypoints = new List<Vector3D>();
            readonly List<SearchNode> _open = new List<SearchNode>();
            readonly Dictionary<Vector3I, SearchNode> _nodes =
                new Dictionary<Vector3I, SearchNode>();
            readonly HashSet<Vector3I> _closed = new HashSet<Vector3I>();
            Vector3D _plannedDestination;
            Vector3I _goalCell;
            Vector3I _startCell;
            bool _searching;

            public Navigator(ShipState ship, BspSpatialMap map,
                GlobalTemporalBsp globalMap)
            {
                _ship = ship;
                _map = map;
                _globalMap = globalMap;
            }

            public void Update()
            {
                if (!MayMove())
                {
                    ReleaseControl();
                    return;
                }
                IMyShipController controller = _ship.ReferenceController;
                Vector3D position = controller.GetPosition();
                Vector3D toFinal = _ship.Target_Destination - position;
                if (toFinal.Length() <= ArrivalMeters)
                {
                    _ship.Free_Move = false;
                    _waypoints.Clear();
                    ReleaseControl();
                    return;
                }
                if (Vector3D.DistanceSquared(_plannedDestination,
                    _ship.Target_Destination) > 1 ||
                    _waypoints.Count == 0 && !_searching)
                    BeginPlan(position);
                ExpandSearch();
                while (_waypoints.Count > 0 && Vector3D.Distance(
                    position, _waypoints[0]) <= ArrivalMeters)
                    _waypoints.RemoveAt(0);
                if (_waypoints.Count == 0)
                {
                    if (!_searching)
                        BeginPlan(position);
                    Brake(controller);
                    return;
                }
                Simplify(position);
                Fly(controller, position, _waypoints[0]);
                _ship.Navigation_Waypoint_Count = _waypoints.Count;
            }

            bool MayMove()
            {
                if (!_ship.Free_Move || _ship.Hard_Stop != 0 ||
                    _ship.ReferenceController == null)
                    return false;
                ShipState.SupervisorState state = _ship.Supervisor_State;
                return state == ShipState.SupervisorState.Normal ||
                    state == ShipState.SupervisorState.Search ||
                    state == ShipState.SupervisorState.Track_Radio ||
                    state == ShipState.SupervisorState.Return_To_Base;
            }

            void BeginPlan(Vector3D position)
            {
                _plannedDestination = _ship.Target_Destination;
                _waypoints.Clear();
                _open.Clear();
                _nodes.Clear();
                _closed.Clear();
                Vector3D direction = Vector3D.Normalize(
                    _plannedDestination - position);
                Vector3D localGoal = position + direction * Math.Min(
                    PlanningHorizonMeters,
                    Vector3D.Distance(position, _plannedDestination));
                bool unknown;
                if (SegmentClear(position, localGoal, out unknown) && !unknown)
                {
                    _waypoints.Add(localGoal);
                    _ship.Navigation_Dangerous_Space = unknown ||
                        _globalMap.GetTimestamp(localGoal) == 0;
                    _searching = false;
                    return;
                }
                Vector3I start = Cell(position);
                _startCell = start;
                _goalCell = Cell(localGoal);
                SearchNode root = new SearchNode
                {
                    Cell = start,
                    G = 0,
                    F = Heuristic(start, _goalCell)
                };
                _open.Add(root);
                _nodes[start] = root;
                _searching = true;
                _ship.Navigation_Dangerous_Space = true;
            }

            void ExpandSearch()
            {
                if (!_searching)
                    return;
                for (int expansion = 0;
                    expansion < _ship.Navigation_Nodes_Per_Frame &&
                    _open.Count > 0;
                    expansion++)
                {
                    int best = 0;
                    for (int i = 1; i < _open.Count; i++)
                        if (_open[i].F < _open[best].F)
                            best = i;
                    SearchNode current = _open[best];
                    _open.RemoveAt(best);
                    if (_closed.Contains(current.Cell))
                        continue;
                    _closed.Add(current.Cell);
                    if (current.Cell == _goalCell)
                    {
                        BuildPath(current);
                        _searching = false;
                        return;
                    }
                    AddNeighbor(current, new Vector3I(1, 0, 0));
                    AddNeighbor(current, new Vector3I(-1, 0, 0));
                    AddNeighbor(current, new Vector3I(0, 1, 0));
                    AddNeighbor(current, new Vector3I(0, -1, 0));
                    AddNeighbor(current, new Vector3I(0, 0, 1));
                    AddNeighbor(current, new Vector3I(0, 0, -1));
                }
                if (_open.Count == 0)
                    _searching = false;
            }

            void AddNeighbor(SearchNode parent, Vector3I offset)
            {
                Vector3I cell = parent.Cell + offset;
                if (_closed.Contains(cell) || _nodes.Count >= MaximumSearchNodes ||
                    Math.Abs(cell.X - _startCell.X) > 24 ||
                    Math.Abs(cell.Y - _startCell.Y) > 24 ||
                    Math.Abs(cell.Z - _startCell.Z) > 24)
                    return;
                byte state = ClearanceState(World(cell));
                if (state == 2)
                    return;
                double g = parent.G + (state == 0 ? 1.3 : 1.0);
                SearchNode node;
                if (_nodes.TryGetValue(cell, out node) && g >= node.G)
                    return;
                if (node == null)
                {
                    node = new SearchNode { Cell = cell };
                    _nodes[cell] = node;
                }
                node.Parent = parent;
                node.G = g;
                node.F = g + Heuristic(cell, _goalCell);
                _open.Add(node);
            }

            void BuildPath(SearchNode node)
            {
                _waypoints.Clear();
                while (node.Parent != null)
                {
                    _waypoints.Insert(0, World(node.Cell));
                    node = node.Parent;
                }
            }

            void Simplify(Vector3D position)
            {
                for (int i = _waypoints.Count - 1; i > 0; i--)
                {
                    bool unknown;
                    if (!SegmentClear(position, _waypoints[i], out unknown) ||
                        unknown)
                        continue;
                    _waypoints.RemoveRange(0, i);
                    _ship.Navigation_Dangerous_Space = unknown;
                    break;
                }
            }

            bool SegmentClear(Vector3D start, Vector3D end,
                out bool unknown)
            {
                unknown = false;
                double distance = Vector3D.Distance(start, end);
                int samples = Math.Max(1, (int)Math.Ceiling(
                    distance / CellMeters));
                for (int i = 1; i <= samples; i++)
                {
                    byte state = ClearanceState(Vector3D.Lerp(
                        start, end, i / (double)samples));
                    if (state == 2)
                        return false;
                    if (state == 0)
                        unknown = true;
                }
                return true;
            }

            byte ClearanceState(Vector3D position)
            {
                double radius = Math.Max(CellMeters * 0.5,
                    _ship.Collision_Bounding_Radius_Meters + 5);
                Vector3D[] probes =
                {
                    position, position + Vector3D.Right * radius,
                    position + Vector3D.Left * radius,
                    position + Vector3D.Up * radius,
                    position + Vector3D.Down * radius,
                    position + Vector3D.Forward * radius,
                    position + Vector3D.Backward * radius
                };
                byte result = 1;
                for (int i = 0; i < probes.Length; i++)
                {
                    byte value = _map.GetValue(probes[i]);
                    if (value == 2)
                        return 2;
                    if (value == 0)
                        result = 0;
                }
                return result;
            }

            void Fly(IMyShipController controller, Vector3D position,
                Vector3D waypoint)
            {
                Vector3D direction = Vector3D.Normalize(waypoint - position);
                bool unknown;
                SegmentClear(position, waypoint, out unknown);
                bool danger = unknown ||
                    _globalMap.GetTimestamp(waypoint) == 0 ||
                    _ship.Velocity_Vector_Encounter_Detected != 0;
                _ship.Navigation_Dangerous_Space = danger;
                double limit = danger ? _ship.Cruise_Speed :
                    _ship.Travel_Speed;
                double distance = Vector3D.Distance(position, waypoint);
                double targetSpeed = Math.Min(limit, Math.Max(2,
                    Math.Min(distance * 0.2, _ship.Target_Speed)));
                Vector3D velocity = controller.GetShipVelocities().LinearVelocity;
                double speed = velocity.Length();
                Vector3D acceleration = Vector3D.Zero;
                if (_ship.Needs_Avoidance != 0 && speed > targetSpeed + 4)
                    acceleration = -Vector3D.Normalize(velocity) * 5;
                else if (speed < targetSpeed - 4)
                    acceleration = direction * 5;
                acceleration -= controller.GetNaturalGravity();
                MatrixD frame = controller.WorldMatrix;
                ApplyTranslation(acceleration, frame);
                Face(direction);
                controller.DampenersOverride = true;
            }

            void Apply(List<IMyThrust> group, double acceleration)
            {
                float power = (float)Math.Max(0, Math.Min(1,
                    acceleration / 5.0));
                for (int i = 0; i < group.Count; i++)
                    group[i].ThrustOverridePercentage = power;
            }

            void ApplyTranslation(Vector3D acceleration, MatrixD frame)
            {
                Apply(_ship.Forward, Vector3D.Dot(acceleration, frame.Forward));
                Apply(_ship.Backward, Vector3D.Dot(acceleration, frame.Backward));
                Apply(_ship.Up, Vector3D.Dot(acceleration, frame.Up));
                Apply(_ship.Down, Vector3D.Dot(acceleration, frame.Down));
                Apply(_ship.Left, Vector3D.Dot(acceleration, frame.Left));
                Apply(_ship.Right, Vector3D.Dot(acceleration, frame.Right));
            }

            void Face(Vector3D direction)
            {
                IMyShipController controller = _ship.ReferenceController;
                Vector3D rotation = Vector3D.Cross(
                    controller.WorldMatrix.Forward, direction) * 2;
                for (int i = 0; i < _ship.Gyroscopes.Count; i++)
                {
                    IMyGyro gyro = _ship.Gyroscopes[i];
                    Vector3D local = Vector3D.TransformNormal(rotation,
                        MatrixD.Transpose(gyro.WorldMatrix));
                    gyro.GyroOverride = true;
                    gyro.Pitch = (float)local.X;
                    gyro.Yaw = (float)local.Y;
                    gyro.Roll = (float)local.Z;
                }
            }

            void Brake(IMyShipController controller)
            {
                ClearOverrides();
                controller.DampenersOverride = true;
            }

            void ReleaseControl()
            {
                ClearOverrides();
                for (int i = 0; i < _ship.Gyroscopes.Count; i++)
                    _ship.Gyroscopes[i].GyroOverride = false;
            }

            void ClearOverrides()
            {
                for (int i = 0; i < _ship.Thrusters.Count; i++)
                    _ship.Thrusters[i].ThrustOverridePercentage = 0;
            }

            Vector3I Cell(Vector3D point)
            {
                return new Vector3I((int)Math.Floor(point.X / CellMeters),
                    (int)Math.Floor(point.Y / CellMeters),
                    (int)Math.Floor(point.Z / CellMeters));
            }

            Vector3D World(Vector3I cell)
            {
                return (new Vector3D(cell) + new Vector3D(0.5)) * CellMeters;
            }

            double Heuristic(Vector3I a, Vector3I b)
            {
                return Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) +
                    Math.Abs(a.Z - b.Z);
            }
        }
    }
}
