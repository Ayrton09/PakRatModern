using System;
using System.IO;

namespace PakRatModern.Core
{
    /// <summary>
    /// Descompresion LZMA para lo que Source guarda comprimido: lumps del BSP
    /// (<c>bspzip -repack -compress</c>, habitual en TF2) y entradas del PAK con
    /// el metodo 14 del ZIP.
    ///
    /// Es un port del decodificador de referencia del LZMA SDK (LzmaSpec.cpp, de
    /// dominio publico). Decodifica a un buffer del tamano ya conocido, asi que
    /// el propio buffer hace de diccionario y la salida nunca excede lo declarado.
    /// </summary>
    public static class Lzma
    {
        /// <summary>'LZMA' leido como uint32 little-endian.</summary>
        private const uint SourceHeaderId = 0x414D5A4C;

        /// <summary>id(4) + actualSize(4) + lzmaSize(4) + propiedades(5).</summary>
        public const int SourceHeaderSize = 17;

        /// <summary>
        /// Decodifica un stream LZMA crudo. <paramref name="properties"/> son los 5
        /// bytes de propiedades (lc/lp/pb y tamano de diccionario).
        /// </summary>
        public static byte[] Decode(byte[] input, int offset, int count, byte[] properties, long outSize)
        {
            if (outSize < 0 || outSize > int.MaxValue)
                throw new InvalidDataException($"LZMA output size is out of range: {outSize}.");
            if (offset < 0 || count < 0 || (long)offset + count > input.Length)
                throw new InvalidDataException("LZMA stream is out of range.");

            return new Decoder(input, offset, count, properties).Run((int)outSize);
        }

        /// <summary>Si los datos empiezan con la cabecera LZMA que usa Source para sus lumps.</summary>
        public static bool IsSourceCompressed(byte[] data, int offset, int length)
        {
            return length >= SourceHeaderSize &&
                   offset >= 0 && (long)offset + SourceHeaderSize <= data.Length &&
                   BitConverter.ToUInt32(data, offset) == SourceHeaderId;
        }

        /// <summary>
        /// Descomprime un bloque en el formato de Source:
        /// <c>'LZMA' + actualSize + lzmaSize + propiedades[5] + stream</c>.
        /// </summary>
        public static byte[] DecodeSourceBlock(byte[] data, int offset, int length, long maxSize)
        {
            if (!IsSourceCompressed(data, offset, length))
                throw new InvalidDataException("Block does not have a Source LZMA header.");

            var actualSize = BitConverter.ToUInt32(data, offset + 4);
            var lzmaSize = BitConverter.ToUInt32(data, offset + 8);

            if (actualSize > maxSize)
                throw new InvalidDataException($"Compressed block is too large once expanded: {actualSize} bytes. Limit: {maxSize} bytes.");
            if (lzmaSize > length - SourceHeaderSize)
                throw new InvalidDataException("Compressed block is truncated.");

            var properties = new byte[5];
            Array.Copy(data, offset + 12, properties, 0, 5);
            return Decode(data, offset + SourceHeaderSize, (int)lzmaSize, properties, actualSize);
        }

        /// <summary>Tamano total que ocupa en el archivo un bloque LZMA de Source.</summary>
        public static long SourceBlockLength(byte[] data, int offset)
        {
            return SourceHeaderSize + (long)BitConverter.ToUInt32(data, offset + 8);
        }

        private sealed class Decoder
        {
            private const int NumBitModelTotalBits = 11;
            private const uint BitModelTotal = 1u << NumBitModelTotalBits;
            private const int NumMoveBits = 5;
            private const uint TopValue = 1u << 24;
            private const int NumPosBitsMax = 4;
            private const int NumStates = 12;
            private const int NumLenToPosStates = 4;
            private const int NumAlignBits = 4;
            private const int EndPosModelIndex = 14;
            private const int NumFullDistances = 1 << (EndPosModelIndex >> 1);
            private const int MatchMinLen = 2;

            private readonly byte[] _input;
            private int _inPos;
            private readonly int _inEnd;
            private uint _range;
            private uint _code;

            private readonly int _lc;
            private readonly int _lpMask;
            private readonly int _pbMask;
            private readonly uint _dictSize;

            private readonly ushort[] _literals;
            private readonly ushort[] _isMatch = Probs(NumStates << NumPosBitsMax);
            private readonly ushort[] _isRep = Probs(NumStates);
            private readonly ushort[] _isRepG0 = Probs(NumStates);
            private readonly ushort[] _isRepG1 = Probs(NumStates);
            private readonly ushort[] _isRepG2 = Probs(NumStates);
            private readonly ushort[] _isRep0Long = Probs(NumStates << NumPosBitsMax);
            private readonly ushort[] _posSlot = Probs(NumLenToPosStates << 6);
            private readonly ushort[] _posDecoders = Probs(1 + NumFullDistances - EndPosModelIndex);
            private readonly ushort[] _align = Probs(1 << NumAlignBits);
            private readonly LenDecoder _len = new LenDecoder();
            private readonly LenDecoder _repLen = new LenDecoder();

            public Decoder(byte[] input, int offset, int count, byte[] properties)
            {
                if (properties == null || properties.Length < 5)
                    throw new InvalidDataException("LZMA properties are missing.");

                int d = properties[0];
                if (d >= 9 * 5 * 5)
                    throw new InvalidDataException("LZMA properties are invalid.");

                _lc = d % 9;
                d /= 9;
                var lp = d % 5;
                var pb = d / 5;

                _lpMask = (1 << lp) - 1;
                _pbMask = (1 << pb) - 1;
                _dictSize = Math.Max(BitConverter.ToUInt32(properties, 1), 1u << 12);
                _literals = Probs(0x300 << (_lc + lp));

                _input = input;
                _inPos = offset;
                _inEnd = offset + count;
            }

            public byte[] Run(int outSize)
            {
                var output = new byte[outSize];
                if (outSize == 0) return output;

                InitRangeDecoder();

                var pos = 0;
                uint rep0 = 0, rep1 = 0, rep2 = 0, rep3 = 0;
                var state = 0;

                while (pos < outSize)
                {
                    var posState = pos & _pbMask;

                    if (DecodeBit(_isMatch, (state << NumPosBitsMax) + posState) == 0)
                    {
                        DecodeLiteral(output, pos, state, rep0);
                        pos++;
                        state = state < 4 ? 0 : (state < 10 ? state - 3 : state - 6);
                        continue;
                    }

                    int len;
                    if (DecodeBit(_isRep, state) != 0)
                    {
                        if (pos == 0) throw Corrupt();

                        if (DecodeBit(_isRepG0, state) == 0)
                        {
                            if (DecodeBit(_isRep0Long, (state << NumPosBitsMax) + posState) == 0)
                            {
                                state = state < 7 ? 9 : 11;
                                output[pos] = output[pos - rep0 - 1];
                                pos++;
                                continue;
                            }
                        }
                        else
                        {
                            uint dist;
                            if (DecodeBit(_isRepG1, state) == 0)
                            {
                                dist = rep1;
                            }
                            else
                            {
                                if (DecodeBit(_isRepG2, state) == 0)
                                {
                                    dist = rep2;
                                }
                                else
                                {
                                    dist = rep3;
                                    rep3 = rep2;
                                }
                                rep2 = rep1;
                            }
                            rep1 = rep0;
                            rep0 = dist;
                        }

                        len = _repLen.Decode(this, posState);
                        state = state < 7 ? 8 : 11;
                    }
                    else
                    {
                        rep3 = rep2;
                        rep2 = rep1;
                        rep1 = rep0;
                        len = _len.Decode(this, posState);
                        state = state < 7 ? 7 : 10;

                        rep0 = DecodeDistance(len);
                        if (rep0 == 0xFFFFFFFF)
                            throw new InvalidDataException("LZMA stream ended before the declared size.");
                        if (rep0 >= _dictSize || rep0 >= (uint)pos)
                            throw Corrupt();
                    }

                    len += MatchMinLen;
                    if (len > outSize - pos)
                        throw new InvalidDataException("LZMA stream is larger than the declared size.");

                    // Copia byte a byte: la coincidencia puede solaparse con lo que escribe.
                    var src = pos - (int)rep0 - 1;
                    for (var i = 0; i < len; i++)
                        output[pos++] = output[src++];
                }

                return output;
            }

            private void DecodeLiteral(byte[] output, int pos, int state, uint rep0)
            {
                uint prevByte = pos > 0 ? output[pos - 1] : 0u;
                var litState = ((pos & _lpMask) << _lc) + (int)(prevByte >> (8 - _lc));
                var baseIndex = 0x300 * litState;
                var symbol = 1;

                if (state >= 7)
                {
                    uint matchByte = output[pos - (int)rep0 - 1];
                    do
                    {
                        var matchBit = (int)((matchByte >> 7) & 1);
                        matchByte <<= 1;
                        var bit = DecodeBit(_literals, baseIndex + ((1 + matchBit) << 8) + symbol);
                        symbol = (symbol << 1) | bit;
                        if (matchBit != bit) break;
                    }
                    while (symbol < 0x100);
                }

                while (symbol < 0x100)
                    symbol = (symbol << 1) | DecodeBit(_literals, baseIndex + symbol);

                output[pos] = (byte)(symbol - 0x100);
            }

            private uint DecodeDistance(int len)
            {
                var lenState = Math.Min(len, NumLenToPosStates - 1);
                var posSlot = BitTreeDecode(_posSlot, lenState << 6, 6);
                if (posSlot < 4) return (uint)posSlot;

                var numDirectBits = (posSlot >> 1) - 1;
                var dist = (uint)((2 | (posSlot & 1)) << numDirectBits);

                if (posSlot < EndPosModelIndex)
                {
                    dist += (uint)BitTreeReverseDecode(_posDecoders, (int)dist - posSlot, numDirectBits);
                }
                else
                {
                    dist += DecodeDirectBits(numDirectBits - NumAlignBits) << NumAlignBits;
                    dist += (uint)BitTreeReverseDecode(_align, 0, NumAlignBits);
                }

                return dist;
            }

            // --- Decodificador de rango ---

            private void InitRangeDecoder()
            {
                _range = 0xFFFFFFFF;
                _code = 0;
                var first = NextByte();
                for (var i = 0; i < 4; i++) _code = (_code << 8) | NextByte();
                if (first != 0 || _code == _range) throw Corrupt();
            }

            private byte NextByte()
            {
                if (_inPos >= _inEnd) throw new InvalidDataException("LZMA stream is truncated.");
                return _input[_inPos++];
            }

            private void Normalize()
            {
                if (_range < TopValue)
                {
                    _range <<= 8;
                    _code = (_code << 8) | NextByte();
                }
            }

            internal int DecodeBit(ushort[] probs, int index)
            {
                uint v = probs[index];
                var bound = (_range >> NumBitModelTotalBits) * v;
                int symbol;

                if (_code < bound)
                {
                    v += (BitModelTotal - v) >> NumMoveBits;
                    _range = bound;
                    symbol = 0;
                }
                else
                {
                    v -= v >> NumMoveBits;
                    _code -= bound;
                    _range -= bound;
                    symbol = 1;
                }

                probs[index] = (ushort)v;
                Normalize();
                return symbol;
            }

            private uint DecodeDirectBits(int numBits)
            {
                uint result = 0;
                do
                {
                    _range >>= 1;
                    _code -= _range;
                    var t = 0u - (_code >> 31);
                    _code += _range & t;
                    if (_code == _range) throw Corrupt();
                    Normalize();
                    result = (result << 1) + (t + 1);
                }
                while (--numBits > 0);
                return result;
            }

            internal int BitTreeDecode(ushort[] probs, int offset, int numBits)
            {
                var m = 1;
                for (var i = 0; i < numBits; i++) m = (m << 1) + DecodeBit(probs, offset + m);
                return m - (1 << numBits);
            }

            private int BitTreeReverseDecode(ushort[] probs, int offset, int numBits)
            {
                int m = 1, symbol = 0;
                for (var i = 0; i < numBits; i++)
                {
                    var bit = DecodeBit(probs, offset + m);
                    m = (m << 1) + bit;
                    symbol |= bit << i;
                }
                return symbol;
            }

            private static InvalidDataException Corrupt() => new InvalidDataException("LZMA stream is corrupt.");

            private static ushort[] Probs(int count)
            {
                var probs = new ushort[count];
                for (var i = 0; i < probs.Length; i++) probs[i] = (ushort)(BitModelTotal >> 1);
                return probs;
            }

            private sealed class LenDecoder
            {
                private readonly ushort[] _choice = Probs(2);
                private readonly ushort[] _low = Probs(1 << (NumPosBitsMax + 3));
                private readonly ushort[] _mid = Probs(1 << (NumPosBitsMax + 3));
                private readonly ushort[] _high = Probs(1 << 8);

                public int Decode(Decoder rc, int posState)
                {
                    if (rc.DecodeBit(_choice, 0) == 0) return rc.BitTreeDecode(_low, posState << 3, 3);
                    if (rc.DecodeBit(_choice, 1) == 0) return 8 + rc.BitTreeDecode(_mid, posState << 3, 3);
                    return 16 + rc.BitTreeDecode(_high, 0, 8);
                }
            }
        }
    }
}
