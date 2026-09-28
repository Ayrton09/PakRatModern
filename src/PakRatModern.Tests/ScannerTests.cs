using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PakRatModern.Core;

namespace PakRatModern.Tests
{
    /// <summary>Pruebas del scanner de referencias sobre un BSP sintetico.</summary>
    internal static class ScannerTests
    {
        public static void Run(Action<bool, string> check)
        {
            TestNormalization(check);
            TestModelCompanions(check);
            TestEntityScanning(check);
            TestTexDataScanning(check);
            TestStaticProps(check);
            TestMdlMaterials(check);
            TestMapExtras(check);
        }

        private static void TestNormalization(Action<bool, string> check)
        {
            check(GameReference.Normalize(@"custom\wall.vmt") == "materials/custom/wall.vmt",
                "normalizacion: no antepuso materials/ ni convirtio las barras");
            check(GameReference.Normalize("materials/custom/wall.vmt") == "materials/custom/wall.vmt",
                "normalizacion: duplico el prefijo materials/");
            check(GameReference.Normalize("props/crate.mdl") == "models/props/crate.mdl",
                "normalizacion: no antepuso models/");
            check(GameReference.Normalize("ambient/wind.wav") == "sound/ambient/wind.wav",
                "normalizacion: no antepuso sound/");
            check(GameReference.Normalize("../escape.vmt") == null,
                "normalizacion: acepto una ruta con ..");
            check(GameReference.Normalize(@"C:\juegos\x.vmt") == null,
                "normalizacion: acepto una ruta con unidad de disco");

            check(GameReference.ToMaterialVmt("custom/wall.vtf") == "materials/custom/wall.vmt",
                "material: no convirtio .vtf a .vmt");
            check(GameReference.ToMaterialVmt("custom/wall") == "materials/custom/wall.vmt",
                "material: no agrego la extension .vmt");
            check(GameReference.ToMaterialVmt("sprites/laser.spr") == "materials/sprites/laser.vmt",
                "material: no convirtio .spr a .vmt");
            check(GameReference.IsRenderTarget("_rt_WaterReflection") && !GameReference.IsRenderTarget("custom/rt_wall"),
                "material: no distingue un render target de una textura");
        }

        private static void TestModelCompanions(Action<bool, string> check)
        {
            var set = NewSet();
            GameReference.AddModelWithCompanions(set, "props/crate.mdl");

            foreach (var expected in new[]
            {
                "models/props/crate.mdl", "models/props/crate.vvd", "models/props/crate.phy",
                "models/props/crate.dx80.vtx", "models/props/crate.dx90.vtx", "models/props/crate.sw.vtx",
            })
            {
                check(set.Contains(expected), $"acompaniantes del modelo: falta {expected}");
            }

            // Los modelos brush internos ("*3") no son archivos
            var brush = NewSet();
            GameReference.AddModelReference(brush, "*3");
            check(brush.Count == 0, "acompaniantes: trato un brush model (*3) como archivo");

            var numeric = NewSet();
            GameReference.AddModelReference(numeric, "42");
            check(numeric.Count == 0, "acompaniantes: trato un valor numerico como modelo");
        }

        private static void TestEntityScanning(Action<bool, string> check)
        {
            var entities = string.Join("\n", new[]
            {
                "{",
                "\"classname\" \"worldspawn\"",
                "\"skyname\" \"sky_dust\"",
                "\"detailmaterial\" \"detail/detailsprites\"",
                "}",
                "{",
                "\"classname\" \"prop_static\"",
                "\"model\" \"models/props/barrel.mdl\"",
                "}",
                "{",
                "\"classname\" \"func_brush\"",
                "\"model\" \"*7\"",
                "}",
                "{",
                "\"classname\" \"infodecal\"",
                "\"texture\" \"decals/custom/graffiti\"",
                "}",
            });

            var bsp = SyntheticBsp.Build(entities: Encoding.ASCII.GetBytes(entities));
            var refs = BspReferenceScanner.Collect(bsp, "de_test", includeExtras: false);

            check(refs.Contains("models/props/barrel.mdl"), "entidades: no detecto el prop_static");
            check(refs.Contains("models/props/barrel.dx90.vtx"), "entidades: no agrego los acompaniantes del modelo");
            check(refs.Contains("materials/skybox/sky_dustup.vmt"), "entidades: no expandio el skybox");
            check(refs.Contains("materials/skybox/sky_dustbk.vtf"), "entidades: falta una cara del skybox");
            check(refs.Count(r => r.StartsWith("materials/skybox/")) == 12,
                "entidades: el skybox deberia aportar 12 archivos (6 caras x vmt+vtf)");
            check(refs.Contains("materials/detail/detailsprites.vmt"), "entidades: no detecto detailmaterial");
            check(refs.Contains("materials/decals/custom/graffiti.vmt"), "entidades: no detecto la textura del decal");
            check(!refs.Any(r => r.Contains("*")), "entidades: se colo un brush model");

            TestSpritesTexturesAndScripts(check);
        }

        /// <summary>
        /// Entidades cuyos archivos no son modelos ni materiales comunes: los
        /// sprites guardan un material en "model", env_beam usa .spr,
        /// env_projectedtexture carga un .vtf y los VScripts viven en
        /// scripts/vscripts/.
        /// </summary>
        private static void TestSpritesTexturesAndScripts(Action<bool, string> check)
        {
            var entities = string.Join("\n", new[]
            {
                "{", "\"classname\" \"env_sprite\"", "\"model\" \"sprites/mymap/glow.vmt\"", "}",
                "{", "\"classname\" \"env_glow\"", "\"model\" \"sprites/mymap/halo.spr\"", "}",
                "{", "\"classname\" \"env_beam\"", "\"texture\" \"sprites/mymap/beam.spr\"", "}",
                "{", "\"classname\" \"env_projectedtexture\"", "\"texturename\" \"effects/mymap/flashlight\"", "}",
                "{", "\"classname\" \"logic_script\"", "\"vscripts\" \"mymap/logic.nut mymap/extra\"", "}",
            });

            var refs = BspReferenceScanner.Collect(SyntheticBsp.Build(entities: Encoding.ASCII.GetBytes(entities)), "x", false);

            check(refs.Contains("materials/sprites/mymap/glow.vmt"), "entidades: no detecto el material de env_sprite");
            check(refs.Contains("materials/sprites/mymap/halo.vmt"), "entidades: no resolvio el .spr de env_glow a .vmt");
            check(refs.Contains("materials/sprites/mymap/beam.vmt"), "entidades: no resolvio el .spr de env_beam a .vmt");
            check(!refs.Any(r => r.EndsWith(".spr.vmt", StringComparison.OrdinalIgnoreCase)), "entidades: genero una ruta .spr.vmt");
            check(refs.Contains("materials/effects/mymap/flashlight.vtf") && !refs.Contains("materials/effects/mymap/flashlight.vmt"),
                "entidades: texturename de env_projectedtexture es un .vtf, no un .vmt");
            check(refs.Contains("scripts/vscripts/mymap/logic.nut") && refs.Contains("scripts/vscripts/mymap/extra.nut"),
                "entidades: no detecto los VScripts");
        }

        private static void TestTexDataScanning(Action<bool, string> check)
        {
            var names = new[] { "custom/concrete", "nature/grass.vmt" };

            var strData = new List<byte>();
            var offsets = new List<int>();
            foreach (var name in names)
            {
                offsets.Add(strData.Count);
                strData.AddRange(Encoding.ASCII.GetBytes(name));
                strData.Add(0);
            }

            var strTable = new List<byte>();
            foreach (var off in offsets) strTable.AddRange(BitConverter.GetBytes(off));

            var bsp = SyntheticBsp.Build(
                texDataStringData: strData.ToArray(),
                texDataStringTable: strTable.ToArray());

            var refs = BspReferenceScanner.Collect(bsp, "de_test", includeExtras: false);

            check(refs.Contains("materials/custom/concrete.vmt"), "texdata: no agrego la extension .vmt");
            check(refs.Contains("materials/nature/grass.vmt"), "texdata: duplico la extension .vmt");
        }

        private static void TestStaticProps(Action<bool, string> check)
        {
            var models = new[] { "models/props/tree.mdl", "models/props/rock.mdl" };

            // sub-lump 'sprp': dictCount + N nombres de 128 bytes
            var sprp = new List<byte>();
            sprp.AddRange(BitConverter.GetBytes(models.Length));
            foreach (var m in models)
            {
                var name = new byte[128];
                var raw = Encoding.ASCII.GetBytes(m);
                Array.Copy(raw, name, raw.Length);
                sprp.AddRange(name);
            }

            var bsp = SyntheticBsp.Build(staticPropData: sprp.ToArray());
            var refs = BspReferenceScanner.Collect(bsp, "de_test", includeExtras: false);

            check(refs.Contains("models/props/tree.mdl"), "static props: no detecto tree.mdl");
            check(refs.Contains("models/props/rock.mdl"), "static props: no detecto rock.mdl");
            check(refs.Contains("models/props/tree.vvd"), "static props: no agrego los acompaniantes");
        }

        private static void TestMdlMaterials(Action<bool, string> check)
        {
            // studiohdr_t minimo con 1 textura y 1 directorio
            var mdl = new byte[512];
            Encoding.ASCII.GetBytes("IDST").CopyTo(mdl, 0);

            const int textureIndex = 256;
            const int cdTextureIndex = 384;
            const int nameOffset = 320;
            const int dirStringOffset = 420;

            BitConverter.GetBytes(1).CopyTo(mdl, 204);              // numtextures
            BitConverter.GetBytes(textureIndex).CopyTo(mdl, 208);   // textureindex
            BitConverter.GetBytes(1).CopyTo(mdl, 212);              // numcdtextures
            BitConverter.GetBytes(cdTextureIndex).CopyTo(mdl, 216); // cdtextureindex

            // El offset del nombre es relativo al inicio de su struct
            BitConverter.GetBytes(nameOffset - textureIndex).CopyTo(mdl, textureIndex);
            Encoding.ASCII.GetBytes("wood01").CopyTo(mdl, nameOffset);

            BitConverter.GetBytes(dirStringOffset).CopyTo(mdl, cdTextureIndex);
            Encoding.ASCII.GetBytes("models/props/").CopyTo(mdl, dirStringOffset);

            var refs = MdlReader.GetMaterialRefs(mdl);

            check(refs.Count == 1, $"mdl: se esperaba 1 material, hay {refs.Count} -> {string.Join(", ", refs)}");
            check(refs.Contains("materials/models/props/wood01.vmt"),
                $"mdl: ruta de material incorrecta -> {string.Join(", ", refs)}");

            check(MdlReader.GetMaterialRefs(new byte[10]).Count == 0, "mdl: no rechazo un archivo truncado");
            check(MdlReader.GetMaterialRefs(new byte[300]).Count == 0, "mdl: no rechazo un archivo sin la firma IDST");

            // Un nombre con subcarpeta se compone igual con $cdmaterials, como el motor
            Encoding.ASCII.GetBytes("sub/wood").CopyTo(mdl, nameOffset);
            var composed = MdlReader.GetMaterialRefs(mdl);
            check(composed.Count == 1 && composed[0] == "materials/models/props/sub/wood.vmt",
                $"mdl: no compuso el directorio con un nombre con subcarpeta -> {string.Join(", ", composed)}");

            check(GameReference.JoinModelMaterialPath("models/props/", "sub/wood") == "models/props/sub/wood",
                "mdl: JoinModelMaterialPath ignoro el directorio");
        }

        private static void TestMapExtras(Action<bool, string> check)
        {
            var bsp = SyntheticBsp.Build();

            var without = BspReferenceScanner.Collect(bsp, "de_dust2", includeExtras: false);
            check(without.Count == 0, $"extras: con includeExtras=false no deberia haber referencias, hay {without.Count}");

            var with = BspReferenceScanner.Collect(bsp, "de_dust2", includeExtras: true);
            check(with.Contains("maps/de_dust2.nav"), "extras: falta el .nav");
            check(with.Contains("resource/overviews/de_dust2_radar.dds"), "extras: falta el radar");
            check(with.Contains("materials/overviews/de_dust2.vmt"), "extras: falta el overview");
            check(with.Contains("maps/de_dust2_particles.txt"), "extras: falta el manifiesto de particulas");
            check(with.Contains("scripts/soundscapes_de_dust2.txt"), "extras: falta el soundscape");
            check(with.Count == 12, $"extras: se esperaban 12 archivos, hay {with.Count}");
        }

        private static HashSet<string> NewSet() => new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }
}
