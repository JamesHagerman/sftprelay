﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Net.Sockets;

namespace SharpSSH.NG
{
    class ProxySOCKS5 : Proxy
    {
        private static int DEFAULTPORT = 1080;
        private string proxy_host;
        private int proxy_port;
        private Stream In;
        private Stream Out;
        private TcpClient socket;
        private string user;
        private string passwd;

        public ProxySOCKS5(string proxy_host)
        {
            int port = DEFAULTPORT;
            string host = proxy_host;
            if (proxy_host.IndexOf(':') != -1)
            {
                try
                {
                    host = proxy_host.Substring(0, proxy_host.IndexOf(':'));
                    port = int.Parse(proxy_host.Substring(proxy_host.IndexOf(':') + 1));
                }
                catch //(Exception e)
                {
                }
            }
            this.proxy_host = host;
            this.proxy_port = port;
        }
        public ProxySOCKS5(string proxy_host, int proxy_port)
        {
            this.proxy_host = proxy_host;
            this.proxy_port = proxy_port;
        }
        public void setUserPasswd(string user, string passwd)
        {
            this.user = user;
            this.passwd = passwd;
        }
        public void connect(SocketFactory socket_factory, string host, int port, int timeout)
        {
            try
            {
                if (socket_factory == null)
                {
                    socket = Util.createSocket(proxy_host, proxy_port, timeout);
                    //socket=new Socket(proxy_host, proxy_port);    
                    In = socket.GetStream();
                    Out = socket.GetStream();
                }
                else
                {
                    socket = socket_factory.createSocket(proxy_host, proxy_port);
                    In = socket_factory.GetStream(socket);
                    Out = socket_factory.GetStream(socket);
                }
                if (timeout > 0)
                {
                    socket.setSoTimeout(timeout);
                }
                socket.NoDelay=true;

                byte[] buf = new byte[1024];
                int index = 0;

                /*
                                   +----+----------+----------+
                                   |VER | NMETHODS | METHODS  |
                                   +----+----------+----------+
                                   | 1  |    1     | 1 to 255 |
                                   +----+----------+----------+

                   The VER field is set to X'05' for this version of the protocol.  The
                   NMETHODS field contains the number of method identifier octets that
                   appear in the METHODS field.

                   The values currently defined for METHOD are:

                          o  X'00' NO AUTHENTICATION REQUIRED
                          o  X'01' GSSAPI
                          o  X'02' USERNAME/PASSWORD
                          o  X'03' to X'7F' IANA ASSIGNED
                          o  X'80' to X'FE' RESERVED FOR PRIVATE METHODS
                          o  X'FF' NO ACCEPTABLE METHODS
                */

                buf[index++] = 5;

                buf[index++] = 2;
                buf[index++] = 0;           // NO AUTHENTICATION REQUIRED
                buf[index++] = 2;           // USERNAME/PASSWORD

                Out.Write(buf, 0, index);

                /*
                    The server selects from one of the methods given in METHODS, and
                    sends a METHOD selection message:

                                         +----+--------+
                                         |VER | METHOD |
                                         +----+--------+
                                         | 1  |   1    |
                                         +----+--------+
                */
                //In.Read(buf, 0, 2);
                fill(In, buf, 2);

                bool check = false;
                switch ((buf[1]) & 0xff)
                {
                    case 0:                // NO AUTHENTICATION REQUIRED
                        check = true;
                        break;
                    case 2:                // USERNAME/PASSWORD
                        if (user == null || passwd == null) break;

                        /*
                           Once the SOCKS V5 server has started, and the client has selected the
                           Username/Password Authentication protocol, the Username/Password
                           subnegotiation begins.  This begins with the client producing a
                           Username/Password request:

                                   +----+------+----------+------+----------+
                                   |VER | ULEN |  UNAME   | PLEN |  PASSWD  |
                                   +----+------+----------+------+----------+
                                   | 1  |  1   | 1 to 255 |  1   | 1 to 255 |
                                   +----+------+----------+------+----------+

                           The VER field contains the current version of the subnegotiation,
                           which is X'01'. The ULEN field contains the length of the UNAME field
                           that follows. The UNAME field contains the username as known to the
                           source operating system. The PLEN field contains the length of the
                           PASSWD field that follows. The PASSWD field contains the password
                           association with the given UNAME.
                        */
                        index = 0;
                        buf[index++] = 1;
                        buf[index++] = (byte)(user.Length);
                        Array.Copy(user.getBytes(), 0, buf, index, user.Length);
                        index += user.Length;
                        buf[index++] = (byte)(passwd.Length);
                        Array.Copy(passwd.getBytes(), 0, buf, index, passwd.Length);
                        index += passwd.Length;

                        Out.Write(buf, 0, index);

                        /*
                           The server verifies the supplied UNAME and PASSWD, and sends the
                           following response:

                                                +----+--------+
                                                |VER | STATUS |
                                                +----+--------+
                                                | 1  |   1    |
                                                +----+--------+

                           A STATUS field of X'00' indicates success. If the server returns a
                           `failure' (STATUS value other than X'00') status, it MUST close the
                           connection.
                        */
                        //In.Read(buf, 0, 2);
                        fill(In, buf, 2);
                        if (buf[1] == 0)
                            check = true;
                        break;
                    default:
                        break;
                }

                if (!check)
                {
                    try { socket.Close(); }
                    catch //(Exception eee)
                    {
                    }
                    throw new JSchException("fail in SOCKS5 proxy");
                }

                /*
                      The SOCKS request is formed as follows:

                        +----+-----+-------+------+----------+----------+
                        |VER | CMD |  RSV  | ATYP | DST.ADDR | DST.PORT |
                        +----+-----+-------+------+----------+----------+
                        | 1  |  1  | X'00' |  1   | Variable |    2     |
                        +----+-----+-------+------+----------+----------+

                      Where:

                      o  VER    protocol version: X'05'
                      o  CMD
                         o  CONNECT X'01'
                         o  BIND X'02'
                         o  UDP ASSOCIATE X'03'
                      o  RSV    RESERVED
                         o  ATYP   address type of following address
                         o  IP V4 address: X'01'
                         o  DOMAINNAME: X'03'
                         o  IP V6 address: X'04'
                      o  DST.ADDR       desired destination address
                      o  DST.PORT desired destination port in network octet
                         order
                */

                index = 0;
                buf[index++] = 5;
                buf[index++] = 1;       // CONNECT
                buf[index++] = 0;

                byte[] hostb = host.getBytes();
                int len = hostb.Length;
                buf[index++] = 3;      // DOMAINNAME
                buf[index++] = (byte)(len);
                Array.Copy(hostb, 0, buf, index, len);
                index += len;
                buf[index++] = (byte)(port >> 8);
                buf[index++] = (byte)(port & 0xff);

                Out.Write(buf, 0, index);

                /*
                   The SOCKS request information is sent by the client as soon as it has
                   established a connection to the SOCKS server, and completed the
                   authentication negotiations.  The server evaluates the request, and
                   returns a reply formed as follows:

                        +----+-----+-------+------+----------+----------+
                        |VER | REP |  RSV  | ATYP | BND.ADDR | BND.PORT |
                        +----+-----+-------+------+----------+----------+
                        | 1  |  1  | X'00' |  1   | Variable |    2     |
                        +----+-----+-------+------+----------+----------+

                   Where:

                   o  VER    protocol version: X'05'
                   o  REP    Reply field:
                      o  X'00' succeeded
                      o  X'01' general SOCKS server failure
                      o  X'02' connection not allowed by ruleset
                      o  X'03' Network unreachable
                      o  X'04' Host unreachable
                      o  X'05' Connection refused
                      o  X'06' TTL expired
                      o  X'07' Command not supported
                      o  X'08' Address type not supported
                      o  X'09' to X'FF' unassigned
                    o  RSV    RESERVED
                    o  ATYP   address type of following address
                      o  IP V4 address: X'01'
                      o  DOMAINNAME: X'03'
                      o  IP V6 address: X'04'
                    o  BND.ADDR       server bound address
                    o  BND.PORT       server bound port in network octet order
                */

                //In.Read(buf, 0, 4);
                fill(In, buf, 4);

                if (buf[1] != 0)
                {
                    try { socket.Close(); }
                    catch //(Exception eee)
                    {
                    }
                    throw new JSchException("ProxySOCKS5: server returns " + buf[1]);
                }

                switch (buf[3] & 0xff)
                {
                    case 1:
                        //In.Read(buf, 0, 6);
                        fill(In, buf, 6);
                        break;
                    case 3:
                        //In.Read(buf, 0, 1);
                        fill(In, buf, 1);
                        //In.Read(buf, 0, buf[0]+2);
                        fill(In, buf, (buf[0] & 0xff) + 2);
                        break;
                    case 4:
                        //In.Read(buf, 0, 18);
                        fill(In, buf, 18);
                        break;
                    default:
                        break;
                }
            }
                /*
            catch (RuntimeException e)
            {
                throw e;
            }
                 */
            catch (Exception e)
            {
                try { if (socket != null)socket.Close(); }
                catch //(Exception eee)
                {
                }
                string message = "ProxySOCKS5: " + e.ToString();
                throw new JSchException(message,e);
            }
        }
        public Stream getInputStream() { return In; }
        public Stream getOutputStream() { return Out; }
        public TcpClient getSocket() { return socket; }
        public void Close()
        {
            try
            {
                if (In != null) In.Close();
                if (Out != null) Out.Close();
                if (socket != null) socket.Close();
            }
            catch //(Exception e)
            {
            }
            In = null;
            Out = null;
            socket = null;
        }
        public static int getDefaultPort()
        {
            return DEFAULTPORT;
        }
        private void fill(Stream In, byte[] buf, int len)
        {
            int s = 0;
            while (s < len)
            {
                int i = In.Read(buf, s, len - s);
                if (i <= 0)
                {
                    throw new JSchException("ProxySOCKS5: stream is closed");
                }
                s += i;
            }
        }
    }
}
