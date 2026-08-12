using System;
using _1RM.Service.Locality;
using Shawn.Utils;
using System.Collections.Generic;
using System.Linq;

namespace _1RM.Model
{
    public partial class GlobalData : NotifyPropertyChangedBase
    {

        private List<Tag> _tagList = new List<Tag>();
        public List<Tag> TagList
        {
            get => _tagList;
            private set => SetAndNotifyIfChanged(ref _tagList, value);
        }


        /// <summary>
        /// Reload tags from servers, this will read all servers and update the TagList.
        /// </summary>
        public void ReloadTagsFromServers()
        {
            // Get distinct tags from servers. Keyed lookup rather than scanning the accumulated list twice
            // per occurrence: with a few hundred distinct tags across a large server list that scan was
            // hundreds of thousands of culture-sensitive comparisons, each allocating a lowercased copy.
            var tags = new Dictionary<string, Tag>(StringComparer.OrdinalIgnoreCase);
            LocalityTagService.Load();
            foreach (var tagNames in VmItemList.Select(x => x.Server.Tags))
            {
                foreach (var tagName in tagNames)
                {
                    var tn = tagName.Trim().ToLower();
                    if (tags.TryGetValue(tn, out var existed))
                    {
                        existed.ItemsCount++;
                        continue;
                    }
                    bool isPinned = LocalityTagService.GetIsPinned(tn);
                    int customOrder = LocalityTagService.GetCustomOrder(tn);
                    tags.Add(tn, new Tag(tn, isPinned, customOrder) { ItemsCount = 1 });
                }
            }

            TagList = tags.Values.OrderBy(x => x.CustomOrder).ThenBy(x => x.Name).ToList();
            foreach (var viewModel in VmItemList.Where(viewModel => viewModel.Server.Tags.Count > 0))
            {
                viewModel.ReLoadTags();
            }
        }
    }
}