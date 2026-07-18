using System;
using System.Collections.Generic;
using System.Text;

namespace B4XEngineCore
{
    public class BinaryWriter
    {
        private byte[] _buf;
        private int _pos;
        private int _len;

        public BinaryWriter(int initialCapacity = 4096)
        { _buf = new byte[initialCapacity]; _pos = 0; _len = 0; }

        public int Position { get => _pos; set => _pos = value; }
        public int Length => _len;

        public byte[] ToBuffer()
        {
            var result = new byte[_len];
            Array.Copy(_buf, 0, result, 0, _len);
            return result;
        }

        public void WriteFrom(BinaryWriter other)
        {
            var data = other.ToBuffer();
            EnsureCapacity(data.Length);
            Array.Copy(data, 0, _buf, _pos, data.Length);
            _pos += data.Length;
            if (_pos > _len) _len = _pos;
        }

        public void WriteByte(byte value)
        {
            EnsureCapacity(1);
            _buf[_pos++] = value;
            if (_pos > _len) _len = _pos;
        }

        public void WriteInt16(short value)
        {
            EnsureCapacity(2);
            BitConverter.GetBytes(value).CopyTo(_buf, _pos);
            _pos += 2;
            if (_pos > _len) _len = _pos;
        }

        public void WriteInt32(int value)
        {
            EnsureCapacity(4);
            BitConverter.GetBytes(value).CopyTo(_buf, _pos);
            _pos += 4;
            if (_pos > _len) _len = _pos;
        }

        public void WriteFloat(float value)
        {
            EnsureCapacity(4);
            BitConverter.GetBytes(value).CopyTo(_buf, _pos);
            _pos += 4;
            if (_pos > _len) _len = _pos;
        }

        public void WriteDouble(double value)
        {
            EnsureCapacity(8);
            BitConverter.GetBytes(value).CopyTo(_buf, _pos);
            _pos += 8;
            if (_pos > _len) _len = _pos;
        }

        public void WriteBytes(byte[] data)
        {
            EnsureCapacity(data.Length);
            Array.Copy(data, 0, _buf, _pos, data.Length);
            _pos += data.Length;
            if (_pos > _len) _len = _pos;
        }

        public void WriteLengthPrefixedString(string str)
        {
            var bytes = Encoding.UTF8.GetBytes(str);
            WriteInt32(bytes.Length);
            if (bytes.Length > 0) WriteBytes(bytes);
        }

        public void WriteStringRef(Dictionary<string, int> table, string str)
        {
            if (!table.TryGetValue(str, out int index))
            {
                index = table.Count;
                table[str] = index;
            }
            WriteInt32(index);
        }

        public void Write7BitEncodedString(string str)
        {
            var bytes = Encoding.UTF8.GetBytes(str);
            Write7BitEncodedInt(bytes.Length);
            if (bytes.Length > 0) WriteBytes(bytes);
        }

        public void Write7BitEncodedInt(int value)
        {
            uint v = (uint)value;
            while (v >= 0x80)
            {
                WriteByte((byte)((v & 0x7F) | 0x80));
                v >>= 7;
            }
            WriteByte((byte)(v & 0x7F));
        }

        private void EnsureCapacity(int additionalBytes)
        {
            int required = _pos + additionalBytes;
            if (required <= _buf.Length) return;
            int newSize = _buf.Length * 2;
            while (newSize < required) newSize *= 2;
            var newBuf = new byte[newSize];
            Array.Copy(_buf, newBuf, _len);
            _buf = newBuf;
        }
    }
}
