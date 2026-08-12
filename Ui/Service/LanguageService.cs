using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using _1RM.Model;
using _1RM.Utils;
using _1RM.Utils.Tracing;
using Shawn.Utils;
using Shawn.Utils.Interface;
using Shawn.Utils.Wpf;

namespace _1RM.Service
{
    public class MockLanguageService : ILanguageService
    {
        public void AddXamlLanguageResources(string code, string fullName)
        {
            return;
        }

        public string Translate(Enum e)
        {
            return e.ToString();
        }

        public string Translate(string key)
        {
            return key;
        }

        public string Translate(string key, params object[] parameters)
        {
            return key;
        }
    }

    public class LanguageService : ILanguageService
    {
        public const string NAME = "Name";
        public const string XXX_IS_ALREADY_EXISTED = "XXX is already existed!";
        public const string CAN_NOT_BE_EMPTY = "Can not be empty!";

        private const string FALLBACK_CODE = "en-us";

        private string _languageCode = FALLBACK_CODE;
        private readonly ResourceDictionary _applicationResourceDictionary;

        /// <summary>Parsed dictionaries, keyed by language code. Filled on demand, never up front.</summary>
        private readonly Dictionary<string, ResourceDictionary> _resources = new Dictionary<string, ResourceDictionary>();

        /// <summary>
        /// Codes that ship inside the assembly. Knowing a language exists is just a filename; it costs nothing
        /// and does not require reading the file.
        /// </summary>
        private readonly HashSet<string> _builtInCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Display names, filled in as dictionaries get parsed.</summary>
        private readonly Dictionary<string, string> _codeToName = new Dictionary<string, string>();

        private bool _allNamesResolved;

        /// <summary>
        /// code => language display name, all codes in lower case, ref https://en.wikipedia.org/wiki/Language_code
        ///
        /// Reading this parses every shipped language, because the display name is stored inside each file.
        /// Only the language picker needs the full list, and by the time it is on screen startup is long over.
        /// </summary>
        public Dictionary<string, string> LanguageCode2Name
        {
            get
            {
                if (!_allNamesResolved)
                {
                    foreach (var code in _builtInCodes)
                        GetOrLoad(code);
                    _allNamesResolved = true;
                }
                return _codeToName;
            }
        }


        public LanguageService(ResourceDictionary applicationResourceDictionary)
        {
            _applicationResourceDictionary = applicationResourceDictionary;
            foreach (var file in LanguagesResources.Files)
            {
                _builtInCodes.Add(ToCode(file));
            }
        }

        public void AddXamlLanguageResources(string code, string fullName)
        {
            var resourceDictionary = GetResourceDictionaryByXamlFilePath(fullName);
            if (resourceDictionary?.Contains("language_name") != true) return;
            AddLanguage(code, resourceDictionary["language_name"].ToString()!, resourceDictionary);
        }

        private static string ToCode(string fileName)
        {
            var code = fileName.ToLower();
            return code.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)
                ? code.Replace(".xaml", "")
                : code;
        }

        private ResourceDictionary? GetOrLoad(string code)
        {
            code = ToCode(code);
            if (_resources.TryGetValue(code, out var existed))
                return existed;
            if (!_builtInCodes.Contains(code))
                return null;

            var path = ResourceUriHelper.GetUriPathFromCurrentAssembly($"Resources/Languages/{code}.xaml");
            var r = GetResourceDictionaryByXamlUri(path);
            Debug.Assert(r != null);
            Debug.Assert(r?.Contains("language_name") == true);
            if (r == null) return null;
            AddLanguage(code, r["language_name"].ToString()!, r);
            return r;
        }

        private static readonly string[] Special_Marks_in_XAML_Content = { "&", "<", ">", "\r", "\n" };
        private static readonly string[] Special_Characters_in_XAML_Content = { "&amp;", "&lt;", "&gt;", "\\r", "\\n" };
        private static ResourceDictionary? GetResourceDictionaryByXamlUri(string path)
        {
            try
            {
                var resourceDictionary = MultiLanguageHelper.LangDictFromXamlUri(new Uri(path));
                if (resourceDictionary != null)
                {
                    foreach (var key in resourceDictionary.Keys)
                    {
                        if (resourceDictionary[key] is string val)
                        {
                            for (int j = 0; j < Special_Characters_in_XAML_Content.Length; j++)
                            {
                                val = val.Replace(Special_Characters_in_XAML_Content[j], Special_Marks_in_XAML_Content[j]);
                            }
                            resourceDictionary[key] = val;
                        }
                    }
                    return resourceDictionary;
                }
            }
            catch (Exception e)
            {
                SimpleLogHelper.Error(e);
            }
            return null;
        }

        private static ResourceDictionary? GetResourceDictionaryByXamlFilePath(string path)
        {
            Debug.Assert(path.EndsWith(".xaml", true, CultureInfo.InstalledUICulture));
            try
            {
                var resourceDictionary = MultiLanguageHelper.LangDictFromXamlFile(path);
                if (resourceDictionary != null)
                {
                    foreach (var key in resourceDictionary.Keys)
                    {
                        if (resourceDictionary[key] is string val)
                        {
                            for (int j = 0; j < Special_Characters_in_XAML_Content.Length; j++)
                            {
                                val = val.Replace(Special_Characters_in_XAML_Content[j], Special_Marks_in_XAML_Content[j]);
                            }
                            resourceDictionary[key] = val;
                        }
                    }
                    return resourceDictionary;
                }
            }
            catch (Exception e)
            {
                SimpleLogHelper.Error(e);
            }
            return null;
        }


        private void AddLanguage(string code, string name, ResourceDictionary resourceDictionary)
        {
            // not added to _builtInCodes: that set drives the on-demand load and is iterated while loading,
            // and an already parsed dictionary is found in _resources before the set is ever consulted
            _codeToName[code] = name;
            _resources[code] = resourceDictionary;
        }

        public bool SetLanguage(string code)
        {
            code = ToCode(code);
            var resource = GetOrLoad(code);
            if (resource == null)
                return false;

            _languageCode = code;

            var en = GetOrLoad(FALLBACK_CODE);
            if (en == null)
                return false;
            var missingFields = MultiLanguageHelper.FindMissingFields(en, resource);
            if (missingFields.Count > 0)
            {
                foreach (var field in missingFields)
                {
                    resource.Add(field, en[field]);
                }
#if DEBUG
                var mf = string.Join(", ", missingFields);
                MessageBox.Show($"language resource missing:\r\n {mf}", Translate("Error"), MessageBoxButton.OK, MessageBoxImage.Error, MessageBoxResult.None);
                File.WriteAllText("LANGUAGE_ERROR.txt", mf);
#endif
            }

            _applicationResourceDictionary.ChangeLanguage(resource);
            GlobalEventHelper.OnLanguageChanged?.Invoke();
            return true;
        }

        public string Translate(Enum e)
        {
            var key = e.GetType().Name + e;
            return Translate(key);
        }

        public string Translate(string key)
        {
            if (string.IsNullOrEmpty(key) || _applicationResourceDictionary == null)
                return "";
            else
            {
                string val = key;
                key = key.Trim(new[] { '\'' });
                if (_applicationResourceDictionary.Contains(key))
                {
                    val = _applicationResourceDictionary[key].ToString() ?? key;
                }
                else
                {
                    string message = "";
                    var stacktrace = new StackTrace();
                    for (var i = 0; i < stacktrace.FrameCount; i++)
                    {
                        var frame = stacktrace.GetFrame(i);
                        if (frame == null) continue;
                        message += frame.GetMethod() + " -> " + frame.GetFileName() + ": " + frame.GetFileLineNumber() + "\r\n";
                    }

                    UnifyTracing.Error(new Exception($"[Warning] In {_languageCode}, key not found: `{key}`"), new Dictionary<string, string>()
                    {
                        {"StackTrace", message}
                    });
#if DEBUG
                    var tw = new StreamWriter("need translation " + _languageCode + ".txt", true);
                    tw.WriteLine(key);
                    tw.Close();
#endif
                }
                return val;
            }
        }

        public string Translate(string key, params object[] parameters)
        {
            var format = Translate(key);
            if (string.IsNullOrEmpty(format))
                return "!" + key + (parameters.Length > 0 ? ":" + string.Join(",", parameters) : "") + "!";
            return string.Format(format, parameters);
        }
    }
}
