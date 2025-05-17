using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace PDLimits
{
    public static class PDLimits
    {
        private static BinaryReader _Reader;
        private static List<ushort> _Forbidden;
        private static List<ushort> _Limited;
        private static List<ushort> _SemiLimited;

        private static int _ForbiddenCount;
        private static int _LimitedCount;
        private static int _SemiLimitedCount;

        public static void Load(string PDLimtits)
        {
            // Load the PDLimits file
            _Reader = new BinaryReader(File.Open(PDLimtits, FileMode.Open, FileAccess.Read, FileShare.Read));
            _ForbiddenCount = _Reader.ReadUInt16();

            //Read _ForbiddenCount*2 bytes and store each value in _Forbidden
            _Forbidden = new List<ushort>();
            for (int i = 0; i < _ForbiddenCount; i++)
            {
                _Forbidden.Add(_Reader.ReadUInt16());
            }

            _LimitedCount = _Reader.ReadUInt16();

            //Read _LimitedCount*2 bytes and store each value in _Limited
            _Limited = new List<ushort>();
            for (int i = 0; i < _LimitedCount; i++)
            {
                _Limited.Add(_Reader.ReadUInt16());
            }

            _SemiLimitedCount = _Reader.ReadUInt16();

            //Read _SemiLimitedCount*2 bytes and store each value in _SemiLimited
            _SemiLimited = new List<ushort>();
            for (int i = 0; i < _SemiLimitedCount; i++)
            {
                _SemiLimited.Add(_Reader.ReadUInt16());
            }

            _Reader.Close();
        }

        public static List<ushort> GetForbidden()
        {
            return _Forbidden;
        }

        public static List<ushort> GetLimited()
        {
            return _Limited;
        }

        public static List<ushort> GetSemiLimited()
        {
            return _SemiLimited;
        }

        public static int GetForbiddenCount()
        {
            return _ForbiddenCount;
        }

        public static int GetLimitedCount()
        {
            return _LimitedCount;
        }

        public static int GetSemiLimitedCount()
        {
            return _SemiLimitedCount;
        }

        public static void Remove_CardFromSemiLimited(ushort CardID)
        {
            _SemiLimited.Remove(CardID);
            _SemiLimitedCount--;
        }
        public static void Remove_CardFromForbidden(ushort CardID)
        {
            _Forbidden.Remove(CardID);
            _ForbiddenCount--;
        }

        public static void Remove_CardFromLimited(ushort CardID)
        {
            _Limited.Remove(CardID);
            _LimitedCount--;
        }

        public static void Save()
        {
            // Save the PDLimits file
            var _Writer = new BinaryWriter(File.Open("pd_limits.bin", FileMode.CreateNew, FileAccess.Write, FileShare.Read));
            _Writer.Write((ushort)_ForbiddenCount);
            //Write each value in _Forbidden as a 2 byte value
            for (int i = 0; i < _ForbiddenCount; i++)
            {
                _Writer.Write((ushort)_Forbidden[i]);
            }
            _Writer.Write((ushort)_LimitedCount);
            //Write each value in _Limited as a 2 byte value
            for (int i = 0; i < _LimitedCount; i++)
            {
                _Writer.Write((ushort)_Limited[i]);
            }
            //Write each value in _SemiLimited as a 2 byte value
            for (int i = 0; i < _SemiLimitedCount; i++)
            {
                _Writer.Write((ushort)_SemiLimited[i]);
            }
            _Writer.Close();
        }


        public static void Add_CardToSemiLimited(ushort CardID)
        {
            _SemiLimited.Add(CardID);
            _ForbiddenCount++;
        }

        public static void Add_CardToForbidden(ushort CardID)
        {
            _Forbidden.Add(CardID);
            _ForbiddenCount++;
        }

        public static void Add_CardToLimited(ushort CardID)
        {
            _Limited.Add(CardID);
            _LimitedCount++;
        }

    }
}
