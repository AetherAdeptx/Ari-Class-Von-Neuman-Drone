using System;
using System.Collections.Generic;
using System.Text;
using VRageMath;

namespace IngameScript
{
    public partial class Program
    {
        sealed class OreSample
        {
            public Vector3D Position;
            public double Time;
        }

        sealed class OreMemory
        {
            const int MaxPerOre = 100;
            readonly Dictionary<string, Queue<OreSample>> _ore =
                new Dictionary<string, Queue<OreSample>>();

            public int OreTypeCount { get { return _ore.Count; } }

            public int TotalLocationCount
            {
                get
                {
                    int count = 0;
                    foreach (Queue<OreSample> list in _ore.Values)
                        count += list.Count;
                    return count;
                }
            }

            public void Mark(string ore, Vector3D position, double time)
            {
                if (string.IsNullOrWhiteSpace(ore))
                    return;
                Queue<OreSample> list;
                if (!_ore.TryGetValue(ore, out list))
                    _ore[ore] = list = new Queue<OreSample>();
                list.Enqueue(new OreSample { Position = position, Time = time });
                while (list.Count > MaxPerOre)
                    list.Dequeue();
            }

            public string Encode(int budget)
            {
                StringBuilder text = new StringBuilder("OM1|");
                foreach (KeyValuePair<string, Queue<OreSample>> item in _ore)
                {
                    foreach (OreSample sample in item.Value)
                    {
                        string record = item.Key + ":" + sample.Time.ToString("0") +
                            ":" + V(sample.Position) + "|";
                        if (text.Length + record.Length > budget)
                            return text.ToString();
                        text.Append(record);
                    }
                }
                return text.ToString();
            }

            public bool Decode(string encoded)
            {
                if (string.IsNullOrWhiteSpace(encoded))
                    return true;
                if (!encoded.StartsWith("OM1|"))
                    return false;
                _ore.Clear();
                string[] records = encoded.Substring(4).Split('|');
                for (int i = 0; i < records.Length; i++)
                {
                    string[] values = records[i].Split(':');
                    double time;
                    Vector3D position;
                    if (values.Length == 3 && double.TryParse(values[1], out time) &&
                        TryV(values[2], out position))
                        Mark(values[0], position, time);
                }
                return true;
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
