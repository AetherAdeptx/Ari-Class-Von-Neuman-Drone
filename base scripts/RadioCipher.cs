using System;
using System.Text;
using System.Collections.Generic;

namespace IngameScript
{
    public partial class Program
    {
        sealed class RadioCipher
        {
            readonly ShipState _ship;
            readonly long _nodeAddress;
            readonly uint[] _key = new uint[4];
            string _loadedKey;
            ulong _counter;
            readonly HashSet<ulong> _seenNonces = new HashSet<ulong>();
            readonly Queue<ulong> _nonceOrder = new Queue<ulong>();

            public RadioCipher(ShipState ship, long nodeAddress)
            {
                _ship = ship;
                _nodeAddress = nodeAddress;
            }

            public bool Protect(string plain, out string packet)
            {
                if (!LoadKey())
                {
                    packet = null;
                    return false;
                }
                ulong nonce = (ulong)DateTime.UtcNow.Ticks ^
                    (ulong)_nodeAddress ^ ++_counter;
                byte[] bytes = Encoding.UTF8.GetBytes(plain);
                Crypt(bytes, nonce);
                ulong mac = Mac(bytes, nonce);
                packet = "E1|" + nonce.ToString("X16") + "|" +
                    Convert.ToBase64String(bytes) + "|" +
                    mac.ToString("X16");
                return true;
            }

            public bool Unprotect(string packet, out string plain)
            {
                plain = null;
                if (!LoadKey() || string.IsNullOrEmpty(packet))
                    return false;
                string[] parts = packet.Split('|');
                ulong nonce;
                ulong receivedMac;
                byte[] bytes;
                if (parts.Length != 4 || parts[0] != "E1" ||
                    !ulong.TryParse(parts[1],
                        System.Globalization.NumberStyles.HexNumber, null,
                        out nonce) ||
                    !ulong.TryParse(parts[3],
                        System.Globalization.NumberStyles.HexNumber, null,
                        out receivedMac))
                    return false;
                try { bytes = Convert.FromBase64String(parts[2]); }
                catch { return false; }
                if (Mac(bytes, nonce) != receivedMac)
                    return false;
                if (_seenNonces.Contains(nonce))
                    return false;
                _seenNonces.Add(nonce);
                _nonceOrder.Enqueue(nonce);
                if (_nonceOrder.Count > 128)
                    _seenNonces.Remove(_nonceOrder.Dequeue());
                Crypt(bytes, nonce);
                plain = Encoding.UTF8.GetString(bytes);
                return true;
            }

            bool LoadKey()
            {
                string value = _ship.Radio_Encryption_Key;
                if (string.IsNullOrEmpty(value))
                    return false;
                if (value == _loadedKey)
                    return true;
                _loadedKey = value;
                uint hash = 2166136261;
                for (int word = 0; word < 4; word++)
                {
                    for (int i = 0; i < value.Length; i++)
                        hash = (hash ^ value[i]) * 16777619u;
                    hash ^= (uint)(0x9E3779B9u * (word + 1));
                    _key[word] = hash;
                }
                return true;
            }

            void Crypt(byte[] bytes, ulong nonce)
            {
                for (int offset = 0; offset < bytes.Length; offset += 8)
                {
                    ulong stream = Encrypt(nonce + (ulong)(offset / 8));
                    for (int i = 0; i < 8 && offset + i < bytes.Length; i++)
                        bytes[offset + i] ^= (byte)(stream >> (i * 8));
                }
            }

            ulong Mac(byte[] bytes, ulong nonce)
            {
                ulong state = Encrypt(nonce ^ 0xA5A5A5A55A5A5A5AUL);
                for (int offset = 0; offset < bytes.Length; offset += 8)
                {
                    ulong block = 0;
                    for (int i = 0; i < 8 && offset + i < bytes.Length; i++)
                        block |= (ulong)bytes[offset + i] << (i * 8);
                    state = Encrypt(state ^ block);
                }
                return Encrypt(state ^ (ulong)bytes.Length);
            }

            ulong Encrypt(ulong block)
            {
                uint left = (uint)block;
                uint right = (uint)(block >> 32);
                uint sum = 0;
                const uint delta = 0x9E3779B9;
                for (int round = 0; round < 32; round++)
                {
                    left += (((right << 4) ^ (right >> 5)) + right) ^
                        (sum + _key[sum & 3]);
                    sum += delta;
                    right += (((left << 4) ^ (left >> 5)) + left) ^
                        (sum + _key[(sum >> 11) & 3]);
                }
                return left | ((ulong)right << 32);
            }
        }
    }
}
