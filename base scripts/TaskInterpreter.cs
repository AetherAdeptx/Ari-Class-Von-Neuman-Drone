using System;
using System.Collections.Generic;
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
            public int NextLine;

            public TaskObject(string phrase, long source)
            {
                Phrase = phrase ?? "";
                Lines = Phrase.Replace('\r', '\n').Split(
                    new char[] { '\n', ';' },
                    StringSplitOptions.RemoveEmptyEntries);
                Source = source;
            }
        }

        sealed class TaskInterpreter
        {
            public static readonly string[] Commands =
            {
                "SET_DESTINATION", "FREE_MOVE", "CRUISE_SPEED",
                "TRAVEL_SPEED", "STATE", "AUTO_TRACKING", "SET_HOME",
                "SET_DOCK", "SYNC_MAPS", "FREE_MODE"
            };

            readonly ShipState _ship;
            readonly Supervisor _supervisor;
            readonly RadioNetwork _radio;
            readonly Queue<TaskObject> _tasks = new Queue<TaskObject>();
            double _elapsedSeconds;

            public int Count { get { return _tasks.Count; } }

            public TaskInterpreter(ShipState ship, Supervisor supervisor,
                RadioNetwork radio)
            {
                _ship = ship;
                _supervisor = supervisor;
                _radio = radio;
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
                int objects = Math.Min(_ship.Tasks_Per_Frame, _tasks.Count);
                for (int taskIndex = 0; taskIndex < objects; taskIndex++)
                {
                    TaskObject task = _tasks.Dequeue();
                    int lines = Math.Min(_ship.Task_Object_Lines_Per_Frame,
                        task.Lines.Length - task.NextLine);
                    for (int line = 0; line < lines; line++)
                        Execute(task.Lines[task.NextLine++]);
                    if (task.NextLine < task.Lines.Length)
                        _tasks.Enqueue(task);
                }
                _ship.Task_Object_Queue_Count = _tasks.Count;
            }

            void Execute(string text)
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
                        _ship.Free_Move = IsOn(arguments);
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
                        _ship.Free_Move = IsOn(arguments);
                        break;
                }
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
