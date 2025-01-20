using System.Collections.Generic;

namespace PDLimits
{
    public static class PDLimits
    {
        private static BinaryReader _Reader;
        private static List<int> _Forbidden;
        private static List<int> _Limited;

        private static int _ForbiddenCount;
        private static int _LimitedCount;

        public static void Load(string PDLimtits)
        {
            // Load the PDLimits file
            _Reader = new BinaryReader(File.Open(PDLimtits, FileMode.Open, FileAccess.Read, FileShare.Read));
            _ForbiddenCount = _Reader.ReadUInt16();

            //Read _ForbiddenCount*2 bytes and store each value in _Forbidden
            _Forbidden = new List<int>();
            for (int i = 0; i < _ForbiddenCount; i++)
            {
                _Forbidden.Add(_Reader.ReadUInt16());
            }

            _LimitedCount = _Reader.ReadUInt16();

            //Read _LimitedCount*2 bytes and store each value in _Limited
            _Limited = new List<int>();
            for (int i = 0; i < _LimitedCount; i++)
            {
                _Limited.Add(_Reader.ReadUInt16());
            }
        }

        public static List<int> GetForbidden()
        {
            return _Forbidden;
        }

        public static List<int> GetLimited()
        {
            return _Limited;
        }

        public static void Close()
        {
            _Reader.Close();
        }

        public static int GetForbiddenCount()
        {
            return _ForbiddenCount;
        }

        public static int GetLimitedCount()
        {
            return _LimitedCount;
        }

    }
}
