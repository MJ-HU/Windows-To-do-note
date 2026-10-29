using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace TaskbarMemo
{
    /// <summary>
    /// 中英文混排的按字符折行。
    /// 中文没有空格，靠 TextRenderer 的 WordBreak 折行会把整段挤成一行，
    /// 所以这里自己量宽度逐字断行。
    /// </summary>
    public static class TextWrap
    {
        public static List<string> Wrap(string text, Font font, int width)
        {
            List<string> lines = new List<string>();
            if (width <= 10) return lines;
            if (text == null) return lines;

            string norm = text.Replace("\r\n", "\n").Replace('\r', '\n');
            string[] paras = norm.Split('\n');

            foreach (string para in paras)
            {
                if (para.Length == 0)
                {
                    lines.Add("");
                    continue;
                }

                StringBuilder cur = new StringBuilder();
                foreach (char ch in para)
                {
                    cur.Append(ch);

                    if (cur.Length > 1 && TextRenderer.MeasureText(cur.ToString(), font).Width > width)
                    {
                        char last = cur[cur.Length - 1];
                        cur.Length = cur.Length - 1;
                        lines.Add(cur.ToString());
                        cur.Length = 0;
                        cur.Append(last);
                    }
                }

                if (cur.Length > 0) lines.Add(cur.ToString());
            }

            return lines;
        }

        /// <summary>把一行裁到能放下省略号为止。</summary>
        public static string FitEllipsis(string line, Font font, int width)
        {
            string s = line == null ? "" : line;
            while (s.Length > 0 && TextRenderer.MeasureText(s + "…", font).Width > width)
            {
                s = s.Substring(0, s.Length - 1);
            }
            return s + "…";
        }

        /// <summary>只取第一行（任务栏条状模式用）。</summary>
        public static string FirstLine(string text, Font font, int width)
        {
            List<string> lines = Wrap(text, font, width);
            if (lines.Count == 0) return "";

            string s = lines[0];
            if (lines.Count > 1) return FitEllipsis(s, font, width);
            if (TextRenderer.MeasureText(s, font).Width > width) return FitEllipsis(s, font, width);
            return s;
        }
    }
}
