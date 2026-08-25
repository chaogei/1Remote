using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using _1RM.Model;
using _1RM.Model.Protocol;
using _1RM.Service.DataSource;
using _1RM.Utils;
using _1RM.Utils.Tracing;
using _1RM.View;
using _1RM.View.Launcher;
using Newtonsoft.Json;
using Shawn.Utils;

namespace _1RM.Service.Locality
{
    public enum EnumServerOrderBy
    {
        IdAsc = -1,
        ProtocolAsc = 0,
        ProtocolDesc = 1,
        NameAsc = 2,
        NameDesc = 3,

        //TagAsc = 4,
        //TagDesc = 5,
        AddressAsc = 6,
        AddressDesc = 7,

        /// <summary>
        /// Most recently connected first. The timestamps were already being recorded and used by the tray
        /// menu; the main list simply had no way to order by them.
        /// </summary>
        LastConnectTimeDesc = 8,

        Custom = 999,
    }

    internal class LocalitySettings
    {
        public double MainWindowTop = double.NaN;
        public double MainWindowLeft = double.NaN;
        public double MainWindowWidth = 800;
        public double MainWindowHeight = 530;
        public WindowState MainWindowState = WindowState.Normal;
        public double TabWindowTop = -1;
        public double TabWindowLeft = -1;
        public double TabWindowWidth = 800;
        public double TabWindowHeight = 600;
        public WindowState TabWindowState = WindowState.Normal;
        public WindowStyle TabWindowStyle = WindowStyle.SingleBorderWindow;
        public int FtpColumnFileNameLength = -1;
        public int FtpColumnFileTimeLength = -1;
        public int FtpColumnFileTypeLength = -1;
        public int FtpColumnFileSizeLength = -1;
        public Dictionary<string, string> Misc = new Dictionary<string, string>();
    }

    public sealed class LocalityService
    {
        /// <summary>
        /// How long a change waits for the ones behind it. Long enough that a drag or a resize produces one
        /// write instead of hundreds, short enough that the file is current again before the user can do
        /// anything else with it.
        /// </summary>
        private const int SAVE_DEBOUNCE_MS = 500;

        private readonly LocalitySettings _localitySettings;
        public static string JsonPath => Path.Combine(AppPathHelper.Instance.LocalityDirPath, ".locality.json");
        public bool CanSave = true;

        private readonly System.Timers.Timer _saveTimer;
        /// <summary>Guards <see cref="_pendingJson"/> and the timer. Never held across the disk write.</summary>
        private readonly object _pendingLock = new object();
        /// <summary>Guards the file itself, so the debounce tick and the shutdown flush cannot interleave.</summary>
        private readonly object _writeLock = new object();
        private string? _pendingJson;

        /// <summary>
        /// Records the change and lets the disk write happen a moment later, off the caller's thread.
        ///
        /// Dragging the main window raises LocationChanged for every mouse move, and resizing raises
        /// SizeChanged just as often; both land here. Writing the file inline meant the dispatcher doing a
        /// full open-write-close per mouse move — unnoticeable on a local SSD, and not unnoticeable at all
        /// on a synced folder or one an on-access scanner is watching, where RetryHelper then sleeps
        /// between attempts on the UI thread. The document is serialised here, on the caller's thread, so
        /// the settings are still only ever read by the thread that owns them.
        /// </summary>
        private void Save()
        {
            if (!CanSave) return;

            string json;
            try
            {
                json = JsonConvert.SerializeObject(this._localitySettings, Formatting.Indented);
            }
            catch (Exception e)
            {
                UnifyTracing.Error(e);
                return;
            }

            lock (_pendingLock)
            {
                _pendingJson = json;
                // restart the window: the change after this one is probably microseconds away
                _saveTimer.Stop();
                _saveTimer.Start();
            }
        }

        /// <summary>
        /// Writes whatever is pending, now, on the calling thread. Runs on the debounce tick and once more
        /// at shutdown so a change made in the last half second still reaches disk.
        /// </summary>
        public void Flush()
        {
            string? json;
            lock (_pendingLock)
            {
                _saveTimer.Stop();
                json = _pendingJson;
                _pendingJson = null;
            }
            if (json == null) return;

            lock (_writeLock)
            {
                try
                {
                    AppPathHelper.CreateDirIfNotExist(AppPathHelper.Instance.LocalityDirPath, false);
                    RetryHelper.Try(() => { File.WriteAllText(JsonPath, json, Encoding.UTF8); },
                        actionOnError: exception => UnifyTracing.Error(exception));
                }
                catch (Exception e)
                {
                    UnifyTracing.Error(e);
                }
            }
        }

        public LocalityService()
        {
            // Load
            _localitySettings = new LocalitySettings();
            try
            {
                var tmp = JsonConvert.DeserializeObject<LocalitySettings>(File.ReadAllText(JsonPath));
                if (tmp != null)
                    _localitySettings = tmp;
            }
            catch
            {
                // ignored
            }

            _saveTimer = new System.Timers.Timer(SAVE_DEBOUNCE_MS) { AutoReset = false };
            _saveTimer.Elapsed += (_, _) =>
            {
                try
                {
                    Flush();
                }
                catch (Exception e)
                {
                    // an escaping exception on a timer thread would take the process down
                    UnifyTracing.Error(e);
                }
            };
        }


        public double MainWindowTop
        {
            get => _localitySettings.MainWindowTop;
            set
            {
                if (double.IsNaN(_localitySettings.MainWindowTop) || Math.Abs(_localitySettings.MainWindowTop - value) > 0.001)
                {
                    _localitySettings.MainWindowTop = value;
                    Save();
                }
            }
        }

        public double MainWindowLeft
        {
            get => _localitySettings.MainWindowLeft;
            set
            {
                if (double.IsNaN(_localitySettings.MainWindowLeft) || Math.Abs(_localitySettings.MainWindowLeft - value) > 0.001)
                {
                    _localitySettings.MainWindowLeft = value;
                    Save();
                }
            }
        }

        public double MainWindowWidth
        {
            get => _localitySettings.MainWindowWidth;
            set
            {
                if (Math.Abs(_localitySettings.MainWindowWidth - value) > 0.001)
                {
                    _localitySettings.MainWindowWidth = value;
                    Save();
                }
            }
        }

        public double MainWindowHeight
        {
            get => _localitySettings.MainWindowHeight;
            set
            {
                if (Math.Abs(_localitySettings.MainWindowHeight - value) > 0.001)
                {
                    _localitySettings.MainWindowHeight = value;
                    Save();
                }
            }
        }

        public WindowState MainWindowState
        {
            get => _localitySettings.MainWindowState;
            set
            {
                if (_localitySettings.MainWindowState != value)
                {
                    _localitySettings.MainWindowState = value;
                    Save();
                }
            }
        }

        public double TabWindowTop
        {
            get => _localitySettings.TabWindowTop;
            set
            {
                if (Math.Abs(_localitySettings.TabWindowTop - value) > 0.001)
                {
                    _localitySettings.TabWindowTop = value;
                    Save();
                }
            }
        }

        public double TabWindowLeft
        {
            get => _localitySettings.TabWindowLeft;
            set
            {
                if (Math.Abs(_localitySettings.TabWindowLeft - value) > 0.001)
                {
                    _localitySettings.TabWindowLeft = value;
                    Save();
                }
            }
        }

        public double TabWindowWidth
        {
            get => _localitySettings.TabWindowWidth;
            set
            {
                if (Math.Abs(_localitySettings.TabWindowWidth - value) > 0.001)
                {
                    _localitySettings.TabWindowWidth = value;
                    Save();
                }
            }
        }

        public double TabWindowHeight
        {
            get => _localitySettings.TabWindowHeight;
            set
            {
                if (Math.Abs(_localitySettings.TabWindowHeight - value) > 0.001)
                {
                    _localitySettings.TabWindowHeight = value;
                    Save();
                }
            }
        }

        public WindowState TabWindowState
        {
            get => _localitySettings.TabWindowState;
            set
            {
                if (_localitySettings.TabWindowState != value)
                {
                    _localitySettings.TabWindowState = value;
                    Save();
                }
            }
        }

        public WindowStyle TabWindowStyle
        {
            get => _localitySettings.TabWindowStyle;
            set
            {
                if (_localitySettings.TabWindowStyle != value)
                {
                    _localitySettings.TabWindowStyle = value;
                    Save();
                }
            }
        }

        public int FtpColumnFileNameLength
        {
            get => _localitySettings.FtpColumnFileNameLength;
            set
            {
                if (_localitySettings.FtpColumnFileNameLength != value)
                {
                    _localitySettings.FtpColumnFileNameLength = value;
                    Save();
                }
            }
        }

        public int FtpColumnFileTimeLength
        {
            get => _localitySettings.FtpColumnFileTimeLength;
            set
            {
                if (_localitySettings.FtpColumnFileTimeLength != value)
                {
                    _localitySettings.FtpColumnFileTimeLength = value;
                    Save();
                }
            }
        }

        public int FtpColumnFileTypeLength
        {
            get => _localitySettings.FtpColumnFileTypeLength;
            set
            {
                if (_localitySettings.FtpColumnFileTypeLength != value)
                {
                    _localitySettings.FtpColumnFileTypeLength = value;
                    Save();
                }
            }
        }
        public int FtpColumnFileSizeLength
        {
            get => _localitySettings.FtpColumnFileSizeLength;
            set
            {
                if (_localitySettings.FtpColumnFileSizeLength != value)
                {
                    _localitySettings.FtpColumnFileSizeLength = value;
                    Save();
                }
            }
        }

        public void SetMisc(string key, string value)
        {
            if (_localitySettings.Misc.ContainsKey(key))
            {
                if (_localitySettings.Misc[key] == value) return;
                _localitySettings.Misc[key] = value;
            }
            else
            {
                _localitySettings.Misc.Add(key, value);
            }
            Save();
        }

        
        public T GetMisc<T>(string key, T defaultValue = default!)
        {
            var value = _localitySettings.Misc.ContainsKey(key) ? _localitySettings.Misc[key] : "";
            if (typeof(T) == typeof(int))
            {
                return int.TryParse(value, out var result) ? (T)(object)result : defaultValue;
            }
            if (typeof(T) == typeof(bool))
            {
                return bool.TryParse(value, out var result) ? (T)(object)result : defaultValue;
            }
            if (typeof(T) == typeof(float))
            {
                return float.TryParse(value, out var result) ? (T)(object)result : defaultValue;
            }
            if (typeof(T) == typeof(double))
            {
                return double.TryParse(value, out var result) ? (T)(object)result : defaultValue;
            }
            if (typeof(T) == typeof(string))
            {
                return (T)(object)value;
            }
            throw new NotSupportedException($"Not support type {typeof(T)}");
        }
    }
}