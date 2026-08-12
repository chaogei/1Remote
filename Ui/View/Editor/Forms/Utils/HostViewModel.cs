using System.Collections.Generic;
using System.Linq;
using _1RM.Model.Protocol.Base;
using _1RM.Service;
using _1RM.Utils;
using _1RM.Utils.Proxy;

namespace _1RM.View.Editor.Forms.Utils;

public class HostViewModel : NotifyPropertyChangedBaseScreen
{
    public ProtocolBaseWithAddressPort New { get; }
    public HostViewModel(ProtocolBaseWithAddressPort protocol)
    {
        New = protocol;
    }

    /// <summary>
    /// The picker entries, with a leading "no proxy" item so the combo can be bound straight to
    /// <see cref="ProtocolBase.ProxyName"/> without a converter.
    /// </summary>
    public List<string> ProxyNames
    {
        get
        {
            var names = new List<string> { ProxyConfig.NO_PROXY };
            names.AddRange(IoC.Get<ProxyService>().Proxies
                .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                .Select(x => x.Name));
            // a server may still point at a proxy that has since been renamed or deleted; keep the stale
            // name selectable so opening the editor does not silently drop it
            if (!string.IsNullOrEmpty(New.ProxyName) && !names.Contains(New.ProxyName))
                names.Add(New.ProxyName);
            return names;
        }
    }
}
