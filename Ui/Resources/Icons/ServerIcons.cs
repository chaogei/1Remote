using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Resources;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Shawn.Utils;
using Shawn.Utils.Wpf.Native;

namespace _1RM.Resources.Icons
{
    /// <summary>
    /// The built-in server icon library, read straight out of the assembly's resource set.
    ///
    /// This used to run at startup and, for each of the ~140 icons, decode the PNG, convert it to a GDI+
    /// Bitmap, convert that to a BitmapSource, convert it back to a Bitmap, re-encode it as a PNG and
    /// finally Base64 it — four pixel buffers and a fresh encode per icon, with none of the GDI+ bitmaps
    /// disposed. Now nothing happens until something actually asks for an icon, and the Base64 form is just
    /// the original resource bytes, which is both cheaper and lossless.
    /// </summary>
    public class ServerIcons
    {
        #region singleton

        private static ServerIcons? _uniqueInstance;
        public static ServerIcons Instance => _uniqueInstance ??= new ServerIcons();

        /// <summary>
        /// Optional warm-up. Only reads the raw bytes, no decoding, so it is cheap enough for startup.
        /// </summary>
        public static void Init()
        {
            var instance = Instance;
            Task.Run(() =>
            {
                try
                {
                    _ = instance._rawIcons.Value;
                }
                catch (Exception e)
                {
                    SimpleLogHelper.Warning($"ServerIcons: warm-up failed, {e.Message}");
                }
            });
        }

        #endregion singleton

        private readonly Lazy<IReadOnlyList<byte[]>> _rawIcons;
        private readonly Lazy<List<string>> _iconsBase64;
        private readonly Lazy<List<BitmapSource>> _icons;

        private ServerIcons()
        {
            _rawIcons = new Lazy<IReadOnlyList<byte[]>>(ReadRawIcons, LazyThreadSafetyMode.ExecutionAndPublication);
            _iconsBase64 = new Lazy<List<string>>(
                () => _rawIcons.Value.Select(Convert.ToBase64String).ToList(),
                LazyThreadSafetyMode.ExecutionAndPublication);
            _icons = new Lazy<List<BitmapSource>>(
                () => _rawIcons.Value.Select(Decode).Where(x => x != null).Select(x => x!).ToList(),
                LazyThreadSafetyMode.ExecutionAndPublication);
        }

        /// <summary>Base64 of the original PNG bytes — this is the form stored on a server record.</summary>
        public List<string> IconsBase64 => _iconsBase64.Value;

        /// <summary>Decoded and frozen, so they can be shared across threads and bound without copying.</summary>
        public List<BitmapSource> Icons => _icons.Value;

        private static IReadOnlyList<byte[]> ReadRawIcons()
        {
            var result = new List<byte[]>();
            try
            {
                var assembly = typeof(ServerIcons).Assembly;
                var mgr = new ResourceManager(assembly.GetName().Name + ".g", assembly);
                using var set = mgr.GetResourceSet(CultureInfo.CurrentCulture, true, true);
                if (set == null) return result;

                var keys = new List<string>();
                foreach (DictionaryEntry each in set)
                {
                    var key = each.Key.ToString()!;
                    if (key.IndexOf("resources/icons/", StringComparison.OrdinalIgnoreCase) >= 0
                        && string.Equals(Path.GetExtension(key), ".png", StringComparison.OrdinalIgnoreCase))
                    {
                        keys.Add(key);
                    }
                }

                var keyArray = keys.ToArray();
                Array.Sort(keyArray, NaturalCmpLogicalW.Get());
                foreach (var key in keyArray)
                {
                    if (set.GetObject(key, true) is not UnmanagedMemoryStream stream) continue;
                    var bytes = new byte[stream.Length];
                    var offset = 0;
                    while (offset < bytes.Length)
                    {
                        var read = stream.Read(bytes, offset, bytes.Length - offset);
                        if (read <= 0) break;
                        offset += read;
                    }
                    if (offset == bytes.Length)
                        result.Add(bytes);
                }
            }
            catch (Exception e)
            {
                SimpleLogHelper.Error($"ServerIcons: cannot read the icon resources, {e.Message}");
            }
            return result;
        }

        private static BitmapSource? Decode(byte[] bytes)
        {
            try
            {
                using var ms = new MemoryStream(bytes);
                // OnLoad so the stream can be closed straight away, Freeze so the result is thread-safe
                var frame = BitmapFrame.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                frame.Freeze();
                return frame;
            }
            catch (Exception e)
            {
                SimpleLogHelper.Warning($"ServerIcons: cannot decode an icon, {e.Message}");
                return null;
            }
        }
    }
}
