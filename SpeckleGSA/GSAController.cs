﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Interop.Gsa_10_0;
using SpeckleStructures;

namespace SpeckleGSA
{
    public class GSAController
    {
        public string SenderPropertiesStreamID
        {
            get
            {
                if (senders.ContainsKey("properties"))
                    return senders["properties"].StreamID;
                else
                    return "";
            }
        }
        public string SenderNodesStreamID { get
            {
                if (senders.ContainsKey("nodes"))
                    return senders["nodes"].StreamID;
                else
                    return "";
            }
        }
        public string SenderElementsStreamID {
            get
            {
                if (senders.ContainsKey("elements"))
                    return senders["elements"].StreamID;
                else
                    return "";
            }
        }

        private StreamManager streamManager;
        private UserManager userManager;
        private Dictionary<string, SpeckleGSASender> senders;
        private Dictionary<string, SpeckleGSAReceiver> receivers;
        
        public GSAController()
        {
            userManager = null;

            senders = new Dictionary<string, SpeckleGSASender>();
            receivers = new Dictionary<string, SpeckleGSAReceiver>();
        }
        
        #region Server
        public void Login(string email, string password, string serverAddress)
        {
            var tempUserManager = new UserManager(email, password, serverAddress);
            
            if (tempUserManager.Login() == 0)
            { 
                Status.AddMessage("Successfully logged in");
                userManager = tempUserManager;
                streamManager = new StreamManager(userManager.ServerAddress, userManager.ApiToken);
            }
            else
                Status.AddError("Failed to login");
        }

        public List<Tuple<string, string>> GetStreamList()
        {
            if (userManager == null | streamManager == null)
            {
                Status.AddError("Not logged in");
                return null;
            }

            try
            {
                Status.AddMessage("Fetching stream list.");
                var response = streamManager.GetStreams().Result;
                Status.AddMessage("Finished fetching stream list.");
                return response;
            }
            catch (Exception e)
            {
                Status.AddError(e.Message);
                return null;
            }
        }

        public void CloneModelStreams()
        {
            if (userManager == null | streamManager == null)
            {
                Status.AddError("Not logged in");
                return;
            }
            
            foreach (KeyValuePair<string, SpeckleGSASender> kvp in senders)
            {
                streamManager.CloneStream(kvp.Value.StreamID).ContinueWith(res => Status.AddMessage("Cloned " + kvp.Key + " stream to ID : " + res.Result));
            }
        }

        public List<Tuple<string,string>> GetSenderStreams()
        {
            List<Tuple<string, string>> streams = new List<Tuple<string, string>>();

            foreach (KeyValuePair<string, SpeckleGSASender> kvp in senders)
                streams.Add(new Tuple<string, string>(kvp.Value.StreamName, kvp.Value.StreamID));

            return streams;
        }
        #endregion

        #region GSA
        public async Task ExportObjects(string modelName)
        {
            List<Task> taskList = new List<Task>();

            if (!GSA.IsInit)
            {
                Status.AddError("GSA link not found.");
                return;
            }

            GSA.UpdateUnits();

            // Initialize object read priority list
            Dictionary<Type, List<Type>> typePrerequisites = new Dictionary<Type, List<Type>>();
            
            IEnumerable<Type> objTypes = typeof(GSA)
                .Assembly.GetTypes()
                .Where(t => t.IsSubclassOf(typeof(StructuralObject)) && !t.IsAbstract);

            Status.ChangeStatus("Preparing to read GSA Objects");

            foreach (Type t in objTypes)
            {
                if (t.GetMethod("GetObjects",
                    new Type[] { typeof(Dictionary<Type, List<StructuralObject>>) }) == null)
                    continue;

                if (t.GetField("Stream") == null) continue;

                List<Type> prereq = new List<Type>();
                if (t.GetField("ReadPrerequisite") != null)
                    prereq = ((Type[])t.GetField("ReadPrerequisite").GetValue(null)).ToList();
                        
                typePrerequisites[t] = prereq;
            }
            
            // Read objects
            Dictionary<Type, List<StructuralObject>> bucketObjects = new Dictionary<Type, List<StructuralObject>>();

            List<Type> currentBatch = new List<Type>();
            do
            {
                currentBatch = typePrerequisites.Where(i => i.Value.Count == 0).Select(i => i.Key).ToList();
                
                foreach (Type t in currentBatch)
                {
                    Status.ChangeStatus("Reading " + t.Name);

                    t.GetMethod("GetObjects",
                        new Type[] { typeof(Dictionary<Type, List<StructuralObject>>) })
                        .Invoke(null, new object[] { bucketObjects });
                    
                    typePrerequisites.Remove(t);

                    foreach (KeyValuePair<Type,List<Type>> kvp in typePrerequisites)
                        if (kvp.Value.Contains(t))
                            kvp.Value.Remove(t);
                }
            } while (currentBatch.Count > 0);
            
            // Seperate objects into streams
            Dictionary<string, List<object>> streamBuckets = new Dictionary<string, List<object>>();

            Status.ChangeStatus("Preparing stream buckets");

            foreach (KeyValuePair<Type, List<StructuralObject>> kvp in bucketObjects)
            {
                string stream = (string)kvp.Key.GetField("Stream").GetValue(null);

                if (!streamBuckets.ContainsKey(stream))
                    streamBuckets[stream] = (kvp.Value as IList).Cast<object>().ToList();
                else
                    streamBuckets[stream].AddRange((kvp.Value as IList).Cast<object>().ToList());
            }

            // Send package
            Status.ChangeStatus("Sending to Server");

            foreach (KeyValuePair<string, List<object>> kvp in streamBuckets)
            {
                // Create sender if not initialized
                if (!senders.ContainsKey(kvp.Key))
                {
                    Status.AddMessage(kvp.Key + " sender not initialized. Creating new " + kvp.Key + " sender.");
                    senders[kvp.Key] = new SpeckleGSASender(userManager.ServerAddress, userManager.ApiToken);
                    await senders[kvp.Key].InitializeSender();
                }

                senders[kvp.Key].UpdateName(modelName + "." + kvp.Key);
                
                // Send package asynchronously
                Task task = new Task(() =>
                {
                    try
                    { 
                        senders[kvp.Key].SendGSAObjects(
                            new Dictionary<string, List<object>>() {
                                { "All", kvp.Value }
                            });
                    }
                    catch (Exception ex)
                    {
                        Status.AddError(ex.Message);
                    }
                });
                task.Start();
                taskList.Add(task);
            }
            
            await Task.WhenAll(taskList);

            // Complete
            Status.ChangeStatus("Sending complete", 0);

            Status.AddMessage("Sending complete!");
        }        

        public async Task ImportObjects(Dictionary<string, string> streamIDs)
        {
            List<Task> taskList = new List<Task>();

            Dictionary<Type, List<StructuralObject>> objects = new Dictionary<Type, List<StructuralObject>>();

            if (!GSA.IsInit)
            {
                Status.AddError("GSA link not found.");
                return;
            }

            GSA.UpdateUnits();

            // Pull objects from server asynchronously
            Dictionary<string,List<object>> convertedObjects = new Dictionary<string, List<object>>();

            Status.ChangeStatus("Receiving from server");
            foreach (KeyValuePair<string, string> kvp in streamIDs)
            {
                if (kvp.Value == "")
                    Status.AddMessage("No " + kvp.Key + " stream specified.");
                else
                {
                    Status.AddMessage("Creating receiver " + kvp.Key);
                    receivers[kvp.Key] = new SpeckleGSAReceiver(userManager.ServerAddress, userManager.ApiToken);
                    await receivers[kvp.Key].InitializeReceiver(kvp.Value);

                    if (receivers[kvp.Key].StreamID == null || receivers[kvp.Key].StreamID == "")
                        Status.AddError("Could not connect to " + kvp.Key + " stream.");
                    else
                    {
                        
                        Task task = new Task(() =>
                        {
                            try
                            {
                                convertedObjects[kvp.Key] = receivers[kvp.Key].GetGSAObjects();
                            }
                            catch (Exception ex)
                            {
                                Status.AddError(ex.Message);
                            }
                        });
                        task.Start();
                        taskList.Add(task);
                    }
                }
            }

            await Task.WhenAll(taskList);

            // Initialize object write priority list
            Dictionary<Type, List<Type>> typePrerequisites = new Dictionary<Type, List<Type>>();

            IEnumerable<Type> objTypes = typeof(GSA)
                .Assembly.GetTypes()
                .Where(t => t.IsSubclassOf(typeof(StructuralObject)) && !t.IsAbstract);

            Status.ChangeStatus("Preparing to write GSA Objects");

            foreach (Type t in objTypes)
            {
                if (t.GetMethod("WriteObjects",
                    new Type[] { typeof(Dictionary<Type, List<StructuralObject>>) }) == null)
                    continue;
                
                List<Type> prereq = new List<Type>();
                if (t.GetField("WritePrerequisite") != null)
                    prereq = ((Type[])t.GetField("WritePrerequisite").GetValue(null)).ToList();

                typePrerequisites[t] = prereq;
            }

            List<KeyValuePair<Type,List<Type>>> typeCastPriorty = typePrerequisites.ToList();

            typeCastPriorty.Sort((x, y) => x.Value.Count().CompareTo(y.Value.Count()));

            // Populate dictionary
            Status.ChangeStatus("Bucketing objects");
            foreach (KeyValuePair<string,List<object>> kvp in convertedObjects)
            {
                double scaleFactor = (1.0).ConvertUnit(receivers[kvp.Key].Units.ShortUnitName(), GSA.Units);

                foreach (object obj in kvp.Value)
                { 
                    if (obj == null) continue;
                    
                    try
                    {
                        if (obj.IsList())
                        {
                            foreach(StructuralObject o in obj as IList)
                            {
                                o.Scale(scaleFactor);

                                Type castType = typeCastPriorty.Where(t => t.Key.IsSubclassOf(o.GetType())).First().Key;

                                if (castType == null) continue;

                                if (!objects.ContainsKey(castType))
                                    objects[castType] = new List<StructuralObject>() { (StructuralObject)Activator.CreateInstance(castType, o)};
                                else
                                    (objects[castType] as List<StructuralObject>).Add( (StructuralObject)Activator.CreateInstance(castType, o) );
                            }
                        }
                        else
                        {
                            (obj as StructuralObject).Scale(scaleFactor);

                            Type castType = typeCastPriorty.Where(t => t.Key.IsSubclassOf(obj.GetType())).First().Key;

                            if (castType == null) continue;

                            if (!objects.ContainsKey(castType))
                                objects[castType] = new List<StructuralObject>() { (StructuralObject)Activator.CreateInstance(castType, obj) };
                            else
                                (objects[castType] as List<StructuralObject>).Add( (StructuralObject)Activator.CreateInstance(castType, obj) );
                        }
                    }
                    catch (Exception ex)
                    {
                        Status.AddError(ex.Message);
                    }
                }
            }
            
            // Set up counter
            GSARefCounters.Clear();

            foreach (KeyValuePair<Type, List<StructuralObject>> kvp in objects)
            {
                // Reserve reference
                GSARefCounters.AddObjRefs((string)kvp.Key.GetField("GSAKeyword").GetValue(null),
                    (kvp.Value as IList).Cast<StructuralObject>().Select(o => o.Reference).ToList());
            }

            // Initialize object write priority list
            Status.ChangeStatus("Preparing to write GSA Object");


            // Clear GSA file
            foreach (KeyValuePair<Type, List<Type>> kvp in typePrerequisites)
            {
                Status.ChangeStatus("Clearing " + kvp.Key.Name);

                try
                {
                    string keyword = (string)kvp.Key.GetField("GSAKeyword").GetValue(null);
                    int highestRecord = (int)GSA.RunGWACommand("HIGHEST," + keyword);

                    GSA.RunGWACommand("BLANK," + kvp.Key.GetField("GSAKeyword").GetValue(null) + ",1," + highestRecord.ToString());
                }
                catch { }
            }

            // Write objects
            List<Type> currentBatch = new List<Type>();
            do
            {
                currentBatch = typePrerequisites.Where(i => i.Value.Count == 0).Select(i => i.Key).ToList();

                foreach (Type t in currentBatch)
                {
                    Status.ChangeStatus("Writing " + t.Name);

                    t.GetMethod("WriteObjects",
                        new Type[] { typeof(Dictionary<Type, List<StructuralObject>>) })
                        .Invoke(null, new object[] { objects });

                    typePrerequisites.Remove(t);

                    foreach (KeyValuePair<Type, List<Type>> kvp in typePrerequisites)
                        if (kvp.Value.Contains(t))
                            kvp.Value.Remove(t);
                }
            } while (currentBatch.Count > 0);


            GSA.UpdateViews();

            Status.ChangeStatus("Receiving complete", 0);
            Status.AddMessage("Receiving completed!");
        }
        #endregion
    }

}
