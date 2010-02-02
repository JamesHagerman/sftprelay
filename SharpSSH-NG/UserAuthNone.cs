﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SharpSSH.NG
{
    class UserAuthNone : UserAuth
    {
        private const int SSH_MSG_SERVICE_ACCEPT = 6;
        private string methods = null;

        public bool start(Session session)
        {
            base.start(session);


            // send
            // byte      SSH_MSG_SERVICE_REQUEST(5)
            // string    service name "ssh-userauth"
            packet.reset();
            buf.putByte((byte)Session.SSH_MSG_SERVICE_REQUEST);
            buf.putString("ssh-userauth".getBytes());
            session.write(packet);

            if (JSch.getLogger().isEnabled(Logger.INFO))
            {
                JSch.getLogger().log(Logger.INFO,
                                     "SSH_MSG_SERVICE_REQUEST sent");
            }

            // receive
            // byte      SSH_MSG_SERVICE_ACCEPT(6)
            // string    service name
            buf = session.read(buf);
            int command = buf.getCommand();

            bool result = (command == SSH_MSG_SERVICE_ACCEPT);

            if (JSch.getLogger().isEnabled(Logger.INFO))
            {
                JSch.getLogger().log(Logger.INFO,
                                     "SSH_MSG_SERVICE_ACCEPT received");
            }
            if (!result)
                return false;

            byte[] _username = null;
            _username = Util.str2byte(username);

            // send
            // byte      SSH_MSG_USERAUTH_REQUEST(50)
            // string    user name
            // string    service name ("ssh-connection")
            // string    "none"
            packet.reset();
            buf.putByte((byte)SSH_MSG_USERAUTH_REQUEST);
            buf.putString(_username);
            buf.putString("ssh-connection".getBytes());
            buf.putString("none".getBytes());
            session.write(packet);

            while (true)
            {
                buf = session.read(buf);
                command = buf.getCommand() & 0xff;

                if (command == SSH_MSG_USERAUTH_SUCCESS)
                {
                    return true;
                }
                if (command == SSH_MSG_USERAUTH_BANNER)
                {
                    buf.getInt(); buf.getByte(); buf.getByte();
                    byte[] _message = buf.getString();
                    byte[] lang = buf.getString();
                    string message = null;
                    try
                    {
                        message = new string(_message, "UTF-8");
                    }
                    catch (java.io.UnsupportedEncodingException e)
                    {
                        message = new string(_message);
                    }
                    if (userinfo != null)
                    {
                        try
                        {
                            userinfo.showMessage(message);
                        }
                        catch (RuntimeException ee)
                        {
                        }
                    }
                    goto loop;
                }
                if (command == SSH_MSG_USERAUTH_FAILURE)
                {
                    buf.getInt(); buf.getByte(); buf.getByte();
                    byte[] foo = buf.getString();
                    int partial_success = buf.getByte();
                    methods = new string(foo);
                    //System.err.println("UserAuthNONE: "+methods+
                    //		   " partial_success:"+(partial_success!=0));
                    //	if(partial_success!=0){
                    //	  throw new JSchPartialAuthException(new string(foo));
                    //	}

                    break;
                }
                else
                {
                    //      System.err.println("USERAUTH fail ("+command+")");
                    throw new JSchException("USERAUTH fail (" + command + ")");
                }
            loop:
                null;
            }
            //throw new JSchException("USERAUTH fail");
            return false;
        }
        string getMethods()
        {
            return methods;
        }
    }
}
