// Copyright (C) 2025, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.

using Duplicati.Library.Interface;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Backend
{
    public class KeyUploader : IWebModule
    {
        private const string OPTION_URL = "ssh-url";
        private const string OPTION_KEY = "ssh-pubkey";

        private const string AUTHORIZED_KEYS_FILE = "authorized_keys";
        private const string SSH_FOLDER = ".ssh";
        private const string AUTHORIZED_KEYS_PATH = SSH_FOLDER + "/" + AUTHORIZED_KEYS_FILE;

        private const short SSH_FOLDER_PERMISSIONS = 755;// 0x1ED /*755*/;
        private const short AUTHORIZED_KEYS_PERMISSIONS = 644; //0x1A4 /*644*/;
        private const short AUTHORIZED_KEYS_BACKUP_PERMISSIONS = 600; //0x180 /*600*/;

        #region IWebModule implementation

        public IDictionary<string, string> Execute(IDictionary<string, string> options)
        {
            var res = new Dictionary<string, string>();

            options.TryGetValue(OPTION_URL, out var url);
            options.TryGetValue(OPTION_KEY, out var pubkey_s);

            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentException(OPTION_URL);
            if (string.IsNullOrWhiteSpace(pubkey_s))
                throw new ArgumentException(OPTION_KEY);

            var uri = new Utility.Uri(url);
            foreach (var key in uri.QueryParameters.AllKeys)
                if (!string.IsNullOrWhiteSpace(key))
                    options[key] = uri.QueryParameters[key] ?? "";


            pubkey_s = pubkey_s.Trim();
            var pubkey = pubkey_s.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();

            if (pubkey.Length != 3)
                throw new ArgumentException(OPTION_KEY);

            using (var connection = new SSHv2(url, (Dictionary<string, string?>)options))
            {
                var client = connection.CreateConnection(CancellationToken.None).Await();
                try
                {
                    client.ChangeDirectory(SSH_FOLDER);
                }
                catch
                {
                    client.CreateDirectory(SSH_FOLDER);
                    client.ChangePermissions(SSH_FOLDER, SSH_FOLDER_PERMISSIONS);
                    client.ChangeDirectory(SSH_FOLDER);
                }

                var sshfolder = client.ListDirectory(".").First(x => x.Name == ".");
                client.ChangeDirectory("..");

                if (!sshfolder.OwnerCanRead || !sshfolder.OwnerCanWrite)
                    client.ChangePermissions(SSH_FOLDER, SSH_FOLDER_PERMISSIONS);

                var authorized_keys = "";
                byte[]? authorized_keys_bytes = null;

                var existing_authorized_keys = client.ListDirectory(SSH_FOLDER).Any(x => x.Name == AUTHORIZED_KEYS_FILE);
                if (existing_authorized_keys)
                {
                    using (var ms = new MemoryStream())
                    {
                        client.DownloadFile(AUTHORIZED_KEYS_PATH, ms);
                        authorized_keys_bytes = ms.ToArray();
                        authorized_keys = System.Text.Encoding.ASCII.GetString(authorized_keys_bytes);
                    }
                }

                var keys = authorized_keys.Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                var cleaned_keys = keys.Select(x => x.Trim()).Where(x => x.Length > 0 && !x.StartsWith("#", StringComparison.Ordinal));

                // Does the key already exist?
                if (cleaned_keys.Any(x =>
                {
                    var els = x.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(y => y.Trim()).Where(y => y.Length > 0).ToArray();
                    return els.Length == 3 && els[0] == pubkey[0] && els[1] == pubkey[1];
                }))
                {
                    res["status"] = "Key already existed";
                }
                else
                {
                    var new_file = authorized_keys;
                    if (new_file.Trim().Length > 0)
                        new_file = authorized_keys.Trim() + "\n";
                    new_file += string.Join(" ", pubkey) + "\n";

                    if (existing_authorized_keys && authorized_keys_bytes != null)
                    {
                        var filename = AUTHORIZED_KEYS_PATH + ".backup-" + Utility.Utility.SerializeDateTime(DateTime.UtcNow);
                        using (var ms = new MemoryStream(authorized_keys_bytes))
                            client.UploadFile(ms, filename);
                        client.ChangePermissions(filename, AUTHORIZED_KEYS_BACKUP_PERMISSIONS);
                    }

                    using (var ms = new MemoryStream(System.Text.Encoding.ASCII.GetBytes(new_file)))
                        client.UploadFile(ms, AUTHORIZED_KEYS_PATH);

                    if (!existing_authorized_keys)
                        client.ChangePermissions(AUTHORIZED_KEYS_PATH, AUTHORIZED_KEYS_PERMISSIONS);

                    res["status"] = "Key updated";
                }

            }

            return res;
        }

        public string Key => "ssh-keyupload";
        public string DisplayName => Strings.KeyUploader.DisplayName;
        public string Description => Strings.KeyUploader.Description;

        public IList<ICommandLineArgument> SupportedCommands => new ICommandLineArgument[] {
            new CommandLineArgument(OPTION_URL, CommandLineArgument.ArgumentType.String, Strings.KeyUploader.UrlShort, Strings.KeyUploader.UrlLong),
            new CommandLineArgument(OPTION_KEY, CommandLineArgument.ArgumentType.String, Strings.KeyUploader.PubkeyShort, Strings.KeyUploader.PubkeyLong),
        }.Union(new SSHv2().SupportedCommands).ToList();

        #endregion
    }
}

