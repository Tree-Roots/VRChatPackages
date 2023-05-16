using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VRC.SDK3.Data;
using VRC.SDKBase;

namespace Tests.DataContainers
{
    public class DataTokenTests
    {

        [Test]
        public void TestIsNull()
        {
            Assert.IsTrue(new DataToken().IsNull);
            Assert.IsFalse(new DataToken((byte)5).IsNull);
            Assert.IsFalse(new DataToken((sbyte)5).IsNull);
            Assert.IsFalse(new DataToken((short)5).IsNull);
            Assert.IsFalse(new DataToken((ushort)5).IsNull);
            Assert.IsFalse(new DataToken((int)5).IsNull);
            Assert.IsFalse(new DataToken((uint)5).IsNull);
            Assert.IsFalse(new DataToken((long)5).IsNull);
            Assert.IsFalse(new DataToken((ulong)5).IsNull);
            Assert.IsFalse(new DataToken((float)5).IsNull);
            Assert.IsFalse(new DataToken((double)5).IsNull);
            Assert.IsFalse(new DataToken(true).IsNull);
            Assert.IsFalse(new DataToken("string").IsNull);
            string nullstring = null;
            Assert.IsTrue(new DataToken(nullstring).IsNull);
            Assert.IsFalse(new DataToken(new DataList(){"4", "5"}).IsNull);
            Assert.IsFalse(new DataToken(new DataDictionary(){["key"]="value"}).IsNull);
            Assert.IsFalse(new DataToken(new bool[] {false, true, false}).IsNull);
        }
        
        [Test]
        public void TestEmpty()
        {
            SetAndGet("empty", new DataToken());
        }

        [Test]
        public void TestBool()
        {
            SetAndGet("bool true", new DataToken(true));
            SetAndGet("bool false", new DataToken(false));
        }
        [Test]
        public void TestByte()
        {
            SetAndGet("byte number", (byte)4);
            SetAndGet("max byte number", byte.MaxValue);
            SetAndGet("min byte number", byte.MinValue);
        }
        [Test]
        public void TestSByte()
        {
            SetAndGet("sbyte number", (sbyte)4);
            SetAndGet("max sbyte number", sbyte.MaxValue);
            SetAndGet("min sbyte number", sbyte.MinValue);
        }
        [Test]
        public void TestShort()
        {
            SetAndGet("short number", (short)4);
            SetAndGet("max short number", short.MaxValue);
            SetAndGet("min short number", short.MinValue);
        }
        [Test]
        public void TestUShort()
        {
            SetAndGet("ushort number", (ushort)4);
            SetAndGet("max ushort number", ushort.MaxValue);
            SetAndGet("min ushort number", ushort.MinValue);
        }
        [Test]
        public void TestInt()
        {
            SetAndGet("int number", (int)4);
            SetAndGet("max int number", int.MaxValue);
            SetAndGet("min int number", int.MinValue);
        }
        [Test]
        public void TestUInt()
        {
            SetAndGet("uint number", (uint)4);
            SetAndGet("max uint number", uint.MaxValue);
            SetAndGet("min uint number", uint.MinValue);
        }
        [Test]
        public void TestLong()
        {
            SetAndGet("long number", (long)4);
            SetAndGet("max long number", long.MaxValue);
            SetAndGet("min long number", long.MinValue);
        }
        [Test]
        public void TestULong()
        {
            SetAndGet("ulong number", (ulong)4);
            SetAndGet("max ulong number", ulong.MaxValue);
            SetAndGet("min ulong number", ulong.MinValue);
        }
        [Test]
        public void TestFloat()
        {
            SetAndGet("float number", 0.123f);
            SetAndGet("max float number", float.MaxValue);
            SetAndGet("large float number", float.MinValue);
            SetAndGet("epsilon float number", float.Epsilon);
            SetAndGet("Negative Infinity float number", float.NegativeInfinity);
            SetAndGet("Positive Infinity float number", float.PositiveInfinity);
            SetAndGet("NaN float number", float.NaN);
            SetAndGet("Epsilon float number", float.Epsilon);
        }
        [Test]
        public void TestDouble()
        {
            SetAndGet("double number", 0.12341233);
            SetAndGet("max double number", double.MaxValue);
            SetAndGet("min double number", double.MinValue);
            SetAndGet("Negative Infinity double number", double.NegativeInfinity);
            SetAndGet("Positive Infinity double number", double.PositiveInfinity);
            SetAndGet("NaN double number", double.NaN);
            SetAndGet("Epsilon double number", double.Epsilon);
        }

        [Test]
        public void TestString()
        {
            SetAndGet("backspace", new DataToken("\b"));
            SetAndGet("form feed", new DataToken("\f"));
            SetAndGet("newline", new DataToken("\n"));
            SetAndGet("carriage return", new DataToken("\r"));
            SetAndGet("tab", new DataToken("\t"));
            SetAndGet("quotes", new DataToken("\""));
            SetAndGet("victory hand", new DataToken("✌"));
            SetAndGet("mandarin", new DataToken("䉟"));
            SetAndGet("greater than", new DataToken(">"));
        }

        [Test]
        public void TestReference()
        {
            SetAndGet("bool array reference", new DataToken(new bool[] {false, true, false}));
        }

        private void SetAndGet(string title, DataToken inToken)
        {
            SetAndGetDictionary(title, inToken);
            SetAndGetList(title, inToken);
        }
        private void SetAndGetDictionary(string title, DataToken inToken)
        {
            DataDictionary dataDictionary = new DataDictionary();
            dataDictionary.SetValue("key", inToken);
            Assert.IsTrue(dataDictionary.TryGetValue("key", out DataToken outToken), $"{title} Failed to get value with error {outToken}");
            Assert.AreEqual(inToken, outToken, $"{title} Input and output tokens were not the same");
        }
        private void SetAndGetList(string title, DataToken inToken)
        {
            DataList dataList = new DataList();
            dataList.Add(inToken);
            Assert.IsTrue(dataList.TryGetValue(0, out DataToken outToken), $"{title} Failed to get value with error {outToken}");
            Assert.AreEqual(inToken, outToken,  $"{title} Input and output tokens were not the same");
        }

    }
}
