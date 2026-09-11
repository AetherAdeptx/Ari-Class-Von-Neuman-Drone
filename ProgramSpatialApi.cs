using VRageMath;

namespace IngameScript
{
    public partial class Program
    {
        public void SetSpatialCacheDistance(int kilometersPerAxis)
        {
            _ship.SetSpatialCacheDistance(kilometersPerAxis);
        }

        public int GetSpatialCacheDistance()
        {
            return _ship.GetSpatialCacheDistance();
        }

        public double GetSpatialCacheSpanKilometers()
        {
            return _ship.GetSpatialCacheSpanKilometers();
        }

        // 0 = unknown, 1 = observed empty, 2 = observed matter.
        public byte GetSpatialValue(Vector3D localPositionMeters)
        {
            return _ship.GetSpatialValue(localPositionMeters);
        }

        public byte GetSpatialValueAtWorld(Vector3D worldPositionMeters)
        {
            return _ship.GetSpatialValueAtWorld(worldPositionMeters);
        }

        public string EncodeSpatialMap()
        {
            return _ship.EncodeSpatialMap();
        }

        public bool DecodeSpatialMap(string encoded)
        {
            return _ship.DecodeSpatialMap(encoded);
        }
    }
}
