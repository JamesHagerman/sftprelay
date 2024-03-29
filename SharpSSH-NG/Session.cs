﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using System.IO;
using System.Threading;

namespace SharpSSH.NG
{
    public class Session
    {
        private const string version = "JSCH-0.1.42";

        // http://ietf.org/internet-drafts/draft-ietf-secsh-assignednumbers-01.txt
        internal const int SSH_MSG_DISCONNECT = 1;
        internal const int SSH_MSG_IGNORE = 2;
        internal const int SSH_MSG_UNIMPLEMENTED = 3;
        internal const int SSH_MSG_DEBUG = 4;
        internal const int SSH_MSG_SERVICE_REQUEST = 5;
        internal const int SSH_MSG_SERVICE_ACCEPT = 6;
        internal const int SSH_MSG_KEXINIT = 20;
        internal const int SSH_MSG_NEWKEYS = 21;
        internal const int SSH_MSG_KEXDH_INIT = 30;
        internal const int SSH_MSG_KEXDH_REPLY = 31;
        internal const int SSH_MSG_KEX_DH_GEX_GROUP = 31;
        internal const int SSH_MSG_KEX_DH_GEX_INIT = 32;
        internal const int SSH_MSG_KEX_DH_GEX_REPLY = 33;
        internal const int SSH_MSG_KEX_DH_GEX_REQUEST = 34;
        internal const int SSH_MSG_GLOBAL_REQUEST = 80;
        internal const int SSH_MSG_REQUEST_SUCCESS = 81;
        internal const int SSH_MSG_REQUEST_FAILURE = 82;
        internal const int SSH_MSG_CHANNEL_OPEN = 90;
        internal const int SSH_MSG_CHANNEL_OPEN_CONFIRMATION = 91;
        internal const int SSH_MSG_CHANNEL_OPEN_FAILURE = 92;
        internal const int SSH_MSG_CHANNEL_WINDOW_ADJUST = 93;
        internal const int SSH_MSG_CHANNEL_DATA = 94;
        internal const int SSH_MSG_CHANNEL_EXTENDED_DATA = 95;
        internal const int SSH_MSG_CHANNEL_EOF = 96;
        internal const int SSH_MSG_CHANNEL_CLOSE = 97;
        internal const int SSH_MSG_CHANNEL_REQUEST = 98;
        internal const int SSH_MSG_CHANNEL_SUCCESS = 99;
        internal const int SSH_MSG_CHANNEL_FAILURE = 100;

        private byte[] V_S;                                 // server version
        private byte[] V_C = ("SSH-2.0-" + version).getBytes(); // client version

        private byte[] I_C; // the payload of the client's SSH_MSG_KEXINIT
        private byte[] I_S; // the payload of the server's SSH_MSG_KEXINIT
        //private byte[] K_S; // the host key

        private byte[] session_id;

        private byte[] IVc2s;
        private byte[] IVs2c;
        private byte[] Ec2s;
        private byte[] Es2c;
        private byte[] MACc2s;
        private byte[] MACs2c;

        private int seqi = 0;
        private int seqo = 0;

        string[] guess = null;
        private Cipher s2ccipher;
        private Cipher c2scipher;
        private MAC s2cmac;
        private MAC c2smac;
        //private byte[] mac_buf;
        private byte[] s2cmac_result1;
        private byte[] s2cmac_result2;

        private Compression deflater;
        private Compression inflater;

        private IO io;
        private TcpClient socket;
        private int timeout = 0;

        private bool isConnected = false;

        private bool isAuthed = false;

        private Thread connectThread = null;
        private object _lock = new object();

        internal bool x11_forwarding = false;
        internal bool agent_forwarding = false;

        internal Stream In = null;
        internal Stream Out = null;

        internal static Random random;

        internal Buffer buf;
        internal Packet packet;

        SocketFactory socket_factory = null;

        private Dictionary<string,string> config = null;

        private Proxy proxy = null;
        private UserInfo userinfo;

        private string hostKeyAlias = null;
        private int serverAliveInterval = 0;
        private int serverAliveCountMax = 1;

        internal bool daemon_thread = false;

        internal string host = "127.0.0.1";
        internal int port = 22;

        internal string username = null;
        internal byte[] password = null;

        internal JSch jsch;

        internal Session(JSch jsch)
            : base()
        {

            this.jsch = jsch;
            buf = new Buffer();
            packet = new Packet(buf);
        }

        public void connect()
        {
            connect(timeout);
        }

        public void connect(int connectTimeout)
        {
            if (isConnected)
            {
                throw new JSchException("session is already connected");
            }

            io = new IO();
            if (random == null)
            {
                try
                {
                    Type c = Type.GetType(getConfig("random"));
                    random = (Random)(c.newInstance());
                }
                catch (Exception e)
                {
                    throw new JSchException(e.ToString(), e);
                }
            }
            Packet.setRandom(random);

            if (JSch.getLogger().isEnabled(Logger.INFO))
            {
                JSch.getLogger().log(Logger.INFO,
                                     "Connecting to " + host + " port " + port);
            }

            try
            {
                int i, j;

                if (proxy == null)
                {
                    Stream In;
                    Stream Out;
                    if (socket_factory == null)
                    {
                        socket = Util.createSocket(host, port, connectTimeout);
                        In = socket.GetStream();
                        Out = socket.GetStream();
                    }
                    else
                    {
                        socket = socket_factory.createSocket(host, port);
                        In = socket_factory.GetStream(socket);
                        Out = socket_factory.GetStream(socket);
                    }
                    //if(timeout>0){ socket.setSoTimeout(timeout); }
                    socket.NoDelay = true;
                    io.setInputStream(In);
                    io.setOutputStream(Out);
                }
                else
                {
                    lock (proxy)
                    {
                        proxy.connect(socket_factory, host, port, connectTimeout);
                        io.setInputStream(proxy.getInputStream());
                        io.setOutputStream(proxy.getOutputStream());
                        socket = proxy.getSocket();
                    }
                }

                if (connectTimeout > 0 && socket != null)
                {
                    socket.ReceiveTimeout = connectTimeout;
                    socket.SendTimeout = connectTimeout;
                    //socket.setSoTimeout(connectTimeout);
                }

                isConnected = true;

                if (JSch.getLogger().isEnabled(Logger.INFO))
                {
                    JSch.getLogger().log(Logger.INFO,
                                         "Connection established");
                }

                jsch.addSession(this);

                {
                    // Some Cisco devices will miss to read '\n' if it is sent separately.
                    byte[] foo = new byte[V_C.Length + 1];
                    Array.Copy(V_C, 0, foo, 0, V_C.Length);
                    foo[foo.Length - 1] = (byte)'\n';
                    io.put(foo, 0, foo.Length);
                }

                while (true)
                {
                    i = 0;
                    j = 0;
                    while (i < buf.buffer.Length)
                    {
                        j = io.getByte();
                        if (j < 0) break;
                        buf.buffer[i] = (byte)j; i++;
                        if (j == 10) break;
                    }
                    if (j < 0)
                    {
                        throw new JSchException("connection is closed by foreign host");
                    }

                    if (buf.buffer[i - 1] == 10)
                    {    // 0x0a
                        i--;
                        if (i > 0 && buf.buffer[i - 1] == 13)
                        {  // 0x0d
                            i--;
                        }
                    }

                    if (i <= 3 ||
                       ((i != buf.buffer.Length) &&
                        (buf.buffer[0] != 'S' || buf.buffer[1] != 'S' ||
                         buf.buffer[2] != 'H' || buf.buffer[3] != '-')))
                    {
                        // It must not start with 'SSH-'
                        //Console.Error.WriteLine(Encoding.UTF8.GetString(buf.buffer, 0, i);
                        continue;
                    }

                    if (i == buf.buffer.Length ||
                       i < 7 ||                                      // SSH-1.99 or SSH-2.0
                       (buf.buffer[4] == '1' && buf.buffer[6] != '9')  // SSH-1.5
                       )
                    {
                        throw new JSchException("invalid server's version string");
                    }
                    break;
                }

                V_S = new byte[i]; Array.Copy(buf.buffer, 0, V_S, 0, i);
                //Console.Error.WriteLine("V_S: ("+i+") ["+Encoding.UTF8.GetString(V_S)+"]");

                if (JSch.getLogger().isEnabled(Logger.INFO))
                {
                    JSch.getLogger().log(Logger.INFO,
                                         "Remote version string: " + Encoding.UTF8.GetString(V_S));
                    JSch.getLogger().log(Logger.INFO,
                                         "Local version string: " + Encoding.UTF8.GetString(V_C));
                }

                send_kexinit();

                buf = Read(buf);
                if (buf.getCommand() != SSH_MSG_KEXINIT)
                {
                    throw new JSchException("invalid protocol: " + buf.getCommand());
                }

                if (JSch.getLogger().isEnabled(Logger.INFO))
                {
                    JSch.getLogger().log(Logger.INFO,
                                         "SSH_MSG_KEXINIT received");
                }

                KeyExchange kex = receive_kexinit(buf);

                while (true)
                {
                    buf = Read(buf);
                    if (kex.getState() == buf.getCommand())
                    {
                        bool result = kex.next(buf);
                        if (!result)
                        {
                            //Console.Error.WriteLine("verify: "+result);
                            in_kex = false;
                            throw new JSchException("verify: " + result);
                        }
                    }
                    else
                    {
                        in_kex = false;
                        throw new JSchException("invalid protocol(kex): " + buf.getCommand());
                    }
                    if (kex.getState() == KeyExchange.STATE_END)
                    {
                        break;
                    }
                }

                try { checkHost(host, port, kex); }
                catch (JSchException ee)
                {
                    in_kex = false;
                    throw ee;
                }

                send_newkeys();

                // receive SSH_MSG_NEWKEYS(21)
                buf = Read(buf);
                //Console.Error.WriteLine("read: 21 ? "+buf.getCommand());
                if (buf.getCommand() == SSH_MSG_NEWKEYS)
                {

                    if (JSch.getLogger().isEnabled(Logger.INFO))
                    {
                        JSch.getLogger().log(Logger.INFO,
                                             "SSH_MSG_NEWKEYS received");
                    }

                    receive_newkeys(buf, kex);
                }
                else
                {
                    in_kex = false;
                    throw new JSchException("invalid protocol(newkyes): " + buf.getCommand());
                }

                bool auth = false;
                bool auth_cancel = false;

                UserAuth ua = null;
                try
                {
                    Type c = Type.GetType(getConfig("userauth.none"));
                    ua = (UserAuth)(c.newInstance());
                }
                catch (Exception e)
                {
                    throw new JSchException(e.ToString(), e);
                }

                auth = ua.start(this);

                string cmethods = getConfig("PreferredAuthentications");
                string[] cmethoda = Util.split(cmethods, ",");

                string smethods = null;
                if (!auth)
                {
                    smethods = ((UserAuthNone)ua).getMethods();
                    if (smethods != null)
                    {
                        smethods = smethods.ToLower();
                    }
                    else
                    {
                        // methods: publickey,password,keyboard-interactive
                        //smethods="publickey,password,keyboard-interactive";
                        smethods = cmethods;
                    }
                }

                string[] smethoda = Util.split(smethods, ",");

                int methodi = 0;

                while (true)
                {

                loop:

                    while (!auth &&
                          cmethoda != null && methodi < cmethoda.Length)
                    {

                        string method = cmethoda[methodi++];
                        bool acceptable = false;
                        for (int k = 0; k < smethoda.Length; k++)
                        {
                            
                            if (smethoda[k].Equals(method))
                            {
                                acceptable = true;
                                break;
                            }
                        }
                        if (!acceptable)
                        {

                            continue;
                        }


                        if (JSch.getLogger().isEnabled(Logger.INFO))
                        {
                            string str = "Authentications that can continue: ";
                            for (int k = methodi - 1; k < cmethoda.Length; k++)
                            {
                                str += cmethoda[k];
                                if (k + 1 < cmethoda.Length)
                                    str += ",";
                            }
                            JSch.getLogger().log(Logger.INFO,
                                                 str);
                            JSch.getLogger().log(Logger.INFO,
                                                 "Next authentication method: " + method);
                        }

                        ua = null;
                        try
                        {
                            Type c = null;
                            if (getConfig("userauth." + method) != null)
                            {
                                c = Type.GetType(getConfig("userauth." + method));
                                ua = (UserAuth)(c.newInstance());
                            }
                        }
                        catch
                        {
                            if (JSch.getLogger().isEnabled(Logger.WARN))
                            {
                                JSch.getLogger().log(Logger.WARN,
                                                     "failed to load " + method + " method");
                            }
                        }

                        if (ua != null)
                        {
                            auth_cancel = false;
                            try
                            {
                                auth = ua.start(this);
                                if (auth &&
                                   JSch.getLogger().isEnabled(Logger.INFO))
                                {
                                    JSch.getLogger().log(Logger.INFO,
                                                         "Authentication succeeded (" + method + ").");
                                }
                            }
                            catch (JSchAuthCancelException )
                            {
                                auth_cancel = true;
                            }
                            catch (JSchPartialAuthException ee)
                            {
                                smethods = ee.getMethods();
                                smethoda = Util.split(smethods, ",");
                                methodi = 0;
                                //Console.Error.WriteLine("PartialAuth: "+methods);
                                auth_cancel = false;
                                goto loop;
                            }
                             /*
                            catch (RuntimeException ee)
                            {
                                throw ee;
                            }*/
                            catch //(Exception ee)
                            {
                                //Console.Error.WriteLine("ee: "+ee); // SSH_MSG_DISCONNECT: 2 Too many authentication failures
                                goto outloop;
                            }
                        }
                    }
                    break;
                }
            outloop:
                if (!auth)
                {
                    if (auth_cancel)
                        throw new JSchException("Auth cancel");
                    throw new JSchException("Auth fail");
                }

                if (connectTimeout > 0 || timeout > 0)
                {
                    socket.SendTimeout = timeout;
                    socket.ReceiveTimeout = timeout;
                    //socket.setSoTimeout(timeout);
                }

                isAuthed = true;

                lock (_lock)
                {
                    if (isConnected)
                    {
                        connectThread = new Thread(run);
                        connectThread.Name="Connect thread " + host + " session";
                        if (daemon_thread)
                        {
                            connectThread.IsBackground=daemon_thread;
                        }
                        connectThread.Start();
                    }
                    else
                    {
                        // The session has been already down and
                        // we don't have to start new thread.
                    }
                }
            }
            catch (Exception e)
            {
                in_kex = false;
                if (isConnected)
                {
                    try
                    {
                        packet.reset();
                        buf.putByte((byte)SSH_MSG_DISCONNECT);
                        buf.putInt(3);
                        buf.putString(e.ToString().getBytes());
                        buf.putString("en".getBytes());
                        write(packet);
                        disconnect();
                    }
                    catch //(Exception ee)
                    {
                    }
                }
                isConnected = false;
                //e.printStackTrace();
                /*if (e is RuntimeException) throw (RuntimeException)e;*/
                if (e is JSchException) throw (JSchException)e;
                throw new JSchException("Session.connect: " + e);
            }
            finally
            {
                Util.bzero(this.password);
                this.password = null;
            }
        }

        private KeyExchange receive_kexinit(Buffer buf)
        {
            int j = buf.getInt();
            if (j != buf.getLength())
            {    // packet was compressed and
                buf.getByte();           // j is the size of deflated packet.
                I_S = new byte[buf.index - 5];
            }
            else
            {
                I_S = new byte[j - 1 - buf.getByte()];
            }
            Array.Copy(buf.buffer, buf.s, I_S, 0, I_S.Length);

            if (!in_kex)
            {     // We are in rekeying activated by the remote!
                send_kexinit();
            }

            guess = KeyExchange.guess(I_S, I_C);
            if (guess == null)
            {
                throw new JSchException("Algorithm negotiation fail");
            }

            if (!isAuthed &&
               (guess[KeyExchange.PROPOSAL_ENC_ALGS_CTOS].Equals("none") ||
                (guess[KeyExchange.PROPOSAL_ENC_ALGS_STOC].Equals("none"))))
            {
                throw new JSchException("NONE Cipher should not be chosen before authentification is successed.");
            }

            KeyExchange kex = null;
            try
            {
                Type c = Type.GetType(getConfig(guess[KeyExchange.PROPOSAL_KEX_ALGS]));
                kex = (KeyExchange)(c.newInstance());
            }
            catch (Exception e)
            {
                throw new JSchException(e.ToString(), e);
            }

            kex.init(this, V_S, V_C, I_S, I_C);
            return kex;
        }

        private bool in_kex = false;
        public void rekey()
        {
            send_kexinit();
        }
        private void send_kexinit()
        {
            if (in_kex)
                return;

            string cipherc2s = getConfig("cipher.c2s");
            string ciphers2c = getConfig("cipher.s2c");

            string[] not_available = checkCiphers(getConfig("CheckCiphers"));
            if (not_available != null && not_available.Length > 0)
            {
                cipherc2s = Util.diffString(cipherc2s, not_available);
                ciphers2c = Util.diffString(ciphers2c, not_available);
                if (cipherc2s == null || ciphers2c == null)
                {
                    throw new JSchException("There are not any available ciphers.");
                }
            }

            in_kex = true;

            // byte      SSH_MSG_KEXINIT(20)
            // byte[16]  cookie (random bytes)
            // string    kex_algorithms
            // string    server_host_key_algorithms
            // string    encryption_algorithms_client_to_server
            // string    encryption_algorithms_server_to_client
            // string    mac_algorithms_client_to_server
            // string    mac_algorithms_server_to_client
            // string    compression_algorithms_client_to_server
            // string    compression_algorithms_server_to_client
            // string    languages_client_to_server
            // string    languages_server_to_client
            Buffer buf = new Buffer();                // send_kexinit may be invoked
            Packet packet = new Packet(buf);          // by user thread.
            packet.reset();
            buf.putByte((byte)SSH_MSG_KEXINIT);
            lock (random)
            {
                random.fill(buf.buffer, buf.index, 16); buf.skip(16);
            }
            buf.putString(getConfig("kex").getBytes());
            buf.putString(getConfig("server_host_key").getBytes());
            buf.putString(cipherc2s.getBytes());
            buf.putString(ciphers2c.getBytes());
            buf.putString(getConfig("mac.c2s").getBytes());
            buf.putString(getConfig("mac.s2c").getBytes());
            buf.putString(getConfig("compression.c2s").getBytes());
            buf.putString(getConfig("compression.s2c").getBytes());
            buf.putString(getConfig("lang.c2s").getBytes());
            buf.putString(getConfig("lang.s2c").getBytes());
            buf.putByte((byte)0);
            buf.putInt(0);

            buf.setOffSet(5);
            I_C = new byte[buf.getLength()];
            buf.getByte(I_C);

            write(packet);

            if (JSch.getLogger().isEnabled(Logger.INFO))
            {
                JSch.getLogger().log(Logger.INFO,
                                     "SSH_MSG_KEXINIT sent");
            }
        }

        private void send_newkeys()
        {
            // send SSH_MSG_NEWKEYS(21)
            packet.reset();
            buf.putByte((byte)SSH_MSG_NEWKEYS);
            write(packet);

            if (JSch.getLogger().isEnabled(Logger.INFO))
            {
                JSch.getLogger().log(Logger.INFO,
                                     "SSH_MSG_NEWKEYS sent");
            }
        }

        private void checkHost(string chost, int port, KeyExchange kex)
        {
            string shkc = getConfig("StrictHostKeyChecking");

            if (hostKeyAlias != null)
            {
                chost = hostKeyAlias;
            }

            //Console.Error.WriteLine("shkc: "+shkc);

            byte[] K_S = kex.getHostKey();
            string key_type = kex.getKeyType();
            string key_fprint = kex.getFingerPrint();

            if (hostKeyAlias == null && port != 22)
            {
                chost = ("[" + chost + "]:" + port);
            }

            //    hostkey=new HostKey(chost, K_S);

            HostKeyRepository hkr = jsch.getHostKeyRepository();
            int i = 0;
            lock (hkr)
            {
                i = hkr.check(chost, K_S);
            }

            bool insert = false;

            if ((shkc.Equals("ask") || shkc.Equals("yes")) &&
               i == HostKeyRepository.CHANGED)
            {
                string file = null;
                lock (hkr)
                {
                    file = hkr.getKnownHostsRepositoryID();
                }
                if (file == null) { file = "known_hosts"; }

                bool b = false;

                if (userinfo != null)
                {
                    string message =
            "WARNING: REMOTE HOST IDENTIFICATION HAS CHANGED!\n" +
            "IT IS POSSIBLE THAT SOMEONE IS DOING SOMETHING NASTY!\n" +
            "Someone could be eavesdropping on you right now (man-in-the-middle attack)!\n" +
            "It is also possible that the " + key_type + " host key has just been changed.\n" +
            "The fingerprint for the " + key_type + " key sent by the remote host is\n" +
            key_fprint + ".\n" +
            "Please contact your system administrator.\n" +
            "Add correct host key in " + file + " to get rid of this message.";

                    if (shkc.Equals("ask"))
                    {
                        b = userinfo.promptYesNo(message +
                                               "\nDo you want to delete the old key and insert the new key?");
                    }
                    else
                    {  // shkc.Equals("yes")
                        userinfo.showMessage(message);
                    }
                }

                if (!b)
                {
                    throw new JSchException("HostKey has been changed: " + chost);
                }

                lock (hkr)
                {
                    hkr.remove(chost,
                               (key_type.Equals("DSA") ? "ssh-dss" : "ssh-rsa"),
                               null);
                    insert = true;
                }
            }

            if ((shkc.Equals("ask") || shkc.Equals("yes")) &&
               (i != HostKeyRepository.OK) && !insert)
            {
                if (shkc.Equals("yes"))
                {
                    throw new JSchException("reject HostKey: " + host);
                }
                //Console.Error.WriteLine("finger-print: "+key_fprint);
                if (userinfo != null)
                {
                    bool foo = userinfo.promptYesNo(
                "The authenticity of host '" + host + "' can't be established.\n" +
                key_type + " key fingerprint is " + key_fprint + ".\n" +
                "Are you sure you want to continue connecting?"
                                     );
                    if (!foo)
                    {
                        throw new JSchException("reject HostKey: " + host);
                    }
                    insert = true;
                }
                else
                {
                    if (i == HostKeyRepository.NOT_INCLUDED)
                        throw new JSchException("UnknownHostKey: " + host + ". " + key_type + " key fingerprint is " + key_fprint);
                    else
                        throw new JSchException("HostKey has been changed: " + host);
                }
            }

            if (shkc.Equals("no") &&
               HostKeyRepository.NOT_INCLUDED == i)
            {
                insert = true;
            }

            if (i == HostKeyRepository.OK &&
               JSch.getLogger().isEnabled(Logger.INFO))
            {
                JSch.getLogger().log(Logger.INFO,
                                     "Host '" + host + "' is known and mathces the " + key_type + " host key");
            }

            if (insert &&
               JSch.getLogger().isEnabled(Logger.WARN))
            {
                JSch.getLogger().log(Logger.WARN,
                                     "Permanently added '" + host + "' (" + key_type + ") to the list of known hosts.");
            }

            string hkh = getConfig("HashKnownHosts");
            if (hkh.Equals("yes") && (hkr is KnownHosts))
            {
                hostkey = ((KnownHosts)hkr).createHashedHostKey(chost, K_S);
            }
            else
            {
                hostkey = new HostKey(chost, K_S);
            }

            if (insert)
            {
                lock (hkr)
                {
                    hkr.add(hostkey, userinfo);
                }

            }

        }

        //public void start(){ (new Thread(this.run)).start();  }

        public Channel openChannel(string type)
        {
            if (!isConnected)
            {
                throw new JSchException("session is down");
            }
            try
            {
                Channel channel = Channel.getChannel(type);
                addChannel(channel);
                channel.init();
                return channel;
            }
            catch //(Exception e)
            {
                //e.printStackTrace();
            }
            return null;
        }

        // encode will bin invoked in write with synchronization.
        public void encode(Packet packet)
        {
            //Console.Error.WriteLine("encode: "+packet.buffer.getCommand());
            //Console.Error.WriteLine("        "+packet.buffer.index);
            //if(packet.buffer.getCommand()==96){
            //Thread.dumpStack();
            //}
            if (deflater != null)
            {
                packet.buffer.index = deflater.compress(packet.buffer.buffer,
                                  5, packet.buffer.index);
            }
            if (c2scipher != null)
            {
                //packet.padding(c2scipher.getIVSize());
                packet.padding(c2scipher_size);
                int pad = packet.buffer.buffer[4];
                lock (random)
                {
                    random.fill(packet.buffer.buffer, packet.buffer.index - pad, pad);
                }
            }
            else
            {
                packet.padding(8);
            }

            if (c2smac != null)
            {
                c2smac.update(seqo);
                c2smac.update(packet.buffer.buffer, 0, packet.buffer.index);
                c2smac.doFinal(packet.buffer.buffer, packet.buffer.index);
            }
            if (c2scipher != null)
            {
                byte[] buf = packet.buffer.buffer;
                c2scipher.update(buf, 0, packet.buffer.index, buf, 0);
            }
            if (c2smac != null)
            {
                packet.buffer.skip(c2smac.getBlockSize());
            }
        }

        int[] uncompress_len = new int[1];

        private int s2ccipher_size = 8;
        private int c2scipher_size = 8;
        public Buffer Read(Buffer buf)
        {
            int j = 0;
            while (true)
            {
                buf.reset();
                io.getByte(buf.buffer, buf.index, s2ccipher_size);
                buf.index += s2ccipher_size;
                if (s2ccipher != null)
                {
                    s2ccipher.update(buf.buffer, 0, s2ccipher_size, buf.buffer, 0);
                }

                j = JavaCompat.ToInt32Big(buf.buffer, 0);

                //j = unchecked((int)((uint)((((uint)buf.buffer[0] << 24) & 0xff000000U) |
                //  ((buf.buffer[1] << 16) & 0x00ff0000U) |
                //  ((buf.buffer[2] << 8) & 0x0000ff00U) |
                //  ((buf.buffer[3]) & 0x000000ffU))));
                // RFC 4253 6.1. Maximum Packet Length
                if (j < 5 || j > (32768 - 4))
                {
                    throw new IOException("invalid data");
                }
                j = j + 4 - s2ccipher_size;
                //if(j<0){
                //  throw new IOException("invalid data");
                //}
                if ((buf.index + j) > buf.buffer.Length)
                {
                    byte[] foo = new byte[buf.index + j];
                    Array.Copy(buf.buffer, 0, foo, 0, buf.index);
                    buf.buffer = foo;
                }

                if ((j % s2ccipher_size) != 0)
                {
                    string message = "Bad packet length " + j;
                    if (JSch.getLogger().isEnabled(Logger.FATAL))
                    {
                        JSch.getLogger().log(Logger.FATAL, message);
                    }
                    packet.reset();
                    buf.putByte((byte)SSH_MSG_DISCONNECT);
                    buf.putInt(3);
                    buf.putString(message.getBytes());
                    buf.putString("en".getBytes());
                    write(packet);
                    disconnect();
                    throw new JSchException("SSH_MSG_DISCONNECT: " + message);
                }

                if (j > 0)
                {
                    io.getByte(buf.buffer, buf.index, j); buf.index += (j);
                    if (s2ccipher != null)
                    {
                        s2ccipher.update(buf.buffer, s2ccipher_size, j, buf.buffer, s2ccipher_size);
                    }
                }

                if (s2cmac != null)
                {
                    s2cmac.update(seqi);
                    s2cmac.update(buf.buffer, 0, buf.index);

                    s2cmac.doFinal(s2cmac_result1, 0);
                    io.getByte(s2cmac_result2, 0, s2cmac_result2.Length);
                    if (!Util.array_equals(s2cmac_result1, s2cmac_result2))
                    {
                        throw new IOException("MAC Error");
                    }
                }

                seqi++;

                if (inflater != null)
                {
                    //inflater.uncompress(buf);
                    int pad = buf.buffer[4];
                    uncompress_len[0] = buf.index - 5 - pad;
                    byte[] foo = inflater.uncompress(buf.buffer, 5, uncompress_len);
                    if (foo != null)
                    {
                        buf.buffer = foo;
                        buf.index = 5 + uncompress_len[0];
                    }
                    else
                    {
                        Console.Error.WriteLine("fail in inflater");
                        break;
                    }
                }

                int type = buf.getCommand() & 0xff;
                //Console.Error.WriteLine("read: "+type);
                if (type == SSH_MSG_DISCONNECT)
                {
                    buf.rewind();
                    buf.getInt(); buf.getShort();
                    int reason_code = buf.getInt();
                    byte[] description = buf.getString();
                    byte[] language_tag = buf.getString();
                    throw new JSchException("SSH_MSG_DISCONNECT: " +
                                    reason_code +
                                " " + Encoding.Default.GetString(description) +
                                " " + Encoding.Default.GetString(language_tag));
                    //break;
                }
                else if (type == SSH_MSG_IGNORE)
                {
                }
                else if (type == SSH_MSG_UNIMPLEMENTED)
                {
                    buf.rewind();
                    buf.getInt(); buf.getShort();
                    int reason_id = buf.getInt();
                    if (JSch.getLogger().isEnabled(Logger.INFO))
                    {
                        JSch.getLogger().log(Logger.INFO,
                                             "Received SSH_MSG_UNIMPLEMENTED for " + reason_id);
                    }
                }
                else if (type == SSH_MSG_DEBUG)
                {
                    buf.rewind();
                    buf.getInt(); buf.getShort();
                    /*
                        byte always_display=(byte)buf.getByte();
                        byte[] message=buf.getString();
                        byte[] language_tag=buf.getString();
                        Console.Error.WriteLine("SSH_MSG_DEBUG:"+
                                   " "+Encoding.UTF8.GetString(message)+
                                   " "+Encoding.UTF8.GetString(language_tag));
                    */
                }
                else if (type == SSH_MSG_CHANNEL_WINDOW_ADJUST)
                {
                    buf.rewind();
                    buf.getInt(); buf.getShort();
                    Channel c = Channel.getChannel(buf.getInt(), this);
                    if (c == null)
                    {
                    }
                    else
                    {
                        c.addRemoteWindowSize(buf.getInt());
                    }
                }
                else if (type == 52/*SSH_MSG_USERAUTH_SUCCESS*/)
                {
                    isAuthed = true;
                    if (inflater == null && deflater == null)
                    {
                        string method;
                        method = guess[KeyExchange.PROPOSAL_COMP_ALGS_CTOS];
                        initDeflater(method);

                        method = guess[KeyExchange.PROPOSAL_COMP_ALGS_STOC];
                        initInflater(method);
                    }
                    break;
                }
                else
                {
                    break;
                }
            }
            buf.rewind();
            return buf;
        }

        internal byte[] getSessionId()
        {
            return session_id;
        }

        private void receive_newkeys(Buffer buf, KeyExchange kex)
        {
            updateKeys(kex);
            in_kex = false;
        }
        private void updateKeys(KeyExchange kex)
        {
            byte[] K = kex.getK();
            byte[] H = kex.getH();
            HASH hash = kex.getHash();

            //    string[] guess=kex.guess;

            if (session_id == null)
            {
                session_id = new byte[H.Length];
                Array.Copy(H, 0, session_id, 0, H.Length);
            }

            /*
              Initial IV client to server:     HASH (K || H || "A" || session_id)
              Initial IV server to client:     HASH (K || H || "B" || session_id)
              Encryption key client to server: HASH (K || H || "C" || session_id)
              Encryption key server to client: HASH (K || H || "D" || session_id)
              Integrity key client to server:  HASH (K || H || "E" || session_id)
              Integrity key server to client:  HASH (K || H || "F" || session_id)
            */

            buf.reset();
            buf.putMPInt(K);
            buf.putByte(H);
            buf.putByte((byte)0x41);
            buf.putByte(session_id);
            hash.update(buf.buffer, 0, buf.index);
            IVc2s = hash.digest();

            int j = buf.index - session_id.Length - 1;

            buf.buffer[j]++;
            hash.update(buf.buffer, 0, buf.index);
            IVs2c = hash.digest();

            buf.buffer[j]++;
            hash.update(buf.buffer, 0, buf.index);
            Ec2s = hash.digest();

            buf.buffer[j]++;
            hash.update(buf.buffer, 0, buf.index);
            Es2c = hash.digest();

            buf.buffer[j]++;
            hash.update(buf.buffer, 0, buf.index);
            MACc2s = hash.digest();

            buf.buffer[j]++;
            hash.update(buf.buffer, 0, buf.index);
            MACs2c = hash.digest();

            try
            {
                Type c;
                string method;

                method = guess[KeyExchange.PROPOSAL_ENC_ALGS_STOC];
                c = Type.GetType(getConfig(method));
                s2ccipher = (Cipher)(c.newInstance());
                while (s2ccipher.getBlockSize() > Es2c.Length)
                {
                    buf.reset();
                    buf.putMPInt(K);
                    buf.putByte(H);
                    buf.putByte(Es2c);
                    hash.update(buf.buffer, 0, buf.index);
                    byte[] foo = hash.digest();
                    byte[] bar = new byte[Es2c.Length + foo.Length];
                    Array.Copy(Es2c, 0, bar, 0, Es2c.Length);
                    Array.Copy(foo, 0, bar, Es2c.Length, foo.Length);
                    Es2c = bar;
                }
                s2ccipher.init(Cipher.DECRYPT_MODE, Es2c, IVs2c);
                s2ccipher_size = s2ccipher.getIVSize();

                method = guess[KeyExchange.PROPOSAL_MAC_ALGS_STOC];
                c = Type.GetType(getConfig(method));
                s2cmac = (MAC)(c.newInstance());
                s2cmac.init(MACs2c);
                //mac_buf=new byte[s2cmac.getBlockSize()];
                s2cmac_result1 = new byte[s2cmac.getBlockSize()];
                s2cmac_result2 = new byte[s2cmac.getBlockSize()];

                method = guess[KeyExchange.PROPOSAL_ENC_ALGS_CTOS];
                c = Type.GetType(getConfig(method));
                c2scipher = (Cipher)(c.newInstance());
                while (c2scipher.getBlockSize() > Ec2s.Length)
                {
                    buf.reset();
                    buf.putMPInt(K);
                    buf.putByte(H);
                    buf.putByte(Ec2s);
                    hash.update(buf.buffer, 0, buf.index);
                    byte[] foo = hash.digest();
                    byte[] bar = new byte[Ec2s.Length + foo.Length];
                    Array.Copy(Ec2s, 0, bar, 0, Ec2s.Length);
                    Array.Copy(foo, 0, bar, Ec2s.Length, foo.Length);
                    Ec2s = bar;
                }
                c2scipher.init(Cipher.ENCRYPT_MODE, Ec2s, IVc2s);
                c2scipher_size = c2scipher.getIVSize();

                method = guess[KeyExchange.PROPOSAL_MAC_ALGS_CTOS];
                c = Type.GetType(getConfig(method));
                c2smac = (MAC)(c.newInstance());
                c2smac.init(MACc2s);

                method = guess[KeyExchange.PROPOSAL_COMP_ALGS_CTOS];
                initDeflater(method);

                method = guess[KeyExchange.PROPOSAL_COMP_ALGS_STOC];
                initInflater(method);
            }
            catch (Exception e)
            {
                if (e is JSchException)
                    throw e;
                throw new JSchException(e.ToString(), e);
                //Console.Error.WriteLine("updatekeys: "+e); 
            }
        }
        /*[MethodImpl(MethodImplOptions.Synchronized)]*/
        /*public*/
        /*synchronized*/
        internal void write(Packet packet, Channel c, int length)
        {
            while (true)
            {
                if (in_kex)
                {
                    Thread.Sleep(10);
                    continue;
                }
                lock (c)
                {
                    if (c.rwsize >= length)
                    {
                        c.rwsize -= length;
                        break;
                    }
                }
                if (c.close || !c.isConnected())
                {
                    throw new IOException("channel is broken");
                }

                bool sendit = false;
                int s = 0;
                byte command = 0;
                int recipient = -1;
                lock (c)
                {
                    if (c.rwsize > 0)
                    {
                        int len = c.rwsize;
                        if (len > length)
                        {
                            len = length;
                        }
                        if (len != length)
                        {
                            s = packet.shift(len, (c2smac != null ? c2smac.getBlockSize() : 0));
                        }
                        command = packet.buffer.getCommand();
                        recipient = c.getRecipient();
                        length -= len;
                        c.rwsize -= len;
                        sendit = true;
                    }
                }
                if (sendit)
                {
                    _write(packet);
                    if (length == 0)
                    {
                        return;
                    }
                    packet.unshift(command, recipient, s, length);
                }

                lock (c)
                {
                    if (in_kex)
                    {
                        continue;
                    }
                    if (c.rwsize >= length)
                    {
                        c.rwsize -= length;
                        break;
                    }
                    try
                    {
                        c.notifyme++;
                        Monitor.Wait(c, 100);
                    }
                    catch (ThreadInterruptedException )
                    {
                    }
                    finally
                    {
                        c.notifyme--;
                    }
                }

            }
            _write(packet);
        }

        public void write(Packet packet)
        {
            // Console.Error.WriteLine("in_kex="+in_kex+" "+(packet.buffer.getCommand()));
            while (in_kex)
            {
                byte command = packet.buffer.getCommand();
                //Console.Error.WriteLine("command: "+command);
                if (command == SSH_MSG_KEXINIT ||
                   command == SSH_MSG_NEWKEYS ||
                   command == SSH_MSG_KEXDH_INIT ||
                   command == SSH_MSG_KEXDH_REPLY ||
                   command == SSH_MSG_KEX_DH_GEX_GROUP ||
                   command == SSH_MSG_KEX_DH_GEX_INIT ||
                   command == SSH_MSG_KEX_DH_GEX_REPLY ||
                   command == SSH_MSG_KEX_DH_GEX_REQUEST ||
                   command == SSH_MSG_DISCONNECT)
                {
                    break;
                }
                try { Thread.Sleep(10); }
                catch (ThreadInterruptedException ) { };
            }
            _write(packet);
        }

        private void _write(Packet packet)
        {
            lock (_lock)
            {
                encode(packet);
                if (io != null)
                {
                    io.put(packet);
                    seqo++;
                }
            }
        }

        Thread thread;
        public void run()
        {
            thread = new Thread(this.run);

            byte[] foo;
            Buffer buf = new Buffer();
            Packet packet = new Packet(buf);
            int i = 0;
            Channel channel;
            int[] start = new int[1];
            int[] length = new int[1];
            KeyExchange kex = null;

            int stimeout = 0;
            try
            {
                while (isConnected &&
                  thread != null)
                {
                    try
                    {
                        buf = Read(buf);
                        stimeout = 0;
                    }
                    catch (IOException/*SocketTimeoutException*/ ee)
                    {
                        if (!in_kex && stimeout < serverAliveCountMax)
                        {
                            sendKeepAliveMsg();
                            stimeout++;
                            continue;
                        }
                        throw ee;
                    }

                    int msgType = buf.getCommand() & 0xff;

                    if (kex != null && kex.getState() == msgType)
                    {
                        bool result = kex.next(buf);
                        if (!result)
                        {
                            throw new JSchException("verify: " + result);
                        }
                        continue;
                    }

                    switch (msgType)
                    {
                        case SSH_MSG_KEXINIT:
                            //Console.Error.WriteLine("KEXINIT");
                            kex = receive_kexinit(buf);
                            break;

                        case SSH_MSG_NEWKEYS:
                            //Console.Error.WriteLine("NEWKEYS");
                            send_newkeys();
                            receive_newkeys(buf, kex);
                            kex = null;
                            break;

                        case SSH_MSG_CHANNEL_DATA:
                            buf.getInt();
                            buf.getByte();
                            buf.getByte();
                            i = buf.getInt();
                            channel = Channel.getChannel(i, this);
                            foo = buf.getString(start, length);
                            if (channel == null)
                            {
                                break;
                            }

                            if (length[0] == 0)
                            {
                                break;
                            }

                            try
                            {
                                channel.write(foo, start[0], length[0]);
                            }
                            catch //(Exception e)
                            {
                                //Console.Error.WriteLine(e);
                                try { channel.disconnect(); }
                                catch /* (Exception ee)*/ { }
                                break;
                            }
                            int len = length[0];
                            channel.setLocalWindowSize(channel.lwsize - len);
                            if (channel.lwsize < channel.lwsize_max / 2)
                            {
                                packet.reset();
                                buf.putByte((byte)SSH_MSG_CHANNEL_WINDOW_ADJUST);
                                buf.putInt(channel.getRecipient());
                                buf.putInt(channel.lwsize_max - channel.lwsize);
                                write(packet);
                                channel.setLocalWindowSize(channel.lwsize_max);
                            }
                            break;

                        case SSH_MSG_CHANNEL_EXTENDED_DATA:
                            buf.getInt();
                            buf.getShort();
                            i = buf.getInt();
                            channel = Channel.getChannel(i, this);
                            buf.getInt();                   // data_type_code == 1
                            foo = buf.getString(start, length);
                            //Console.Error.WriteLine("stderr: "+Encoding.UTF8.GetString(foo,start[0],length[0]));
                            if (channel == null)
                            {
                                break;
                            }

                            if (length[0] == 0)
                            {
                                break;
                            }

                            channel.write_ext(foo, start[0], length[0]);

                            len = length[0];
                            channel.setLocalWindowSize(channel.lwsize - len);
                            if (channel.lwsize < channel.lwsize_max / 2)
                            {
                                packet.reset();
                                buf.putByte((byte)SSH_MSG_CHANNEL_WINDOW_ADJUST);
                                buf.putInt(channel.getRecipient());
                                buf.putInt(channel.lwsize_max - channel.lwsize);
                                write(packet);
                                channel.setLocalWindowSize(channel.lwsize_max);
                            }
                            break;

                        case SSH_MSG_CHANNEL_WINDOW_ADJUST:
                            buf.getInt();
                            buf.getShort();
                            i = buf.getInt();
                            channel = Channel.getChannel(i, this);
                            if (channel == null)
                            {
                                break;
                            }
                            channel.addRemoteWindowSize(buf.getInt());
                            break;

                        case SSH_MSG_CHANNEL_EOF:
                            buf.getInt();
                            buf.getShort();
                            i = buf.getInt();
                            channel = Channel.getChannel(i, this);
                            if (channel != null)
                            {
                                //channel.eof_remote=true;
                                //channel.eof();
                                channel.EofRemote();
                            }
                            /*
                            packet.reset();
                            buf.putByte((byte)SSH_MSG_CHANNEL_EOF);
                            buf.putInt(channel.getRecipient());
                            write(packet);
                            */
                            break;
                        case SSH_MSG_CHANNEL_CLOSE:
                            buf.getInt();
                            buf.getShort();
                            i = buf.getInt();
                            channel = Channel.getChannel(i, this);
                            if (channel != null)
                            {
                                //	      channel.Close();
                                channel.disconnect();
                            }
                            /*
                                if(Channel.pool.Count==0){
                              thread=null;
                            }
                            */
                            break;
                        case SSH_MSG_CHANNEL_OPEN_CONFIRMATION:
                            buf.getInt();
                            buf.getShort();
                            i = buf.getInt();
                            channel = Channel.getChannel(i, this);
                            if (channel == null)
                            {
                                //break;
                            }
                            int r = buf.getInt();
                            int rws = buf.getInt();
                            int rps = buf.getInt();

                            channel.setRemoteWindowSize(rws);
                            channel.setRemotePacketSize(rps);
                            channel.setRecipient(r);
                            break;
                        case SSH_MSG_CHANNEL_OPEN_FAILURE:
                            buf.getInt();
                            buf.getShort();
                            i = buf.getInt();
                            channel = Channel.getChannel(i, this);
                            if (channel == null)
                            {
                                //break;
                            }
                            int reason_code = buf.getInt();
                            //foo=buf.getString();  // additional textual information
                            //foo=buf.getString();  // language tag 
                            channel.exitstatus = reason_code;
                            channel.close = true;
                            channel.eof_remote = true;
                            channel.setRecipient(0);
                            break;
                        case SSH_MSG_CHANNEL_REQUEST:
                            buf.getInt();
                            buf.getShort();
                            i = buf.getInt();
                            foo = buf.getString();
                            bool reply = (buf.getByte() != 0);
                            channel = Channel.getChannel(i, this);
                            if (channel != null)
                            {
                                byte reply_type = (byte)SSH_MSG_CHANNEL_FAILURE;
                                if ((Encoding.UTF8.GetString(foo)).Equals("exit-status"))
                                {
                                    i = buf.getInt();             // exit-status
                                    channel.setExitStatus(i);
                                    //	    Console.Error.WriteLine("exit-stauts: "+i);
                                    //          channel.Close();
                                    reply_type = (byte)SSH_MSG_CHANNEL_SUCCESS;
                                }
                                if (reply)
                                {
                                    packet.reset();
                                    buf.putByte(reply_type);
                                    buf.putInt(channel.getRecipient());
                                    write(packet);
                                }
                            }
                            else
                            {
                            }
                            break;
                        case SSH_MSG_CHANNEL_OPEN:
                            buf.getInt();
                            buf.getShort();
                            foo = buf.getString();
                            string ctyp = Encoding.UTF8.GetString(foo);
                            if (!"forwarded-tcpip".Equals(ctyp) &&
                           !("x11".Equals(ctyp) && x11_forwarding) &&
                           !("auth-agent@openssh.com".Equals(ctyp) && agent_forwarding))
                            {
                                //Console.Error.WriteLine("Session.run: CHANNEL OPEN "+ctyp); 
                                //throw new IOException("Session.run: CHANNEL OPEN "+ctyp);
                                packet.reset();
                                buf.putByte((byte)SSH_MSG_CHANNEL_OPEN_FAILURE);
                                buf.putInt(buf.getInt());
                                buf.putInt(Channel.SSH_OPEN_ADMINISTRATIVELY_PROHIBITED);
                                buf.putString("".getBytes());
                                buf.putString("".getBytes());
                                write(packet);
                                break;
                            }
                            else
                            {
                                channel = Channel.getChannel(ctyp);
                                addChannel(channel);
                                channel.getData(buf);
                                channel.init();

                                Thread tmp = new Thread(channel.run);
                                tmp.Name="Channel " + ctyp + " " + host;
                                if (daemon_thread)
                                {
                                    tmp.IsBackground=daemon_thread;
                                }
                                tmp.Start();
                                break;
                            }
                        case SSH_MSG_CHANNEL_SUCCESS:
                            buf.getInt();
                            buf.getShort();
                            i = buf.getInt();
                            channel = Channel.getChannel(i, this);
                            if (channel == null)
                            {
                                break;
                            }
                            channel.reply = 1;
                            break;
                        case SSH_MSG_CHANNEL_FAILURE:
                            buf.getInt();
                            buf.getShort();
                            i = buf.getInt();
                            channel = Channel.getChannel(i, this);
                            if (channel == null)
                            {
                                break;
                            }
                            channel.reply = 0;
                            break;
                        case SSH_MSG_GLOBAL_REQUEST:
                            buf.getInt();
                            buf.getShort();
                            foo = buf.getString();       // request name
                            reply = (buf.getByte() != 0);
                            if (reply)
                            {
                                packet.reset();
                                buf.putByte((byte)SSH_MSG_REQUEST_FAILURE);
                                write(packet);
                            }
                            break;
                        case SSH_MSG_REQUEST_FAILURE:
                        case SSH_MSG_REQUEST_SUCCESS:
                            Thread t = grr.getThread();
                            if (t != null)
                            {
                                grr.setReply(msgType == SSH_MSG_REQUEST_SUCCESS ? 1 : 0);
                                t.Interrupt();
                            }
                            break;
                        default:
                            //Console.Error.WriteLine("Session.run: unsupported type "+msgType); 
                            throw new IOException("Unknown SSH message type " + msgType);
                    }
                }
            }
            catch (Exception e)
            {
                if (JSch.getLogger().isEnabled(Logger.INFO))
                {
                    JSch.getLogger().log(Logger.INFO,
                                         "Caught an exception, leaving main loop due to " + e.Message);
                }
                //Console.Error.WriteLine("# Session.run");
                //e.printStackTrace();
            }
            try
            {
                disconnect();
            }
            catch (NullReferenceException )
            {
                //Console.Error.WriteLine("@1");
                //e.printStackTrace();
            }
            catch //(Exception e)
            {
                //Console.Error.WriteLine("@2");
                //e.printStackTrace();
            }
            isConnected = false;
        }

        public void disconnect()
        {
            if (!isConnected) return;
            //Console.Error.WriteLine(this+": disconnect");
            //Thread.dumpStack();
            if (JSch.getLogger().isEnabled(Logger.INFO))
            {
                JSch.getLogger().log(Logger.INFO,
                                     "Disconnecting from " + host + " port " + port);
            }
            /*
            for(int i=0; i<Channel.pool.Count; i++){
              try{
                Channel c=Channel.pool[i];
            if(c.session==this) c.eof();
              }
              catch(Exception e){
              }
            } 
            */

            Channel.disconnect(this);

            isConnected = false;

            PortWatcher.delPort(this);
            ChannelForwardedTCPIP.delPort(this);

            lock (_lock)
            {
                if (connectThread != null)
                {
                    Thread.Sleep(1); // .yield();
                    connectThread.Interrupt();
                    connectThread = null;
                }
            }
            thread = null;
            try
            {
                if (io != null)
                {
                    if (io.In != null) io.In.Close();
                    if (io.Out != null) io.Out.Close();
                    if (io.out_ext != null) io.out_ext.Close();
                }
                if (proxy == null)
                {
                    if (socket != null)
                        socket.Close();
                }
                else
                {
                    lock (proxy)
                    {
                        proxy.Close();
                    }
                    proxy = null;
                }
            }
            catch //(Exception e)
            {
                //      e.printStackTrace();
            }
            io = null;
            socket = null;
            //    lock(jsch.pool){
            //      jsch.pool.Remove(this);
            //    }

            jsch.removeSession(this);

            //System.gc();
        }

        public int setPortForwardingL(int lport, string host, int rport)
        {
            return setPortForwardingL("127.0.0.1", lport, host, rport);
        }
        public int setPortForwardingL(string boundaddress, int lport, string host, int rport)
        {
            return setPortForwardingL(boundaddress, lport, host, rport, null);
        }
        public int setPortForwardingL(string boundaddress, int lport, string host, int rport, ServerSocketFactory ssf)
        {
            PortWatcher pw = PortWatcher.addPort(this, boundaddress, lport, host, rport, ssf);
            Thread tmp = new Thread(pw.run);
            tmp.Name="PortWatcher Thread for " + host;
            if (daemon_thread)
            {
                tmp.IsBackground=daemon_thread;
            }
            tmp.Start();
            return pw.lport;
        }
        public void delPortForwardingL(int lport)
        {
            delPortForwardingL("127.0.0.1", lport);
        }
        public void delPortForwardingL(string boundaddress, int lport)
        {
            PortWatcher.delPort(this, boundaddress, lport);
        }
        public string[] getPortForwardingL()
        {
            return PortWatcher.getPortForwarding(this);
        }

        public void setPortForwardingR(int rport, string host, int lport)
        {
            setPortForwardingR(null, rport, host, lport, (SocketFactory)null);
        }
        public void setPortForwardingR(string bind_address, int rport, string host, int lport)
        {
            setPortForwardingR(bind_address, rport, host, lport, (SocketFactory)null);
        }
        public void setPortForwardingR(int rport, string host, int lport, SocketFactory sf)
        {
            setPortForwardingR(null, rport, host, lport, sf);
        }
        public void setPortForwardingR(string bind_address, int rport, string host, int lport, SocketFactory sf)
        {
            ChannelForwardedTCPIP.addPort(this, bind_address, rport, host, lport, sf);
            setPortForwarding(bind_address, rport);
        }

        public void setPortForwardingR(int rport, string daemon)
        {
            setPortForwardingR(null, rport, daemon, null);
        }
        public void setPortForwardingR(int rport, string daemon, object[] arg)
        {
            setPortForwardingR(null, rport, daemon, arg);
        }
        public void setPortForwardingR(string bind_address, int rport, string daemon, object[] arg)
        {
            ChannelForwardedTCPIP.addPort(this, bind_address, rport, daemon, arg);
            setPortForwarding(bind_address, rport);
        }

        private class GlobalRequestReply
        {
            private Thread thread = null;
            private int reply = -1;
            internal void setThread(Thread thread)
            {
                this.thread = thread;
                this.reply = -1;
            }
            internal Thread getThread() { return thread; }
            internal void setReply(int reply) { this.reply = reply; }
            internal int getReply() { return this.reply; }
        }
        private GlobalRequestReply grr = new GlobalRequestReply();
        private void setPortForwarding(string bind_address, int rport)
        {
            lock (grr)
            {
                Buffer buf = new Buffer(100); // ??
                Packet packet = new Packet(buf);

                string address_to_bind = ChannelForwardedTCPIP.normalize(bind_address);

                try
                {
                    // byte SSH_MSG_GLOBAL_REQUEST 80
                    // string "tcpip-forward"
                    // bool want_reply
                    // string  address_to_bind
                    // uint32  port number to bind
                    packet.reset();
                    buf.putByte((byte)SSH_MSG_GLOBAL_REQUEST);
                    buf.putString("tcpip-forward".getBytes());
                    //      buf.putByte((byte)0);
                    buf.putByte((byte)1);
                    buf.putString(address_to_bind.getBytes());
                    buf.putInt(rport);
                    write(packet);
                }
                catch (Exception e)
                {
                    throw new JSchException(e.Message,e);
                }

                grr.setThread(Thread.CurrentThread);
                try { Thread.Sleep(10000); }
                catch //(Exception e)
                {
                }
                int reply = grr.getReply();
                grr.setThread(null);
                if (reply == 0)
                {
                    throw new JSchException("remote port forwarding failed for listen port " + rport);
                }
            }
        }
        public void delPortForwardingR(int rport)
        {
            ChannelForwardedTCPIP.delPort(this, rport);
        }

        private void initDeflater(string method)
        {
            if (method.Equals("none"))
            {
                deflater = null;
                return;
            }
            string foo = getConfig(method);
            if (foo != null)
            {
                if (method.Equals("zlib") ||
                   (isAuthed && method.Equals("zlib@openssh.com")))
                {
                    try
                    {
                        Type c = Type.GetType(foo);
                        deflater = (Compression)(c.newInstance());
                        int level = 6;
                        try { level = int.Parse(getConfig("compression_level")); }
                        catch /*(Exception ee)*/ { }
                        deflater.init(Compression.DEFLATER, level);
                    }
                    catch (Exception ee)
                    {
                        throw new JSchException(ee.ToString(), ee);
                        //Console.Error.WriteLine(foo+" isn't accessible.");
                    }
                }
            }
        }
        private void initInflater(string method)
        {
            if (method.Equals("none"))
            {
                inflater = null;
                return;
            }
            string foo = getConfig(method);
            if (foo != null)
            {
                if (method.Equals("zlib") ||
                   (isAuthed && method.Equals("zlib@openssh.com")))
                {
                    try
                    {
                        Type c = Type.GetType(foo);
                        inflater = (Compression)(c.newInstance());
                        inflater.init(Compression.INFLATER, 0);
                    }
                    catch (Exception ee)
                    {
                        throw new JSchException(ee.ToString(), ee);
                        //Console.Error.WriteLine(foo+" isn't accessible.");
                    }
                }
            }
        }

        internal void addChannel(Channel channel)
        {
            channel.setSession(this);
        }

        public void setProxy(Proxy proxy) { this.proxy = proxy; }
        public void setHost(string host) { this.host = host; }
        public void setPort(int port) { this.port = port; }
        internal void setUserName(string username) { this.username = username; }
        public void setUserInfo(UserInfo userinfo) { this.userinfo = userinfo; }
        public UserInfo getUserInfo() { return userinfo; }
        public void setInputStream(Stream In) { this.In = In; }
        public void setOutputStream(Stream Out) { this.Out = Out; }
        public void setX11Host(string host) { ChannelX11.setHost(host); }
        public void setX11Port(int port) { ChannelX11.setPort(port); }
        public void setX11Cookie(string cookie) { ChannelX11.setCookie(cookie); }
        public void setPassword(string password)
        {
            if (password != null)
                this.password = Util.str2byte(password);
        }
        public void setPassword(byte[] password)
        {
            if (password != null)
            {
                this.password = new byte[password.Length];
                Array.Copy(password, 0, this.password, 0, password.Length);
            }
        }

        public void setConfig(Dictionary<string,string> newconf)
        {
            lock (_lock)
            {
                if (config == null)
                    config = new Dictionary<string, string>();
                foreach (KeyValuePair<string, string> kv in newconf)
                {
                    config[kv.Key] = kv.Value;
                }
            }
        }

        public void setConfig(string key, string value)
        {
            lock (_lock)
            {
                if (config == null)
                {
                    config = new Dictionary<string,string>();
                }
                config[key]= value;
            }
        }

        public string getConfig(string key)
        {
            if (config != null)
            {
                if (config.ContainsKey(key))
                {
                    return config[key];
                }
            }
            return JSch.getConfig(key);
        }

        public void setSocketFactory(SocketFactory sfactory)
        {
            socket_factory = sfactory;
        }
        public bool Connected { get { return isConnected; } }
        public int getTimeout() { return timeout; }
        public void setTimeout(int timeout)
        {
            if (socket == null)
            {
                if (timeout < 0)
                {
                    throw new JSchException("invalid timeout value");
                }
                this.timeout = timeout;
                return;
            }
            try
            {
                socket.setSoTimeout(timeout);
                this.timeout = timeout;
            }
            catch (Exception e)
            {
                throw new JSchException(e.Message,e);
            }
        }
        public string getServerVersion()
        {
            return Encoding.UTF8.GetString(V_S);
        }
        public string getClientVersion()
        {
            return Encoding.UTF8.GetString(V_C);
        }
        public void setClientVersion(string cv)
        {
            V_C = cv.getBytes();
        }

        public void sendIgnore()
        {
            Buffer buf = new Buffer();
            Packet packet = new Packet(buf);
            packet.reset();
            buf.putByte((byte)SSH_MSG_IGNORE);
            write(packet);
        }

        private static readonly byte[] keepalivemsg = "keepalive@jcraft.com".getBytes();
        public void sendKeepAliveMsg()
        {
            Buffer buf = new Buffer();
            Packet packet = new Packet(buf);
            packet.reset();
            buf.putByte((byte)SSH_MSG_GLOBAL_REQUEST);
            buf.putString(keepalivemsg);
            buf.putByte((byte)1);
            write(packet);
        }

        private HostKey hostkey = null;
        public HostKey getHostKey() { return hostkey; }
        public string getHost() { return host; }
        public string getUserName() { return username; }
        public int getPort() { return port; }
        public void setHostKeyAlias(string hostKeyAlias)
        {
            this.hostKeyAlias = hostKeyAlias;
        }
        public string getHostKeyAlias()
        {
            return hostKeyAlias;
        }

        public void setServerAliveInterval(int interval)
        {
            setTimeout(interval);
            this.serverAliveInterval = interval;
        }
        public void setServerAliveCountMax(int count)
        {
            this.serverAliveCountMax = count;
        }

        public int getServerAliveInterval()
        {
            return this.serverAliveInterval;
        }
        public int getServerAliveCountMax()
        {
            return this.serverAliveCountMax;
        }

        public void setDaemonThread(bool enable)
        {
            this.daemon_thread = enable;
        }

        private string[] checkCiphers(string ciphers)
        {
            if (ciphers == null || ciphers.Length == 0)
                return null;

            if (JSch.getLogger().isEnabled(Logger.INFO))
            {
                JSch.getLogger().log(Logger.INFO,
                                     "CheckCiphers: " + ciphers);
            }

            List<string> result = new List<string>();
            string[] _ciphers = Util.split(ciphers, ",");
            for (int i = 0; i < _ciphers.Length; i++)
            {
                if (!checkCipher(getConfig(_ciphers[i])))
                {
                    result.Add(_ciphers[i]);
                }
            }
            if (result.Count == 0)
                return null;
            string[] foo = new string[result.Count];
            Array.Copy(result.ToArray(), 0, foo, 0, result.Count);

            if (JSch.getLogger().isEnabled(Logger.INFO))
            {
                for (int i = 0; i < foo.Length; i++)
                {
                    JSch.getLogger().log(Logger.INFO,
                                         foo[i] + " is not available.");
                }
            }

            return foo;
        }

        internal static bool checkCipher(string cipher)
        {
            try
            {
                Type c = Type.GetType(cipher);
                Cipher _c = (Cipher)(c.newInstance());
                _c.init(Cipher.ENCRYPT_MODE,
                        new byte[_c.getBlockSize()],
                        new byte[_c.getIVSize()]);
                return true;
            }
            catch //(Exception e)
            {
                return false;
            }
        }
    }
}
