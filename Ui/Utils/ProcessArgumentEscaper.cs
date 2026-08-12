using System.Text;

namespace _1RM.Utils
{
    /// <summary>
    /// Quotes a single value so that <c>CommandLineToArgvW</c> — which is what every Windows process uses to
    /// split <c>ProcessStartInfo.Arguments</c> back into argv — hands it to the child as exactly one
    /// argument.
    ///
    /// Without this, a password containing a space silently becomes two arguments, and a user name of
    /// <c>root -proxycmd calc.exe</c> turns into a real <c>-proxycmd</c> switch that PuTTY will happily
    /// execute. Values arrive from shared databases and from mRemoteNG/CSV imports, so they are not all
    /// typed by the person sitting at the machine.
    ///
    /// net48 has no ProcessStartInfo.ArgumentList, which is why this is done by hand.
    /// </summary>
    public static class ProcessArgumentEscaper
    {
        private static readonly char[] MustQuote = { ' ', '\t', '\n', '\v', '"' };

        public static string Escape(string? argument)
        {
            argument ??= "";
            if (argument.Length > 0 && argument.IndexOfAny(MustQuote) < 0)
                return argument;

            var sb = new StringBuilder(argument.Length + 2);
            sb.Append('"');
            var i = 0;
            while (i < argument.Length)
            {
                // A run of backslashes is only special when it precedes a quote, or ends the argument right
                // before the closing quote we add. In both cases it has to be doubled.
                var backslashes = 0;
                while (i < argument.Length && argument[i] == '\\')
                {
                    backslashes++;
                    i++;
                }

                if (i == argument.Length)
                {
                    sb.Append('\\', backslashes * 2);
                }
                else if (argument[i] == '"')
                {
                    sb.Append('\\', backslashes * 2 + 1);
                    sb.Append('"');
                    i++;
                }
                else
                {
                    sb.Append('\\', backslashes);
                    sb.Append(argument[i]);
                    i++;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}
