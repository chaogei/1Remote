using Newtonsoft.Json;
using Shawn.Utils;

namespace _1RM.Utils.SessionInput
{
    /// <summary>
    /// A saved command, so the things typed into a dozen servers a week are typed once.
    /// </summary>
    public class CommandSnippet : NotifyPropertyChangedBase
    {
        private string _name = "";
        public string Name
        {
            get => _name;
            set
            {
                if (SetAndNotifyIfChanged(ref _name, value.Length > 64 ? value.Substring(0, 64) : value))
                    RaisePropertyChanged(nameof(DisplayName));
            }
        }

        private string _content = "";
        public string Content
        {
            get => _content;
            set
            {
                if (SetAndNotifyIfChanged(ref _content, value ?? ""))
                {
                    RaisePropertyChanged(nameof(Preview));
                    RaisePropertyChanged(nameof(DisplayName));
                }
            }
        }

        private bool _appendEnter = true;
        /// <summary>
        /// Whether to press Enter after it. Off is for the case where the snippet is the start of a command
        /// the user means to finish by hand.
        /// </summary>
        public bool AppendEnter
        {
            get => _appendEnter;
            set => SetAndNotifyIfChanged(ref _appendEnter, value);
        }

        /// <summary>An unnamed snippet is listed by its command, which is better than an empty row.</summary>
        [JsonIgnore]
        public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Preview : Name;

        /// <summary>First line only, shortened — the list is scanned, not read.</summary>
        [JsonIgnore]
        public string Preview
        {
            get
            {
                var firstLine = (Content ?? "").Replace("\r\n", "\n").Split('\n')[0].Trim();
                if (firstLine.Length <= 60) return firstLine;
                return firstLine.Substring(0, 57) + "...";
            }
        }

        public CommandSnippet CloneMe() => (CommandSnippet)MemberwiseClone();
    }
}
