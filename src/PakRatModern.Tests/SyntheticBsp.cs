using System;
using System.Collections.Generic;
using PakRatModern.Core;

namespace PakRatModern.Tests
{
    /// <summary>
    /// Construye BSPs minimos pero validos para las pruebas, sin depender de mapas
    /// reales. Solo se rellenan los lumps que cada test necesita.
    /// </summary>
    internal static class SyntheticBsp
    {
        public static BspFile Build(
            byte[] entities = null,
            byte[] texDataStringData = null,
            byte[] texDataStringTable = null,
            byte[] staticPropData = null,
            byte[] pak = null)
        {
            var lumps = new Lump[PakLimits.LumpCount];
            for (var i = 0; i < lumps.Length; i++) lumps[i] = new Lump();

            var body = new List<byte>();

            void Place(int index, byte[] data)
            {
                if (data == null || data.Length == 0) return;
                Pad();
                lumps[index].FileOfs = PakLimits.HeaderSize + body.Count;
                lumps[index].FileLen = data.Length;
                body.AddRange(data);
            }

            void Pad()
            {
                while (body.Count % 4 != 0) body.Add(0);
            }

            Place(PakLimits.EntitiesLumpIndex, entities);
            Place(PakLimits.TexDataStringDataLumpIndex, texDataStringData);
            Place(PakLimits.TexDataStringTableLumpIndex, texDataStringTable);

            if (staticPropData != null && staticPropData.Length > 0)
            {
                // El game lump guarda offsets ABSOLUTOS del archivo, asi que hay que
                // reservar primero su cabecera para saber donde caera el sub-lump.
                Pad();
                var gameLumpStart = PakLimits.HeaderSize + body.Count;
                const int headerSize = 4 + 16;               // count + una entrada
                var sprpStart = gameLumpStart + headerSize;

                var header = new List<byte>();
                header.AddRange(BitConverter.GetBytes(1));                              // lumpCount
                header.AddRange(BitConverter.GetBytes(PakLimits.StaticPropGameLumpId)); // id 'sprp'
                header.AddRange(BitConverter.GetBytes((ushort)0));                      // flags
                header.AddRange(BitConverter.GetBytes((ushort)4));                      // version
                header.AddRange(BitConverter.GetBytes(sprpStart));                      // fileOfs absoluto
                header.AddRange(BitConverter.GetBytes(staticPropData.Length));          // fileLen

                lumps[PakLimits.GameLumpIndex].FileOfs = gameLumpStart;
                lumps[PakLimits.GameLumpIndex].FileLen = headerSize + staticPropData.Length;

                body.AddRange(header);
                body.AddRange(staticPropData);
            }

            Place(PakLimits.PakLumpIndex, pak);

            var raw = new byte[PakLimits.HeaderSize + body.Count];
            var serialized = BspFile.SerializeHeader(21, 7, lumps);
            Array.Copy(serialized, 0, raw, 0, serialized.Length);
            body.CopyTo(0, raw, PakLimits.HeaderSize, body.Count);

            return BspFile.Parse(raw);
        }
    }
}
