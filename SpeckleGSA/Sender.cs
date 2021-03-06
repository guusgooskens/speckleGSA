﻿using Interop.Gsa_10_1;
using SpeckleCore;
using SpeckleGSAInterfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace SpeckleGSA
{
  /// <summary>
  /// Responsible for reading and sending GSA models.
  /// </summary>
  public class Sender : BaseReceiverSender
  {
    public Dictionary<Type, string> StreamMap;

    private Dictionary<string, ISpeckleGSASender> Senders;

    private readonly Dictionary<Type, List<Type>> FilteredReadTypePrereqs = new Dictionary<Type, List<Type>>();

    //These need to be accessed using a lock
    private object traversedSerialisedLock = new object();
    private readonly List<Type> traversedSerialisedTypes = new List<Type>();
    

    /// <summary>
    /// Initializes sender.
    /// </summary>
    /// <param name="restApi">Server address</param>
    /// <param name="apiToken">API token of account</param>
    /// <returns>Task</returns>
    public async Task<List<string>> Initialize(string restApi, string apiToken, Func<string, string, ISpeckleGSASender> gsaSenderCreator)
    {
			var statusMessages = new List<string>();

			if (IsInit) return statusMessages;

			if (!GSA.IsInit)
			{
				Status.AddError("GSA link not found.");
				return statusMessages;
			}

      GSA.SetAssembliesSenderDictionaries();

      var startTime = DateTime.Now;      
      Status.ChangeStatus("Reading GSA data into cache");

      //Update cache
      int numRowsUpdated = 0;
      int numKeywords = 0;
      var updatedCache = await Task.Run(() => UpdateCache(out numRowsUpdated, out numKeywords));
      if (!updatedCache)
      {
        Status.AddError("Error in communicating GSA - please check if the GSA file has been closed down");
        return statusMessages;
      }
      Status.AddMessage("Read " + numRowsUpdated + " GWA lines across " + numKeywords + " keywords into cache");

      // Grab all GSA related object
      Status.ChangeStatus("Preparing to read GSA Objects");

      // Run initialize sender method in interfacer
      var objTypes = GetAssembliesTypes();
      var streamNames = GetStreamNames(objTypes);

      CreateStreamMap(objTypes);

      // Create the streams
      Status.ChangeStatus("Creating streams");

      await CreateInitialiseSenders(streamNames, gsaSenderCreator, restApi, apiToken);

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

      //Clear previously-sent objects
      GSA.ClearSenderDictionaries();

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

#if DEBUG
        foreach (var t in currentBatch)
        {
          ProcessTypeForSending(t, ref changeDetected);
        }
#else
        Parallel.ForEach(currentBatch, t =>
        {
          ProcessTypeForSending(t, ref changeDetected);
        }
        );
#endif
      } while (currentBatch.Count > 0);

      if (!changeDetected)
      {
        Status.ChangeStatus("Finished sending", 100);
        IsBusy = false;
        return;
      }

      // Separate objects into streams
      var streamBuckets = CreateStreamBuckets();

      TimeSpan duration = DateTime.Now - startTime;
      Status.AddMessage("Duration of conversion to Speckle: " + duration.ToString(@"hh\:mm\:ss"));
      startTime = DateTime.Now;

      // Send package
      Status.ChangeStatus("Sending to Server");

      foreach (var k in streamBuckets.Keys)
      {
        Status.ChangeStatus("Sending to stream: " + Senders[k].StreamID);

        var title = GSA.gsaProxy.GetTitle();
        var streamName = GSA.Settings.SeparateStreams ? title + "." + k : title;

        Senders[k].UpdateName(streamName);
        Senders[k].SendGSAObjects(streamBuckets[k]);
      }

      duration = DateTime.Now - startTime;
      Status.AddMessage("Duration of sending to Speckle: " + duration.ToString(@"hh\:mm\:ss"));

      IsBusy = false;
      Status.ChangeStatus("Finished sending", 100);
    }

    private void ProcessTypeForSending(Type t, ref bool changeDetected)
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

    /// <summary>
    /// Dispose receiver.
    /// </summary>
    public void Dispose()
    {
      foreach (KeyValuePair<string, Tuple<string, string>> kvp in GSA.SenderInfo)
      {
        Senders[kvp.Key].Dispose();
      }
    }

    private async Task CreateInitialiseSenders(List<string> streamNames, Func<string, string, ISpeckleGSASender> GSASenderCreator, string restApi, string apiToken)
    {
      GSA.RemoveUnusedStreamInfo(streamNames);

      Senders = new Dictionary<string, ISpeckleGSASender>();

      foreach (string streamName in streamNames)
      {
        Senders[streamName] = GSASenderCreator(restApi, apiToken);

        if (!GSA.SenderInfo.ContainsKey(streamName))
        {
          Status.AddMessage("Creating new sender for " + streamName);
          await Senders[streamName].InitializeSender(null, null, streamName);
          GSA.SenderInfo[streamName] = new Tuple<string, string>(Senders[streamName].StreamID, Senders[streamName].ClientID);
        }
        else
        {
          await Senders[streamName].InitializeSender(GSA.SenderInfo[streamName].Item1, GSA.SenderInfo[streamName].Item2, streamName);
        }
      }
    }

    private Dictionary<string, Dictionary<string, List<object>>> CreateStreamBuckets()
    {
      var streamBuckets = new Dictionary<string, Dictionary<string, List<object>>>();

      var currentObjects = GSA.GetAllConvertedGsaObjectsByType();
      foreach (var kvp in currentObjects)
      {
        var targetStream = GSA.Settings.SeparateStreams ? StreamMap[kvp.Key] : "Full Model";

        foreach (object obj in kvp.Value)
        {
          if (GSA.Settings.SendOnlyMeaningfulNodes)
          {
            if (obj.GetType().Name == "GSANode" && !(bool)obj.GetType().GetField("ForceSend").GetValue(obj))
            {
              continue;
            }
          }
          object insideVal = obj.GetType().GetProperty("Value").GetValue(obj);

          ((SpeckleObject)insideVal).GenerateHash();

          if (!streamBuckets.ContainsKey(targetStream))
          {
            streamBuckets[targetStream] = new Dictionary<string, List<object>>();
          }

          if (streamBuckets[targetStream].ContainsKey(insideVal.GetType().Name))
          {
            streamBuckets[targetStream][insideVal.GetType().Name].Add(insideVal);
          }
          else
          {
            streamBuckets[targetStream][insideVal.GetType().Name] = new List<object>() { insideVal };
          }
        }
      }
      return streamBuckets;
    }

    private List<Type> GetAssembliesTypes()
    {
      // Grab GSA interface type
      var interfaceType = typeof(IGSASpeckleContainer);

      var assemblies = SpeckleInitializer.GetAssemblies();
      var objTypes = new List<Type>();
      foreach (var ass in assemblies)
      {
        var types = ass.GetTypes();
        objTypes.AddRange(types.Where(t => interfaceType.IsAssignableFrom(t) && t != interfaceType));
      }
      return objTypes;
    }

    private List<string> GetStreamNames(List<Type> objTypes)
    {
      var streamNames = (GSA.Settings.SendOnlyResults) ? new List<string> { "results" }
       : (GSA.Settings.SeparateStreams)
         ? objTypes.Select(t => (string)t.GetAttribute("Stream")).Distinct().ToList()
         : new List<string>() { "Full Model" };
      return streamNames;
    }

    private void CreateStreamMap(List<Type> objTypes)
    {
      StreamMap = new Dictionary<Type, string>();
      foreach (Type t in objTypes)
      {
        var streamAttribute = t.GetAttribute("Stream");
        if (streamAttribute != null)
        {
          StreamMap[t] = (string)streamAttribute;
        }
      }
    }

  protected List<string> GetFilteredKeywords()
    {
      var keywords = new List<string>();
      keywords.AddRange(GetFilteredKeywords(FilteredReadTypePrereqs));

      return keywords;
    }

    private bool UpdateCache(out int numUpdated, out int numKeywords)
    {
      var keywords = SettingsToKeywords();
      GSA.gsaCache.Clear();
      try
      {
        var data = GSA.gsaProxy.GetGwaData(keywords, false);
        for (int i = 0; i < data.Count(); i++)
        {
          var applicationId = (string.IsNullOrEmpty(data[i].ApplicationId)) ? null : data[i].ApplicationId;
          GSA.gsaCache.Upsert(
            data[i].Keyword,
            data[i].Index,
            data[i].GwaWithoutSet,
            streamId: data[i].StreamId,
            applicationId: applicationId,
            gwaSetCommandType: data[i].GwaSetType);
        }
        numKeywords = keywords.Count();
        numUpdated = data.Count();
        return true;
      }
      catch
      {
        numKeywords = 0;
        numUpdated = 0;
        return false;
      }
    }

    private List<string> SettingsToKeywords()
    {
      var keywords = new List<string>();

      //Filter out Prereqs that are excluded by the layer selection
      // Remove wrong layer objects from Prereqs
      if (GSA.Settings.SendOnlyResults)
      {
        //Ensure the load-relatd types are into the cache too so that the load cases and combos are there to resolve the load cases listed by the user
        var bucketNames = GSA.Settings.SendOnlyResults ? new string[] { "results" } : null;

        var streamLayerPrereqs = GSA.ReadTypePrereqs.Where(t =>
          bucketNames.Any(s => s.Equals((string)t.Key.GetAttribute("Stream"), StringComparison.InvariantCultureIgnoreCase))
          && ObjectTypeMatchesLayer(t.Key, GSA.Settings.TargetLayer));

        foreach (var kvp in streamLayerPrereqs)
        {
          FilteredReadTypePrereqs[kvp.Key] = kvp.Value.Where(l =>
            ObjectTypeMatchesLayer(l, GSA.Settings.TargetLayer)
            && bucketNames.Any(s => s.Equals((string)l.GetAttribute("Stream"), StringComparison.InvariantCultureIgnoreCase))).ToList();
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

          //The load tasks and combos need to be loaded to to ensure they can be used to process the list of cases to be sent
          var extraLoadCaseTypes = GSA.ReadTypePrereqs.Where(et => et.Key.Name.EndsWith("LoadTask", StringComparison.InvariantCultureIgnoreCase)
            || et.Key.Name.EndsWith("LoadCombo", StringComparison.InvariantCultureIgnoreCase));
          if (extraLoadCaseTypes.Count() > 0)
          {
            GetFilteredKeywords(extraLoadCaseTypes).ForEach(kw =>
            {
              if (!keywords.Contains(kw))
              {
                keywords.Add(kw);
              }
            });
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
        GetFilteredKeywords().ForEach(kw =>
        {
          if (!keywords.Contains(kw))
          {
            keywords.Add(kw);
          }
        });
      }

      return keywords;
    }
  }
}
