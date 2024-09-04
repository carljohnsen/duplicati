﻿// Copyright (C) 2024, The Duplicati Team
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

using System;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Duplicati.Library.RestAPI;
using System.Text.Json;
using System.Text;

namespace Duplicati.Server.Database
{
    public class ServerSettings
    {
        public static class CONST
        {
            public const string STARTUP_DELAY = "startup-delay";
            public const string DOWNLOAD_SPEED_LIMIT = "max-download-speed";
            public const string UPLOAD_SPEED_LIMIT = "max-upload-speed";
            public const string THREAD_PRIORITY = "thread-priority";
            public const string LAST_WEBSERVER_PORT = "last-webserver-port";
            public const string IS_FIRST_RUN = "is-first-run";
            public const string SERVER_PORT_CHANGED = "server-port-changed";
            public const string SERVER_PASSPHRASE = "server-passphrase";
            public const string SERVER_PASSPHRASE_SALT = "server-passphrase-salt";
            public const string UPDATE_CHECK_LAST = "last-update-check";
            public const string UPDATE_CHECK_INTERVAL = "update-check-interval";
            public const string UPDATE_CHECK_NEW_VERSION = "update-check-latest";
            public const string UNACKED_ERROR = "unacked-error";
            public const string UNACKED_WARNING = "unacked-warning";
            public const string SERVER_LISTEN_INTERFACE = "server-listen-interface";
            public const string SERVER_SSL_CERTIFICATE = "server-ssl-certificate";
            public const string HAS_FIXED_INVALID_BACKUPID = "has-fixed-invalid-backup-id";
            public const string UPDATE_CHANNEL = "update-channel";
            public const string USAGE_REPORTER_LEVEL = "usage-reporter-level";
            public const string DISABLE_TRAY_ICON_LOGIN = "disable-tray-icon-login";
            public const string SERVER_ALLOWED_HOSTNAMES = "allowed-hostnames";
            public const string JWT_CONFIG = "jwt-config";
            public const string PBKDF_CONFIG = "pbkdf-config";
            public const string AUTOGENERATED_PASSPHRASE = "autogenerated-passphrase";
            public const string DISABLE_VISUAL_CAPTCHA = "disable-visual-captcha";
            public const string ENCRYPTED_FIELDS = "encrypted-fields";
            public const string PRELOAD_SETTINGS_HASH = "preload-settings-hash";
        }

        private readonly Dictionary<string, string> settings;
        private readonly Connection databaseConnection;
        private Library.AutoUpdater.UpdateInfo m_latestUpdate;

        internal ServerSettings(Connection con)
        {
            settings = new Dictionary<string, string>();
            databaseConnection = con;
            ReloadSettings();
        }

        public void ReloadSettings()
        {
            lock (databaseConnection.m_lock)
            {
                settings.Clear();
                foreach (var n in typeof(CONST).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly | System.Reflection.BindingFlags.Static).Select(x => (string)x.GetValue(null)))
                    settings[n] = null;
                foreach (var n in databaseConnection.GetSettings(Connection.SERVER_SETTINGS_ID))
                    settings[n.Name] = n.Value;
            }
        }

        public void UpdateSettings(Dictionary<string, string> newsettings, bool clearExisting)
        {
            if (newsettings == null)
                throw new ArgumentNullException();

            lock (databaseConnection.m_lock)
            {
                m_latestUpdate = null;
                if (clearExisting)
                    settings.Clear();

                foreach (var k in newsettings)
                    if (!clearExisting && newsettings[k.Key] == null && k.Key.StartsWith("--", StringComparison.Ordinal))
                        settings.Remove(k.Key);
                    else
                        settings[k.Key] = newsettings[k.Key];

                // Prevent user from logging themselves out, by disabling the login and not knowing the password
                if (DisableTrayIconLogin && AutogeneratedPassphrase)
                    settings[CONST.DISABLE_TRAY_ICON_LOGIN] = false.ToString();
            }

            SaveSettings();
        }

        private void SaveSettings()
        {
            databaseConnection.SetSettings(
                from n in settings
                select (Duplicati.Server.Serialization.Interface.ISetting)new Setting()
                {
                    Filter = "",
                    Name = n.Key,
                    Value = n.Value
                }, Database.Connection.SERVER_SETTINGS_ID);

            if (FIXMEGlobal.IsServerStarted)
            {
                FIXMEGlobal.NotificationUpdateService.IncrementLastDataUpdateId();
                FIXMEGlobal.StatusEventNotifyer.SignalNewEvent();
                // If throttle options were changed, update now
                FIXMEGlobal.WorkerThreadsManager.UpdateThrottleSpeeds();
            }

            // In case the usage reporter is enabled or disabled, refresh now
            if (FIXMEGlobal.StartOrStopUsageReporter != null)
                FIXMEGlobal.StartOrStopUsageReporter();
        }

        public string StartupDelayDuration
        {
            get
            {
                return settings[CONST.STARTUP_DELAY];
            }
            set
            {
                lock (databaseConnection.m_lock)
                    settings[CONST.STARTUP_DELAY] = value;
                SaveSettings();
            }
        }

        public System.Threading.ThreadPriority? ThreadPriorityOverride
        {
            get
            {
                var tp = settings[CONST.THREAD_PRIORITY];
                if (string.IsNullOrEmpty(tp))
                    return null;

                System.Threading.ThreadPriority r;
                if (Enum.TryParse<System.Threading.ThreadPriority>(tp, true, out r))
                    return r;

                return null;
            }
            set
            {
                lock (databaseConnection.m_lock)
                    settings[CONST.THREAD_PRIORITY] = value.HasValue ? Enum.GetName(typeof(System.Threading.ThreadPriority), value.Value) : null;
            }
        }

        public string DownloadSpeedLimit
        {
            get
            {
                return settings[CONST.DOWNLOAD_SPEED_LIMIT];
            }
            set
            {
                lock (databaseConnection.m_lock)
                    settings[CONST.DOWNLOAD_SPEED_LIMIT] = value;
                SaveSettings();
            }
        }

        public string UploadSpeedLimit
        {
            get
            {
                return settings[CONST.UPLOAD_SPEED_LIMIT];
            }
            set
            {
                lock (databaseConnection.m_lock)
                    settings[CONST.UPLOAD_SPEED_LIMIT] = value;
                SaveSettings();
            }
        }

        public bool IsFirstRun
        {
            get
            {
                return Duplicati.Library.Utility.Utility.ParseBoolOption(settings, CONST.IS_FIRST_RUN);
            }
            set
            {
                lock (databaseConnection.m_lock)
                    settings[CONST.IS_FIRST_RUN] = value.ToString();
                SaveSettings();
            }
        }

        public bool UnackedError
        {
            get
            {
                return Duplicati.Library.Utility.Utility.ParseBool(settings[CONST.UNACKED_ERROR], false);
            }
            set
            {
                lock (databaseConnection.m_lock)
                    settings[CONST.UNACKED_ERROR] = value.ToString();
                SaveSettings();
            }
        }

        public bool UnackedWarning
        {
            get
            {
                return Duplicati.Library.Utility.Utility.ParseBool(settings[CONST.UNACKED_WARNING], false);
            }
            set
            {
                lock (databaseConnection.m_lock)
                    settings[CONST.UNACKED_WARNING] = value.ToString();
                SaveSettings();
            }
        }

        public bool ServerPortChanged
        {
            get
            {
                return Duplicati.Library.Utility.Utility.ParseBool(settings[CONST.SERVER_PORT_CHANGED], false);
            }
            set
            {
                lock (databaseConnection.m_lock)
                    settings[CONST.SERVER_PORT_CHANGED] = value.ToString();
                SaveSettings();
            }
        }

        public bool DisableTrayIconLogin
        {
            get
            {
                return Duplicati.Library.Utility.Utility.ParseBool(settings[CONST.DISABLE_TRAY_ICON_LOGIN], false);
            }
            set
            {
                lock (databaseConnection.m_lock)
                    settings[CONST.DISABLE_TRAY_ICON_LOGIN] = value.ToString();
                SaveSettings();
            }
        }

        public bool AutogeneratedPassphrase
        {
            get
            {
                return Duplicati.Library.Utility.Utility.ParseBool(settings[CONST.AUTOGENERATED_PASSPHRASE], false);
            }
        }

        public bool DisableVisualCaptcha
        {
            get
            {
                return Duplicati.Library.Utility.Utility.ParseBool(settings[CONST.DISABLE_VISUAL_CAPTCHA], false);
            }
            set
            {
                lock (databaseConnection.m_lock)
                    settings[CONST.DISABLE_VISUAL_CAPTCHA] = value.ToString();
                SaveSettings();
            }
        }

        public int LastWebserverPort
        {
            get
            {
                var tp = settings[CONST.LAST_WEBSERVER_PORT];
                int p;
                if (string.IsNullOrEmpty(tp) || !int.TryParse(tp, out p))
                    return -1;

                return p;
            }
            set
            {
                lock (databaseConnection.m_lock)
                    settings[CONST.LAST_WEBSERVER_PORT] = value.ToString();
                SaveSettings();
            }
        }

        /// <summary>
        /// This class is used to store the PBKDF configuration parameters
        /// </summary>        
        private record PbkdfConfig(string Algorithm, int Version, string Salt, int Iterations, string HashAlorithm, string Hash)
        {
            /// <summary>
            /// The version to embed in the configuration
            /// </summary>
            private const int _Version = 1;
            /// <summary>
            /// The algorithm to use
            /// </summary>
            private const string _Algorithm = "PBKDF2";
            /// <summary>
            /// The hash algorithm to use
            /// </summary>
            private const string _HashAlorithm = "SHA256";
            /// <summary>
            /// The number of iterations to use
            /// </summary>
            private const int _Iterations = 10000;
            /// <summary>
            /// The size of the hash
            /// </summary>
            private const int _HashSize = 32;

            /// <summary>
            /// Creates a new PBKDF2 configuration with a random salt
            /// </summary>
            /// <param name="password">The password to hash</param>
            public static PbkdfConfig CreatePBKDF2(string password)
            {
                var prng = RandomNumberGenerator.Create();
                var buf = new byte[_HashSize];
                prng.GetBytes(buf);

                var salt = Convert.ToBase64String(buf);
                var pbkdf2 = new Rfc2898DeriveBytes(password, buf, _Iterations, new HashAlgorithmName(_HashAlorithm));
                var pwd = Convert.ToBase64String(pbkdf2.GetBytes(_HashSize));

                return new PbkdfConfig(_Algorithm, _Version, salt, _Iterations, _HashAlorithm, pwd);
            }

            /// <summary>
            /// Verifies a password against a PBKDF2 configuration
            /// </summary>
            /// <param name="password">The password to verify</param>
            /// <returns>True if the password matches the configuration</returns>
            public bool VerifyPassword(string password)
            {
                var pbkdf2 = new Rfc2898DeriveBytes(password, Convert.FromBase64String(Salt), Iterations, new HashAlgorithmName(HashAlorithm));
                var pwd = Convert.ToBase64String(pbkdf2.GetBytes(_HashSize));

                return pwd == Hash;
            }
        }

        /// <summary>
        /// Verifies a password against the stored PBKDF configuration
        /// </summary>
        public bool VerifyWebserverPassword(string password)
        {
            var config = settings[CONST.PBKDF_CONFIG];
            if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(config))
                return false;

            return JsonSerializer.Deserialize<PbkdfConfig>(config).VerifyPassword(LegacyPreparePassword(password));
        }

        /// <summary>
        /// Prepares a password by pre-hashing it with a legacy salt, if needed
        /// </summary>
        /// <param name="password">The password to hash</param>
        /// <returns>The hashed password</returns>
        private string LegacyPreparePassword(string password)
        {
            if (string.IsNullOrWhiteSpace(settings[CONST.SERVER_PASSPHRASE_SALT]))
                return password;

            var buf = Convert.FromBase64String(settings[CONST.SERVER_PASSPHRASE_SALT]);
            var sha256 = SHA256.Create();
            var str = Encoding.UTF8.GetBytes(password);

            sha256.TransformBlock(str, 0, str.Length, str, 0);
            sha256.TransformFinalBlock(buf, 0, buf.Length);
            return Convert.ToBase64String(sha256.Hash);
        }

        /// <summary>
        /// Upgrades the password to a PBKDF configuration, if using the legacy password setup
        /// </summary>        
        public void UpgradePasswordToKBDF()
        {
            if (!string.IsNullOrWhiteSpace(settings[CONST.PBKDF_CONFIG]))
                return;

            // Generate a random password if one is not set
            var password = settings[CONST.SERVER_PASSPHRASE];
            var autogenerated = false;
            if (string.IsNullOrWhiteSpace(password))
            {
                password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
                settings[CONST.SERVER_PASSPHRASE_SALT] = null;
                autogenerated = true;
            }

            // This will create a new PBKDF2 configuration
            // In case the password already exists in the database,
            // it will need use the pre-salted password as the password
            var config = PbkdfConfig.CreatePBKDF2(password);
            lock (databaseConnection.m_lock)
            {
                settings[CONST.PBKDF_CONFIG] = JsonSerializer.Serialize(config);
                settings[CONST.SERVER_PASSPHRASE] = null;
                settings[CONST.AUTOGENERATED_PASSPHRASE] = autogenerated.ToString();
                if (autogenerated)
                    settings[CONST.DISABLE_TRAY_ICON_LOGIN] = false.ToString();
            }

            SaveSettings();
        }

        /// <summary>
        /// Sets the webserver password
        /// </summary>
        /// <param name="password">The password to set</param>
        public void SetWebserverPassword(string password)
        {
            if (string.IsNullOrWhiteSpace(password))
                throw new Exception("Disabling password protection is not supported");

            var config = PbkdfConfig.CreatePBKDF2(password);
            lock (databaseConnection.m_lock)
            {
                settings[CONST.SERVER_PASSPHRASE] = null;
                settings[CONST.SERVER_PASSPHRASE_SALT] = null;
                settings[CONST.AUTOGENERATED_PASSPHRASE] = false.ToString();
                settings[CONST.PBKDF_CONFIG] = JsonSerializer.Serialize(config);
            }

            SaveSettings();
        }

        public void SetAllowedHostnames(string allowedHostnames)
        {
            lock (databaseConnection.m_lock)
                settings[CONST.SERVER_ALLOWED_HOSTNAMES] = allowedHostnames;

            SaveSettings();
        }

        public string AllowedHostnames => settings[CONST.SERVER_ALLOWED_HOSTNAMES];

        public string JWTConfig
        {
            get => settings[CONST.JWT_CONFIG];
            set
            {
                lock (databaseConnection.m_lock)
                    settings[CONST.JWT_CONFIG] = value;
                SaveSettings();
            }
        }

        public DateTime LastUpdateCheck
        {
            get
            {
                long t;
                if (long.TryParse(settings[CONST.UPDATE_CHECK_LAST], out t))
                    return new DateTime(t, DateTimeKind.Utc);
                else
                    return new DateTime(0, DateTimeKind.Utc);
            }
            set
            {
                lock (databaseConnection.m_lock)
                    settings[CONST.UPDATE_CHECK_LAST] = value.ToUniversalTime().Ticks.ToString();
                SaveSettings();
            }
        }

        public string UpdateCheckInterval
        {
            get
            {
                var tp = settings[CONST.UPDATE_CHECK_INTERVAL];
                if (string.IsNullOrWhiteSpace(tp))
                    tp = "1W";

                return tp;
            }
            set
            {
                lock (databaseConnection.m_lock)
                    settings[CONST.UPDATE_CHECK_INTERVAL] = value;
                SaveSettings();
                FIXMEGlobal.UpdatePoller.Reschedule();
            }
        }

        public DateTime NextUpdateCheck
        {
            get
            {
                try
                {
                    return Duplicati.Library.Utility.Timeparser.ParseTimeInterval(UpdateCheckInterval, LastUpdateCheck);
                }
                catch
                {
                    return LastUpdateCheck.AddDays(7);
                }
            }
        }

        public Library.AutoUpdater.UpdateInfo UpdatedVersion
        {
            get
            {
                if (string.IsNullOrWhiteSpace(settings[CONST.UPDATE_CHECK_NEW_VERSION]))
                    return null;

                try
                {
                    if (m_latestUpdate != null)
                        return m_latestUpdate;

                    using (var tr = new System.IO.StringReader(settings[CONST.UPDATE_CHECK_NEW_VERSION]))
                        return m_latestUpdate = Server.Serialization.Serializer.Deserialize<Library.AutoUpdater.UpdateInfo>(tr);
                }
                catch
                {
                }

                return null;
            }
            set
            {
                string result = null;
                if (value != null)
                {
                    var sb = new System.Text.StringBuilder();
                    using (var tw = new System.IO.StringWriter(sb))
                        Server.Serialization.Serializer.SerializeJson(tw, value);

                    result = sb.ToString();
                }

                m_latestUpdate = value;
                lock (databaseConnection.m_lock)
                    settings[CONST.UPDATE_CHECK_NEW_VERSION] = result;

                SaveSettings();
            }
        }

        public string ServerListenInterface
        {
            get
            {
                return settings[CONST.SERVER_LISTEN_INTERFACE];
            }
            set
            {
                lock (databaseConnection.m_lock)
                    settings[CONST.SERVER_LISTEN_INTERFACE] = value;
                SaveSettings();
            }
        }

        public X509Certificate2 ServerSSLCertificate
        {
            get
            {
                if (String.IsNullOrEmpty(settings[CONST.SERVER_SSL_CERTIFICATE]))
                    return null;

                if (OperatingSystem.IsWindows())
                    return new X509Certificate2(Convert.FromBase64String(settings[CONST.SERVER_SSL_CERTIFICATE]));
                else
                    return new X509Certificate2(Convert.FromBase64String(settings[CONST.SERVER_SSL_CERTIFICATE]), "");
            }
            set
            {
                if (value == null)
                {
                    lock (databaseConnection.m_lock)
                        settings[CONST.SERVER_SSL_CERTIFICATE] = String.Empty;
                }
                else
                {
                    if (OperatingSystem.IsWindows())
                        lock (databaseConnection.m_lock)
                            settings[CONST.SERVER_SSL_CERTIFICATE] = Convert.ToBase64String(value.Export(X509ContentType.Pkcs12));
                    else
                        lock (databaseConnection.m_lock)
                            settings[CONST.SERVER_SSL_CERTIFICATE] = Convert.ToBase64String(value.Export(X509ContentType.Pkcs12, ""));
                }
                SaveSettings();
            }
        }

        public bool FixedInvalidBackupId
        {
            get
            {
                return Duplicati.Library.Utility.Utility.ParseBool(settings[CONST.HAS_FIXED_INVALID_BACKUPID], false);
            }
            set
            {
                lock (databaseConnection.m_lock)
                    settings[CONST.HAS_FIXED_INVALID_BACKUPID] = value.ToString();
                SaveSettings();
            }
        }

        public string UpdateChannel
        {
            get
            {
                return settings[CONST.UPDATE_CHANNEL];
            }
            set
            {
                lock (databaseConnection.m_lock)
                    settings[CONST.UPDATE_CHANNEL] = value;
                SaveSettings();
            }
        }

        public string UsageReporterLevel
        {
            get
            {
                return settings[CONST.USAGE_REPORTER_LEVEL];
            }
            set
            {
                lock (databaseConnection.m_lock)
                    settings[CONST.USAGE_REPORTER_LEVEL] = value;
                SaveSettings();
            }
        }

        public bool EncryptedFields
        {
            get
            {
                return Duplicati.Library.Utility.Utility.ParseBool(settings[CONST.ENCRYPTED_FIELDS], false);
            }
            set
            {
                lock (databaseConnection.m_lock)
                    settings[CONST.ENCRYPTED_FIELDS] = value.ToString();
                SaveSettings();
            }
        }

        public string PreloadSettingsHash
        {
            get
            {
                return settings[CONST.PRELOAD_SETTINGS_HASH];
            }
            set
            {
                lock (databaseConnection.m_lock)
                    settings[CONST.PRELOAD_SETTINGS_HASH] = value;
                SaveSettings();
            }
        }
    }
}
