﻿using NUnit.Framework;
using SpeckleCore;
using SpeckleGSA;
using SpeckleUtil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SpeckleGSAInterfaces;
using System.IO;
using System.Diagnostics;
using System.Collections;
using Interop.Gsa_10_1;
using Microsoft.SqlServer.Server;
using System.Runtime.InteropServices;

namespace SpeckleGSAProxy.Test
{
  [TestFixture]
  public class ProxyTests
  {
    public static string[] savedJsonFileNames = new[] { "U7ntEJkzdZ.json", "lfsaIEYkR.json", "NaJD7d5kq.json", "UNg87ieJG.json" };

    private string TestDataDirectory { get => AppDomain.CurrentDomain.BaseDirectory.TrimEnd(new[] { '\\' }) + @"\..\..\TestData\"; }

    [Test]
    public async Task ReceiveTestContinuousMerge()
    {
      for (var n = 0; n < 50; n++)
      {
        GSA.gsaProxy = new TestProxy();
        GSA.Init();

        var streamIds = savedJsonFileNames.Select(fn => fn.Split(new[] { '.' }).First()).ToList();
        GSA.ReceiverInfo = streamIds.Select(si => new Tuple<string, string>(si, null)).ToList();

        //Create receiver with all streams
        var receiver = new Receiver() { Receivers = streamIds.ToDictionary(s => s, s => (ISpeckleGSAReceiver)new TestSpeckleGSAReceiver()) };
        LoadObjectsIntoReceiver(receiver, savedJsonFileNames, TestDataDirectory);

        GSA.gsaProxy.NewFile();

        //This will load data from all streams into the cache
        await receiver.Initialize("", "");

        //RECEIVE EVENT #1: first of continuous
        receiver.Trigger(null, null);

        //Check cache to see if object have been received
        var records = ((IGSACacheForTesting)GSA.gsaCache).Records;
        var latestGwaAfter1 = new List<string>(records.Where(r => r.Latest).Select(r => r.Gwa));
        Assert.AreEqual(100, records.Where(r => r.Latest).Count());
        Assert.AreEqual(0, records.Where(r => string.IsNullOrEmpty(r.StreamId)).Count());
        Assert.IsTrue(records.All(r => r.Gwa.Contains(r.StreamId)));

        //Refresh with new copy of objects so they aren't the same (so the merging code isn't trying to merge each object onto itself)
        LoadObjectsIntoReceiver(receiver, savedJsonFileNames, TestDataDirectory);

        //RECEIVE EVENT #2: second of continuous
        receiver.Trigger(null, null);

        //Check cache to see if object have been merged correctly and no extraneous calls to GSA is created
        var latestGwaAfter2 = new List<string>(records.Where(r => r.Latest).Select(r => r.Gwa));
        var diff = latestGwaAfter2.Where(a2 => !latestGwaAfter1.Any(a1 => string.Equals(a1, a2, StringComparison.InvariantCultureIgnoreCase))).ToList();
        records = ((IGSACacheForTesting)GSA.gsaCache).Records;
        Assert.AreEqual(100, records.Where(r => r.Latest).Count());
        Assert.AreEqual(110, records.Count());

        GSA.gsaProxy.Close();
      }
    }

    [Test]
    public async Task ReceiveTestStreamSubset()
    {
      for (var n = 0; n < 100; n++)
      {
        Debug.WriteLine("");
        Debug.WriteLine("Test run number: " + (n + 1));
        Debug.WriteLine("");

        GSA.gsaProxy = new TestProxy();
        GSA.Init();

        var streamIds = savedJsonFileNames.Select(fn => fn.Split(new[] { '.' }).First()).ToList();
        GSA.ReceiverInfo = streamIds.Select(si => new Tuple<string, string>(si, null)).ToList();

        //Create receiver with all streams
        var receiver = new Receiver() { Receivers = streamIds.ToDictionary(s => s, s => (ISpeckleGSAReceiver)new TestSpeckleGSAReceiver()) };
        LoadObjectsIntoReceiver(receiver, savedJsonFileNames, TestDataDirectory);

        GSA.gsaProxy.NewFile();

        //This will load data from all streams into the cache
        await receiver.Initialize("", "");

        //RECEIVE EVENT #1: single
        receiver.Trigger(null, null);

        //RECEIVE EVENT #2: single with reduced streams

        //Add contents of cache to the test proxy so they can be the source for the renewed hydration of the cache in the Initialize call
        CopyCacheToTestProxy();

        var streamIdsToTest = streamIds.Take(3).ToList();
        GSA.ReceiverInfo = streamIdsToTest.Select(si => new Tuple<string, string>(si, null)).ToList();

        //Yes the real SpeckleGSA does create a new receiver.  This time, create them with not all streams active
        receiver = new Receiver() { Receivers = streamIdsToTest.ToDictionary(s => s, s => (ISpeckleGSAReceiver)new TestSpeckleGSAReceiver()) };

        var records = ((IGSACacheForTesting)GSA.gsaCache).Records;
        Assert.AreEqual(3, GSA.ReceiverInfo.Count());
        Assert.AreEqual(3, receiver.Receivers.Count());
        Assert.AreEqual(4, records.Select(r => r.StreamId).Distinct().Count());

        await receiver.Initialize("", "");

        //Refresh with new copy of objects so they aren't the same (so the merging code isn't trying to merge each object onto itself)
        var streamObjectsTuples = ExtractObjects(savedJsonFileNames.Where(fn => streamIdsToTest.Any(ft => fn.Contains(ft))).ToArray(), TestDataDirectory);
        var objectsToExclude = streamObjectsTuples.Where(t => t.Item2.Name == "LSP-Lockup" || t.Item2.Type == "Structural2DThermalLoad").ToArray();
        for (int i = 0; i < objectsToExclude.Count(); i++)
        {
          streamObjectsTuples.Remove(objectsToExclude[i]);
        }
        for (int i = 0; i < streamIdsToTest.Count(); i++)
        {
          ((TestSpeckleGSAReceiver)receiver.Receivers[streamIds[i]]).Objects = streamObjectsTuples.Where(t => t.Item1 == streamIds[i]).Select(t => t.Item2).ToList();
        }

        receiver.Trigger(null, null);

        //Check the other streams aren't affected by only having some active
        records = ((IGSACacheForTesting)GSA.gsaCache).Records;
        if (records.Where(r => r.Latest).Count() < 98)
        {

        }
        Assert.AreEqual(98, records.Where(r => r.Latest).Count());
        //-------

        GSA.gsaProxy.Close();
      }
    }

    [TestCase("sjc.gwb")]
    public async Task SendTest(string filename)
    {
      GSA.gsaProxy = new GSAProxy();
      GSA.Init();

      Status.MessageAdded += (s, e) => Debug.WriteLine("Message: " + e.Message);
      Status.ErrorAdded += (s, e) => Debug.WriteLine("Error: " + e.Message);
      Status.StatusChanged += (s, e) => Debug.WriteLine("Status: " + e.Name);

      GSA.SenderInfo = new Dictionary<string, Tuple<string, string>>() { { "testStream", new Tuple<string, string>("testStreamId", "testClientId") } };

      var sender = new Sender();

      GSA.gsaProxy.OpenFile(Path.Combine(TestDataDirectory, filename), true);

      var testSender = new TestSpeckleGSASender();

      //This will load data from all streams into the cache
      await sender.Initialize("", "", (restApi, apiToken) => testSender);

      //RECEIVE EVENT #1: first of continuous
      sender.Trigger();

      GSA.gsaProxy.Close();

      var a = new SpeckleObject();
      var sentObjects = testSender.sentObjects.SelectMany(kvp => kvp.Value.Select(v => (SpeckleObject)v)).ToList();

      Assert.AreEqual(1481, sentObjects.Where(so => so.Type.EndsWith("Structural1DElement")).Count());
      Assert.AreEqual(50, sentObjects.Where(so => so.Type.Contains("Structural2DElement")).Count());
      Assert.AreEqual(172, sentObjects.Where(so => so.Type.EndsWith("Structural2DVoid")).Count());
    }


    [TestCase("SET\tMEMB.8:{speckle_app_id:gh/a}\t5\tTheRest", "MEMB.8", 5, "gh/a", "MEMB.8:{speckle_app_id:gh/a}\t5\tTheRest")]
    [TestCase("MEMB.8:{speckle_app_id:gh/a}\t5\tTheRest", "MEMB.8", 5, "gh/a", "MEMB.8:{speckle_app_id:gh/a}\t5\tTheRest")]
    [TestCase("SET_AT\t2\tLOAD_2D_THERMAL.2:{speckle_app_id:gh/a}\tTheRest", "LOAD_2D_THERMAL.2", 2, "gh/a", "LOAD_2D_THERMAL.2:{speckle_app_id:gh/a}\tTheRest")]
    [TestCase("LOAD_2D_THERMAL.2:{speckle_app_id:gh/a}\tTheRest", "LOAD_2D_THERMAL.2", 0, "gh/a", "LOAD_2D_THERMAL.2:{speckle_app_id:gh/a}\tTheRest")]
    public void ParseGwaCommandTests(string gwa, string expKeyword, int expIndex, string expAppId, string expGwaWithoutSet)
    {
      var gsaProxy = new GSAProxy();
      gsaProxy.ParseGeneralGwa(gwa, out string keyword, out int? foundIndex, out string streamId, out string applicationId, out string gwaWithoutSet, out SpeckleGSAInterfaces.GwaSetCommandType? gwaSetCommandType);
      var index = foundIndex ?? 0;

      Assert.AreEqual(expKeyword, keyword);
      Assert.AreEqual(expIndex, index);
      Assert.AreEqual(expAppId, applicationId);
      Assert.AreEqual(expGwaWithoutSet, gwaWithoutSet);
    }

    [Test]
    public void TestProxyGetDataForCache()
    {
      var proxy = new GSAProxy();
      proxy.OpenFile(Path.Combine(TestDataDirectory, "Structural Demo 191010.gwb"));
      var data = proxy.GetGwaData(DesignLayerKeywords, false);

      Assert.AreEqual(192, data.Count());
      proxy.Close();
    }

    #region private_methods
    private void LoadObjectsIntoReceiver(Receiver receiver, string[] savedJsonFileNames, string testDataDirectory)
    {
      var streamIds = savedJsonFileNames.Select(fn => fn.Split(new[] { '.' }).First()).ToList();
      var streamObjectsTuples = ExtractObjects(savedJsonFileNames, testDataDirectory);
      for (int i = 0; i < streamIds.Count(); i++)
      {
        ((TestSpeckleGSAReceiver)receiver.Receivers[streamIds[i]]).Objects = streamObjectsTuples.Where(t => t.Item1 == streamIds[i]).Select(t => t.Item2).ToList();
      }
    }

    private void CopyCacheToTestProxy()
    {
      var latestRecords = ((IGSACacheForTesting)GSA.gsaCache).Records.Where(r => r.Latest).ToList();
      latestRecords.ForEach(r => ((TestProxy)GSA.gsaProxy).AddDataLine(r.Keyword, r.Index, r.StreamId, r.ApplicationId, r.Gwa, r.GwaSetCommandType));
    }

    private List<Tuple<string, SpeckleObject>> ExtractObjects(string[] fileNames, string directory)
    {
      var speckleObjects = new List<Tuple<string, SpeckleObject>>();
      foreach (var fileName in fileNames)
      {
        var json = Helper.ReadFile(fileName, directory);
        var streamId = fileName.Split(new[] { '.' }).First();

        var response = ResponseObject.FromJson(json);
        for (int i = 0; i < response.Resources.Count(); i++)
        {
          speckleObjects.Add(new Tuple<string, SpeckleObject>(streamId, response.Resources[i]));
        }
      }
      return speckleObjects;
    }
    #endregion

    public static string[] DesignLayerKeywords = new string[] { 
      "LOAD_2D_THERMAL.2",
      "ALIGN.1",
      "PATH.1",
      "USER_VEHICLE.1",
      "RIGID.3",
      "ASSEMBLY.3",
      "LOAD_GRAVITY.3",
      "PROP_SPR.4",
      "ANAL.1",
      "TASK.1",
      "GEN_REST.2",
      "ANAL_STAGE.3",
      "LIST.1",
      "LOAD_GRID_LINE.2",
      "POLYLINE.1",
      "GRID_SURFACE.1",
      "GRID_PLANE.4",
      "AXIS.1",
      "MEMB.8",
      "NODE.3",
      "LOAD_GRID_AREA.2",
      "LOAD_2D_FACE.2",
      "EL.4",
      "PROP_2D.6",
      "MAT_STEEL.4",
      "MAT_CONCRETE.17",
      "LOAD_BEAM",
      "LOAD_NODE.2",
      "COMBINATION.1",
      "LOAD_TITLE.2",
      "PROP_SEC.3",
      "PROP_MASS.2",
      "GRID_LINE.1"
    };
  }
}
