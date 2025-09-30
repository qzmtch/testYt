using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace YtDlpGui.Wpf.Utils
{
    public static class ProgressLineParser
    {
        static readonly Regex Rx = new Regex(@"```math
download```\s+(?<p>\d{1,3}(?:\.\d+)?)%",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static (double percent, string line) TryParse(string line)
        {
            var m = Rx.Match(line ?? "");
            if (!m.Success) return (double.NaN, line);
            if (double.TryParse(m.Groups["p"].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var p))
                return (Math.Max(0, Math.Min(100, p)), line);
            return (double.NaN, line);
        }
    }
}
