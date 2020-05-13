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
    private readonly Dictionary<string, ISpeckleGSASender> Senders = new Dictionary<string, ISpeckleGSASender>();

    private Dictionary<Type, List<Type>> FilteredReadTypePrereqs = new Dictionary<Type, List<Type>>();
    public Dictionary<Type, string> StreamMap = new Dictionary<Type, string>();

    //These need to be accessed using a lock
    private object traversedSerialisedLock = new object();
    private Dictionary<Type, List<object>> currentObjects = new Dictionary<Type, List<object>>();
    private List<Type> traversedSerialisedTypes = new List<Type>();

    /// <summary>
    /// Initializes sender.
    /// </summary>
    /// <param name="restApi">Server address</param>
    /// <param name="apiToken">API token of account</param>
    /// <returns>Task</returns>
    public async Task<List<string>> Initialize(string restApi, string apiToken, Func<string, string, ISpeckleGSASender> GSASenderCreator)
    {
			var statusMessages = new List<string>();

			if (IsInit) return statusMessages;

			if (!GSA.IsInit)
			{
				Status.AddError("GSA link not found.");
				return statusMessages;
			}

      var startTime = DateTime.Now;

			var attributeType = typeof(GSAObject);
      var keywords = new List<string>();

      //Filter out Prereqs that are excluded by the layer selection
      // Remove wrong layer objects from Prereqs
      if (GSA.Settings.SendOnlyResults)
      {
        var stream = GSA.Settings.SendOnlyResults ? "results" : null;
        var streamLayerPrereqs = GSA.ReadTypePrereqs.Where(t => (string)t.Key.GetAttribute("Stream") == stream && ObjectTypeMatchesLayer(t.Key, GSA.Settings.TargetLayer));
        foreach (var kvp in streamLayerPrereqs)
        {
          FilteredReadTypePrereqs[kvp.Key] = kvp.Value.Where(l => ObjectTypeMatchesLayer(l, GSA.Settings.TargetLayer)
            && (string)l.GetAttribute("Stream") == stream).ToList();
        }

        //If only results then the keywords for the objects which have results still need to be retrieved.  Note these are different
        //to the keywords of the types to be sent (which, being result objects, are blank in this case).
        foreach (var t in FilteredReadTypePrereqs.Keys)
        {
          var subKeywords = (string[])t.GetAttribute("SubGSAKeywords");
          foreach (var skw in subKeywords)
          {
            if (skw.Length > 0 && !keywords.Contains(skw))
            {
              keywords.Add(skw);
            }
          }
        }
			}
			else
			{
				var layerPrereqs = GSA.ReadTypePrereqs.Where(t => ObjectTypeMatchesLayer(t.Key, GSA.Settings.TargetLayer));
				foreach (var kvp in layerPrereqs)
				{
					FilteredReadTypePrereqs[kvp.Key] = kvp.Value.Where(l => ObjectTypeMatchesLayer(l, GSA.Settings.TargetLayer)).ToList();
				}
        keywords.AddRange(GetFilteredKeywords());
      }

      Status.ChangeStatus("Reading GSA data into cache");

      int numRowsUpdated = 0;
      var updatedCache = await Task.Run(() => UpdateCache(keywords, out numRowsUpdated));
      if (!updatedCache)
      {
        Status.AddError("Error in communicating GSA - please check if the GSA file has been closed down");
        return statusMessages;
      }

      Status.AddMessage("Read " + numRowsUpdated + " GWA lines across " + keywords.Count() + " keywords into cache");

      // Grab GSA interface type
      var interfaceType = typeof(IGSASpeckleContainer);

      // Grab all GSA related object
      Status.ChangeStatus("Preparing to read GSA Objects");

			// Run initialize sender method in interfacer
			var assemblies = SpeckleInitializer.GetAssemblies();
			var objTypes = new List<Type>();
      foreach (var ass in assemblies)
      {
        var types = ass.GetTypes();
				objTypes.AddRange(types.Where(t => interfaceType.IsAssignableFrom(t) && t != interfaceType));
      }

			foreach (Type t in objTypes)
			{
				var streamAttribute = t.GetAttribute("Stream");
				if (streamAttribute != null)
				{
					StreamMap[t] = (string)streamAttribute;
				}
			}

      // Create the streams
      Status.ChangeStatus("Creating streams");

			var streamNames = (GSA.Settings.SeparateStreams) ? objTypes.Select(t => (string)t.GetAttribute("Stream")).Distinct().ToList() : new List<string>() { "Full Model" };

      foreach (string streamName in streamNames)
      {
        Senders[streamName] = GSASenderCreator(restApi, apiToken);

        if (!GSA.SenderInfo.ContainsKey(streamName))
        {
          Status.AddMessage(streamName + " sender not initialized. Creating new " + streamName + " sender.");
          await Senders[streamName].InitializeSender(null, null, streamName);
          GSA.SenderInfo[streamName] = new Tuple<string, string>(Senders[streamName].StreamID, Senders[streamName].ClientID);
        }
        else
        {
          await Senders[streamName].InitializeSender(GSA.SenderInfo[streamName].Item1, GSA.SenderInfo[streamName].Item2, streamName);
        }
      }

      TimeSpan duration = DateTime.Now - startTime;
      Status.AddMessage("Duration of initialisation: " + duration.ToString(@"hh\:mm\:ss"));
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

      var startTime = DateTime.Now;

      IsBusy = true;
			GSA.Settings.Units = GSA.gsaProxy.GetUnits();

      var gsaStaticObjects = GetAssembliesStaticTypes();

      //Clear previously-sent objects
      gsaStaticObjects.ForEach(dict => dict.Clear());

      // Read objects
      var currentBatch = new List<Type>();

      bool changeDetected = false;
      do
      {
        ExecuteWithLock(ref traversedSerialisedLock, () =>
        {
          currentBatch = FilteredReadTypePrereqs.Where(i => i.Value.Count(x => !traversedSerialisedTypes.Contains(x)) == 0).Select(i => i.Key).ToList();
          currentBatch.RemoveAll(i => traversedSerialisedTypes.Contains(i));
        });

        Parallel.ForEach(currentBatch, t =>
        {
          if (changeDetected) // This will skip the first read but it avoids flickering
          {
            Status.ChangeStatus("Reading " + t.Name);
          }

          //The SpeckleStructural kit actually does serialisation (calling of ToSpeckle()) by type, not individual object.  This is due to
          //GSA offering bulk GET based on type.
          //So if the ToSpeckle() call for the type is successful it does all the objects of that type and returns SpeckleObject.
          //If there is an error, then the SpeckleCore Converter.Serialise will return SpeckleNull.  
          //The converted objects are stored in the kit in its own collection, not returned by Serialise() here.
          var dummyObject = Activator.CreateInstance(t);
          var result = Converter.Serialise(dummyObject);

          if (!(result is SpeckleNull))
          {
            changeDetected = true;
          }

          ExecuteWithLock(ref traversedSerialisedLock, () => traversedSerialisedTypes.Add(t));
        }
        );
      } while (currentBatch.Count > 0);

			foreach (var dict in gsaStaticObjects)
			{
        var allObjects = dict.GetAll();
        foreach (var t in allObjects.Keys)
        {
          if (!currentObjects.ContainsKey(t))
          {
            currentObjects[t] = new List<object>();
          }
          currentObjects[t].AddRange(allObjects[t]);
        }
			}

			if (!changeDetected)
      {
        Status.ChangeStatus("Finished sending", 100);
        IsBusy = false;
        return;
      }

      // Separate objects into streams
      var streamBuckets = new Dictionary<string, Dictionary<string, List<object>>>();

      foreach (var kvp in currentObjects)
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

      TimeSpan duration = DateTime.Now - startTime;
      Status.AddMessage("Duration of conversion to Speckle: " + duration.ToString(@"hh\:mm\:ss"));
      startTime = DateTime.Now;

      // Send package
      Status.ChangeStatus("Sending to Server");

      Parallel.ForEach(streamBuckets, kvp =>
      {
        Status.ChangeStatus("Sending to stream: " + Senders[kvp.Key].StreamID);

        var streamName = "";
        var title = GSA.gsaProxy.GetTitle();
        streamName = GSA.Settings.SeparateStreams ? title + "." + kvp.Key : title;

        Senders[kvp.Key].UpdateName(streamName);
        Senders[kvp.Key].SendGSAObjects(kvp.Value);
      });

      duration = DateTime.Now - startTime;
      Status.AddMessage("Duration of sending to Speckle: " + duration.ToString(@"hh\:mm\:ss"));
      IsBusy = false;
      Status.ChangeStatus("Finished sending", 100);
    }

    /// <summary>
    /// Dispose receiver.
    /// </summary>
    public void Dispose()
    {
      foreach (KeyValuePair<string, Tuple<string, string>> kvp in GSA.SenderInfo)
        Senders[kvp.Key].Dispose();
    }

    protected List<string> GetFilteredKeywords()
    {
      var keywords = new List<string>();
      keywords.AddRange(GetFilteredKeywords(FilteredReadTypePrereqs));

      return keywords;
    }

    private bool UpdateCache(List<string> keywords, out int numUpdated)
    {
      GSA.gsaCache.Clear();
      try
      {
        var data = GSA.gsaProxy.GetGwaData(keywords, false);
        for (int i = 0; i < data.Count(); i++)
        {
          GSA.gsaCache.Upsert(
            data[i].Keyword,
            data[i].Index,
            data[i].GwaWithoutSet,
            streamId: data[i].StreamId,
            //This needs to be revised as this logic is in the kit too
            applicationId: (string.IsNullOrEmpty(data[i].ApplicationId))
              ? ("gsa/" + data[i].Keyword + "_" + data[i].Index.ToString())
              : data[i].ApplicationId,
            gwaSetCommandType: data[i].GwaSetType);
        }
        numUpdated = data.Count();
        return true;
      }
      catch
      {
        numUpdated = 0;
        return false;
      }

    }
  }
}
