using System;

namespace UniversalConvert.App
{
    /// <summary>
    /// 简化版语义化版本（SemVer）比较，支持 prerelease（如 1.4.0-dev.1）。
    /// 稳定版 > 预发布版；预发布按分段比较（数字段按数值，其余按字典序）。
    /// </summary>
    public sealed class SemVersion : IComparable<SemVersion>
    {
        public int Major { get; }
        public int Minor { get; }
        public int Patch { get; }
        public string Prerelease { get; }

        public SemVersion(int major, int minor, int patch, string prerelease)
        {
            Major = major;
            Minor = minor;
            Patch = patch;
            Prerelease = prerelease;
        }

        public bool IsPrerelease => Prerelease != null;

        public static SemVersion Parse(string version)
        {
            if (string.IsNullOrEmpty(version)) return null;

            var v = version.Trim().TrimStart('v', 'V');

            // 去掉构建元数据（+...）
            var plus = v.IndexOf('+');
            if (plus >= 0) v = v.Substring(0, plus);

            // 提取 prerelease（-...）
            string prerelease = null;
            var dash = v.IndexOf('-');
            if (dash >= 0)
            {
                prerelease = v.Substring(dash + 1);
                v = v.Substring(0, dash);
            }

            var parts = v.Split('.');
            if (parts.Length < 2) return null;

            int major, minor, patch = 0;
            if (!int.TryParse(parts[0], out major)) return null;
            if (!int.TryParse(parts[1], out minor)) return null;
            if (parts.Length >= 3) int.TryParse(parts[2], out patch);

            return new SemVersion(major, minor, patch, prerelease);
        }

        public int CompareTo(SemVersion other)
        {
            if (other == null) return 1;

            int c = Major.CompareTo(other.Major);
            if (c != 0) return c;
            c = Minor.CompareTo(other.Minor);
            if (c != 0) return c;
            c = Patch.CompareTo(other.Patch);
            if (c != 0) return c;

            if (Prerelease == null && other.Prerelease == null) return 0;
            if (Prerelease == null) return 1;   // 稳定版 > 预发布
            if (other.Prerelease == null) return -1;
            return ComparePrerelease(Prerelease, other.Prerelease);
        }

        private static int ComparePrerelease(string a, string b)
        {
            var ap = a.Split('.');
            var bp = b.Split('.');
            int n = Math.Min(ap.Length, bp.Length);
            for (int i = 0; i < n; i++)
            {
                int ai, bi;
                bool aNum = int.TryParse(ap[i], out ai);
                bool bNum = int.TryParse(bp[i], out bi);

                int c;
                if (aNum && bNum) c = ai.CompareTo(bi);
                else if (aNum) c = -1;   // 数字段 < 字母段（SemVer 规则）
                else if (bNum) c = 1;
                else c = string.CompareOrdinal(ap[i], bp[i]);

                if (c != 0) return c;
            }
            return ap.Length.CompareTo(bp.Length);
        }

        public override string ToString()
        {
            var v = Major + "." + Minor + "." + Patch;
            return Prerelease == null ? v : v + "-" + Prerelease;
        }
    }
}
