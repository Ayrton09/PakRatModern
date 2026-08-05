using System.Collections.Generic;
using System.Text;

namespace PakRatModern.Core
{
    /// <summary>
    /// Extrae los materiales que declara un modelo <c>.mdl</c>.
    ///
    /// Un .mdl no guarda rutas completas: guarda nombres de textura por un lado y
    /// directorios de busqueda por otro, y el motor prueba todas las combinaciones.
    /// Por eso hay que generar el producto cartesiano de ambas listas.
    /// </summary>
    public static class MdlReader
    {
        private const int MinHeaderSize = 220;
        private const int TextureStructSize = 64;

        // Offsets dentro de studiohdr_t
        private const int NumTexturesOffset = 204;
        private const int TextureIndexOffset = 208;
        private const int NumCdTexturesOffset = 212;
        private const int CdTextureIndexOffset = 216;

        // Topes de cordura: un .mdl legitimo no los alcanza, y evitan que un
        // archivo corrupto haga iterar millones de veces.
        private const int MaxTextures = 4096;
        private const int MaxCdTextures = 1024;

        public static IReadOnlyList<string> GetMaterialRefs(byte[] bytes)
        {
            var refs = new List<string>();
            if (bytes == null || bytes.Length < MinHeaderSize) return refs;

            if (Encoding.ASCII.GetString(bytes, 0, 4) != "IDST") return refs;

            var numTextures = GameReference.ReadInt32Safe(bytes, NumTexturesOffset);
            var textureIndex = GameReference.ReadInt32Safe(bytes, TextureIndexOffset);
            var numCdTextures = GameReference.ReadInt32Safe(bytes, NumCdTexturesOffset);
            var cdTextureIndex = GameReference.ReadInt32Safe(bytes, CdTextureIndexOffset);

            if (numTextures == null || textureIndex == null || numTextures < 0 || numTextures > MaxTextures)
                return refs;

            if (numCdTextures == null || cdTextureIndex == null || numCdTextures < 0 || numCdTextures > MaxCdTextures)
                numCdTextures = 0;

            var textureNames = new List<string>();
            for (var i = 0; i < numTextures.Value; i++)
            {
                var structOffset = textureIndex.Value + (i * TextureStructSize);
                if (structOffset < 0 || structOffset + TextureStructSize > bytes.Length) break;

                var nameOffset = GameReference.ReadInt32Safe(bytes, structOffset);
                if (nameOffset == null) continue;

                // El offset del nombre es relativo al inicio de su propia struct.
                var name = GameReference.ReadNullTerminated(bytes, structOffset + nameOffset.Value);
                if (!string.IsNullOrWhiteSpace(name)) textureNames.Add(name);
            }

            if (textureNames.Count == 0) return refs;

            var textureDirs = new List<string>();
            if (numCdTextures > 0 && cdTextureIndex > 0)
            {
                for (var i = 0; i < numCdTextures.Value; i++)
                {
                    var dirOffset = GameReference.ReadInt32Safe(bytes, cdTextureIndex.Value + (i * 4));
                    if (dirOffset == null) continue;

                    var dir = GameReference.ReadNullTerminated(bytes, dirOffset.Value);
                    if (!string.IsNullOrWhiteSpace(dir)) textureDirs.Add(dir);
                }
            }

            // Sin directorios declarados, el nombre se usa tal cual.
            if (textureDirs.Count == 0) textureDirs.Add(string.Empty);

            foreach (var textureName in textureNames)
            {
                foreach (var textureDir in textureDirs)
                {
                    var materialPath = GameReference.JoinModelMaterialPath(textureDir, textureName);
                    var reference = GameReference.ToMaterialVmt(materialPath);
                    if (reference != null) refs.Add(reference);
                }
            }

            return refs;
        }
    }
}
