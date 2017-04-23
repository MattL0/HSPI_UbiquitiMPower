﻿using HomeSeerAPI;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Net;
using System.Linq;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using NullGuard;

namespace Hspi
{
    using static System.FormattableString;

    internal enum DeviceType
    {
        [Description("Switch")]
        Output = 1,

        Power,
        Current,
        Voltage,

        [Description("Power Factor")]
        PowerFactor,

        Energy,
    }

    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal class MPowerDevice : IEquatable<MPowerDevice>
    {
        public MPowerDevice(string id, IPAddress deviceIP, string username, string password, ISet<DeviceType> enabledTypes)
        {
            Password = password;
            Username = username;
            DeviceIP = deviceIP;
            EnabledTypes = enabledTypes;
            Id = id;
        }

        public string Id { get; }
        public ISet<DeviceType> EnabledTypes { get; }
        public IPAddress DeviceIP { get; }
        public string Username { get; }
        public string Password { get; }

        public bool Equals(MPowerDevice other)
        {
            return Id == other.Id &&
                Username == other.Username &&
                Password == other.Password &&
                DeviceIP == other.DeviceIP &&
                EnabledTypes.SetEquals(other.EnabledTypes);
        }
    }

    /// <summary>
    /// Class to store PlugIn Configuration
    /// </summary>
    /// <seealso cref="System.IDisposable" />
    internal class PluginConfig : IDisposable
    {
        public event EventHandler<EventArgs> ConfigChanged;

        private const char DeviceIdsSeparator = '|';

        /// <summary>
        /// Initializes a new instance of the <see cref="PluginConfig"/> class.
        /// </summary>
        /// <param name="HS">The homeseer application.</param>
        public PluginConfig(IHSApplication HS)
        {
            this.HS = HS;

            debugLogging = GetValue(DebugLoggingKey, false);
            string deviceIdsConcatString = GetValue(DeviceIds, string.Empty);
            var deviceIds = deviceIdsConcatString.Split(DeviceIdsSeparator);

            foreach (var deviceId in deviceIds)
            {
                if (string.IsNullOrWhiteSpace(deviceId))
                {
                    continue;
                }
                string ipAddressString = GetValue(IPAddressKey, string.Empty, deviceId);
                IPAddress.TryParse(ipAddressString, out var deviceIP);

                var enabledTypes = new SortedSet<DeviceType>();

                foreach (var item in System.Enum.GetValues(typeof(DeviceType)))
                {
                    var enabled = GetValue(item.ToString(), false, deviceId);
                    if (enabled)
                    {
                        enabledTypes.Add((DeviceType)item);
                    }
                }

                string username = GetValue(UserNameKey, string.Empty, deviceId);
                string passwordEncrypted = GetValue(PasswordKey, string.Empty, deviceId);
                string password = HS.DecryptString(passwordEncrypted, EncryptPassword);

                devices.Add(deviceId, new MPowerDevice(deviceId, deviceIP, username, password, enabledTypes));
            }
        }

        /// <summary>
        /// Gets or sets the devices
        /// </summary>
        /// <value>
        /// The API key.
        /// </value>
        public IReadOnlyDictionary<string, MPowerDevice> Devices
        {
            get
            {
                configLock.EnterReadLock();
                try
                {
                    return new ReadOnlyDictionary<string, MPowerDevice>(devices);
                }
                finally
                {
                    configLock.ExitReadLock();
                }
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether debug logging is enabled.
        /// </summary>
        /// <value>
        ///   <c>true</c> if [debug logging]; otherwise, <c>false</c>.
        /// </value>
        public bool DebugLogging
        {
            get
            {
                configLock.EnterReadLock();
                try
                {
                    return debugLogging;
                }
                finally
                {
                    configLock.ExitReadLock();
                }
            }

            set
            {
                configLock.EnterWriteLock();
                try
                {
                    SetValue(DebugLoggingKey, value);
                    debugLogging = value;
                }
                finally
                {
                    configLock.ExitWriteLock();
                }
            }
        }

        public void AddDevice(MPowerDevice device)
        {
            configLock.EnterWriteLock();
            try
            {
                devices[device.Id] = device;

                SetValue(IPAddressKey, device.DeviceIP.ToString(), device.Id);
                SetValue(UserNameKey, device.Username, device.Id);
                SetValue(PasswordKey, HS.EncryptString(device.Password, EncryptPassword), device.Id);

                foreach (var item in System.Enum.GetValues(typeof(DeviceType)))
                {
                    SetValue(item.ToString(), device.EnabledTypes.Contains((DeviceType)item), device.Id);
                }

                SetValue(DeviceIds, devices.Keys.Aggregate((x, y) => x + DeviceIdsSeparator + y));
            }
            finally
            {
                configLock.ExitWriteLock();
            }
        }

        public void RemoveDevice(string deviceId)
        {
            configLock.EnterWriteLock();
            try
            {
                devices.Remove(deviceId);
                if (devices.Count > 0)
                {
                    SetValue(DeviceIds, devices.Keys.Aggregate((x, y) => x + DeviceIdsSeparator + y));
                }
                else
                {
                    SetValue(DeviceIds, string.Empty);
                }
                HS.ClearINISection(deviceId, FileName);
            }
            finally
            {
                configLock.ExitWriteLock();
            }
        }

        private T GetValue<T>(string key, T defaultValue)
        {
            return GetValue(key, defaultValue, DefaultSection);
        }

        private T GetValue<T>(string key, T defaultValue, string section)
        {
            string stringValue = HS.GetINISetting(section, key, null, FileName);

            if (stringValue != null)
            {
                try
                {
                    T result = (T)System.Convert.ChangeType(stringValue, typeof(T), CultureInfo.InvariantCulture);
                    return result;
                }
                catch (Exception)
                {
                    return defaultValue;
                }
            }
            return defaultValue;
        }

        private void SetValue<T>(string key, T value)
        {
            SetValue<T>(key, value, DefaultSection);
        }

        private void SetValue<T>(string key, T value, string section)
        {
            string stringValue = System.Convert.ToString(value, CultureInfo.InvariantCulture);
            HS.SaveINISetting(section, key, stringValue, FileName);
        }

        /// <summary>
        /// Fires event that configuration changed.
        /// </summary>
        public void FireConfigChanged()
        {
            if (ConfigChanged != null)
            {
                var ConfigChangedCopy = ConfigChanged;
                ConfigChangedCopy(this, EventArgs.Empty);
            }
        }

        #region IDisposable Support

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    configLock.Dispose();
                }
                disposedValue = true;
            }
        }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            Dispose(true);
        }

        #endregion IDisposable Support

        private const string UserNameKey = "UserName";
        private const string PasswordKey = "Password";
        private const string DeviceIds = "DevicesIds";
        private const string DebugLoggingKey = "DebugLogging";
        private readonly static string FileName = Invariant($"{Path.GetFileName(System.Reflection.Assembly.GetEntryAssembly().Location)}.ini");
        private const string IPAddressKey = "IPAddress";
        private const string DefaultSection = "Settings";
        private const string EncryptPassword = "Not sure what is more secure";

        private readonly Dictionary<string, MPowerDevice> devices = new Dictionary<string, MPowerDevice>();
        private readonly IHSApplication HS;
        private bool debugLogging;
        private bool disposedValue = false;
        private readonly ReaderWriterLockSlim configLock = new ReaderWriterLockSlim();
    };
}