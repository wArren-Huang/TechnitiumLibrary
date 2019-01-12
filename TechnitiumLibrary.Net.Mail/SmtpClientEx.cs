﻿/*
Technitium Library
Copyright (C) 2019  Shreyas Zare (shreyas@technitium.com)

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.

*/

using System;
using System.Net;
using System.Net.Mail;
using System.Reflection;
using System.Security.Cryptography;
using TechnitiumLibrary.Net.Dns;
using TechnitiumLibrary.Net.Proxy;

namespace TechnitiumLibrary.Net.Mail
{
    public class SmtpClientEx : SmtpClient
    {
        #region variables

        readonly static RandomNumberGenerator _rng = new RNGCryptoServiceProvider();

        readonly FieldInfo _localHostName = GetLocalHostNameField();
        DnsClient _dnsClient;
        NetProxy _proxy;

        string _host;
        int _port;

        #endregion

        #region constructors

        public SmtpClientEx()
            : base()
        { }

        public SmtpClientEx(string host)
            : base(host, 25)
        { }

        public SmtpClientEx(string host, int port)
            : base(host, port)
        { }

        #endregion

        #region private

        //Since the fix for this doesn't seem to have made the cut for .NET 4.0, the hacky workaround is still required.
        //Unfortunately, the field name was changed in one of the service packs for .NET 2.0 from "localHostName" to "clientDomain"; .NET 4.0 also uses "clientDomain".
        //To fix the workaround, you'll need to change the GetLocalHostNameField method to:

        private static FieldInfo GetLocalHostNameField()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;

            FieldInfo result = typeof(SmtpClient).GetField("clientDomain", flags);
            if (result == null)
                result = typeof(SmtpClient).GetField("localHostName", flags);

            return result;
        }

        #endregion

        #region public

        public void SetRandomLocalHostName()
        {
            byte[] buffer = new byte[4];
            _rng.GetBytes(buffer);

            this.LocalHostName = BitConverter.ToString(buffer).Replace("-", "");
        }

        public new void Send(string from, string recipients, string subject, string body)
        {
            Send(new MailMessage(from, recipients, subject, body));
        }

        public new void Send(MailMessage message)
        {
            if (string.IsNullOrEmpty(this.Host))
            {
                if (_dnsClient == null)
                    _dnsClient = new DnsClient();

                string[] mxServers = _dnsClient.ResolveMX(message.To[0]);
                if (mxServers.Length > 0)
                    this.Host = mxServers[0];
                else
                    this.Host = message.To[0].Host;

                this.Port = 25;
                this.Credentials = null;
            }

            if (_proxy == null)
            {
                base.Send(message);
            }
            else
            {
                EndPoint remoteEP;

                if (IPAddress.TryParse(_host, out IPAddress address))
                    remoteEP = new IPEndPoint(address, _port);
                else
                    remoteEP = new DomainEndPoint(_host, _port);

                using (TunnelProxy localTunnel = _proxy.CreateLocalTunnelProxy(remoteEP, base.Timeout))
                {
                    base.Host = localTunnel.TunnelEndPoint.Address.ToString();
                    base.Port = localTunnel.TunnelEndPoint.Port;

                    base.Send(message);
                }
            }
        }

        #endregion

        #region properties

        public string LocalHostName
        {
            get
            {
                if (_localHostName == null)
                    return null;

                return _localHostName.GetValue(this) as string;
            }
            set
            {
                _localHostName.SetValue(this, value);
            }
        }

        public DnsClient DnsClient
        {
            get { return _dnsClient; }
            set { _dnsClient = value; }
        }

        public NetProxy Proxy
        {
            get { return _proxy; }
            set
            {
                _proxy = value;

                if (_proxy == null)
                {
                    if (!string.IsNullOrEmpty(_host))
                        base.Host = _host;

                    base.Port = _port;
                }
                else
                {
                    _host = base.Host;
                    _port = base.Port;
                }
            }
        }

        public new string Host
        {
            get
            {
                if (_proxy == null)
                    return base.Host;
                else
                    return _host;
            }
            set
            {
                if (_proxy == null)
                    base.Host = value;
                else
                    _host = value;
            }
        }

        public new int Port
        {
            get
            {
                if (_proxy == null)
                    return base.Port;
                else
                    return _port;
            }
            set
            {
                if (_proxy == null)
                    base.Port = value;
                else
                    _port = value;
            }
        }

        #endregion
    }
}