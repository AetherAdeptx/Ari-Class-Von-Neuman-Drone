using System;
using System.Collections.Generic;
using System.Text;
using VRageMath;

namespace IngameScript
{
    public partial class Program
    {
        sealed class BspSpatialMap
        {
            public sealed class Node
            {
                public Node Low;
                public Node High;
                public byte Value;

                public Node(byte value = 0)
                {
                    Value = value;
                }

                public bool IsLeaf
                {
                    get { return Low == null && High == null; }
                }
            }

            public sealed class BspTree
            {
                public readonly Vector3I Coordinate;
                internal Node Root = new Node();
                internal long EmptyRegionCount;
                internal long AllocatedNodeCount = 1;

                internal BspTree(Vector3I coordinate)
                {
                    Coordinate = coordinate;
                }
            }

            sealed class EncodedTree
            {
                public Vector3I Coordinate;
                public Node Root;
            }

            const string EncodingHeader = "BSP6|";
            const string PreviousEncodingHeader = "BSP3|";
            const string LegacyEncodingHeader = "BSP2|";
            const double LargeGridSizeMeters = 2.5;
            const double LargeGridTreeSizeMeters = 1000.0;
            const long EstimatedNodeBytes = 64;
            const long EstimatedTreeIndexBytes = 256;
            readonly Dictionary<Vector3I, BspTree> _treeLookup =
                new Dictionary<Vector3I, BspTree>();
            int _regionsPerTreeAxis = 100;
            double _treeSizeMeters = LargeGridTreeSizeMeters;
            Vector3I _centerTree;

            public readonly List<BspTree> Trees = new List<BspTree>();
            public double RegionSizeMeters { get; private set; }
            public int SpatialCacheDistance { get; private set; }
            public long EmptyRegionCount { get; private set; }
            public long AllocatedNodeCount { get; private set; }
            public double HalfExtentMeters
            {
                get { return CacheSpanMeters * 0.5; }
            }
            public double CacheSpanMeters
            {
                get { return SpatialCacheDistance * _treeSizeMeters; }
            }
            public double TreeSizeMeters
            {
                get { return _treeSizeMeters; }
            }
            public long EstimatedMemoryBytes
            {
                get
                {
                    return AllocatedNodeCount * EstimatedNodeBytes +
                        Trees.Count * EstimatedTreeIndexBytes;
                }
            }

            public BspSpatialMap(int spatialCacheDistance)
            {
                RegionSizeMeters = 10;
                SpatialCacheDistance = ClampDistance(spatialCacheDistance);
                BuildTreeCache();
            }

            public void Configure(
                double referenceGridSizeMeters,
                int spatialCacheDistance,
                Vector3D worldCenter)
            {
                double newRegionSize = referenceGridSizeMeters * 4.0;
                double newTreeSize = LargeGridTreeSizeMeters *
                    referenceGridSizeMeters / LargeGridSizeMeters;
                int newDistance = ClampDistance(spatialCacheDistance);
                if (Math.Abs(newRegionSize - RegionSizeMeters) < 0.0001 &&
                    Math.Abs(newTreeSize - _treeSizeMeters) < 0.0001 &&
                    newDistance == SpatialCacheDistance)
                {
                    Recenter(worldCenter);
                    return;
                }

                RegionSizeMeters = newRegionSize;
                _treeSizeMeters = newTreeSize;
                _regionsPerTreeAxis =
                    (int)Math.Ceiling(_treeSizeMeters / RegionSizeMeters);
                SpatialCacheDistance = newDistance;
                _centerTree = WorldTree(worldCenter);
                BuildTreeCache();
            }

            public void Recenter(Vector3D worldCenter)
            {
                Vector3I center = WorldTree(worldCenter);
                if (center == _centerTree)
                    return;
                Dictionary<Vector3I, BspTree> old =
                    new Dictionary<Vector3I, BspTree>(_treeLookup);
                _centerTree = center;
                BuildTreeCache(old);
            }

            public void SetSpatialCacheDistance(int distance)
            {
                int clamped = ClampDistance(distance);
                if (clamped == SpatialCacheDistance)
                    return;
                SpatialCacheDistance = clamped;
                BuildTreeCache();
            }

            public void Clear()
            {
                ClearValues();
            }

            // Clears one complete trailing slab: 4x4 = 16 trees for the
            // default Spatial_Cache_Distance of four.
            public int PurgeOppositeDirection(Vector3D localDirection)
            {
                int axis;
                double component;
                if (!TryDominantAxis(localDirection, out axis, out component))
                    return 0;

                int minimum = Axis(_centerTree, axis) -
                    SpatialCacheDistance / 2;
                int maximum = minimum + SpatialCacheDistance - 1;
                int trailingCoordinate = component >= 0 ? minimum : maximum;
                int purged = 0;
                for (int i = 0; i < Trees.Count; i++)
                {
                    BspTree tree = Trees[i];
                    if (Axis(tree.Coordinate, axis) != trailingCoordinate)
                        continue;
                    ResetTree(tree);
                    purged++;
                }
                return purged;
            }

            // During an emergency, clears the next populated slab while
            // preserving the slab containing the PB and its immediate
            // forward neighbor. Returns zero when only those slabs remain.
            public int PurgeNextEmergencyLayer(
                Vector3D localDirection,
                Vector3D currentLocalPosition)
            {
                int axis;
                double component;
                if (!TryDominantAxis(localDirection, out axis, out component))
                    return 0;

                int minimum = Axis(_centerTree, axis) -
                    SpatialCacheDistance / 2;
                int maximum = minimum + SpatialCacheDistance - 1;
                int current = (int)Math.Floor(
                    Axis(currentLocalPosition, axis) / _treeSizeMeters);
                current = Math.Max(minimum, Math.Min(maximum, current));
                int forward = current + (component >= 0 ? 1 : -1);
                forward = Math.Max(minimum, Math.Min(maximum, forward));

                int step = component >= 0 ? 1 : -1;
                int coordinate = component >= 0 ? minimum : maximum;
                while (coordinate >= minimum && coordinate <= maximum)
                {
                    if (coordinate != current && coordinate != forward &&
                        LayerHasAllocatedData(axis, coordinate))
                        return PurgeLayer(axis, coordinate);
                    coordinate += step;
                }
                return 0;
            }

            // Coordinates are stable world-space meters.
            // Missing paths return 0 (solid); only observed empty regions are 1.
            public byte GetValue(Vector3D localPositionMeters)
            {
                BspTree tree;
                Vector3I region;
                if (!TryResolve(localPositionMeters, out tree, out region))
                    return 0;
                return GetNodeValue(
                    tree.Root,
                    Vector3I.Zero,
                    new Vector3I(_regionsPerTreeAxis),
                    region);
            }

            public byte GetValue(
                Vector3I treeCoordinate,
                Vector3I regionCoordinate)
            {
                BspTree tree;
                if (!_treeLookup.TryGetValue(treeCoordinate, out tree) ||
                    !ValidRegion(regionCoordinate))
                    return 0;
                return GetNodeValue(
                    tree.Root,
                    Vector3I.Zero,
                    new Vector3I(_regionsPerTreeAxis),
                    regionCoordinate);
            }

            public void SetValue(Vector3D localPositionMeters, byte value)
            {
                BspTree tree;
                Vector3I region;
                if (!TryResolve(localPositionMeters, out tree, out region))
                    return;

                value = value > 2 ? (byte)2 : value;
                byte oldValue = GetNodeValue(
                    tree.Root,
                    Vector3I.Zero,
                    new Vector3I(_regionsPerTreeAxis),
                    region);
                if (oldValue == value)
                    return;

                SetNodeValue(
                    tree,
                    tree.Root,
                    Vector3I.Zero,
                    new Vector3I(_regionsPerTreeAxis),
                    region,
                    value);
                if (oldValue == 1) { EmptyRegionCount--; tree.EmptyRegionCount--; }
                if (value == 1) { EmptyRegionCount++; tree.EmptyRegionCount++; }
            }

            public string Encode()
            {
                return Encode(int.MaxValue);
            }

            public string Encode(int maximumCharacters)
            {
                StringBuilder encoded = new StringBuilder(EncodingHeader);
                encoded.Append(SpatialCacheDistance).Append('|')
                    .Append(_regionsPerTreeAxis).Append('|')
                    .Append((int)Math.Round(_treeSizeMeters)).Append('|');
                bool first = true;
                for (int i = 0; i < Trees.Count; i++)
                {
                    BspTree tree = Trees[i];
                    if (tree.Root.IsLeaf && tree.Root.Value == 0)
                        continue;
                    StringBuilder record = new StringBuilder();
                    record.Append(tree.Coordinate.X).Append(',')
                        .Append(tree.Coordinate.Y).Append(',')
                        .Append(tree.Coordinate.Z).Append(':');
                    EncodeNode(tree.Root, record);
                    if (encoded.Length + record.Length + (first ? 0 : 1) >
                        maximumCharacters)
                        break;
                    if (!first)
                        encoded.Append(';');
                    encoded.Append(record);
                    first = false;
                }
                return encoded.ToString();
            }

            public bool Decode(string encoded)
            {
                if (string.IsNullOrWhiteSpace(encoded))
                {
                    ClearValues();
                    return true;
                }
                if (!encoded.StartsWith(EncodingHeader))
                    return false;

                int distanceEnd = encoded.IndexOf(
                    '|', EncodingHeader.Length);
                if (distanceEnd < 0)
                    return false;
                int encodedDistance;
                if (!int.TryParse(encoded.Substring(
                    EncodingHeader.Length,
                    distanceEnd - EncodingHeader.Length),
                    out encodedDistance))
                    return false;

                int resolutionEnd = encoded.IndexOf('|', distanceEnd + 1);
                if (resolutionEnd < 0)
                    return false;
                int encodedRegionsPerAxis;
                if (!int.TryParse(encoded.Substring(
                    distanceEnd + 1,
                    resolutionEnd - distanceEnd - 1),
                    out encodedRegionsPerAxis) ||
                    encodedRegionsPerAxis != _regionsPerTreeAxis)
                    return false;

                int treeSizeEnd = encoded.IndexOf('|', resolutionEnd + 1);
                if (treeSizeEnd < 0)
                    return false;
                int encodedTreeSize;
                if (!int.TryParse(encoded.Substring(
                    resolutionEnd + 1,
                    treeSizeEnd - resolutionEnd - 1),
                    out encodedTreeSize) ||
                    Math.Abs(encodedTreeSize - _treeSizeMeters) > 0.0001)
                    return false;

                SetSpatialCacheDistance(encodedDistance);
                string[] records = encoded.Substring(
                    treeSizeEnd + 1).Split(';');
                List<EncodedTree> decoded = new List<EncodedTree>();
                for (int i = 0; i < records.Length; i++)
                {
                    if (records[i].Length == 0)
                        continue;
                    int treeEnd = records[i].IndexOf(':');
                    if (treeEnd < 0)
                        return false;
                    Vector3I coordinate;
                    if (!TryParseCoordinate(
                        records[i].Substring(0, treeEnd), out coordinate) ||
                        !_treeLookup.ContainsKey(coordinate))
                        return false;

                    string tokens = records[i].Substring(treeEnd + 1);
                    int tokenIndex = 0;
                    bool valid = true;
                    Node root = DecodeNode(
                        tokens,
                        ref tokenIndex,
                        Vector3I.Zero,
                        new Vector3I(_regionsPerTreeAxis),
                        ref valid);
                    if (!valid || root == null || tokenIndex != tokens.Length)
                        return false;
                    decoded.Add(new EncodedTree
                    {
                        Coordinate = coordinate,
                        Root = root
                    });
                }

                ClearValues();
                for (int i = 0; i < decoded.Count; i++)
                {
                    BspTree tree = _treeLookup[decoded[i].Coordinate];
                    tree.Root = decoded[i].Root;
                    tree.AllocatedNodeCount = CountNodes(tree.Root);
                    tree.EmptyRegionCount = CountEmptyRegions(
                        tree.Root,
                        Vector3I.Zero,
                        new Vector3I(_regionsPerTreeAxis));
                    AllocatedNodeCount += tree.AllocatedNodeCount - 1;
                    EmptyRegionCount += tree.EmptyRegionCount;
                }
                return true;
            }

            public bool Merge(string encoded)
            {
                if (string.IsNullOrEmpty(encoded) ||
                    !encoded.StartsWith(EncodingHeader))
                    return false;
                int distanceEnd = encoded.IndexOf('|', EncodingHeader.Length);
                int resolutionEnd = distanceEnd < 0 ? -1 :
                    encoded.IndexOf('|', distanceEnd + 1);
                int treeSizeEnd = resolutionEnd < 0 ? -1 :
                    encoded.IndexOf('|', resolutionEnd + 1);
                int resolution;
                int treeSize;
                if (treeSizeEnd < 0 || !int.TryParse(encoded.Substring(
                    distanceEnd + 1, resolutionEnd - distanceEnd - 1),
                    out resolution) || resolution != _regionsPerTreeAxis ||
                    !int.TryParse(encoded.Substring(resolutionEnd + 1,
                    treeSizeEnd - resolutionEnd - 1), out treeSize) ||
                    Math.Abs(treeSize - _treeSizeMeters) > 0.0001)
                    return false;
                string[] records = encoded.Substring(treeSizeEnd + 1).Split(';');
                for (int i = 0; i < records.Length; i++)
                {
                    int split = records[i].IndexOf(':');
                    Vector3I coordinate;
                    if (split < 0 || !TryParseCoordinate(
                        records[i].Substring(0, split), out coordinate))
                        continue;
                    BspTree tree;
                    if (!_treeLookup.TryGetValue(coordinate, out tree))
                        continue;
                    string tokens = records[i].Substring(split + 1);
                    int token = 0;
                    bool valid = true;
                    Node incoming = DecodeNode(tokens, ref token,
                        Vector3I.Zero, new Vector3I(_regionsPerTreeAxis),
                        ref valid);
                    if (!valid || token != tokens.Length)
                        return false;
                    AllocatedNodeCount -= tree.AllocatedNodeCount;
                    EmptyRegionCount -= tree.EmptyRegionCount;
                    tree.Root = MergeNodes(tree.Root, incoming);
                    tree.AllocatedNodeCount = CountNodes(tree.Root);
                    tree.EmptyRegionCount = CountEmptyRegions(tree.Root,
                        Vector3I.Zero, new Vector3I(_regionsPerTreeAxis));
                    AllocatedNodeCount += tree.AllocatedNodeCount;
                    EmptyRegionCount += tree.EmptyRegionCount;
                }
                return true;
            }

            Node MergeNodes(Node current, Node incoming)
            {
                if (incoming.IsLeaf)
                {
                    if (incoming.Value == 0)
                        return current;
                    if (incoming.Value == 2 || current.IsLeaf &&
                        current.Value != 2)
                        return new Node(incoming.Value);
                    return current;
                }
                if (current.IsLeaf)
                    return current.Value == 2 ? current : CloneNode(incoming);
                Node merged = new Node();
                merged.Low = MergeNodes(current.IsLeaf ? current : current.Low,
                    incoming.Low);
                merged.High = MergeNodes(current.IsLeaf ? current : current.High,
                    incoming.High);
                if (merged.Low.IsLeaf && merged.High.IsLeaf &&
                    merged.Low.Value == merged.High.Value)
                    return new Node(merged.Low.Value);
                return merged;
            }

            Node CloneNode(Node node)
            {
                Node clone = new Node(node.Value);
                if (!node.IsLeaf)
                {
                    clone.Low = CloneNode(node.Low);
                    clone.High = CloneNode(node.High);
                }
                return clone;
            }

            bool DecodePrevious(string encoded)
            {
                int distanceEnd = encoded.IndexOf(
                    '|', PreviousEncodingHeader.Length);
                if (distanceEnd < 0)
                    return false;
                int resolutionEnd = encoded.IndexOf('|', distanceEnd + 1);
                if (resolutionEnd < 0)
                    return false;

                string migrated = EncodingHeader + encoded.Substring(
                    PreviousEncodingHeader.Length,
                    resolutionEnd - PreviousEncodingHeader.Length + 1) +
                    ((int)Math.Round(_treeSizeMeters)).ToString() + "|" +
                    encoded.Substring(resolutionEnd + 1);
                return Decode(migrated);
            }

            bool DecodeLegacy(string encoded)
            {
                // BSP2 did not record physical tree size. It is only safe to
                // migrate the original one-kilometer large-grid layout.
                if (Math.Abs(
                    _treeSizeMeters - LargeGridTreeSizeMeters) > 0.0001)
                    return false;

                int distanceEnd = encoded.IndexOf(
                    '|', LegacyEncodingHeader.Length);
                if (distanceEnd < 0)
                    return false;
                int encodedDistance;
                if (!int.TryParse(encoded.Substring(
                    LegacyEncodingHeader.Length,
                    distanceEnd - LegacyEncodingHeader.Length),
                    out encodedDistance))
                    return false;

                SetSpatialCacheDistance(encodedDistance);
                string[] records = encoded.Substring(distanceEnd + 1).Split(';');
                List<Vector3I> trees = new List<Vector3I>();
                List<Vector3I> regions = new List<Vector3I>();
                for (int i = 0; i < records.Length; i++)
                {
                    if (records[i].Length == 0)
                        continue;
                    string[] values = records[i].Split(',');
                    if (values.Length != 6)
                        return false;
                    int[] numbers = new int[6];
                    for (int valueIndex = 0;
                        valueIndex < numbers.Length;
                        valueIndex++)
                    {
                        if (!int.TryParse(
                            values[valueIndex], out numbers[valueIndex]))
                            return false;
                    }
                    Vector3I tree = new Vector3I(
                        numbers[0], numbers[1], numbers[2]);
                    Vector3I region = new Vector3I(
                        numbers[3], numbers[4], numbers[5]);
                    if (!_treeLookup.ContainsKey(tree) || !ValidRegion(region))
                        return false;
                    trees.Add(tree);
                    regions.Add(region);
                }

                ClearValues();
                for (int i = 0; i < trees.Count; i++)
                    SetValue(trees[i], regions[i], 1);
                return true;
            }

            void SetValue(
                Vector3I treeCoordinate,
                Vector3I regionCoordinate,
                byte value)
            {
                BspTree tree;
                if (!_treeLookup.TryGetValue(treeCoordinate, out tree) ||
                    !ValidRegion(regionCoordinate))
                    return;

                byte oldValue = GetNodeValue(
                    tree.Root,
                    Vector3I.Zero,
                    new Vector3I(_regionsPerTreeAxis),
                    regionCoordinate);
                if (oldValue == value)
                    return;

                SetNodeValue(
                    tree,
                    tree.Root,
                    Vector3I.Zero,
                    new Vector3I(_regionsPerTreeAxis),
                    regionCoordinate,
                    value);
                if (oldValue == 1) { EmptyRegionCount--; tree.EmptyRegionCount--; }
                if (value == 1) { EmptyRegionCount++; tree.EmptyRegionCount++; }
            }

            void BuildTreeCache()
            {
                BuildTreeCache(null);
            }

            void BuildTreeCache(Dictionary<Vector3I, BspTree> old)
            {
                _treeLookup.Clear();
                Trees.Clear();
                EmptyRegionCount = 0;
                AllocatedNodeCount = 0;

                Vector3I minimum = _centerTree -
                    new Vector3I(SpatialCacheDistance / 2);
                Vector3I maximum = minimum +
                    new Vector3I(SpatialCacheDistance);
                for (int x = minimum.X; x < maximum.X; x++)
                {
                    for (int y = minimum.Y; y < maximum.Y; y++)
                    {
                        for (int z = minimum.Z; z < maximum.Z; z++)
                        {
                            Vector3I coordinate = new Vector3I(x, y, z);
                            BspTree tree;
                            if (old == null || !old.TryGetValue(
                                coordinate, out tree))
                                tree = new BspTree(coordinate);
                            Trees.Add(tree);
                            _treeLookup.Add(tree.Coordinate, tree);
                            AllocatedNodeCount += tree.AllocatedNodeCount;
                            EmptyRegionCount += tree.EmptyRegionCount;
                        }
                    }
                }
            }

            Vector3I WorldTree(Vector3D position)
            {
                return new Vector3I(
                    (int)Math.Floor(position.X / _treeSizeMeters),
                    (int)Math.Floor(position.Y / _treeSizeMeters),
                    (int)Math.Floor(position.Z / _treeSizeMeters));
            }

            void ClearValues()
            {
                for (int i = 0; i < Trees.Count; i++)
                {
                    Trees[i].Root = new Node();
                    Trees[i].EmptyRegionCount = 0;
                    Trees[i].AllocatedNodeCount = 1;
                }
                EmptyRegionCount = 0;
                AllocatedNodeCount = Trees.Count;
            }

            bool TryResolve(
                Vector3D localPosition,
                out BspTree tree,
                out Vector3I region)
            {
                Vector3I treeCoordinate = new Vector3I(
                    (int)Math.Floor(localPosition.X / _treeSizeMeters),
                    (int)Math.Floor(localPosition.Y / _treeSizeMeters),
                    (int)Math.Floor(localPosition.Z / _treeSizeMeters));
                if (!_treeLookup.TryGetValue(treeCoordinate, out tree))
                {
                    region = Vector3I.Zero;
                    return false;
                }

                Vector3D treeOrigin = new Vector3D(treeCoordinate) *
                    _treeSizeMeters;
                Vector3D insideTree = localPosition - treeOrigin;
                region = new Vector3I(
                    Math.Min(_regionsPerTreeAxis - 1,
                        (int)Math.Floor(insideTree.X / RegionSizeMeters)),
                    Math.Min(_regionsPerTreeAxis - 1,
                        (int)Math.Floor(insideTree.Y / RegionSizeMeters)),
                    Math.Min(_regionsPerTreeAxis - 1,
                        (int)Math.Floor(insideTree.Z / RegionSizeMeters)));
                return ValidRegion(region);
            }

            byte GetNodeValue(
                Node node,
                Vector3I min,
                Vector3I max,
                Vector3I target)
            {
                if (node == null || node.IsLeaf)
                    return node == null ? (byte)0 : node.Value;

                int axis = LongestAxis(max - min);
                int midpoint = (Axis(min, axis) + Axis(max, axis)) / 2;
                if (Axis(target, axis) < midpoint)
                {
                    Vector3I lowMax = max;
                    SetAxis(ref lowMax, axis, midpoint);
                    return GetNodeValue(node.Low, min, lowMax, target);
                }

                Vector3I highMin = min;
                SetAxis(ref highMin, axis, midpoint);
                return GetNodeValue(node.High, highMin, max, target);
            }

            void SetNodeValue(
                BspTree tree,
                Node node,
                Vector3I min,
                Vector3I max,
                Vector3I target,
                byte value)
            {
                if (IsUnitRegion(min, max))
                {
                    node.Value = value;
                    node.Low = null;
                    node.High = null;
                    return;
                }

                if (node.IsLeaf)
                {
                    if (node.Value == value)
                        return;
                    node.Low = new Node(node.Value);
                    node.High = new Node(node.Value);
                    AllocatedNodeCount += 2;
                    tree.AllocatedNodeCount += 2;
                }

                int axis = LongestAxis(max - min);
                int midpoint = (Axis(min, axis) + Axis(max, axis)) / 2;
                if (Axis(target, axis) < midpoint)
                {
                    Vector3I lowMax = max;
                    SetAxis(ref lowMax, axis, midpoint);
                    SetNodeValue(tree, node.Low, min, lowMax, target, value);
                }
                else
                {
                    Vector3I highMin = min;
                    SetAxis(ref highMin, axis, midpoint);
                    SetNodeValue(tree, node.High, highMin, max, target, value);
                }

                if (node.Low.IsLeaf && node.High.IsLeaf &&
                    node.Low.Value == node.High.Value)
                {
                    node.Value = node.Low.Value;
                    node.Low = null;
                    node.High = null;
                    AllocatedNodeCount -= 2;
                    tree.AllocatedNodeCount -= 2;
                }
            }

            void EncodeNode(Node node, StringBuilder encoded)
            {
                if (node.IsLeaf)
                {
                    encoded.Append((char)('0' + node.Value));
                    return;
                }
                encoded.Append('3');
                EncodeNode(node.Low, encoded);
                EncodeNode(node.High, encoded);
            }

            Node DecodeNode(
                string tokens,
                ref int index,
                Vector3I min,
                Vector3I max,
                ref bool valid)
            {
                if (!valid || index >= tokens.Length)
                {
                    valid = false;
                    return null;
                }
                char token = tokens[index++];
                if (token >= '0' && token <= '2')
                    return new Node((byte)(token - '0'));
                if (token != '3' || IsUnitRegion(min, max))
                {
                    valid = false;
                    return null;
                }

                int axis = LongestAxis(max - min);
                int midpoint = (Axis(min, axis) + Axis(max, axis)) / 2;
                Vector3I lowMax = max;
                Vector3I highMin = min;
                SetAxis(ref lowMax, axis, midpoint);
                SetAxis(ref highMin, axis, midpoint);
                Node node = new Node();
                node.Low = DecodeNode(
                    tokens, ref index, min, lowMax, ref valid);
                node.High = DecodeNode(
                    tokens, ref index, highMin, max, ref valid);
                return node;
            }

            long CountEmptyRegions(
                Node node,
                Vector3I min,
                Vector3I max)
            {
                if (node.IsLeaf)
                {
                    if (node.Value != 1)
                        return 0;
                    Vector3I size = max - min;
                    return (long)size.X * size.Y * size.Z;
                }

                int axis = LongestAxis(max - min);
                int midpoint = (Axis(min, axis) + Axis(max, axis)) / 2;
                Vector3I lowMax = max;
                Vector3I highMin = min;
                SetAxis(ref lowMax, axis, midpoint);
                SetAxis(ref highMin, axis, midpoint);
                return CountEmptyRegions(node.Low, min, lowMax) +
                    CountEmptyRegions(node.High, highMin, max);
            }

            long CountNodes(Node node)
            {
                if (node.IsLeaf)
                    return 1;
                return 1 + CountNodes(node.Low) + CountNodes(node.High);
            }

            void ResetTree(BspTree tree)
            {
                EmptyRegionCount -= tree.EmptyRegionCount;
                AllocatedNodeCount -= tree.AllocatedNodeCount - 1;
                tree.Root = new Node();
                tree.EmptyRegionCount = 0;
                tree.AllocatedNodeCount = 1;
            }

            bool LayerHasAllocatedData(int axis, int coordinate)
            {
                for (int i = 0; i < Trees.Count; i++)
                {
                    BspTree tree = Trees[i];
                    if (Axis(tree.Coordinate, axis) == coordinate &&
                        (tree.AllocatedNodeCount > 1 ||
                            tree.EmptyRegionCount > 0))
                        return true;
                }
                return false;
            }

            int PurgeLayer(int axis, int coordinate)
            {
                int purged = 0;
                for (int i = 0; i < Trees.Count; i++)
                {
                    BspTree tree = Trees[i];
                    if (Axis(tree.Coordinate, axis) != coordinate)
                        continue;
                    ResetTree(tree);
                    purged++;
                }
                return purged;
            }

            bool TryDominantAxis(
                Vector3D direction,
                out int axis,
                out double component)
            {
                double x = Math.Abs(direction.X);
                double y = Math.Abs(direction.Y);
                double z = Math.Abs(direction.Z);
                if (Math.Max(x, Math.Max(y, z)) < 0.000001)
                {
                    axis = 0;
                    component = 0;
                    return false;
                }
                if (x >= y && x >= z)
                {
                    axis = 0;
                    component = direction.X;
                }
                else if (y >= z)
                {
                    axis = 1;
                    component = direction.Y;
                }
                else
                {
                    axis = 2;
                    component = direction.Z;
                }
                return true;
            }

            bool TryParseCoordinate(string text, out Vector3I coordinate)
            {
                string[] values = text.Split(',');
                int x;
                int y;
                int z;
                if (values.Length != 3 ||
                    !int.TryParse(values[0], out x) ||
                    !int.TryParse(values[1], out y) ||
                    !int.TryParse(values[2], out z))
                {
                    coordinate = Vector3I.Zero;
                    return false;
                }
                coordinate = new Vector3I(x, y, z);
                return true;
            }

            bool ValidRegion(Vector3I region)
            {
                return region.X >= 0 && region.X < _regionsPerTreeAxis &&
                    region.Y >= 0 && region.Y < _regionsPerTreeAxis &&
                    region.Z >= 0 && region.Z < _regionsPerTreeAxis;
            }

            bool IsUnitRegion(Vector3I min, Vector3I max)
            {
                Vector3I size = max - min;
                return size.X == 1 && size.Y == 1 && size.Z == 1;
            }

            int LongestAxis(Vector3I size)
            {
                if (size.X >= size.Y && size.X >= size.Z)
                    return 0;
                return size.Y >= size.Z ? 1 : 2;
            }

            int ClampDistance(int distance)
            {
                return Math.Max(1, Math.Min(16, distance));
            }

            int Axis(Vector3I value, int axis)
            {
                if (axis == 0)
                    return value.X;
                return axis == 1 ? value.Y : value.Z;
            }

            double Axis(Vector3D value, int axis)
            {
                if (axis == 0)
                    return value.X;
                return axis == 1 ? value.Y : value.Z;
            }

            void SetAxis(ref Vector3I value, int axis, int axisValue)
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
