using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Shawn.Utils;

namespace _1RM.Utils.Tracing
{
    internal static class UnifyTracing
    {
        /// <summary>
        /// Starts crash reporting off the startup path. Bringing the SDK up means loading its assemblies,
        /// JIT-ing them and opening a network connection, and this used to run before the first window was
        /// even constructed. The trade is a short window at launch where a crash goes unreported.
        /// </summary>
        public static void Init()
        {
            Task.Run(() =>
            {
                try
                {
                    SentryIoHelper.Init(Assert.SENTRY_IO_DEN);
                }
                catch (Exception e)
                {
                    SimpleLogHelper.Warning(e);
                }
            });
        }

        public static void Error(Exception e, IDictionary<string, string>? properties = null, Dictionary<string, string>? attachments = null)
        {
            SentryIoHelper.Error(e, properties, attachments);
        }

        public static void TraceSpecial(Dictionary<string, string> kys)
        {
            SentryIoHelper.TraceSpecial(kys);
        }
    }
}
