using System;
using System.Text;
using VRageMath;

namespace IngameScript
{
    public partial class Program
    {
        sealed class GlobalTemporalBsp
        {
            public const double SpanMeters = 32768000;
            public const double CellSizeMeters = 256000;
            public const int CellsPerAxis = 128;
            public const long MemoryBudgetBytes = 16L * 1024L * 1024L;
            public const int MaximumNodes = 1048576;
            const long UnixEpochTicks = 621355968000000000L;
            const long TicksPerSecond = 10000000L;

            readonly int[] _low = new int[MaximumNodes];
            readonly int[] _high = new int[MaximumNodes];
            readonly uint[] _scanTimestampLow = new uint[MaximumNodes];
            readonly uint[] _scanTimestampHigh = new uint[MaximumNodes];
            int _nodeCount = 1;

            public int NodeCount { get { return _nodeCount; } }
            public int ScannedCellCount { get; private set; }
            public bool IsFull { get; private set; }
            public long ReservedBytes
            {
                get
                {
                    return (long)MaximumNodes *
                        (sizeof(int) * 2 + sizeof(uint) * 2);
                }
            }

            public bool MarkScanned(Vector3D worldPosition)
            {
                Vector3I cell;
                if (!TryWorldToCell(worldPosition, out cell))
                    return false;
                ulong previous = GetTimestamp(cell);
                ulong timestamp = (ulong)Math.Max(1,
                    (DateTime.UtcNow.Ticks - UnixEpochTicks) /
                        TicksPerSecond);
                if (!SetTimestamp(
                    0,
                    Vector3I.Zero,
                    new Vector3I(CellsPerAxis),
                    cell,
                    timestamp))
                    return false;
                if (previous == 0)
                    ScannedCellCount++;
                return true;
            }

            public ulong GetTimestamp(Vector3D worldPosition)
            {
                Vector3I cell;
                return TryWorldToCell(worldPosition, out cell)
                    ? GetTimestamp(cell)
                    : 0;
            }

            public string Encode(int maximumCharacters)
            {
                StringBuilder text = new StringBuilder("GT1|");
                EncodeNode(0, Vector3I.Zero,
                    new Vector3I(CellsPerAxis), text, maximumCharacters);
                return text.ToString();
            }

            public bool Merge(string encoded)
            {
                if (string.IsNullOrEmpty(encoded) ||
                    !encoded.StartsWith("GT1|"))
                    return false;
                string[] records = encoded.Substring(4).Split(';');
                for (int i = 0; i < records.Length; i++)
                {
                    if (records[i].Length == 0)
                        continue;
                    string[] values = records[i].Split(',');
                    int cellIndex;
                    ulong timestamp;
                    if (values.Length != 2 ||
                        !int.TryParse(values[0], out cellIndex) ||
                        !ulong.TryParse(values[1], out timestamp) ||
                        cellIndex < 0 ||
                        cellIndex >= CellsPerAxis * CellsPerAxis * CellsPerAxis)
                        return false;
                    Vector3I cell = new Vector3I(
                        cellIndex % CellsPerAxis,
                        cellIndex / CellsPerAxis % CellsPerAxis,
                        cellIndex / (CellsPerAxis * CellsPerAxis));
                    ulong old = GetTimestamp(cell);
                    if (timestamp > old && SetTimestamp(0, Vector3I.Zero,
                        new Vector3I(CellsPerAxis), cell, timestamp) && old == 0)
                        ScannedCellCount++;
                }
                return true;
            }

            void EncodeNode(int node, Vector3I minimum, Vector3I maximum,
                StringBuilder text, int maximumCharacters)
            {
                if (text.Length >= maximumCharacters)
                    return;
                if (_low[node] == 0)
                {
                    ulong timestamp = ((ulong)_scanTimestampHigh[node] << 32) |
                        _scanTimestampLow[node];
                    if (timestamp == 0)
                        return;
                    int index = minimum.X + minimum.Y * CellsPerAxis +
                        minimum.Z * CellsPerAxis * CellsPerAxis;
                    string record = index + "," + timestamp + ";";
                    if (text.Length + record.Length <= maximumCharacters)
                        text.Append(record);
                    return;
                }
                int axis = LongestAxis(maximum - minimum);
                int middle = (Axis(minimum, axis) + Axis(maximum, axis)) / 2;
                Vector3I lowMaximum = maximum;
                Vector3I highMinimum = minimum;
                SetAxis(ref lowMaximum, axis, middle);
                SetAxis(ref highMinimum, axis, middle);
                EncodeNode(_low[node], minimum, lowMaximum, text,
                    maximumCharacters);
                EncodeNode(_high[node], highMinimum, maximum, text,
                    maximumCharacters);
            }

            bool TryWorldToCell(
                Vector3D worldPosition,
                out Vector3I cell)
            {
                double half = SpanMeters * 0.5;
                if (worldPosition.X < -half || worldPosition.X >= half ||
                    worldPosition.Y < -half || worldPosition.Y >= half ||
                    worldPosition.Z < -half || worldPosition.Z >= half)
                {
                    cell = Vector3I.Zero;
                    return false;
                }
                cell = new Vector3I(
                    (int)Math.Floor(
                        (worldPosition.X + half) / CellSizeMeters),
                    (int)Math.Floor(
                        (worldPosition.Y + half) / CellSizeMeters),
                    (int)Math.Floor(
                        (worldPosition.Z + half) / CellSizeMeters));
                return true;
            }

            ulong GetTimestamp(Vector3I target)
            {
                int node = 0;
                Vector3I minimum = Vector3I.Zero;
                Vector3I maximum = new Vector3I(CellsPerAxis);
                while (_low[node] != 0)
                {
                    int axis = LongestAxis(maximum - minimum);
                    int middle = (Axis(minimum, axis) +
                        Axis(maximum, axis)) / 2;
                    if (Axis(target, axis) < middle)
                    {
                        node = _low[node];
                        SetAxis(ref maximum, axis, middle);
                    }
                    else
                    {
                        node = _high[node];
                        SetAxis(ref minimum, axis, middle);
                    }
                }
                return ((ulong)_scanTimestampHigh[node] << 32) |
                    _scanTimestampLow[node];
            }

            bool SetTimestamp(
                int node,
                Vector3I minimum,
                Vector3I maximum,
                Vector3I target,
                ulong timestamp)
            {
                Vector3I size = maximum - minimum;
                if (size.X == 1 && size.Y == 1 && size.Z == 1)
                {
                    _scanTimestampLow[node] = (uint)timestamp;
                    _scanTimestampHigh[node] = (uint)(timestamp >> 32);
                    return true;
                }
                if (_low[node] == 0)
                {
                    if (_nodeCount + 2 > MaximumNodes)
                    {
                        IsFull = true;
                        return false;
                    }
                    int low = _nodeCount++;
                    int high = _nodeCount++;
                    _scanTimestampLow[low] = _scanTimestampLow[node];
                    _scanTimestampLow[high] = _scanTimestampLow[node];
                    _scanTimestampHigh[low] = _scanTimestampHigh[node];
                    _scanTimestampHigh[high] = _scanTimestampHigh[node];
                    _low[node] = low;
                    _high[node] = high;
                }
                int axis = LongestAxis(size);
                int middle = (Axis(minimum, axis) +
                    Axis(maximum, axis)) / 2;
                if (Axis(target, axis) < middle)
                {
                    SetAxis(ref maximum, axis, middle);
                    return SetTimestamp(
                        _low[node], minimum, maximum, target, timestamp);
                }
                SetAxis(ref minimum, axis, middle);
                return SetTimestamp(
                    _high[node], minimum, maximum, target, timestamp);
            }

            int LongestAxis(Vector3I size)
            {
                if (size.X >= size.Y && size.X >= size.Z)
                    return 0;
                return size.Y >= size.Z ? 1 : 2;
            }

            int Axis(Vector3I value, int axis)
            {
                return axis == 0 ? value.X : axis == 1 ? value.Y : value.Z;
            }

            void SetAxis(
                ref Vector3I value,
                int axis,
                int axisValue)
            {
                if (axis == 0)
                    value.X = axisValue;
                else if (axis == 1)
                    value.Y = axisValue;
                else
                    value.Z = axisValue;
            }
        }
    }
}
