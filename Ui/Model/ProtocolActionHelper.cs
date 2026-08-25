using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using _1RM.Model.Protocol;
using _1RM.Model.Protocol.Base;
using _1RM.Model.ProtocolRunner;
using _1RM.Model.ProtocolRunner.Default;
using _1RM.Service;
using _1RM.Utils;
using _1RM.View;
using Shawn.Utils.Interface;
using Shawn.Utils.Wpf.FileSystem;

namespace _1RM.Model;

public static class ProtocolActionHelper
{
    public static List<ProtocolAction> GetActions(this ProtocolBase server, bool forTabHeader = false)
    {
        bool writable = server.DataSource?.IsWritable != false;
        #region Build Actions
        var actions = new List<ProtocolAction>();
        {
            if (!forTabHeader)
            {
                if (IoC.Get<SessionControlService>().TabWindowCount > 0)
                {
                    actions.Add(new ProtocolAction(
                        actionName: IoC.Translate("Connect (New window)"),
                        action: () => { GlobalEventHelper.OnRequestServerConnect?.Invoke(server, fromView: $"{nameof(LauncherWindowView)} - Action - New window", assignTabToken: DateTime.Now.Ticks.ToString()); }
                    ));
                }

                if (server is ProtocolBaseWithAddressPortUserPwd { AlternateCredentials.Count: > 0 } protocol)
                {
                    foreach (var credential in protocol.AlternateCredentials)
                    {
                        actions.Add(new ProtocolAction(
                            actionName: IoC.Translate("Connect") + $" ({IoC.Translate("with alternative")} `{credential.Name}`)",
                            action: () => { GlobalEventHelper.OnRequestServerConnect?.Invoke(server, fromView: $"{nameof(LauncherWindowView)} - Action - AlternateCredentials", assignCredentialName: credential.Name); }
                        ));
                    }
                }

                // external runners
                var protocolConfigurationService = IoC.Get<ProtocolConfigurationService>();
                if (protocolConfigurationService.ProtocolConfigs.ContainsKey(server.Protocol)
                    && protocolConfigurationService.ProtocolConfigs[server.Protocol].Runners.Count > 1)
                {
                    //actions.Add(new ProtocolAction(IoC.Translate("Connect") + $" (Internal)", () => { GlobalEventHelper.OnRequestServerConnect?.Invoke(server.Id, assignRunnerName: protocolConfigurationService.ProtocolConfigs[server.Protocol].Runners.First().Name, fromView: nameof(LauncherWindowView)); }));
                    foreach (var runner in protocolConfigurationService.ProtocolConfigs[server.Protocol].Runners)
                    {
                        if (runner is InternalDefaultRunner) continue;
                        if (runner is ExternalRunner { IsExeExisted: false }) continue;
                        actions.Add(new ProtocolAction(IoC.Translate("Connect") + $" (via {runner.Name})", () =>
                        {
                            GlobalEventHelper.OnRequestServerConnect?.Invoke(server, fromView: $"{nameof(LauncherWindowView)} - Action - {runner.Name}", assignRunnerName: runner.Name);
                        }));
                    }
                }
            }

            if (writable)
            {
                actions.Add(new ProtocolAction(IoC.Translate("Edit"), () =>
                {
                    GlobalEventHelper.OnRequestGoToServerEditPage?.Invoke(server: server, showAnimation: false);
                }));
                actions.Add(new ProtocolAction(IoC.Translate("Duplicate"), () =>
                {
                    GlobalEventHelper.OnRequestGoToServerDuplicatePage?.Invoke(server: server, showAnimation: false);
                }));
                if (!forTabHeader)
                {
                    actions.Add(new ProtocolAction(IoC.Get<ILanguageService>().Translate("Delete"), () =>
                    {
                        if (true == MessageBoxHelper.Confirm(IoC.Translate("confirm_to_delete_selected"), ownerViewModel: IoC.Get<MainWindowViewModel>()))
                        {
                            IoC.Get<GlobalData>().DeleteServer(new[] { server });
                        }
                    }));
                }
            }
        };


        if (server is ProtocolBaseWithAddressPort protocolServerWithAddrPortBase)
        {
            actions.Add(new ProtocolAction(IoC.Translate("server_card_operate_copy_address"),
                () =>
                {
                    try
                    {
                        Clipboard.SetDataObject(
                            IoC.TryGet<ConfigurationService>()?.General.CopyPortWhenCopyAddress == false
                                ? $"{protocolServerWithAddrPortBase.RealAddress}"
                                : $"{protocolServerWithAddrPortBase.RealAddress}:{protocolServerWithAddrPortBase.RealPort}");
                    }
                    catch (Exception)
                    {
                        // ignored
                    }
                }));
        }


        if (writable)
        {
            if (server is ProtocolBaseWithAddressPortUserPwd tmp)
            {
                actions.Add(new ProtocolAction(IoC.Translate("server_card_operate_copy_username"),
                    () =>
                    {
                        try
                        {
                            Clipboard.SetDataObject(tmp.UserName);
                        }
                        catch (Exception)
                        {
                            // ignored
                        }
                    }));
            }

            if (server is ProtocolBaseWithAddressPortUserPwd protocolServerWithAddrPortUserPwdBase)
            {
                actions.Add(new ProtocolAction(IoC.Translate("server_card_operate_copy_password"),
                    action: async () =>
                    {
                        if (await SecondaryVerificationHelper.VerifyAsyncUi() != true) return;

                        try
                        {
                            Clipboard.SetDataObject(UnSafeStringEncipher.DecryptOrReturnOriginalString(protocolServerWithAddrPortUserPwdBase.Password));
                        }
                        catch (Exception)
                        {
                            // ignored
                        }
                    }));
            }
        }


        actions.Add(new ProtocolAction(IoC.Translate("Create desktop shortcut"), () =>
        {
            var iconPath = AppStartupHelper.MakeIcon(server.Id, server.IconImg);
            AppStartupHelper.InstallDesktopShortcutByUlid(server.DisplayName, new[] { server.Id }, iconPath);
        }));


        if (server is SSH ssh)
        {
            actions.Add(new ProtocolAction(IoC.Translate("Open SFTP"), () =>
            {
                // open SFTP when SSH is connected.
                var sftp = new SFTP
                {
                    ColorHex = ssh.ColorHex,
                    IconBase64 = ssh.IconBase64,
                    DisplayName = ssh.DisplayName + " (SFTP)",
                    Address = ssh.Address,
                    Port = ssh.Port,
                    UserName = ssh.UserName,
                    Password = ssh.Password,
                    PrivateKey = ssh.PrivateKey,
                    // this is built from the stored server, so it still holds the real address and has to
                    // take the same route to it as the SSH session would
                    ProxyName = ssh.ProxyName,
                    TrustUnverifiedHost = ssh.TrustUnverifiedHost,
                };
                GlobalEventHelper.OnRequestServerConnect?.Invoke(sftp, fromView: $"{nameof(LauncherWindowView)} - Action - Open SFTP", assignTabToken: DateTime.Now.Ticks.ToString());
            }));
        }


        if (server is RDP rdp)
        {
            actions.Add(new ProtocolAction(IoC.Translate("Export") + " *.rdp", () =>
            {
                var path = SelectFileHelper.SaveFile(filter: "rdp|*.rdp", selectedFileName: rdp.DisplayName + ".rdp");
                if (string.IsNullOrEmpty(path)) return;
                // mstsc opens the exported file on its own, with no tunnel of ours behind it, so it has to
                // name the real host even when this runs on a session that is going through a proxy
                var export = (RDP)rdp.Clone();
                export.Address = rdp.RealAddress;
                export.Port = rdp.RealPort;
                File.WriteAllText(path, export.ToRdpConfig().ToString());
            }));
        }


        #endregion Build Actions

        return actions;
    }

    public static List<ProtocolAction> GetActions(this ProtocolBaseViewModel vm)
    {
        var server = vm.Server;
        return server.GetActions();
    }
}