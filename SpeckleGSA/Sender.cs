﻿using SpeckleCore;
using SpeckleStructuresClasses;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SpeckleGSA
{
    /// <summary>
    /// Responsible for reading and sending GSA models.
    /// </summary>
    public class Sender
    {
        public Dictionary<string, SpeckleGSASender> Senders;
        public Dictionary<Type, List<IGSAObject>> SenderObjectCache;
        public Dictionary<Type, List<Type>> TypePrerequisites;

        public bool IsInit;
        public bool IsBusy;

        /// <summary>
        /// Creates Sender object.
        /// </summary>
        public Sender()
        {
            Senders = new Dictionary<string, SpeckleGSASender>();
            SenderObjectCache = new Dictionary<Type, List<IGSAObject>>();
            TypePrerequisites = new Dictionary<Type, List<Type>>();
            IsInit = false;
            IsBusy = false;
        }

        /// <summary>
        /// Initializes sender.
        /// </summary>
        /// <param name="restApi">Server address</param>
        /// <param name="apiToken">API token of account</param>
        /// <returns>Task</returns>
        public async Task Initialize(string restApi, string apiToken)
        {
            if (IsInit) return;

            if (!GSA.IsInit)
            { 
                Status.AddError("GSA link not found.");
                return;
            }

            GSA.FullClearCache();

            // Initialize object read priority list
            IEnumerable<Type> objTypes = typeof(GSA)
                .Assembly.GetTypes()
                .Where(t => t.IsSubclassOf(typeof(SpeckleObject)) && !t.IsAbstract);

            Status.ChangeStatus("Preparing to read GSA Objects");

            foreach (Type t in objTypes)
            {
                if (t.GetMethod("GetObjects",
                    new Type[] { typeof(Dictionary<Type, List<IGSAObject>>) }) == null)
                    continue;

                if (t.GetAttribute("Stream") == null) continue;

                if (t.GetAttribute("AnalysisLayer") != null)
                    if (GSA.TargetAnalysisLayer && !(bool)t.GetAttribute("AnalysisLayer")) continue;

                if (t.GetAttribute("DesignLayer") != null)
                    if (GSA.TargetDesignLayer && !(bool)t.GetAttribute("DesignLayer")) continue;

                List<Type> prereq = new List<Type>();
                if (t.GetAttribute("ReadPrerequisite") != null)
                    prereq = ((Type[])t.GetAttribute("ReadPrerequisite")).ToList();

                TypePrerequisites[t] = prereq;
            }

            // Remove wrong layer objects from prerequisites
            foreach (Type t in objTypes)
            {
                if (t.GetAttribute("AnalysisLayer") != null)
                    if (GSA.TargetAnalysisLayer && !(bool)t.GetAttribute("AnalysisLayer"))
                        foreach (KeyValuePair<Type, List<Type>> kvp in TypePrerequisites)
                            kvp.Value.Remove(t);

                if (t.GetAttribute("DesignLayer") != null)
                    if (GSA.TargetDesignLayer && !(bool)t.GetAttribute("DesignLayer"))
                        foreach (KeyValuePair<Type, List<Type>> kvp in TypePrerequisites)
                            kvp.Value.Remove(t);
            }

            // Create the streams
            Status.ChangeStatus("Creating streams");

            List<string> streamNames = new List<string>();

            if (Settings.SeperateStreams)
            {
                foreach (Type t in objTypes)
                    streamNames.Add((string)t.GetAttribute("Stream"));
                streamNames = streamNames.Distinct().ToList();
            }
            else
                streamNames.Add("Full Model");

            foreach (string streamName in streamNames)
            {
                Senders[streamName] = new SpeckleGSASender(restApi, apiToken);

                if (!GSA.Senders.ContainsKey(streamName))
                {
                    Status.AddMessage(streamName + " sender not initialized. Creating new " + streamName + " sender.");
                    await Senders[streamName].InitializeSender(null, streamName);
                    GSA.Senders[streamName] = Senders[streamName].StreamID;
                }
                else
                    await Senders[streamName].InitializeSender(GSA.Senders[streamName], streamName);
            }

            Status.ChangeStatus("Ready to stream");
            IsInit = true;
        }

        /// <summary>
        /// Trigger to update stream.
        /// </summary>
        public void Trigger()
        {
            if (IsBusy) return;
            if (!IsInit) return;

            IsBusy = true;
            GSA.ClearCache();
            GSA.UpdateUnits();

            // Read objects
            List<Type> currentBatch = new List<Type>();
            List<Type> traversedTypes = new List<Type>();

            bool changeDetected = false;
            do
            {
                currentBatch = TypePrerequisites.Where(i => i.Value.Count(x => !traversedTypes.Contains(x)) == 0).Select(i => i.Key).ToList();
                currentBatch.RemoveAll(i => traversedTypes.Contains(i));

                foreach (Type t in currentBatch)
                {
                    //Status.ChangeStatus("Reading " + t.Name);

                    bool result = (bool)t.GetMethod("GetObjects",
                        new Type[] { typeof(Dictionary<Type, List<IGSAObject>>) })
                        .Invoke(null, new object[] { SenderObjectCache });

                    if (result)
                        changeDetected = true;

                    traversedTypes.Add(t);
                }
            } while (currentBatch.Count > 0);

            if (!changeDetected)
            {
                Status.ChangeStatus("Finished sending", 100);
                IsBusy = false;
                return;
            }

            // Convert objects to base class
            Dictionary<Type, List<IStructural>> convertedBucket = new Dictionary<Type, List<IStructural>>();
            foreach (KeyValuePair<Type, List<IGSAObject>> kvp in SenderObjectCache)
            {
                if ((kvp.Key == typeof(GSANode)) && Settings.SendOnlyMeaningfulNodes && SenderObjectCache.ContainsKey(typeof(GSANode)))
                {
                    // Remove unimportant nodes
                    convertedBucket[kvp.Key] = kvp.Value.Cast<GSANode>()
                        .Where(n => n.ForceSend ||
                        (n.Restraint != null && n.Restraint.Value.Any(x => x)) ||
                        (n.Restraint != null && n.Stiffness.Value.Any(x => x == 0)) ||
                        n.Mass > 0)
                        .Select(x => x.GetBase()).Cast<IStructural>().ToList();
                }
                else
                {
                    convertedBucket[kvp.Key] = kvp.Value.Select(
                        x => x.GetBase()).Cast<IStructural>().ToList();
                }
            }

            // Seperate objects into streams
            Dictionary<string, Dictionary<string, List<object>>> streamBuckets = new Dictionary<string, Dictionary<string, List<object>>>();

            Status.ChangeStatus("Preparing stream buckets");

            foreach (KeyValuePair<Type, List<IStructural>> kvp in convertedBucket)
            {
                string stream;

                if (Settings.SeperateStreams)
                    stream = (string)kvp.Key.GetAttribute("Stream");
                else
                    stream = "Full Model";

                if (!streamBuckets.ContainsKey(stream))
                    streamBuckets[stream] = new Dictionary<string, List<object>>() { { kvp.Key.BaseType.Name.ToString(), (kvp.Value as IList).Cast<object>().ToList() } };
                else
                    streamBuckets[stream][kvp.Key.BaseType.Name.ToString()] = (kvp.Value as IList).Cast<object>().ToList();
            }

            // Send package
            Status.ChangeStatus("Sending to Server");

            foreach (KeyValuePair<string, Dictionary<string, List<object>>> kvp in streamBuckets)
            {
                string streamName = "";

                if (Settings.SeperateStreams)
                    streamName = GSA.Title() + "." + kvp.Key;
                else
                    streamName = GSA.Title();

                Senders[kvp.Key].UpdateName(streamName);
                Senders[kvp.Key].SendGSAObjects(kvp.Value);
            }

            IsBusy = false;
            Status.ChangeStatus("Finished sending", 100);
        }

        /// <summary>
        /// Dispose receiver.
        /// </summary>
        public void Dispose()
        {
            foreach (KeyValuePair<string, string> kvp in GSA.Senders)
                Senders[kvp.Key].Dispose();
        }
    }
}
