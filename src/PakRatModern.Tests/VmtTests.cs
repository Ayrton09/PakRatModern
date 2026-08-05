using System;
using System.Linq;
using PakRatModern.Core;

namespace PakRatModern.Tests
{
    internal static class VmtTests
    {
        public static void Run(Action<bool, string> check)
        {
            const string vmt = @"
""LightmappedGeneric""
{
    ""$basetexture"" ""custom/wall01""
    ""$bumpmap""     ""custom/wall01_normal""
    ""$envmap""      ""env_cubemap""
    ""$surfaceprop"" ""concrete""
    ""$detail""      ""detail/noise.vtf""
    ""$translucent"" ""1""
    ""$color""       ""[1 .5 .25]""
    ""$bottommaterial"" ""custom/water_below""
    include ""materials/base/shared.vmt""
}";

            var refs = VmtParser.GetDependencies(vmt).ToList();

            check(refs.Contains("materials/custom/wall01.vtf"), "vmt: falta $basetexture");
            check(refs.Contains("materials/custom/wall01_normal.vtf"), "vmt: falta $bumpmap");
            check(refs.Contains("materials/detail/noise.vtf"), "vmt: duplico la extension de $detail");
            check(refs.Contains("materials/custom/water_below.vmt"), "vmt: $bottommaterial deberia ser .vmt");
            check(refs.Contains("materials/base/shared.vmt"), "vmt: no siguio el include");

            check(!refs.Any(r => r.Contains("env_cubemap")), "vmt: env_cubemap no es un archivo");
            check(!refs.Any(r => r.Contains("concrete")), "vmt: $surfaceprop no es una ruta");
            check(!refs.Contains("materials/1.vtf"), "vmt: trato el numero de $translucent como textura");
            check(!refs.Any(r => r.Contains("[")), "vmt: trato un vector como ruta");

            check(VmtParser.GetDependencies(string.Empty).Count == 0, "vmt: contenido vacio deberia dar 0 refs");
            check(VmtParser.GetDependencies(null).Count == 0, "vmt: null deberia dar 0 refs");

            // Un valor sin comillas tambien es sintaxis valida en VMT
            var unquoted = VmtParser.GetDependencies("$basetexture custom/sinComillas").ToList();
            check(unquoted.Contains("materials/custom/sinComillas.vtf"), "vmt: no acepto valores sin comillas");
        }
    }
}
