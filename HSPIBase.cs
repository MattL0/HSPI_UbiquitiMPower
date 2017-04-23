﻿using HomeSeerAPI;
using HSCF.Communication.Scs.Communication;
using HSCF.Communication.Scs.Communication.EndPoints.Tcp;
using HSCF.Communication.ScsServices.Client;
using Hspi.Connector;
using Hspi.Exceptions;
using NullGuard;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Hspi
{
    using static System.FormattableString;

    /// <summary>
    /// Basic Functionality of HSPI
    /// </summary>
    /// <seealso cref="Hspi.IPlugInAPI2" />
    /// <seealso cref="System.IDisposable" />
    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal abstract class HspiBase : IPlugInAPI2, ILogger, IDisposable
    {
        protected HspiBase(string name, int capabilities = (int)Enums.eCapabilities.CA_IO, string instanceFriendlyName = "",
                           bool supportMutipleInstances = false, int accessLevel = 1, bool supportsMultipleInstancesSingleEXE = true,
                           bool supportsAddDevice = false, bool hsComPort = false, bool supportConfigDevice = false, bool supportConfigDeviceAll = false)
        {
            this.name = name;
            this.instanceFriendlyName = instanceFriendlyName;
            this.capabilities = capabilities;
            this.supportMutipleInstances = supportMutipleInstances;
            this.accessLevel = accessLevel;
            this.supportsMultipleInstancesSingleEXE = supportsMultipleInstancesSingleEXE;
            this.supportsAddDevice = supportsAddDevice;
            this.hsComPort = hsComPort;
            this.supportConfigDevice = supportConfigDevice;
            this.supportConfigDeviceAll = supportConfigDeviceAll;
        }

        public override bool HasTriggers => false;
        public override string Name => name;
        public override int TriggerCount => 0;
        public override bool Connected => HsClient.CommunicationState == CommunicationStates.Connected;

        protected IAppCallbackAPI Callback { get; private set; }
        protected IScsServiceClient<IAppCallbackAPI> CallbackClient { get; private set; }
        protected IScsServiceClient<IHSApplication> HsClient { get; private set; }
        protected IHSApplication HS { get; private set; }

        protected CancellationToken ShutdownCancellationToken => cancellationTokenSource.Token;

        public override int AccessLevel() => accessLevel;

        public override string ActionBuildUI([AllowNull]string uniqueControlId, IPlugInAPI.strTrigActInfo actionInfo) => string.Empty;

        public override bool ActionConfigured(IPlugInAPI.strTrigActInfo actionInfo) => true;

        public override int ActionCount() => 0;

        public override string ActionFormatUI(IPlugInAPI.strTrigActInfo actionInfo) => string.Empty;

        public override IPlugInAPI.strMultiReturn ActionProcessPostUI([AllowNull]NameValueCollection postData,
                                                IPlugInAPI.strTrigActInfo actionInfo) => new IPlugInAPI.strMultiReturn();

        public override bool ActionReferencesDevice(IPlugInAPI.strTrigActInfo actionInfo, int deviceId) => false;

        public override int Capabilities() => capabilities;

        public override string ConfigDevice(int deviceId, [AllowNull]string user, int userRights, bool newDevice) => string.Empty;

        [SuppressMessage("Microsoft.Performance", "CA1804:RemoveUnusedLocals", MessageId = "apiVersion")]
        public void Connect(string serverAddress, int serverPort)
        {
            try
            {
                HsClient = ScsServiceClientBuilder.CreateClient<IHSApplication>(new ScsTcpEndPoint(serverAddress, serverPort), this);
                HsClient.Connect();
                HS = HsClient.ServiceProxy;

                var apiVersion = HS.APIVersion; // just to make sure our connection is valid
            }
            catch (Exception ex)
            {
                throw new HspiConnectionException(Invariant($"Error connecting homeseer SCS client: {ex.Message}"), ex);
            }

            try
            {
                CallbackClient = ScsServiceClientBuilder.CreateClient<IAppCallbackAPI>(new ScsTcpEndPoint(serverAddress, serverPort), this);
                CallbackClient.Connect();
                Callback = CallbackClient.ServiceProxy;

                var apiVersion = Callback.APIVersion; // just to make sure our connection is valid
            }
            catch (Exception ex)
            {
                throw new HspiConnectionException(Invariant($"Error connecting callback SCS client: {ex.Message}"), ex);
            }

            // Establish the reverse connection from homeseer back to our plugin
            try
            {
                HS.Connect(Name, InstanceFriendlyName());
            }
            catch (Exception ex)
            {
                throw new HspiConnectionException(Invariant($"Error connecting homeseer to our plugin: {ex.Message}"), ex);
            }

            HsClient.Disconnected += HsClient_Disconnected;
        }

        private void HsClient_Disconnected(object sender, EventArgs e)
        {
            cancellationTokenSource.Cancel();
        }

        public void WaitforShutDownOrDisconnect()
        {
            cancellationTokenSource.Token.WaitHandle.WaitOne();
        }

        public void Dispose()
        {
            Dispose(true);
        }

        public override string GenPage([AllowNull]string link) => string.Empty;

        public override string get_ActionName(int actionNumber) => string.Empty;

        public override bool get_Condition(IPlugInAPI.strTrigActInfo actionInfo) => false;

        public override bool get_HasConditions(int triggerNumber) => false;

        public override int get_SubTriggerCount(int triggerNumber) => 0;

        public override string get_SubTriggerName(int triggerNumber, int subTriggerNumber) => string.Empty;

        public override bool get_TriggerConfigured(IPlugInAPI.strTrigActInfo actionInfo) => true;

        public override string get_TriggerName(int triggerNumber) => string.Empty;

        public override string GetPagePlugin([AllowNull]string page, [AllowNull]string user, int userRights, [AllowNull]string queryString) => string.Empty;

        public override bool HandleAction(IPlugInAPI.strTrigActInfo actionInfo) => false;

        public override void HSEvent(Enums.HSEvent eventType, [AllowNull]object[] parameters)
        {
        }

        public override string InstanceFriendlyName() => instanceFriendlyName;

        public override IPlugInAPI.strInterfaceStatus InterfaceStatus()
        {
            var s = new IPlugInAPI.strInterfaceStatus { intStatus = IPlugInAPI.enumInterfaceStatus.OK };
            return s;
        }

        public override string PagePut([AllowNull]string data) => string.Empty;

        public override object PluginFunction([AllowNull]string functionName, [AllowNull]object[] parameters) => null;

        public override object PluginPropertyGet([AllowNull]string propertyName, [AllowNull]object[] parameters) => null;

        public override void PluginPropertySet([AllowNull]string propertyName, [AllowNull]object value)
        {
        }

        public override IPlugInAPI.PollResultInfo PollDevice(int deviceId)
        {
            var pollResult = new IPlugInAPI.PollResultInfo
            {
                Result = IPlugInAPI.enumPollResult.Device_Not_Found,
                Value = 0
            };

            return pollResult;
        }

        public override string PostBackProc([AllowNull]string page, [AllowNull]string data, [AllowNull]string user, int userRights) => string.Empty;

        public override bool RaisesGenericCallbacks() => false;

        public override SearchReturn[] Search([AllowNull]string searchString, bool regEx) => null;

        public override void set_Condition(IPlugInAPI.strTrigActInfo actionInfo, bool value)
        {
        }

        public override void SetDeviceValue(int deviceId, double value, bool trigger = true)
        {
            HS.SetDeviceValueByRef(deviceId, value, trigger);
        }

        public override void SetIOMulti([AllowNull]List<CAPI.CAPIControl> colSend)
        {
        }

        public override void ShutdownIO()
        {
            cancellationTokenSource.Cancel();

            if (HsClient != null)
            {
                HsClient.Disconnected -= HsClient_Disconnected;
            }

            this.HsClient.Disconnect();
            this.CallbackClient.Disconnect();
        }

        public override void SpeakIn(int deviceId, [AllowNull]string text, bool wait, [AllowNull]string host)
        {
        }

        public override bool SupportsAddDevice() => supportsAddDevice;

        public override bool SupportsConfigDevice() => supportConfigDevice;

        public override bool SupportsConfigDeviceAll() => supportConfigDeviceAll;

        public override bool SupportsMultipleInstances() => supportMutipleInstances;

        public override bool SupportsMultipleInstancesSingleEXE() => supportsMultipleInstancesSingleEXE;

        public override string TriggerBuildUI([AllowNull]string uniqueControlId, IPlugInAPI.strTrigActInfo triggerInfo) => string.Empty;

        public override string TriggerFormatUI(IPlugInAPI.strTrigActInfo actionInfo) => string.Empty;

        public override IPlugInAPI.strMultiReturn TriggerProcessPostUI([AllowNull]NameValueCollection postData,
            IPlugInAPI.strTrigActInfo actionInfo) => new IPlugInAPI.strMultiReturn();

        public override bool TriggerReferencesDevice(IPlugInAPI.strTrigActInfo actionInfo, int deviceId) => false;

        public override bool TriggerTrue(IPlugInAPI.strTrigActInfo actionInfo) => false;

        public override Enums.ConfigDevicePostReturn ConfigDevicePost(int deviceId,
            [AllowNull]string data,
            [AllowNull]string user,
            int userRights) => Enums.ConfigDevicePostReturn.DoneAndCancel;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    if (HsClient != null)
                    {
                        HsClient.Disconnected -= HsClient_Disconnected;
                        HsClient.Dispose();
                    }
                    CallbackClient?.Dispose();
                    cancellationTokenSource.Dispose();
                }
                disposedValue = true;
            }
        }

        protected override bool GetHasTriggers() => false;

        protected override bool GetHscomPort() => hsComPort;

        protected override int GetTriggerCount() => 0;

        public virtual void DebugLog(string message)
        {
            Trace.WriteLine(message);
            HS.WriteLog(Name, Invariant($"Debug:{message}"));
        }

        public void LogError(string message)
        {
            Trace.TraceError(message);
            HS.WriteLog(Name, Invariant($"Error:{message}"));
        }

        public void LogInfo(string message)
        {
            Trace.TraceInformation(message);
            HS.WriteLog(Name, message);
        }

        public void LogWarning(string message)
        {
            Trace.TraceWarning(message);
            HS.WriteLog(Name, Invariant($"Warning:{message}"));
        }

        private readonly int accessLevel;
        private readonly CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        private readonly int capabilities;
        private readonly bool hsComPort;
        private readonly string instanceFriendlyName;
        private readonly string name;
        private readonly bool supportConfigDevice;
        private readonly bool supportConfigDeviceAll;
        private readonly bool supportMutipleInstances;
        private readonly bool supportsAddDevice;
        private readonly bool supportsMultipleInstancesSingleEXE;
        private bool disposedValue = false;
    }
}