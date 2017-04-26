﻿using HomeSeerAPI;
using NullGuard;
using Scheduler;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Net;
using System.Text;
using System.Web;
using System.Linq;

namespace Hspi
{
    using System.Globalization;
    using static System.FormattableString;

    /// <summary>
    /// Helper class to generate configuration page for plugin
    /// </summary>
    /// <seealso cref="Scheduler.PageBuilderAndMenu.clsPageBuilder" />
    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal class ConfigPage : PageBuilderAndMenu.clsPageBuilder
    {
        private const string IdPrefix = "id_";
        private const int PortsMax = 8;

        /// <summary>
        /// Initializes a new instance of the <see cref="ConfigPage" /> class.
        /// </summary>
        /// <param name="HS">The hs.</param>
        /// <param name="pluginConfig">The plugin configuration.</param>
        public ConfigPage(IHSApplication HS, PluginConfig pluginConfig) : base(pageName)
        {
            this.HS = HS;
            this.pluginConfig = pluginConfig;
        }

        /// <summary>
        /// Gets the name of the web page.
        /// </summary>
        public static string Name => pageName;

        /// <summary>
        /// Get the web page string for the configuration page.
        /// </summary>
        /// <returns>
        /// System.String.
        /// </returns>
        public string GetWebPage(string queryString)
        {
            try
            {
                NameValueCollection parts = HttpUtility.ParseQueryString(queryString);

                string pageType = parts[PageTypeId];

                reset();

                AddHeader(HS.GetPageHeader(Name, "Configuration", string.Empty, string.Empty, false, false));

                System.Text.StringBuilder stb = new System.Text.StringBuilder();
                stb.Append(PageBuilderAndMenu.clsPageBuilder.DivStart("pluginpage", ""));
                switch (pageType)
                {
                    case EditDevicePageType:
                        pluginConfig.Devices.TryGetValue(parts[DeviceIdId], out var device);
                        stb.Append(BuildAddNewWebPageBody(device)); break;

                    default:
                    case null:
                        stb.Append(BuildMainWebPageBody()); break;
                }
                stb.Append(PageBuilderAndMenu.clsPageBuilder.DivEnd());
                AddBody(stb.ToString());

                AddFooter(HS.GetPageFooter());
                suppressDefaultFooter = true;

                return BuildPage();
            }
            catch (Exception)
            {
                return "error";
            }
        }

        /// <summary>
        /// The user has selected a control on the configuration web page.
        /// The post data is provided to determine the control that initiated the post and the state of the other controls.
        /// </summary>
        /// <param name="data">The post data.</param>
        /// <param name="user">The name of logged in user.</param>
        /// <param name="userRights">The rights of the logged in user.</param>
        /// <returns>Any serialized data that needs to be passed back to the web page, generated by the clsPageBuilder class.</returns>
        public string PostBackProc(string data, [AllowNull]string user, int userRights)
        {
            NameValueCollection parts = HttpUtility.ParseQueryString(data);

            string form = parts["id"];

            if (form == NameToIdWithPrefix(SaveDeviceName))
            {
                StringBuilder results = new StringBuilder();

                // Validate
                if (!IPAddress.TryParse(parts[DeviceIPId], out var ipAddress))
                {
                    results.AppendLine("IP Address is not Valid.<br>");
                }

                string name = parts[NameId];
                if (string.IsNullOrWhiteSpace(name))
                {
                    results.AppendLine("Name is not Valid.<br>");
                }

                if (string.IsNullOrWhiteSpace(parts[UserNameId]))
                {
                    results.AppendLine("User name is not Valid.<br>");
                }

                if (results.Length > 0)
                {
                    this.divToUpdate.Add(SaveErrorDivId, results.ToString());
                }
                else
                {
                    string deviceId = parts[DeviceIdId];
                    if (string.IsNullOrWhiteSpace(deviceId))
                    {
                        deviceId = name.Replace(' ', '_').Replace('.', '_');
                    }

                    var enabledTypes = new SortedSet<DeviceType>();

                    foreach (var item in System.Enum.GetValues(typeof(DeviceType)))
                    {
                        if (parts[NameToId(item.ToString())] == "checked")
                        {
                            enabledTypes.Add((DeviceType)item);
                        }
                    }

                    var enabledPorts = new SortedSet<int>();
                    foreach (var item in Enumerable.Range(1, PortsMax))
                    {
                        if (parts[NameToId(item.ToString(CultureInfo.InvariantCulture))] == "checked")
                        {
                            enabledPorts.Add(item);
                        }
                    }

                    var device = new MPowerDevice(deviceId, parts[NameId], ipAddress, parts[UserNameId], parts[PasswordId], enabledTypes, enabledPorts);

                    this.pluginConfig.AddDevice(device);
                    this.pluginConfig.FireConfigChanged();
                    this.divToUpdate.Add(SaveErrorDivId, RedirectPage(Invariant($"/{HttpUtility.UrlEncode(ConfigPage.Name)}")));
                }
            }
            else if (form == NameToIdWithPrefix(CancelDeviceName))
            {
                this.divToUpdate.Add(SaveErrorDivId, RedirectPage(Invariant($"/{HttpUtility.UrlEncode(ConfigPage.Name)}")));
            }
            else if (form == NameToIdWithPrefix(DeleteDeviceName))
            {
                this.pluginConfig.RemoveDevice(parts[DeviceIdId]);
                this.pluginConfig.FireConfigChanged();
                this.divToUpdate.Add(SaveErrorDivId, RedirectPage(Invariant($"/{HttpUtility.UrlEncode(ConfigPage.Name)}")));
            }

            return base.postBackProc(Name, data, user, userRights);
        }

        private const string EditDevicePageType = "addNew";

        private string BuildMainWebPageBody()
        {
            StringBuilder stb = new StringBuilder();

            //stb.Append(PageBuilderAndMenu.clsPageBuilder.FormStart("ftmSettings", "IdSettings", "Post"));

            stb.Append(@"<div>");
            stb.Append(@"<table class='full_width_table'");
            stb.Append("<tr height='5'><td colspane=5></td></tr>");
            stb.Append("<tr><td class='tableheader' colspan=5>Devices</td></tr>");
            stb.Append(@"<tr><td class='tablecolumn'>Name</td>" +
                        "<td class='tablecolumn'>Device IP Address</td>" +
                        "<td class='tablecolumn'>Enabled Devices</td>" +
                        "<td class='tablecolumn'>Enabled Ports</td>" +
                        "<td class='tablecolumn'></td></tr>");

            foreach (var device in pluginConfig.Devices)
            {
                stb.Append(@"<tr>");
                stb.Append(Invariant($"<td class='tablecell'>{device.Value.Name}</td>"));
                stb.Append(Invariant($"<td class='tablecell'>{device.Value.DeviceIP}</td>"));
                stb.Append(@"<td class='tablecell'>");
                foreach (var item in device.Value.EnabledPorts)
                {
                    stb.Append(Invariant($"{item}<br>"));
                }

                stb.Append("</td>");
                stb.Append(@"<td class='tablecell'>");
                foreach (var item in System.Enum.GetValues(typeof(DeviceType)))
                {
                    if (device.Value.EnabledTypes.Contains((DeviceType)item))
                    {
                        string description = EnumHelper.GetDescription((Enum)item);
                        stb.Append(Invariant($"{description}<br>"));
                    }
                }

                stb.Append("</td>");
                stb.Append(Invariant($"<td class='tablecell'>{PageTypeButton(Invariant($"Edit{device.Key}"), "Edit", EditDevicePageType, deviceId: device.Key)}</ td ></ tr > "));
            }
            stb.Append(Invariant($"<tr><td colspan=5>{PageTypeButton("Add New Device", AddNewName, EditDevicePageType)}</td><td></td></tr>"));
            stb.Append("<tr height='5'><td colspan=5></td></tr>");
            stb.Append(Invariant($"<tr><td colspan=5></td></tr>"));
            stb.Append(@"<tr><td colspan=5><div>Icons made by <a href='http://www.freepik.com' title='Freepik' target='_blank'>Freepik</a> from <a href='http://www.flaticon.com' title='Flaticon' target='_blank'>www.flaticon.com</a> is licensed by <a href='http://creativecommons.org/licenses/by/3.0/' title='Creative Commons BY 3.0' target='_blank'>CC 3.0 BY</a></div></td></tr>");
            stb.Append(@"<tr height='5'><td colspan=5></td></tr>");
            stb.Append(@" </table>");
            stb.Append(@"</div>");
            //stb.Append(PageBuilderAndMenu.clsPageBuilder.FormEnd());

            return stb.ToString();
        }

        private string BuildAddNewWebPageBody([AllowNull]MPowerDevice device)
        {
            string name = device != null ? device.Name.ToString() : string.Empty;
            string ip = device != null ? device.DeviceIP.ToString() : string.Empty;
            string userName = device != null ? device.Username : string.Empty;
            string password = device != null ? device.Password : string.Empty;
            string id = device != null ? device.Id : string.Empty;
            ISet<DeviceType> enabledTypes = device != null ? device.EnabledTypes : new SortedSet<DeviceType>();
            ISet<int> enabledPorts = device != null ? device.EnabledPorts : new SortedSet<int>();
            string buttonLabel = device != null ? "Save" : "Add";
            string header = device != null ? "Edit" : "Add New";

            StringBuilder stb = new StringBuilder();

            stb.Append(PageBuilderAndMenu.clsPageBuilder.FormStart("ftmDeviceChange", "IdChange", "Post"));

            //stb.Append(Invariant($"<tr><td class='tablecell'>Debug Logging Enabled:</td><td colspan=2 class='tablecell'>{FormCheckBox(DebugLoggingId, string.Empty, this.pluginConfig.DebugLogging)}</ td ></ tr > "));
            stb.Append(@"<div>");
            stb.Append(@"<table class='full_width_table'");
            stb.Append("<tr height='5'><td style='width:25%'></td><td style='width:20%'></td><td style='width:55%'></td></tr>");
            stb.Append(Invariant($"<tr><td class='tableheader' colspan=3>{header}</td></tr>"));
            stb.Append(Invariant($"<tr><td class='tablecell'>Name:</td><td class='tablecell' colspan=2>{HtmlTextBox(NameId, name, @readonly: string.IsNullOrEmpty(id))}</td></tr>"));
            stb.Append(Invariant($"<tr><td class='tablecell'>DeviceIP:</td><td class='tablecell' colspan=2>{HtmlTextBox(DeviceIPId, ip)}</td></tr>"));
            stb.Append(Invariant($"<tr><td class='tablecell'>Username:</td><td class='tablecell' colspan=2>{HtmlTextBox(UserNameId, userName)}</td></tr>"));
            stb.Append(Invariant($"<tr><td class='tablecell'>Password:</td><td class='tablecell' colspan=2>{HtmlTextBox(PasswordId, password, type: "password")}</td></tr>"));
            stb.Append(@"<tr><td class='tablecell'>Enabled Devices</td><td class='tablecell'>");
            foreach (var item in System.Enum.GetValues(typeof(DeviceType)))
            {
                string description = EnumHelper.GetDescription((Enum)item);
                stb.Append(Invariant($"{FormCheckBox(item.ToString(), description, enabledTypes.Contains((DeviceType)item))}<br>"));
            }

            stb.Append(@"</td><td class='tablecell'></td></tr>");
            stb.Append(@"<tr><td class='tablecell'>Enabled Ports</td><td class='tablecell'>");
            foreach (var item in Enumerable.Range(1, PortsMax))
            {
                string itemString = item.ToString(CultureInfo.InvariantCulture);
                stb.Append(FormCheckBox(itemString, itemString, enabledPorts.Contains(item)));
                stb.Append("<br>");
            }
            stb.Append(@"</td><td class='tablecell'></td></tr>");
            stb.Append(Invariant($"<tr><td colspan=3>{HtmlTextBox(DeviceIdId, id, type: "hidden")}<div id='{SaveErrorDivId}' style='color:Red'></div></td><td></td></tr>"));
            stb.Append(Invariant($"<tr><td colspan=3>{FormPageButton(SaveDeviceName, buttonLabel)}"));

            if (device != null)
            {
                stb.Append(FormPageButton(DeleteDeviceName, "Delete"));
            }

            stb.Append(FormPageButton(CancelDeviceName, "Cancel"));
            stb.Append(Invariant($"</td><td></td></tr>"));
            stb.Append("<tr height='5'><td colspan=3></td></tr>");
            stb.Append(@" </table>");
            stb.Append(@"</div>");
            stb.Append(PageBuilderAndMenu.clsPageBuilder.FormEnd());

            return stb.ToString();
        }

        private static string NameToId(string name)
        {
            return name.Replace(' ', '_');
        }

        private static string NameToIdWithPrefix(string name)
        {
            return Invariant($"{IdPrefix}{NameToId(name)}");
        }

        protected static string HtmlTextBox(string name, string defaultText, int size = 25, string type = "text", bool @readonly = false)
        {
            return Invariant($"<input type=\'{type}\' id=\'{NameToIdWithPrefix(name)}\' size=\'{size}\' name=\'{name}\' value=\'{defaultText}\' {(@readonly ? "readonly" : string.Empty)}>");
        }

        protected string FormCheckBox(string name, string label, bool @checked)
        {
            var cb = new clsJQuery.jqCheckBox(name, label, PageName, true, true)
            {
                id = NameToIdWithPrefix(name),
                @checked = @checked,
                autoPostBack = false,
            };
            return cb.Build();
        }

        protected string PageTypeButton(string name, string label, string type, string deviceId = null)
        {
            var b = new clsJQuery.jqButton(name, label, PageName, false)
            {
                id = NameToIdWithPrefix(name),
                url = Invariant($"/{HttpUtility.UrlEncode(ConfigPage.Name)}?{PageTypeId}={HttpUtility.UrlEncode(type)}&{DeviceIdId}={HttpUtility.UrlEncode(deviceId ?? string.Empty)}"),
            };

            return b.Build();
        }

        protected string FormPageButton(string name, string label)
        {
            var b = new clsJQuery.jqButton(name, label, PageName, true)
            {
                id = NameToIdWithPrefix(name),
            };

            return b.Build();
        }

        private const string UserNameId = "UserNameId";
        private const string PasswordId = "PasswordId";
        private const string SaveDeviceName = "SaveButton";
        private const string DeviceIdId = "DeviceIdId";
        private const string PageTypeId = "type";
        private const string AddNewName = "Add New";
        private const string DebugLoggingId = "DebugLoggingId";
        private const string DeviceIPId = "DeviceIPId";
        private const string SaveErrorDivId = "message_id";
        private const string ImageDivId = "image_id";
        private const string RefreshIntervalId = "RefreshIntervalId";
        private static readonly string pageName = Invariant($"{PluginData.PlugInName} Config");
        private readonly IHSApplication HS;
        private readonly PluginConfig pluginConfig;
        private const string DeleteDeviceName = "DeleteDeviceName";
        private const string CancelDeviceName = "CancelDeviceName";
        private const string NameId = "NameId";
    }
}