using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;

namespace TaskbarMemo
{
    /// <summary>挑一个系统上确实存在的中文字体，避免落回难看的默认字体。</summary>
    public static class UiFont
    {
        private static string _family;

        public static string Family
        {
            get
            {
                if (_family == null) _family = Pick();
                return _family;
            }
        }

        private static string Pick()
        {
            string[] prefer = new string[]
            {
                "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI", "Tahoma"
            };

            try
            {
                using (InstalledFontCollection col = new InstalledFontCollection())
                {
                    Dictionary<string, bool> set =
                        new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                    foreach (FontFamily f in col.Families)
                    {
                        set[f.Name] = true;
                    }
                    foreach (string p in prefer)
                    {
                        if (set.ContainsKey(p)) return p;
                    }
                }
            }
            catch
            {
            }

            return "Microsoft Sans Serif";
        }

        public static Font Get(float size, FontStyle style)
        {
            try
            {
                return new Font(Family, size, style);
            }
            catch
            {
                return new Font("Microsoft Sans Serif", size, style);
            }
        }
    }
}
