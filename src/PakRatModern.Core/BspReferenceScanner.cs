using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace PakRatModern.Core
{
    /// <summary>
    /// Recolecta todos los archivos que un BSP necesita para cargar bien.
    ///
    /// Se miran tres fuentes distintas porque ninguna sola alcanza: el lump de
    /// entidades (props, skybox, decals), la tabla de texdata (materiales de la
    /// geometria) y el game lump de static props (modelos colocados en el editor,
    /// que no aparecen como entidades).
    /// </summary>
    public static class BspReferenceScanner
    {
        private static readonly Regex KeyValuePattern =
            new Regex("\"([^\"]*)\"\\s*\"([^\"]*)\"", RegexOptions.Compiled);

        private static readonly Regex MaterialKeyPattern =
            new Regex("(texture|decal|overlay|material|sprite)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex FileishPattern =
            new Regex(@"([A-Za-z0-9_\-/\.]+\.(vmt|vtf|mdl|wav|mp3|pcf|txt|vcd))",
                      RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly string[] SkyboxSides = { "up", "dn", "lf", "rt", "ft", "bk" };

        private const int MaxGameLumps = 1024;
        private const int MaxStaticPropDictEntries = 10000;
        private const int StaticPropNameLength = 128;

        public static HashSet<string> Collect(BspFile bsp, string mapName, bool includeExtras)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            CollectFromEntities(bsp, set);
            CollectStaticProps(bsp, set);
            CollectFromTexData(bsp, set);

            if (includeExtras && !string.IsNullOrWhiteSpace(mapName))
                AddMapExtras(set, mapName);

            return set;
        }

        private static void CollectFromEntities(BspFile bsp, ISet<string> set)
        {
            var entitiesBytes = bsp.GetLumpBytes(PakLimits.EntitiesLumpIndex);
            if (entitiesBytes.Length == 0) return;

            var text = Encoding.ASCII.GetString(entitiesBytes).Replace('\0', '\n');

            foreach (Match match in KeyValuePattern.Matches(text))
            {
                var key = match.Groups[1].Value.ToLowerInvariant().Trim();
                var value = match.Groups[2].Value.Trim();
                if (string.IsNullOrWhiteSpace(value)) continue;

                switch (key)
                {
                    case "model":
                    case "gibmodel":
                        GameReference.AddModelReference(set, value);
                        break;

                    case "detailmaterial":
                        GameReference.AddMaterialReference(set, value);
                        break;

                    case "skyname":
                        AddSkybox(set, value);
                        break;

                    default:
                        // Una clave que suene a material y un valor con separador
                        // suele ser una ruta aunque no traiga extension.
                        if (MaterialKeyPattern.IsMatch(key) && (value.Contains("/") || value.Contains("\\")))
                            GameReference.AddMaterialReference(set, value);

                        foreach (Match fileMatch in FileishPattern.Matches(value))
                        {
                            var candidate = fileMatch.Groups[1].Value;
                            if (candidate.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase))
                                GameReference.AddModelWithCompanions(set, candidate);
                            else
                                GameReference.AddRef(set, candidate);
                        }
                        break;
                }
            }
        }

        /// <summary>El skybox son seis caras, cada una con su .vmt y su .vtf.</summary>
        private static void AddSkybox(ISet<string> set, string skyName)
        {
            var sky = skyName.Replace('\\', '/').Trim('/');
            if (string.IsNullOrWhiteSpace(sky)) return;

            foreach (var side in SkyboxSides)
            {
                GameReference.AddRef(set, $"materials/skybox/{sky}{side}.vmt");
                GameReference.AddRef(set, $"materials/skybox/{sky}{side}.vtf");
            }
        }

        private static void CollectFromTexData(BspFile bsp, ISet<string> set)
        {
            var strData = bsp.GetLumpBytes(PakLimits.TexDataStringDataLumpIndex);
            var strTable = bsp.GetLumpBytes(PakLimits.TexDataStringTableLumpIndex);
            if (strData.Length == 0 || strTable.Length == 0) return;

            for (var i = 0; i <= strTable.Length - 4; i += 4)
            {
                var offset = BitConverter.ToInt32(strTable, i);
                if (offset < 0 || offset >= strData.Length) continue;

                var material = GameReference.ReadNullTerminated(strData, offset);
                if (string.IsNullOrWhiteSpace(material)) continue;

                var reference = material.EndsWith(".vmt", StringComparison.OrdinalIgnoreCase)
                    ? "materials/" + material
                    : "materials/" + material + ".vmt";

                GameReference.AddRef(set, reference);
            }
        }

        /// <summary>
        /// Los static props viven en el sub-lump 'sprp' del game lump, con un
        /// diccionario de nombres de modelo de 128 bytes cada uno.
        /// </summary>
        private static void CollectStaticProps(BspFile bsp, ISet<string> set)
        {
            var gameLump = bsp.Lumps[PakLimits.GameLumpIndex];
            if (gameLump.FileLen < 4) return;

            var gameEnd = (long)gameLump.FileOfs + gameLump.FileLen;
            if (gameLump.FileOfs < 0 || gameEnd > bsp.Raw.Length) return;

            try
            {
                using (var ms = new MemoryStream(bsp.Raw, gameLump.FileOfs, gameLump.FileLen, false))
                using (var br = new BinaryReader(ms))
                {
                    var lumpCount = br.ReadInt32();
                    if (lumpCount < 0 || lumpCount > MaxGameLumps) return;

                    for (var i = 0; i < lumpCount; i++)
                    {
                        if (ms.Length - ms.Position < 16) return;

                        var id = br.ReadUInt32();
                        br.ReadUInt16();            // flags
                        br.ReadUInt16();            // version
                        var fileOfs = br.ReadInt32();
                        var fileLen = br.ReadInt32();

                        if (id != PakLimits.StaticPropGameLumpId) continue;
                        if (fileLen < 4 || fileOfs < 0 || (long)fileOfs + fileLen > bsp.Raw.Length) continue;

                        ReadStaticPropDictionary(bsp.Raw, fileOfs, fileLen, set);
                    }
                }
            }
            catch (Exception)
            {
                // Game lump malformado: se conserva lo recolectado hasta aca.
            }
        }

        private static void ReadStaticPropDictionary(byte[] raw, int offset, int length, ISet<string> set)
        {
            using (var ms = new MemoryStream(raw, offset, length, false))
            using (var br = new BinaryReader(ms))
            {
                var dictCount = br.ReadInt32();
                if (dictCount < 0 || dictCount > MaxStaticPropDictEntries) return;

                for (var i = 0; i < dictCount; i++)
                {
                    if (ms.Length - ms.Position < StaticPropNameLength) break;

                    var nameBytes = br.ReadBytes(StaticPropNameLength);
                    var modelName = GameReference.ReadNullTerminated(nameBytes, 0);
                    GameReference.AddModelReference(set, modelName);
                }
            }
        }

        /// <summary>
        /// Archivos que no estan referenciados dentro del BSP pero que el juego
        /// busca por convencion de nombre (radar, navegacion, overview).
        /// </summary>
        private static void AddMapExtras(ISet<string> set, string mapName)
        {
            foreach (var extra in new[]
            {
                $"maps/{mapName}.nav",
                $"maps/{mapName}.txt",
                $"resource/overviews/{mapName}.txt",
                $"resource/overviews/{mapName}.dds",
                $"resource/overviews/{mapName}_radar.dds",
                $"materials/overviews/{mapName}.vmt",
                $"materials/overviews/{mapName}.vtf",
                $"materials/overviews/{mapName}_radar.vmt",
                $"materials/overviews/{mapName}_radar.vtf",
            })
            {
                GameReference.AddRef(set, extra);
            }
        }
    }
}
