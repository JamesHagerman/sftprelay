﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SharpSSH.NG
{
    class RequestWindowChange : Request
    {
        int width_columns = 80;
        int height_rows = 24;
        int width_pixels = 640;
        int height_pixels = 480;
        internal void setSize(int col, int row, int wp, int hp)
        {
            this.width_columns = col;
            this.height_rows = row;
            this.width_pixels = wp;
            this.height_pixels = hp;
        }
        internal override void request(Session session, Channel channel)
        {
            base.request(session, channel);

            Buffer buf = new Buffer();
            Packet packet = new Packet(buf);

            //byte      SSH_MSG_CHANNEL_REQUEST
            //uint32    recipient_channel
            //string    "window-change"
            //bool   FALSE
            //uint32    terminal width, columns
            //uint32    terminal height, rows
            //uint32    terminal width, pixels
            //uint32    terminal height, pixels
            packet.reset();
            buf.putByte((byte)Session.SSH_MSG_CHANNEL_REQUEST);
            buf.putInt(channel.getRecipient());
            buf.putString("window-change".getBytes());
            buf.putByte((byte)(waitForReply() ? 1 : 0));
            buf.putInt(width_columns);
            buf.putInt(height_rows);
            buf.putInt(width_pixels);
            buf.putInt(height_pixels);
            write(packet);
        }
    }
}
