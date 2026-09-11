using System;
using System.Text;

namespace IngameScript
{
    public partial class Program
    {
        sealed class PersistenceStore
        {
            const int MaximumCharacters = 90000;
            readonly BspSpatialMap _local;
            readonly GlobalTemporalBsp _global;
            readonly RadioNetwork _radio;
            readonly RuntimeRegistry _registry;
            readonly OreMemory _ore;
            readonly DestinationBook _destinations;

            public PersistenceStore(BspSpatialMap local,
                GlobalTemporalBsp global, RadioNetwork radio,
                RuntimeRegistry registry, OreMemory ore,
                DestinationBook destinations)
            {
                _local = local;
                _global = global;
                _radio = radio;
                _registry = registry;
                _ore = ore;
                _destinations = destinations;
            }

            public string Encode()
            {
                string registry = _registry.EncodePersistent();
                string radio = _radio.Encode();
                string ore = _ore.Encode(12000);
                string destinations = _destinations.Encode(12000);
                int payloadBudget = MaximumCharacters - registry.Length -
                    radio.Length - ore.Length - destinations.Length - 160;
                int globalBudget = Math.Max(4, payloadBudget / 2);
                string global = _global.Encode(globalBudget);
                string local = _local.Encode(Math.Max(16,
                    payloadBudget - global.Length));
                StringBuilder text = new StringBuilder("VN2|");
                Append(text, registry);
                Append(text, radio);
                Append(text, local);
                Append(text, global);
                Append(text, ore);
                Append(text, destinations);
                return text.ToString();
            }

            public bool Decode(string encoded)
            {
                if (string.IsNullOrEmpty(encoded))
                    return true;
                if (!encoded.StartsWith("VN2|"))
                    return false;
                int offset = 4;
                string registry;
                string radio;
                string local;
                string global;
                string ore = "";
                string destinations = "";
                if (!Read(encoded, ref offset, out registry) ||
                    !Read(encoded, ref offset, out radio) ||
                    !Read(encoded, ref offset, out local) ||
                    !Read(encoded, ref offset, out global))
                    return false;
                Read(encoded, ref offset, out ore);
                Read(encoded, ref offset, out destinations);
                if (!_registry.DecodePersistent(registry))
                    return false;
                _local.SetSpatialCacheDistance(
                    int.Parse(_registry.Values["Spatial_Cache_Distance"]));
                return _radio.Decode(radio) && _local.Decode(local) &&
                    _global.Merge(global) && _ore.Decode(ore) &&
                    _destinations.Decode(destinations);
            }

            void Append(StringBuilder text, string value)
            {
                text.Append(value.Length).Append('|').Append(value);
            }

            bool Read(string text, ref int offset, out string value)
            {
                int end = text.IndexOf('|', offset);
                int length;
                if (end < 0 || !int.TryParse(text.Substring(
                    offset, end - offset), out length) || length < 0 ||
                    end + 1 + length > text.Length)
                {
                    value = null;
                    return false;
                }
                offset = end + 1;
                value = text.Substring(offset, length);
                offset += length;
                return true;
            }
        }
    }
}
