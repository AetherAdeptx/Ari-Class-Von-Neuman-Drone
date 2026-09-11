using System;
using System.Collections.Generic;
using System.Text;
using VRageMath;

namespace IngameScript
{
    public partial class Program
    {
        sealed class DestinationBook
        {
            const int MaxDestinations = 128;
            readonly Dictionary<string, Vector3D> _destinations =
                new Dictionary<string, Vector3D>(StringComparer.OrdinalIgnoreCase);
            readonly Queue<string> _order = new Queue<string>();

            public int Count { get { return _destinations.Count; } }

            public bool Set(string name, Vector3D position)
            {
                if (!ValidName(name))
                    return false;
                if (!_destinations.ContainsKey(name))
                {
                    if (_destinations.Count >= MaxDestinations)
                    {
                        while (_order.Count > 0)
                        {
                            string old = _order.Dequeue();
                            if (_destinations.Remove(old))
                                break;
                        }
                    }
                    _order.Enqueue(name);
                }
                _destinations[name] = position;
                return true;
            }

            public bool TryGet(string name, out Vector3D position)
            {
                return _destinations.TryGetValue(name, out position);
            }

            public string Encode(int budget)
            {
                StringBuilder text = new StringBuilder("DB1|");
                foreach (KeyValuePair<string, Vector3D> item in _destinations)
                {
                    string record = item.Key + ":" + V(item.Value) + "|";
                    if (text.Length + record.Length > budget)
                        return text.ToString();
                    text.Append(record);
                }
                return text.ToString();
            }

            public bool Decode(string encoded)
            {
                if (string.IsNullOrWhiteSpace(encoded))
                    return true;
                if (!encoded.StartsWith("DB1|"))
                    return false;
                _destinations.Clear();
                _order.Clear();
                string[] records = encoded.Substring(4).Split('|');
                for (int i = 0; i < records.Length; i++)
                {
                    int split = records[i].IndexOf(':');
                    Vector3D position;
                    if (split > 0 && TryV(records[i].Substring(split + 1),
                        out position))
                        Set(records[i].Substring(0, split), position);
                }
                return true;
            }

            bool ValidName(string name)
            {
                if (string.IsNullOrWhiteSpace(name) || name.Length > 24)
                    return false;
                return name.IndexOfAny(new char[] { '|', ':', ';', '=', '\n', '\r' }) < 0;
            }

            static string V(Vector3D value)
            {
                return value.X.ToString("0.0") + "," +
                    value.Y.ToString("0.0") + "," +
                    value.Z.ToString("0.0");
            }

            static bool TryV(string text, out Vector3D value)
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
