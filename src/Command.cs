﻿using BeetleX.Buffers;
using System;
using System.Collections.Generic;
using System.Text;

namespace BeetleX.Redis
{
    public abstract class Command
    {
        static Command()
        {
            LineBytes = Encoding.ASCII.GetBytes("\r\n");
            for (int i = 1; i <= MAX_LENGTH_TABLE; i++)
            {
                mMsgHeaderLenData.Add(Encoding.UTF8.GetBytes($"*{i}\r\n"));
                mBodyHeaderLenData.Add(Encoding.UTF8.GetBytes($"${i}\r\n"));
            }
        }

        private const int MAX_LENGTH_TABLE = 1024 * 32;

        private static byte[] LineBytes;

        public static List<byte[]> mMsgHeaderLenData = new List<byte[]>();

        public static byte[] GetMsgHeaderLengthData(int length)
        {
            if (length > MAX_LENGTH_TABLE)
                return null;
            return mMsgHeaderLenData[length - 1];
        }

        public static List<byte[]> mBodyHeaderLenData = new List<byte[]>();

        public static byte[] GetBodyHeaderLenData(int length)
        {
            if (length > MAX_LENGTH_TABLE)
                return null;
            return mBodyHeaderLenData[length - 1];
        }

        public Command()
        {

        }

        public Func<Result, PipeStream, RedisClient, bool> Reader { get; set; }

        public abstract bool Read { get; }

        public IDataFormater DataFormater { get; set; }

        public abstract string Name { get; }

        private byte[] mCommandBuffer = null;

        private List<CommandParameter> mParameters = new List<CommandParameter>();

        protected Command AddText(object text)
        {
            mParameters.Add(new CommandParameter { Value = text });
            return this;
        }

        protected Command AddData(object data)
        {
            mParameters.Add(new CommandParameter { Value = data, DataFormater = this.DataFormater, Serialize = true });
            return this;
        }

        public virtual void OnExecute()
        {
            if (mCommandBuffer == null)
            {
                string value = $"${Name.Length}\r\n{Name}";
                mCommandBuffer = Encoding.ASCII.GetBytes(value);
            }

            mParameters.Add(new CommandParameter { ValueBuffer = mCommandBuffer });
        }

        public void Execute(RedisClient client, PipeStream stream)
        {
            OnExecute();
            var data = GetMsgHeaderLengthData(mParameters.Count);
            if (data != null)
            {
                stream.Write(data, 0, data.Length);
            }
            else
            {
                string headerStr = $"*{mParameters.Count}\r\n";
                stream.Write(headerStr);
            }
            for (int i = 0; i < mParameters.Count; i++)
            {
                mParameters[i].Write(client, stream);
            }
        }

        public class CommandParameter
        {
            public object Value { get; set; }

            public IDataFormater DataFormater { get; set; }

            internal byte[] ValueBuffer { get; set; }

            public bool Serialize
            {
                get; set;
            } = false;

            [ThreadStatic]
            private static byte[] mBuffer = null;

            public void Write(RedisClient client, PipeStream stream)
            {
                if (ValueBuffer != null)
                {
                    stream.Write(ValueBuffer, 0, ValueBuffer.Length);
                }
                else if (Serialize && DataFormater != null)
                {
                    DataFormater.SerializeObject(Value, client, stream);
                }
                else
                {
                    string value = Value as string;
                    if (value == null)
                        value = Value.ToString();
                    if (mBuffer == null)
                        mBuffer = new byte[1024 * 1024];
                    int len = Encoding.UTF8.GetBytes(value, 0, value.Length, mBuffer, 0);
                    var data = GetBodyHeaderLenData(len);
                    if (data != null)
                    {
                        stream.Write(data, 0, data.Length);
                    }
                    else
                    {
                        stream.Write($"${len}\r\n");
                    }
                    stream.Write(mBuffer, 0, len);
                }
                stream.Write(LineBytes, 0, 2);
            }
        }

    }
}
