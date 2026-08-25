using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using _1RM.Service;
using _1RM.Utils;
using _1RM.Utils.Tracing;
using Renci.SshNet;
using Renci.SshNet.Sftp;
using Shawn.Utils;

namespace _1RM.Model.Protocol.FileTransmit.Transmitters
{
    public class TransmitterSFtp : ITransmitter
    {
        private const int CONNECT_TIMEOUT_SECONDS = 15;
        private const int OPERATION_TIMEOUT_MINUTES = 10;
        private const int KEEP_ALIVE_SECONDS = 30;

        public readonly string Hostname;
        public readonly int Port;
        public readonly string Username;
        public readonly string Password;
        public readonly string SshKeyPath;
        /// <summary>When true the host key is not checked. See ProtocolBase.TrustUnverifiedHost.</summary>
        public readonly bool TrustUnverifiedHost;
        private Task SFtpConnection;
        private SftpClient? _sftp = null;

        /// <summary>
        /// Guards the <see cref="_sftp"/> field, and nothing else.
        ///
        /// INVARIANT: never hold it across a call to the server. <see cref="IsConnected"/> is evaluated on
        /// the dispatcher — the transfer commands ask it before they enable themselves — so a holder that
        /// is waiting on the network freezes the whole app, hosted remote sessions included, for as long
        /// as the server takes to answer.
        /// </summary>
        private readonly object _lock = new object();

        public TransmitterSFtp(string host, int port, string username, string key, bool keyIsPassword, bool trustUnverifiedHost = false)
        {
            Hostname = host;
            Port = port;
            Username = username;
            TrustUnverifiedHost = trustUnverifiedHost;
            if (keyIsPassword)
            {
                Password = key;
                SshKeyPath = "";
            }
            else
            {
                Password = "";
                SshKeyPath = key;
            }
            SFtpConnection = InitClient();
        }

        ~TransmitterSFtp()
        {
            Release();
        }

        public async Task Conn()
        {
            await SFtpConnection;
        }

        public bool IsConnected()
        {
            lock (_lock)
            {
                return _sftp?.IsConnected == true;
            }
        }

        public ITransmitter Clone()
        {
            if (!string.IsNullOrWhiteSpace(Password))
                return new TransmitterSFtp(Hostname, Port, Username, Password, true);
            else
                return new TransmitterSFtp(Hostname, Port, Username, SshKeyPath, false);
        }

        public async Task<RemoteItem?> Get(string path)
        {
            await SFtpConnection;
            if (_sftp == null) return null;
            return await Exists(path) ? SftpFile2RemoteItem(_sftp.Get(path)) : null;
        }

        public async Task<List<RemoteItem>> ListDirectoryItems(string path)
        {
            await SFtpConnection;
            var ret = new List<RemoteItem>();
            if (_sftp != null)
            {
                var items = _sftp!.ListDirectory(path);
                var sftpFiles = items as ISftpFile[] ?? items.ToArray();
                if (!sftpFiles.Any())
                    return ret;

                items = sftpFiles.OrderBy(x => x.Name);
                foreach (var item in items)
                {
                    if (item.Name == "." || item.Name == "..")
                        continue;
                    ret.Add(SftpFile2RemoteItem(item));
                }
            }
            return ret;
        }

        public async Task<bool> Exists(string path)
        {
            await SFtpConnection;
            SftpClient? client;
            lock (_lock)
            {
                client = _sftp;
            }
            // The lock is only taken to read the field: Exists is a round trip to the server, and holding
            // it across that is what stalled IsConnected() on the dispatcher.
            return client?.Exists(path) == true;
        }

        private RemoteItem SftpFile2RemoteItem(ISftpFile item)
        {
            var fn = item.FullName;
            var newItem = new RemoteItem()
            {
                Icon = null,
                IsDirectory = item.IsDirectory,
                Name = item.Name,
                FullName = fn,
                LastUpdate = item.LastWriteTime,
                ByteSize = (ulong)Math.Max(item.Length, 0),
            };
            if (item.IsDirectory)
            {
                newItem.Icon = TransmitItemIconCache.GetDictIcon();
                newItem.ByteSize = 0;
                newItem.FileType = "folder";
                if (item.IsSymbolicLink)
                    newItem.Icon = TransmitItemIconCache.GetDictIcon(Environment.GetFolderPath(Environment.SpecialFolder.Favorites));
            }
            else
            {
                if (item.IsSymbolicLink)
                    newItem.FileType = ".lnk";

                if (item.Name.IndexOf(".", StringComparison.Ordinal) > 0)
                {
                    var ext = item.Name.Substring(item.Name.LastIndexOf(".", StringComparison.Ordinal)).ToLower();
                    newItem.FileType = ext;
                    newItem.Icon = TransmitItemIconCache.GetFileIcon(ext);
                }
                else
                {
                    newItem.Icon = TransmitItemIconCache.GetFileIcon();
                }
            }
            return newItem;
        }

        public async Task Delete(string path)
        {
            await SFtpConnection;
            if (_sftp == null) return;
            var item = await Get(path);
            if (item != null)
            {
                if (item is { IsDirectory: true, IsSymlink: false }) // only delete sub files for normal directory, not symlink
                {
                    var sub = _sftp.ListDirectory(path) ?? new List<SftpFile>();
                    foreach (var file in sub)
                    {
                        if (string.IsNullOrWhiteSpace(
                                file.Name
                                    .Replace('.', ' ')
                                    .Replace('\\', ' ')
                                    .Replace('/', ' ')))
                            continue;
                        await Delete((string)file.FullName);
                    }
                    _sftp.DeleteDirectory(path);
                }
                else
                    _sftp.Delete(path);
            }
        }

        public async Task Delete(RemoteItem item)
        {
            await Delete(item.FullName);
        }

        public async Task CreateDirectory(string path)
        {
            await SFtpConnection;
            if (_sftp == null) return;
            if (_sftp.Exists(path) == false)
                _sftp.CreateDirectory(path);
        }

        public async Task RenameFile(string path, string newPath)
        {
            await SFtpConnection;
            if (_sftp == null) return;
            if (_sftp != null && path != newPath && await Exists(path) == true)
                _sftp.RenameFile(path, newPath);
        }

        public async Task UploadFile(string localFilePath, string saveToRemotePath, Action<ulong> writeCallBack, CancellationToken cancellationToken)
        {
            var fi = new FileInfo(localFilePath);
            if (fi?.Exists != true)
                return;

            await SFtpConnection;
            if (_sftp == null) return;
            try
            {
                // check parent
                if (saveToRemotePath.LastIndexOf("/", StringComparison.Ordinal) > 0)
                {
                    var parent = saveToRemotePath.Substring(0,
                        saveToRemotePath.LastIndexOf("/", StringComparison.Ordinal));
                    if (_sftp.Exists(parent) == false)
                        _sftp.CreateDirectory(parent);
                }

                using var fileStream = File.OpenRead(fi.FullName);
                if (!fileStream.CanRead)
                    return;

                _sftp.UploadFile(fileStream, saveToRemotePath, obj =>
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        SimpleLogHelper.Debug("SFTP Upload: cancel by CancellationToken");
                        fileStream.Close();
                        fileStream.Dispose();
                    }
                    writeCallBack?.Invoke(obj);
                });
            }
            catch (Exception)
            {
                if (cancellationToken.IsCancellationRequested == false)
                    throw;
            }
        }

        public async Task DownloadFile(string remoteFilePath, string saveToLocalPath, Action<ulong> readCallBack, CancellationToken cancellationToken)
        {
            await SFtpConnection;
            if (_sftp == null) return;
            try
            {
                var fi = new FileInfo(saveToLocalPath);
                if (fi.Exists)
                    fi.Delete();
                if (fi?.Directory?.Exists == false)
                    fi.Directory.Create();
                using var fileStream = File.OpenWrite(saveToLocalPath);
                if (!fileStream.CanWrite)
                    return;

                _sftp.DownloadFile(remoteFilePath, fileStream, obj =>
                {
                    if (cancellationToken.IsCancellationRequested)
                        fileStream.Close();
                    readCallBack?.Invoke(obj);
                });
            }
            catch (Exception)
            {
                if (cancellationToken.IsCancellationRequested == false)
                    throw;
            }
        }

        public void Release()
        {
            if (SFtpConnection?.IsCompleted == true)
            {
                SFtpConnection?.Dispose();
            }
            ReleaseSftp();
        }

        /// <summary>
        /// SSH.NET trusts every host key unless a handler says otherwise, so leaving this unsubscribed — as
        /// this class did — meant SFTP had no protection against interception at all, while the very same
        /// server reached over SSH was properly verified by PuTTY.
        /// </summary>
        private void OnHostKeyReceived(object? sender, Renci.SshNet.Common.HostKeyEventArgs e)
        {
            if (TrustUnverifiedHost)
            {
                e.CanTrust = true;
                return;
            }

            var fingerprint = HostTrustService.Fingerprint(e.HostKey);
            e.CanTrust = IoC.Get<HostTrustService>().VerifyOrAsk("ssh", Hostname, Port, fingerprint, e.HostKeyName);
        }

        private void ReleaseSftp()
        {
            SftpClient? old;
            lock (_lock)
            {
                old = _sftp;
                _sftp = null;
            }
            if (old == null) return;

            // Both of these talk to the server — Disconnect is a protocol exchange and Dispose waits on it
            // — so they run with the lock released. A dead link makes them take their full timeout, and
            // that used to be time IsConnected() spent blocking the dispatcher.
            try
            {
                old.Disconnect();
            }
            catch (Exception e)
            {
                SimpleLogHelper.Warning($"TransmitterSFtp: disconnect failed, {e.Message}");
            }
            try
            {
                old.Dispose();
            }
            catch (Exception e)
            {
                SimpleLogHelper.Warning($"TransmitterSFtp: dispose failed, {e.Message}");
            }
        }

        private async Task InitClient()
        {
            await Task.Run(() =>
            {
                if (IsConnected() != true)
                {
                    RetryHelper.Try(() =>
                    {
                        ReleaseSftp();
                        ConnectionInfo connectionInfo;
                        if (string.IsNullOrEmpty(Password)
                            && string.IsNullOrEmpty(SshKeyPath) == false
                            && File.Exists(SshKeyPath))
                        {
                            // Deliberately not caught: the old code logged the failure and fell through to
                            // the line below, which then attempted an *empty password* login. The real
                            // reason — usually a key that needs a passphrase — never reached the user.
                            connectionInfo = new ConnectionInfo(Hostname, Port, Username,
                                new PrivateKeyAuthenticationMethod(Username, new PrivateKeyFile(SshKeyPath)));
                        }
                        else
                        {
                            connectionInfo = new ConnectionInfo(Hostname, Port, Username,
                                new PasswordAuthenticationMethod(Username, Password));
                        }
                        connectionInfo.Timeout = TimeSpan.FromSeconds(CONNECT_TIMEOUT_SECONDS);

                        var client = new SftpClient(connectionInfo);
                        client.OperationTimeout = TimeSpan.FromMinutes(OPERATION_TIMEOUT_MINUTES);
                        // without this an idle session is dropped by NAT or the firewall and the failure only
                        // surfaces on the user's next action
                        client.KeepAliveInterval = TimeSpan.FromSeconds(KEEP_ALIVE_SECONDS);
                        client.HostKeyReceived += OnHostKeyReceived;
                        try
                        {
                            client.Connect();
                        }
                        catch
                        {
                            // it never reached the field, so ReleaseSftp will not find it on the next attempt
                            client.Dispose();
                            throw;
                        }

                        // Published only once it is usable. Logging in is the slow part and it happens off
                        // the lock; the field is set here so IsConnected() never sees a half-built client.
                        lock (_lock)
                        {
                            _sftp = client;
                        }
                    });
                }
            });
        }
    }
}