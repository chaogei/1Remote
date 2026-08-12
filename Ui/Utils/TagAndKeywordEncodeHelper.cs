using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using _1RM.Model;
using _1RM.Model.Protocol.Base;
using _1RM.Service;
using VariableKeywordMatcher.Model;

namespace _1RM.Utils
{
    /// <summary>
    /// Provides helper methods for encoding and decoding tags and keywords, as well as matching logic for server filtering.
    /// </summary>
    public static class TagAndKeywordEncodeHelper
    {
        /// <summary>
        /// Normalizes a tag name by removing '#' characters, replacing spaces with '-', trimming, and converting to lower case.
        /// </summary>
        /// <param name="name">The tag name to rectify.</param>
        /// <returns>The rectified tag name.</returns>
        public static string RectifyTagName(string? name)
        {
            return name?.Replace("#", "").Replace(" ", "-").Trim().ToLower() ?? "";
        }

        /// <summary>
        /// Represents the result of decoding a keyword string, including tag filters, partial tag matches, and general keywords.
        /// </summary>
        public class KeywordDecoded
        {
            /// <summary>
            /// List of tag filters, supporting both included and excluded tags.
            /// </summary>
            public List<TagFilter> TagFilterList = new List<TagFilter>();

            /// <summary>
            /// List of tag names that start with a partial keyword entered by the user. For example, typing '#lin' will match all tags starting with 'lin'.
            /// </summary>
            public List<string> IncludeTagsStartWithKeyWord = new List<string>();

            /// <summary>
            /// List of keywords to match against server properties such as name, address, or username.
            /// </summary>
            public List<string> KeyWords = new List<string>();

            /// <summary>
            /// Protocol names typed as <c>proto:ssh</c>. Matched as a prefix, so <c>proto:s</c> keeps both
            /// SSH and SFTP - narrowing by protocol is usually a first pass, not an exact recall.
            /// </summary>
            public List<string> ProtocolFilters = new List<string>();

            /// <summary>Data source names typed as <c>src:team</c>, matched the same way.</summary>
            public List<string> DataSourceFilters = new List<string>();

            /// <summary>
            /// Determines whether all keyword-related lists are empty.
            /// </summary>
            /// <returns>True if all lists are empty; otherwise, false.</returns>
            public bool IsKeywordEmpty()
            {
                return IncludeTagsStartWithKeyWord.Any() != true
                       && TagFilterList.Any() != true
                       && KeyWords.Any() != true
                       && ProtocolFilters.Any() != true
                       && DataSourceFilters.Any() != true;
            }
        }

        /// <summary>
        /// Decodes a keyword string into tag filters, partial tag matches, and general keywords.
        /// </summary>
        /// <param name="keyword">The keyword string to decode.</param>
        /// <returns>A <see cref="KeywordDecoded"/> object containing the parsed results.</returns>
        /// <summary>Typed filter prefixes, e.g. <c>proto:rdp</c> or <c>src:team</c>.</summary>
        private const string PROTOCOL_PREFIX = "proto:";
        private const string SOURCE_PREFIX = "src:";

        public static KeywordDecoded DecodeKeyword(string keyword)
        {
            var words = keyword.Split(new string[] { " " }, StringSplitOptions.RemoveEmptyEntries).ToList();
            var ret = new KeywordDecoded();
            for (var i = 0; i < words.Count; i++)
            {
                var word = words[i];
                if (word.StartsWith("#") || word.StartsWith("-#") || word.StartsWith("+#"))
                {
                    bool isExcluded = word.StartsWith("-#");
                    string tagName = word.Substring(word.IndexOf("#", StringComparison.Ordinal) + 1);
                    if (string.IsNullOrWhiteSpace(tagName))
                        continue;

                    // If the tag name matches a complete tag, add to filter list.
                    if (IoC.Get<GlobalData>().TagList.Any(x => string.Equals(x.Name, tagName, StringComparison.CurrentCultureIgnoreCase)))
                    {
                        ret.TagFilterList.Add(TagFilter.Create(tagName, isExcluded ? TagFilter.FilterType.Excluded : TagFilter.FilterType.Included));
                    }
                    // If the tag name is incomplete, add all matching tags to the partial match list (only for included tags).
                    else if (isExcluded == false)
                    {
                        var tmp = IoC.Get<GlobalData>().TagList.Where(x => x.Name.StartsWith(tagName, StringComparison.OrdinalIgnoreCase)).Select(x => x.Name);
                        ret.IncludeTagsStartWithKeyWord.AddRange(tmp);
                    }
                }
                else if (word.StartsWith(PROTOCOL_PREFIX, StringComparison.OrdinalIgnoreCase))
                {
                    var value = word.Substring(PROTOCOL_PREFIX.Length);
                    if (!string.IsNullOrWhiteSpace(value)) ret.ProtocolFilters.Add(value);
                }
                else if (word.StartsWith(SOURCE_PREFIX, StringComparison.OrdinalIgnoreCase))
                {
                    var value = word.Substring(SOURCE_PREFIX.Length);
                    if (!string.IsNullOrWhiteSpace(value)) ret.DataSourceFilters.Add(value);
                }
                else if (string.IsNullOrEmpty(word) == false)
                {
                    ret.KeyWords.Add(word);
                }
            }

            ret.IncludeTagsStartWithKeyWord = ret.IncludeTagsStartWithKeyWord.Distinct().ToList();

            return ret;
        }

        /// <summary>
        /// Encodes tag filters and keywords into a single keyword string.
        /// </summary>
        /// <param name="tagFilters">A list of tag filters to encode.</param>
        /// <param name="keyWords">A list of keywords to encode.</param>
        /// <returns>The encoded keyword string.</returns>
        public static string EncodeKeyword(List<TagFilter>? tagFilters = null, List<string>? keyWords = null)
        {
            StringBuilder sb = new StringBuilder();
            if (tagFilters != null)
                foreach (var tag in tagFilters.Distinct())
                {
                    if (tag.IsExcluded)
                        sb.Append(" -#" + tag.TagName);
                    else
                        sb.Append(" #" + tag.TagName);
                }

            if (keyWords != null)
                foreach (var keyWord in keyWords.Distinct())
                {
                    sb.Append(" " + keyWord);
                }

            var filterString = sb.ToString().Trim();
            if (string.IsNullOrWhiteSpace(filterString))
                return "";
            return filterString + (keyWords?.Count > 0 ? "" : " ");
        }

        /// <summary>
        /// Determines whether a server matches the specified decoded keywords and tag filters.
        /// </summary>
        /// <param name="server">The server to match.</param>
        /// <param name="keywordDecoded">The decoded keyword object containing filters and keywords.</param>
        /// <param name="matchSubTitle">Whether to include the server's subtitle in the keyword match.</param>
        /// <returns>A tuple: Item1 is true if matched, false otherwise; Item2 is a <see cref="MatchResults"/> object for highlighting, or null if no highlight.</returns>
        public static Tuple<bool, MatchResults?> MatchKeywords(ProtocolBase server, KeywordDecoded keywordDecoded, bool matchSubTitle)
        {
            if (keywordDecoded.IsKeywordEmpty())
            {
                return new Tuple<bool, MatchResults?>(true, null);
            }

            // Check included and excluded tags.
            if (keywordDecoded.TagFilterList.Any())
            {
                bool bTagMatched = keywordDecoded.TagFilterList.All(tagFilter => tagFilter.IsIncluded == server.Tags.Any(x => String.Equals(x, tagFilter.TagName, StringComparison.CurrentCultureIgnoreCase)));
                if (bTagMatched == false)
                {
                    return new Tuple<bool, MatchResults?>(false, null);
                }
            }

            // Check tag names that start with a partial keyword from user input.
            if (keywordDecoded.IncludeTagsStartWithKeyWord.Any())
            {
                bool bTagMatched = keywordDecoded.IncludeTagsStartWithKeyWord.Any(tagName => server.Tags.Any(x => String.Equals(x, tagName, StringComparison.CurrentCultureIgnoreCase)));
                if (bTagMatched == false)
                {
                    return new Tuple<bool, MatchResults?>(false, null);
                }
            }

            // Typed filters: proto: and src:
            if (keywordDecoded.ProtocolFilters.Any()
                && !keywordDecoded.ProtocolFilters.Any(x => server.Protocol.StartsWith(x, StringComparison.OrdinalIgnoreCase)
                                                            || server.ProtocolDisplayName.StartsWith(x, StringComparison.OrdinalIgnoreCase)))
            {
                return new Tuple<bool, MatchResults?>(false, null);
            }

            if (keywordDecoded.DataSourceFilters.Any())
            {
                var sourceName = server.DataSource?.DataSourceName ?? "";
                if (!keywordDecoded.DataSourceFilters.Any(x => sourceName.StartsWith(x, StringComparison.OrdinalIgnoreCase)))
                {
                    return new Tuple<bool, MatchResults?>(false, null);
                }
            }

            // If there are no keywords, return match.
            if (keywordDecoded.KeyWords.Any() != true)
            {
                return new Tuple<bool, MatchResults?>(true, null);
            }

            // The first two positions are load bearing: the launcher reads HitFlags[0] and HitFlags[1] to
            // highlight the title and the subtitle. Everything after them is searched but not highlighted,
            // which is the right treatment for a note or a username - they are not on screen to light up.
            var fields = new List<string>(6)
            {
                server.DisplayName,
                matchSubTitle ? server.SubTitle : "",
                server.ProtocolDisplayName,
                server.Note,
                server is ProtocolBaseWithAddressPortUserPwd withUser ? withUser.UserName : "",
                server.TreeNodes.Count > 0 ? string.Join(" ", server.TreeNodes) : "",
            };
            var mrs = IoC.Get<KeywordMatchService>().Match(fields, keywordDecoded.KeyWords);
            if (mrs.IsMatchAllKeywords)
                return new Tuple<bool, MatchResults?>(true, mrs);
            return new Tuple<bool, MatchResults?>(false, null);
        }

        /// <summary>
        /// Matches a list of servers against a keyword string.
        /// </summary>
        /// <param name="servers">The list of servers to match.</param>
        /// <param name="keyword">The keyword string to decode and match.</param>
        /// <param name="matchSubTitle">Whether to include the server's subtitle in the keyword match.</param>
        /// <returns>A list of tuples indicating match results for each server.</returns>
        public static List<Tuple<bool, MatchResults?>> MatchKeywords(List<ProtocolBase> servers, string keyword, bool matchSubTitle)
        {
            var tmp = TagAndKeywordEncodeHelper.DecodeKeyword(keyword);
            return MatchKeywords(servers, tmp, matchSubTitle);
        }

        /// <summary>
        /// Matches a list of servers against a decoded keyword object.
        /// </summary>
        /// <param name="servers">The list of servers to match.</param>
        /// <param name="keywordDecoded">The decoded keyword object containing filters and keywords.</param>
        /// <param name="matchSubTitle">Whether to include the server's subtitle in the keyword match.</param>
        /// <returns>A list of tuples indicating match results for each server.</returns>
        public static List<Tuple<bool, MatchResults?>> MatchKeywords(List<ProtocolBase> servers, KeywordDecoded keywordDecoded, bool matchSubTitle)
        {
            if (keywordDecoded.IsKeywordEmpty() || servers.Count == 0)
            {
                return servers.Select(_ => new Tuple<bool, MatchResults?>(true, null)).ToList();
            }
            // Capped at the core count. servers.Count / 10 meant 200 tasks for 2000 servers, which just
            // saturates the thread pool and pays its injection delay for no extra parallelism.
            var taskCount = Math.Max(2, Math.Min(Environment.ProcessorCount, servers.Count));
            var results = new ConcurrentDictionary<int, Tuple<bool, MatchResults?>>();
            // The index travels with the item: servers.IndexOf(cache) inside the loop made the whole pass
            // O(N^2).
            var caches = new ConcurrentQueue<KeyValuePair<int, ProtocolBase>>(
                servers.Select((server, index) => new KeyValuePair<int, ProtocolBase>(index, server)));
            var tasks = new List<Task>();
            for (var i = 0; i < taskCount; i++)
            {
                tasks.Add(Task.Run(() =>
                {
                    while (caches.TryDequeue(out var cache))
                    {
                        var r = MatchKeywords(cache.Value, keywordDecoded, matchSubTitle);
                        results.TryAdd(cache.Key, r);
                    }
                }));
            }
            // Wait for all tasks to finish.
            Task.WaitAll(tasks.ToArray());

            // Return the results in the original order.
            return results.OrderBy(x => x.Key).Select(r => r.Value).ToList();
        }
    }
}
