namespace PakRatModern.Core
{
    /// <summary>
    /// Limites y constantes del formato BSP de Source, compartidos con la CLI
    /// (pakrat_modern.py) para que ambas herramientas acepten y rechacen lo mismo.
    /// </summary>
    public static class PakLimits
    {
        public const int LumpCount = 64;
        public const int PakLumpIndex = 40;
        public const int GameLumpIndex = 35;
        public const int EntitiesLumpIndex = 0;
        public const int TexDataStringDataLumpIndex = 43;
        public const int TexDataStringTableLumpIndex = 44;

        /// <summary>'sprp' leido como uint32 little-endian.</summary>
        public const uint StaticPropGameLumpId = 1936749168;

        /// <summary>ident(4) + version(4) + 64 lumps * 16 + mapRevision(4).</summary>
        public const int HeaderSize = 4 + 4 + (LumpCount * 16) + 4;

        public const long MaxBspBytes = 1024L * 1024 * 1024;
        public const long MaxPakEntryBytes = 512L * 1024 * 1024;
        public const long MaxPakTotalBytes = 1536L * 1024 * 1024;
        public const int MaxPakEntries = 20000;

        /// <summary>"VBSP" en ASCII.</summary>
        public static readonly byte[] Ident = { 0x56, 0x42, 0x53, 0x50 };
    }
}
