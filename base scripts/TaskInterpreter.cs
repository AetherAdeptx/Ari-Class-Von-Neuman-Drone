using System;
using System.Collections.Generic;
using System.Text;
using VRageMath;

namespace IngameScript
{
    public partial class Program
    {
        sealed class TaskObject
        {
            public readonly string Phrase;
            public readonly string[] Lines;
            public readonly long Source;
            public long LastCompleted;
            public long Frequency;
            public int NextLine;

            public TaskObject(string phrase, long source, long frequency = 0)
            {
                Phrase = phrase ?? "";
                Lines = Phrase.Replace('\r', '\n').Split(
                    new char[] { '\n', ';' },
                    StringSplitOptions.RemoveEmptyEntries);
                Source = source;
                Frequency = Math.Max(0, frequency);
            }
        }

        sealed class TaskInterpreter
        {
            public static readonly string[] Commands =
            {
                "SET_DESTINATION", "FREE_MOVE", "CRUISE_SPEED",
                "TRAVEL_SPEED", "STATE", "AUTO_TRACKING", "SET_HOME",
                "SET_DOCK", "SYNC_MAPS", "FREE_MODE", "SET", "DUMP_DEBUG",
                "DUMP_REGISTRY", "REPORT", "SET_PROSPECTING", "SET_DEST",
                "GOTO", "DRONE_MODE", "MARK_ORE", "SET_MOTHER_DEST"
            };

            readonly ShipState _ship;
            readonly Supervisor _supervisor;
            readonly RadioNetwork _radio;
            readonly RuntimeRegistry _registry;
            readonly DestinationBook _destinations;
            readonly OreMemory _oreMemory;
            readonly Queue<TaskObject> _tasks = new Queue<TaskObject>();
            readonly StringBuilder _debug = new StringBuilder(4096);
            double _elapsedSeconds;

            public int Count { get { return _tasks.Count; } }

            public TaskInterpreter(ShipState ship, Supervisor supervisor,
                RadioNetwork radio, RuntimeRegistry registry,
                DestinationBook destinations, OreMemory oreMemory)
            {
                _ship = ship;
                _supervisor = supervisor;
                _radio = radio;
                _registry = registry;
                _destinations = destinations;
                _oreMemory = oreMemory;
            }

            public void Enqueue(string phrase, long source)
            {
                if (!string.IsNullOrWhiteSpace(phrase) && phrase.Length <= 4096 &&
                    _tasks.Count < 64)
                    _tasks.Enqueue(new TaskObject(phrase, source));
                _ship.Task_Object_Queue_Count = _tasks.Count;
            }

            public void Update(double elapsedSeconds)
            {
                _elapsedSeconds += elapsedSeconds;
                int budget = _ship.Tasks_Per_Frame;
                int inspected = _tasks.Count;
                while (budget > 0 && inspected-- > 0 && _tasks.Count > 0)
                {
                    TaskObject task = _tasks.Dequeue();
                    long now = (long)_elapsedSeconds;
                    if (task.Frequency > 0 && now - task.LastCompleted < task.Frequency)
                    { _tasks.Enqueue(task); continue; }
                    budget--;
                    int lines = Math.Min(_ship.Task_Object_Lines_Per_Frame,
                        task.Lines.Length - task.NextLine);
                    for (int line = 0; line < lines; line++)
                        if (!Execute(task.Lines[task.NextLine++], task.Source))
                            ReplyError(task.Source);
                    if (task.NextLine >= task.Lines.Length)
                        task.LastCompleted = now;
                    if (task.NextLine < task.Lines.Length || task.Frequency > 0)
                    {
                        if (task.NextLine >= task.Lines.Length) task.NextLine = 0;
                        _tasks.Enqueue(task);
                    }
                }
                _ship.Task_Object_Queue_Count = _tasks.Count;
            }

            bool Execute(string text, long source)
            {
                text = text.Trim();
                int split = text.IndexOf(' ');
                string command = (split < 0 ? text :
                    text.Substring(0, split)).ToUpperInvariant();
                string arguments = split < 0 ? "" :
                    text.Substring(split + 1).Trim();
                int commandIndex = Array.IndexOf(Commands, command);
                Vector3D vector;
                double number;
                ShipState.SupervisorState state;
                switch (commandIndex)
                {
                    case 0:
                        if (TryVector(arguments, out vector))
                            _ship.Target_Destination = vector;
                        break;
                    case 1:
                        _ship.Free_Move = IsOn(arguments) &&
                            _ship.Drone_Mode >= 2;
                        break;
                    case 2:
                        if (double.TryParse(arguments, out number))
                            _ship.Cruise_Speed = Math.Max(1,
                                Math.Min(100, number));
                        break;
                    case 3:
                        if (double.TryParse(arguments, out number))
                            _ship.Travel_Speed = Math.Max(1,
                                Math.Min(100, number));
                        break;
                    case 4:
                        if (Enum.TryParse(arguments.Replace('-', '_'), true,
                            out state))
                            _supervisor.SetState(state);
                        break;
                    case 5:
                        _ship.Auto_Tracking_Flag = IsOn(arguments) ? 1 : 0;
                        break;
                    case 6:
                        if (TryVector(arguments, out vector))
                            _ship.Home_Base_Coordinates = vector;
                        break;
                    case 7:
                        if (TryVector(arguments, out vector))
                            _ship.Assigned_Dock_Coordinates = vector;
                        break;
                    case 8:
                        _radio.RequestMapSync(_elapsedSeconds);
                        break;
                    case 9:
                        _ship.Free_Move = IsOn(arguments) &&
                            _ship.Drone_Mode >= 2;
                        break;
                    case 10:
                        return SetValue(arguments);
                    case 11:
                        Append("DEBUG " + DebugState());
                        Reply(source, _debug.ToString());
                        break;
                    case 12:
                        Reply(source, _registry.EncodePersistent());
                        break;
                    case 13:
                        Append(arguments);
                        break;
                    case 14:
                        string[] prospect = arguments.Split(' ');
                        if (prospect.Length < 2 || !TryVector(prospect[0], out vector) ||
                            !double.TryParse(prospect[1], out number)) return false;
                        _ship.Prospecting_Center = vector;
                        _ship.Prospecting_Radius = Math.Max(100, Math.Min(50000, number));
                        _ship.Prospecting_Enabled = true;
                        break;
                    case 15:
                        return SetNamedDestination(arguments);
                    case 16:
                        if (_destinations.TryGet(arguments, out vector))
                        { _ship.Target_Destination = vector; _ship.Free_Move = _ship.Drone_Mode >= 2; }
                        else return false;
                        break;
                    case 17:
                        int mode;
                        if (!int.TryParse(arguments, out mode)) return false;
                        _ship.Drone_Mode = Math.Max(0, Math.Min(2, mode));
                        if (_ship.Drone_Mode < 2) _ship.Free_Move = false;
                        break;
                    case 18:
                        return MarkOre(arguments);
                    case 19:
                        if (TryVector(arguments, out vector))
                            _ship.Mother_Ship_Final_Destination = vector;
                        else return false;
                        break;
                    default:
                        Append("Unknown command: " + command);
                        return false;
                }
                return true;
            }

            bool SetValue(string text)
            {
                int split = text.IndexOf(' '); double n;
                if (split < 1 || !double.TryParse(text.Substring(split + 1), out n)) return false;
                string key = text.Substring(0, split).ToUpperInvariant();
                if (key == "TARGET_SPEED") _ship.Target_Speed = Math.Max(0, Math.Min(500, n));
                else if (key == "MAX_SPEED") _ship.Max_Speed = Math.Max(0, Math.Min(500, n));
                else if (key == "AVOIDANCE_DISTANCE") _ship.Avoidance_Distance_Per_Speed = Math.Max(.1, Math.Min(50, n));
                else if (key == "TASKS_PER_FRAME") _ship.Tasks_Per_Frame = Math.Max(1, Math.Min(32, (int)n));
                else if (key == "MOTHERSHIP_BOUND") _ship.Mothership_Bound = n != 0;
                else if (key == "MOTHERSHIP_RANGE") _ship.Mothership_Bound_Range = Math.Max(100, Math.Min(50000, n));
                else if (key == "CRITICAL_BATTERY") _ship.Critical_Battery_Level = Math.Max(0, Math.Min(100, n));
                else if (key == "PROSPECTING_RADIUS") _ship.Prospecting_Radius = Math.Max(100, Math.Min(50000, n));
                else if (key == "PROSPECTING") _ship.Prospecting_Enabled = n != 0;
                else if (key == "DRONE_MODE") { _ship.Drone_Mode = Math.Max(0, Math.Min(2, (int)n)); if (_ship.Drone_Mode < 2) _ship.Free_Move = false; }
                else return false;
                return true;
            }

            bool SetNamedDestination(string text)
            {
                int split = text.IndexOf(' ');
                Vector3D vector;
                if (split < 1 || !TryVector(text.Substring(split + 1),
                    out vector))
                    return false;
                return _destinations.Set(text.Substring(0, split), vector);
            }

            bool MarkOre(string text)
            {
                int split = text.IndexOf(' ');
                Vector3D vector;
                if (split < 1 || !TryVector(text.Substring(split + 1),
                    out vector))
                    return false;
                _oreMemory.Mark(text.Substring(0, split), vector,
                    _elapsedSeconds);
                return true;
            }

            string DebugState()
            {
                return "state=" + _ship.Supervisor_State + ",priority=" + _ship.State_Priority +
                    ",blocker=" + _ship.State_Blocker + ",need=" + _ship.Need_Level +
                    ",avoid=" + _ship.Needs_Avoidance + ",tasks=" + _tasks.Count;
            }

            void Append(string text)
            {
                if (text == null) return;
                if (_debug.Length + text.Length + 1 > 4096)
                    _debug.Remove(0, Math.Min(_debug.Length, _debug.Length + text.Length + 1 - 4096));
                _debug.Append(text).Append('\n');
            }

            void ReplyError(long source)
            {
                Append("Task failed; " + DebugState());
                Reply(source, _debug.ToString() + _registry.EncodePersistent());
            }

            void Reply(long source, string text)
            {
                if (source != 0) _radio.SendTask(source, "REPORT " + text.Substring(0, Math.Min(3500, text.Length)));
            }

            bool IsOn(string value)
            {
                return value.Equals("ON", StringComparison.OrdinalIgnoreCase) ||
                    value.Equals("TRUE", StringComparison.OrdinalIgnoreCase) ||
                    value == "1";
            }

            bool TryVector(string text, out Vector3D value)
            {
                string[] parts = text.Split(',');
                double x, y, z;
                if (parts.Length == 3 && double.TryParse(parts[0], out x) &&
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
