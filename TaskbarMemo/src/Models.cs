using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TaskbarMemo
{
    public enum MemoKind
    {
        Custom = 0,
        Routine = 1,
        Future = 2
    }

    /// <summary>一条进入今日显示队列的备忘。</summary>
    public class DisplayEntry
    {
        public MemoKind Kind;
        public string Text;
        public object Source;

        public DisplayEntry(MemoKind kind, string text, object source)
        {
            Kind = kind;
            Text = text == null ? "" : text;
            Source = source;
        }
    }

    /// <summary>自定义备忘：一次性条目，完成后永久不再显示。</summary>
    public class MemoItem
    {
        public string Text { get; set; }
        public bool Done { get; set; }

        /// <summary>完成日期 yyyy-MM-dd，只用于判断"今天是否做过事"。</summary>
        public string DoneDate { get; set; }

        public MemoItem()
        {
            Text = "";
            DoneDate = "";
        }

        public MemoItem(string text)
        {
            Text = text == null ? "" : text;
            DoneDate = "";
        }
    }

    /// <summary>
    /// 常规备忘：week 里每天一条独立任务。
    /// Day = 0(周一) ... 6(周日)。每条有自己的内容、启用开关和完成日期。
    /// </summary>
    public class RoutineItem
    {
        public int Day { get; set; }
        public string Text { get; set; }
        public bool Enabled { get; set; }

        /// <summary>最近一次完成的日期 yyyy-MM-dd。</summary>
        public string LastDone { get; set; }

        /// <summary>旧版本遗留的星期掩码，只用于数据迁移，不再参与逻辑。</summary>
        public int DaysMask { get; set; }

        public RoutineItem()
        {
            Day = -1;          // -1 表示"旧格式，待迁移"
            Text = "";
            Enabled = true;
            LastDone = "";
            DaysMask = 0;
        }
    }

    /// <summary>远期备忘：只在指定日期当天显示。</summary>
    public class FutureItem
    {
        public string Text { get; set; }
        public string Date { get; set; }
        public bool Done { get; set; }

        /// <summary>完成日期 yyyy-MM-dd，只用于判断"今天是否做过事"。</summary>
        public string DoneDate { get; set; }

        public FutureItem()
        {
            Text = "";
            Date = "";
            DoneDate = "";
        }
    }

    public class Settings
    {
        public bool ShowCustom { get; set; }
        public bool ShowRoutine { get; set; }
        public bool ShowFuture { get; set; }
        public bool ScrollEnabled { get; set; }
        public int ScrollSeconds { get; set; }
        public bool AutoStart { get; set; }

        /// <summary>0 = 贴着任务栏显示；1 = 浮在托盘上方。</summary>
        public int Placement { get; set; }

        public Settings()
        {
            ShowCustom = true;
            ShowRoutine = true;
            ShowFuture = true;
            ScrollEnabled = false;
            ScrollSeconds = 10;
            AutoStart = false;
            Placement = 0;
        }
    }

    public class AppData
    {
        public Settings Settings { get; set; }
        public List<MemoItem> Customs { get; set; }
        public List<RoutineItem> Routines { get; set; }
        public List<FutureItem> Futures { get; set; }

        public AppData()
        {
            Settings = new Settings();
            Customs = new List<MemoItem>();
            Routines = new List<RoutineItem>();
            Futures = new List<FutureItem>();
        }
    }

    public static class DayUtil
    {
        public static readonly string[] Names = new string[]
        {
            "周一", "周二", "周三", "周四", "周五", "周六", "周日"
        };

        /// <summary>把 DayOfWeek 换算成 周一=0 ... 周日=6。</summary>
        public static int MondayIndex(DateTime d)
        {
            return ((int)d.DayOfWeek + 6) % 7;
        }

        public static string DateKey(DateTime d)
        {
            return d.ToString("yyyy-MM-dd");
        }

        public static bool TryParseDate(string s, out DateTime d)
        {
            d = DateTime.MinValue;
            if (s == null || s.Trim().Length == 0) return false;
            return DateTime.TryParseExact(s.Trim(), "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out d);
        }

        public static string DescribeDays(int mask)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < 7; i++)
            {
                if (((mask >> i) & 1) == 1)
                {
                    if (sb.Length > 0) sb.Append("、");
                    sb.Append(Names[i]);
                }
            }
            return sb.Length == 0 ? "未设置" : sb.ToString();
        }
    }
}
