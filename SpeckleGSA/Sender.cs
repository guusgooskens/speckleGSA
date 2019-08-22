﻿using SpeckleCore;
using SpeckleGSAInterfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SpeckleGSA
{
  /// <summary>
  /// Responsible for reading and sending GSA models.
  /// </summary>
  public class Sender : BaseReceiverSender
  {
    public Dictionary<Type, List<object>> SenderObjects = new Dictionary<Type, List<object>>();
    public Dictionary<string, SpeckleGSASender> Senders = new Dictionary<string, SpeckleGSASender>();
    public Dictionary<Type, string> StreamMap = new Dictionary<Type, string>();

    /// <summary>
    /// Initializes sender.
    /// </summary>
    /// <param name="restApi">Server address</param>
    /// <param name="apiToken">API token of account</param>
    /// <returns>Task</returns>
    public async Task<List<string>> Initialize(string restApi, string apiToken)
    {
			var statusMessages = new List<string>();

			if (!Initialise(GSA.Settings.SendOnlyResults ? "results" : null)) return statusMessages;

			// Run initialize sender method in interfacer
			var assemblies = SpeckleInitializer.GetAssemblies();

			GSA.Interfacer.InitializeSender();

			// Grab GSA interface type
			var attributeType = typeof(GSAObject);
			var interfaceType = typeof(IGSASpeckleContainer);

      // Grab all GSA related object
      Status.ChangeStatus("Preparing to read GSA Objects");

      var objTypes = new List<Type>();
      foreach (var ass in assemblies)
      {
        var types = ass.GetTypes();
				objTypes.AddRange(types.Where(t => interfaceType.IsAssignableFrom(t) && t != interfaceType));
      }

      // Create the streams
      Status.ChangeStatus("Creating streams");

			var streamNames = (GSA.Settings.SeparateStreams) ? objTypes.Select(t => (string)t.GetAttribute("Stream", attributeType)).Distinct().ToList() : new List<string>() { "Full Model" };

      foreach (string streamName in streamNames)
      {
        Senders[streamName] = new SpeckleGSASender(restApi, apiToken);

        if (!GSA.Senders.ContainsKey(streamName))
        {
          Status.AddMessage(streamName + " sender not initialized. Creating new " + streamName + " sender.");
          await Senders[streamName].InitializeSender(null, null, streamName);
          GSA.Senders[streamName] = new Tuple<string, string> (Senders[streamName].StreamID, Senders[streamName].ClientID);
        }
        else
          await Senders[streamName].InitializeSender(GSA.Senders[streamName].Item1, GSA.Senders[streamName].Item2, streamName);
      }

      Status.ChangeStatus("Ready to stream");
      IsInit = true;

			return statusMessages;
    }

    /// <summary>
    /// Trigger to update stream.
    /// </summary>
    public void Trigger()
    {
      if ((IsBusy) || (!IsInit)) return;

      IsBusy = true;
			GSA.Settings.Units = GSA.Interfacer.GetUnits();

			GSA.Interfacer.PreSending();

			// Read objects
			var currentBatch = new List<Type>();
      var traversedTypes = new List<Type>();

      bool changeDetected = false;
      do
      {
        currentBatch = FilteredTypePrerequisites.Where(i => i.Value.Count(x => !traversedTypes.Contains(x)) == 0).Select(i => i.Key).ToList();
        currentBatch.RemoveAll(i => traversedTypes.Contains(i));

        foreach (var t in currentBatch)
        {
          if (changeDetected) // This will skip the first read but it avoids flickering
            Status.ChangeStatus("Reading " + t.Name);

          var dummyObject = Activator.CreateInstance(t);
          var result = Converter.Serialise(dummyObject);

          if (!(result is SpeckleNull)) changeDetected = true;

          traversedTypes.Add(t);
        }
      } while (currentBatch.Count > 0);

      if (!changeDetected)
      {
        Status.ChangeStatus("Finished sending", 100);
        IsBusy = false;
        return;
      }

      // Separate objects into streams
      var streamBuckets = new Dictionary<string, Dictionary<string, List<object>>>();

      foreach (var kvp in SenderObjects)
      {
        var targetStream = GSA.Settings.SeparateStreams ? StreamMap[kvp.Key] : "Full Model";

        foreach (object obj in kvp.Value)
        {
          if (GSA.Settings.SendOnlyMeaningfulNodes)
          {
            if (obj.GetType().Name == "GSANode" && !(bool)obj.GetType().GetField("ForceSend").GetValue(obj))
              continue;
          }
          object insideVal = obj.GetType().GetProperty("Value").GetValue(obj);

          ((SpeckleObject)insideVal).GenerateHash();

          if (!streamBuckets.ContainsKey(targetStream))
            streamBuckets[targetStream] = new Dictionary<string, List<object>>();

          if (streamBuckets[targetStream].ContainsKey(insideVal.GetType().Name))
            streamBuckets[targetStream][insideVal.GetType().Name].Add(insideVal);
          else
            streamBuckets[targetStream][insideVal.GetType().Name] = new List<object>() { insideVal };
        }
      }

      // Send package
      Status.ChangeStatus("Sending to Server");

      foreach (var kvp in streamBuckets)
      {
        Status.ChangeStatus("Sending to stream: " + Senders[kvp.Key].StreamID);

        var streamName = "";
				var title = GSA.Interfacer.GetTitle();
				streamName = GSA.Settings.SeparateStreams ? title + "." + kvp.Key : title;

        Senders[kvp.Key].UpdateName(streamName);
        Senders[kvp.Key].SendGSAObjects(kvp.Value);
      }

			GSA.Interfacer.PostSending();

			IsBusy = false;
      Status.ChangeStatus("Finished sending", 100);
    }

    /// <summary>
    /// Dispose receiver.
    /// </summary>
    public void Dispose()
    {
      foreach (KeyValuePair<string, Tuple<string, string>> kvp in GSA.Senders)
        Senders[kvp.Key].Dispose();
    }
  }
}
