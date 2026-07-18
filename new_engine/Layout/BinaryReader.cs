using System;
using System.Text;

namespace B4XEngineCore
{
    public class BinaryReader
    {
        private readonly byte[] _buf;
        private int _pos;

        public BinaryReader(byte[] buffer, int offset = 0)
        { _buf = buffer; _pos = offset; }

        public int Position { get => _pos; set => _pos = value; }

        public byte ReadByte()
        {
            if (_pos >= _buf.Length) throw new ParseError("Unexpected end of data", _pos);
            return _buf[_pos++];
        }

        public short ReadInt16()
        {
            if (_pos + 2 > _buf.Length) throw new ParseError("Unexpected end of data", _pos);
            short v = BitConverter.ToInt16(_buf, _pos);
            _pos += 2;
            return v;
        }

        public int ReadInt32()
        {
            if (_pos + 4 > _buf.Length) throw new ParseError("Unexpected end of data", _pos);
            int v = BitConverter.ToInt32(_buf, _pos);
            _pos += 4;
            return v;
        }

        public float ReadFloat()
        {
            if (_pos + 4 > _buf.Length) throw new ParseError("Unexpected end of data", _pos);
            float v = BitConverter.ToSingle(_buf, _pos);
            _pos += 4;
            return v;
        }

        public double ReadDouble()
        {
            if (_pos + 8 > _buf.Length) throw new ParseError("Unexpected end of data", _pos);
            double v = BitConverter.ToDouble(_buf, _pos);
            _pos += 8;
            return v;
        }

        public byte[] ReadBytes(int count)
        {
            if (_pos + count > _buf.Length) throw new ParseError("Unexpected end of data", _pos);
            var result = new byte[count];
            Array.Copy(_buf, _pos, result, 0, count);
            _pos += count;
            return result;
        }

        public string ReadLengthPrefixedString()
        {
            int length = ReadInt32();
            if (length == 0) return "";
            var bytes = ReadBytes(length);
            return Encoding.UTF8.GetString(bytes);
        }

        public string ReadStringRef(string[] table)
        {
            int index = ReadInt32();
            if (index < 0 || index >= table.Length)
                throw new ParseError($"String table index {index} out of range (table size={table.Length})", _pos - 4);
            return table[index];
        }

        public string Read7BitEncodedString()
        {
            int length = Read7BitEncodedInt();
            if (length == 0) return "";
            var bytes = ReadBytes(length);
            return Encoding.UTF8.GetString(bytes);
        }

        public int Read7BitEncodedInt()
        {
            int result = 0, shift = 0;
            while (true)
            {
                byte b = ReadByte();
                result |= (b & 0x7F) << shift;
                if ((b & 0x80) == 0) return result;
                shift += 7;
                if (shift >= 35)
                    throw new ParseError("Bad 7-bit encoded int", _pos - 1);
            }
        }
    }
}
