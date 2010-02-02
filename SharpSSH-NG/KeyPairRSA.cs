﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SharpSSH.NG
{
    class KeyPairRSA : KeyPair
    {
        private byte[] prv_array;
        private byte[] pub_array;
        private byte[] n_array;

        private byte[] p_array;  // prime p
        private byte[] q_array;  // prime q
        private byte[] ep_array; // prime exponent p
        private byte[] eq_array; // prime exponent q
        private byte[] c_array;  // coefficient

        //private int key_size=0;
        private int key_size = 1024;

        public KeyPairRSA(JSch jsch) :
            base(jsch)
        {
        }

        protected override void generate(int key_size)
        {
            this.key_size = key_size;
            try
            {
                Class c = Class.forName(jsch.getConfig("keypairgen.rsa"));
                KeyPairGenRSA keypairgen = (KeyPairGenRSA)(c.newInstance());
                keypairgen.init(key_size);
                pub_array = keypairgen.getE();
                prv_array = keypairgen.getD();
                n_array = keypairgen.getN();

                p_array = keypairgen.getP();
                q_array = keypairgen.getQ();
                ep_array = keypairgen.getEP();
                eq_array = keypairgen.getEQ();
                c_array = keypairgen.getC();

                keypairgen = null;
            }
            catch (Exception e)
            {
                //Console.Error.WriteLine("KeyPairRSA: "+e); 
                if (e is Throwable)
                    throw new JSchException(e.toString(), (Throwable)e);
                throw new JSchException(e.toString());
            }
        }

        private static readonly byte[] begin = "-----BEGIN RSA PRIVATE KEY-----".getBytes();
        private static readonly byte[] end = "-----END RSA PRIVATE KEY-----".getBytes();

        protected override byte[] getBegin() { return begin; }
        protected override byte[] getEnd() { return end; }

        protected override byte[] getPrivateKey()
        {
            int content =
              1 + countLength(1) + 1 +                           // INTEGER
              1 + countLength(n_array.length) + n_array.length + // INTEGER  N
              1 + countLength(pub_array.length) + pub_array.length + // INTEGER  pub
              1 + countLength(prv_array.length) + prv_array.length +  // INTEGER  prv
              1 + countLength(p_array.length) + p_array.length +      // INTEGER  p
              1 + countLength(q_array.length) + q_array.length +      // INTEGER  q
              1 + countLength(ep_array.length) + ep_array.length +    // INTEGER  ep
              1 + countLength(eq_array.length) + eq_array.length +    // INTEGER  eq
              1 + countLength(c_array.length) + c_array.length;      // INTEGER  c

            int total =
              1 + countLength(content) + content;   // SEQUENCE

            byte[] plain = new byte[total];
            int index = 0;
            index = writeSEQUENCE(plain, index, content);
            index = writeINTEGER(plain, index, new byte[1]);  // 0
            index = writeINTEGER(plain, index, n_array);
            index = writeINTEGER(plain, index, pub_array);
            index = writeINTEGER(plain, index, prv_array);
            index = writeINTEGER(plain, index, p_array);
            index = writeINTEGER(plain, index, q_array);
            index = writeINTEGER(plain, index, ep_array);
            index = writeINTEGER(plain, index, eq_array);
            index = writeINTEGER(plain, index, c_array);
            return plain;
        }

        protected override bool parse(byte[] plain)
        {
            /*
            byte[] p_array;
            byte[] q_array;
            byte[] dmp1_array;
            byte[] dmq1_array;
            byte[] iqmp_array;
            */
            try
            {
                int index = 0;
                int length = 0;

                if (vendor == VENDOR_FSECURE)
                {
                    if (plain[index] != 0x30)
                    {                  // FSecure
                        Buffer buf = new Buffer(plain);
                        pub_array = buf.getMPIntBits();
                        prv_array = buf.getMPIntBits();
                        n_array = buf.getMPIntBits();
                        byte[] u_array = buf.getMPIntBits();
                        p_array = buf.getMPIntBits();
                        q_array = buf.getMPIntBits();
                        return true;
                    }
                    return false;
                }

                index++; // SEQUENCE
                length = plain[index++] & 0xff;
                if ((length & 0x80) != 0)
                {
                    int foo = length & 0x7f; length = 0;
                    while (foo-- > 0) { length = (length << 8) + (plain[index++] & 0xff); }
                }

                if (plain[index] != 0x02) return false;
                index++; // INTEGER
                length = plain[index++] & 0xff;
                if ((length & 0x80) != 0)
                {
                    int foo = length & 0x7f; length = 0;
                    while (foo-- > 0) { length = (length << 8) + (plain[index++] & 0xff); }
                }
                index += length;

                //Console.Error.WriteLine("int: len="+length);
                //System.err.print(Integer.toHexString(plain[index-1]&0xff)+":");
                //Console.Error.WriteLine("");

                index++;
                length = plain[index++] & 0xff;
                if ((length & 0x80) != 0)
                {
                    int foo = length & 0x7f; length = 0;
                    while (foo-- > 0) { length = (length << 8) + (plain[index++] & 0xff); }
                }
                n_array = new byte[length];
                Array.Copy(plain, index, n_array, 0, length);
                index += length;
                /*
                Console.Error.WriteLine("int: N len="+length);
                for(int i=0; i<n_array.length; i++){
                System.err.print(Integer.toHexString(n_array[i]&0xff)+":");
                }
                Console.Error.WriteLine("");
                */
                index++;
                length = plain[index++] & 0xff;
                if ((length & 0x80) != 0)
                {
                    int foo = length & 0x7f; length = 0;
                    while (foo-- > 0) { length = (length << 8) + (plain[index++] & 0xff); }
                }
                pub_array = new byte[length];
                Array.Copy(plain, index, pub_array, 0, length);
                index += length;
                /*
                Console.Error.WriteLine("int: E len="+length);
                for(int i=0; i<pub_array.length; i++){
                System.err.print(Integer.toHexString(pub_array[i]&0xff)+":");
                }
                Console.Error.WriteLine("");
                */
                index++;
                length = plain[index++] & 0xff;
                if ((length & 0x80) != 0)
                {
                    int foo = length & 0x7f; length = 0;
                    while (foo-- > 0) { length = (length << 8) + (plain[index++] & 0xff); }
                }
                prv_array = new byte[length];
                Array.Copy(plain, index, prv_array, 0, length);
                index += length;
                /*
                Console.Error.WriteLine("int: prv len="+length);
                for(int i=0; i<prv_array.length; i++){
                System.err.print(Integer.toHexString(prv_array[i]&0xff)+":");
                }
                Console.Error.WriteLine("");
                */

                index++;
                length = plain[index++] & 0xff;
                if ((length & 0x80) != 0)
                {
                    int foo = length & 0x7f; length = 0;
                    while (foo-- > 0) { length = (length << 8) + (plain[index++] & 0xff); }
                }
                p_array = new byte[length];
                Array.Copy(plain, index, p_array, 0, length);
                index += length;
                /*
                Console.Error.WriteLine("int: P len="+length);
                for(int i=0; i<p_array.length; i++){
                System.err.print(Integer.toHexString(p_array[i]&0xff)+":");
                }
                Console.Error.WriteLine("");
                */
                index++;
                length = plain[index++] & 0xff;
                if ((length & 0x80) != 0)
                {
                    int foo = length & 0x7f; length = 0;
                    while (foo-- > 0) { length = (length << 8) + (plain[index++] & 0xff); }
                }
                q_array = new byte[length];
                Array.Copy(plain, index, q_array, 0, length);
                index += length;
                /*
                Console.Error.WriteLine("int: q len="+length);
                for(int i=0; i<q_array.length; i++){
                System.err.print(Integer.toHexString(q_array[i]&0xff)+":");
                }
                Console.Error.WriteLine("");
                */
                index++;
                length = plain[index++] & 0xff;
                if ((length & 0x80) != 0)
                {
                    int foo = length & 0x7f; length = 0;
                    while (foo-- > 0) { length = (length << 8) + (plain[index++] & 0xff); }
                }
                ep_array = new byte[length];
                Array.Copy(plain, index, ep_array, 0, length);
                index += length;
                /*
                Console.Error.WriteLine("int: ep len="+length);
                for(int i=0; i<ep_array.length; i++){
                System.err.print(Integer.toHexString(ep_array[i]&0xff)+":");
                }
                Console.Error.WriteLine("");
                */
                index++;
                length = plain[index++] & 0xff;
                if ((length & 0x80) != 0)
                {
                    int foo = length & 0x7f; length = 0;
                    while (foo-- > 0) { length = (length << 8) + (plain[index++] & 0xff); }
                }
                eq_array = new byte[length];
                Array.Copy(plain, index, eq_array, 0, length);
                index += length;
                /*
                Console.Error.WriteLine("int: eq len="+length);
                for(int i=0; i<eq_array.length; i++){
                System.err.print(Integer.toHexString(eq_array[i]&0xff)+":");
                }
                Console.Error.WriteLine("");
                */
                index++;
                length = plain[index++] & 0xff;
                if ((length & 0x80) != 0)
                {
                    int foo = length & 0x7f; length = 0;
                    while (foo-- > 0) { length = (length << 8) + (plain[index++] & 0xff); }
                }
                c_array = new byte[length];
                Array.Copy(plain, index, c_array, 0, length);
                index += length;
                /*
                Console.Error.WriteLine("int: c len="+length);
                for(int i=0; i<c_array.length; i++){
                System.err.print(Integer.toHexString(c_array[i]&0xff)+":");
                }
                Console.Error.WriteLine("");
                */
            }
            catch (Exception e)
            {
                //Console.Error.WriteLine(e);
                return false;
            }
            return true;
        }


        public override byte[] getPublicKeyBlob()
        {
            byte[] foo = base.getPublicKeyBlob();
            if (foo != null) return foo;

            if (pub_array == null) return null;

            Buffer buf = new Buffer(sshrsa.length + 4 +
                      pub_array.length + 4 +
                      n_array.length + 4);
            buf.putString(sshrsa);
            buf.putString(pub_array);
            buf.putString(n_array);
            return buf.buffer;
        }

        private static readonly byte[] sshrsa = "ssh-rsa".getBytes();
        protected override byte[] getKeyTypeName() { return sshrsa; }
        public override int getKeyType() { return RSA; }

        public override int getKeySize() { return key_size; }
        public override void dispose()
        {
            base.dispose();
            Util.bzero(prv_array);
        }
    }
}
