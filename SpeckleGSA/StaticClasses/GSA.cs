﻿using Interop.Gsa_10_0;
using SpeckleCore;
using SQLite;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SpeckleGSAInterfaces;
using SpeckleGSAProxy;

namespace SpeckleGSA
{
  /// <summary>
  /// Static class which interfaces with GSA
  /// </summary>
  public static class GSA
  {
		public static Settings Settings = new Settings();
		public static GSAInterfacer Interfacer = new GSAInterfacer
		{
			Indexer = new Indexer()
		};

    public static bool IsInit;

    public static Dictionary<string, Tuple<string, string>> Senders { get; set; }
    public static List<Tuple<string, string>> Receivers { get; set; }

		public static Dictionary<Type, List<Type>> TypePrerequisites = new Dictionary<Type, List<Type>>();

		public static void Init()
    {
      if (IsInit) return;

      Senders = new Dictionary<string, Tuple<string, string>>();
      Receivers = new List<Tuple<string, string>>();

      IsInit = true;

      Status.AddMessage("Linked to GSA.");

			InitialiseKits(out List<string> statusMessages);

			if (statusMessages.Count() > 0)
			{
				foreach (var msg in statusMessages)
				{
					Status.AddMessage(msg);
				}
			}
		}

		private static void InitialiseKits(out List<string> statusMessages)
		{
			statusMessages = new List<string>();

			var attributeType = typeof(GSAObject);
			var interfaceType = typeof(IGSASpeckleContainer);

			SpeckleInitializer.Initialize();

			// Run initialize receiver method in interfacer
			var assemblies = SpeckleInitializer.GetAssemblies().Where(a => a.GetTypes().Any(t => t.GetInterfaces().Contains(typeof(ISpeckleInitializer))));

			foreach (var ass in assemblies)
			{
				var types = ass.GetTypes();

				try
				{
					var gsaStatic = types.FirstOrDefault(t => t.GetInterfaces().Contains(typeof(ISpeckleInitializer)) && t.GetProperties().Any(p => p.PropertyType == typeof(IGSAInterfacer)));
					if (gsaStatic == null)
					{
						continue;
					}

					gsaStatic.GetProperty("Interface").SetValue(null, GSA.Interfacer);
					gsaStatic.GetProperty("Settings").SetValue(null, GSA.Settings);
				}
				catch(Exception e)
				{
					//The kits could throw an exception due to an app-specific library not being linked in (e.g.: the Revit SDK).  These libraries aren't of the kind that
					//would contain the static properties searched for anyway, so just continue.
					continue;
				}

				var objTypesMatchingLayer = types.Where(t => interfaceType.IsAssignableFrom(t) && t != interfaceType && ObjectTypeMatchesLayer(t, attributeType));

				//Pass one: for each type who has the correct layer attribute, record its prerequisites (some of which might not be the correct layer)
				foreach (var t in objTypesMatchingLayer)
				{
					TypePrerequisites[t] = (t.GetAttribute("WritePrerequisite", attributeType) == null)
						? new List<Type>()
						: ((Type[])t.GetAttribute("WritePrerequisite", attributeType)).Where(prereqT => ObjectTypeMatchesLayer(prereqT, attributeType)).ToList();
				}
			}
		}

		private static bool ObjectTypeMatchesLayer(Type t, Type attributeType)
		{
			var analysisLayerAttribute = t.GetAttribute("AnalysisLayer", attributeType);
			var designLayerAttribute = t.GetAttribute("DesignLayer", attributeType);

			//If an object type has a layer attribute exists and its boolean value doesn't match the settings target layer, then it doesn't match.  This could be reviewed and simplified.
			if ((analysisLayerAttribute != null && GSA.Settings.TargetAnalysisLayer && !(bool)analysisLayerAttribute)
				|| (designLayerAttribute != null && GSA.Settings.TargetDesignLayer && !(bool)designLayerAttribute))
			{
				return false;
			}
			return true;
		}

		#region File Operations
		/// <summary>
		/// Creates a new GSA file. Email address and server address is needed for logging purposes.
		/// </summary>
		/// <param name="emailAddress">User email address</param>
		/// <param name="serverAddress">Speckle server address</param>
		public static void NewFile(string emailAddress, string serverAddress, bool showWindow = true)
    {
			if (!IsInit) return;

			Interfacer.NewFile(emailAddress, serverAddress, showWindow);

			GetSpeckleClients(emailAddress, serverAddress);

      Status.AddMessage("Created new file.");
    }

    /// <summary>
    /// Opens an existing GSA file. Email address and server address is needed for logging purposes.
    /// </summary>
    /// <param name="path">Absolute path to GSA file</param>
    /// <param name="emailAddress">User email address</param>
    /// <param name="serverAddress">Speckle server address</param>
    public static void OpenFile(string path, string emailAddress, string serverAddress, bool showWindow = true)
    {
			if (!IsInit) return;

			Interfacer.OpenFile(path, emailAddress, serverAddress, showWindow);
			GetSpeckleClients(emailAddress, serverAddress);

			Status.AddMessage("Opened new file.");
    }

    /// <summary>
    /// Close GSA file.
    /// </summary>
    public static void Close()
    {
      if (!IsInit) return;

			Interfacer.Close();
			Senders.Clear();
      Receivers.Clear();
    }
    #endregion

    #region Speckle Client
    /// <summary>
    /// Extracts sender and receiver streams associated with the account.
    /// </summary>
    /// <param name="emailAddress">User email address</param>
    /// <param name="serverAddress">Speckle server address</param>
    public static void GetSpeckleClients(string emailAddress, string serverAddress)
    {
      Senders.Clear();
      Receivers.Clear();

      try
      { 
        string key = emailAddress + "&" + serverAddress.Replace(':', '&');
				
        string res = ((GSAInterfacer)Interfacer).GetSID();

        if (res == "")
          return;

        List<string[]> sids = Regex.Matches(res, @"(?<={).*?(?=})").Cast<Match>()
                .Select(m => m.Value.Split(new char[] { ':' }))
                .Where(s => s.Length == 2)
                .ToList();

        string[] senderList = sids.Where(s => s[0] == "SpeckleSender&" + key).FirstOrDefault();
        string[] receiverList = sids.Where(s => s[0] == "SpeckleReceiver&" + key).FirstOrDefault();

        if (senderList != null && !string.IsNullOrEmpty(senderList[1]))
        {
          string[] senders = senderList[1].Split(new char[] { '&' });

          for (int i = 0; i < senders.Length; i += 3)
            Senders[senders[i]] = new Tuple<string, string>(senders[i + 1], senders[i + 2]);
        }

        if (receiverList != null && !string.IsNullOrEmpty(receiverList[1]))
        {
          string[] receivers = receiverList[1].Split(new char[] { '&' });

          for (int i = 0; i < receivers.Length; i += 2)
            Receivers.Add(new Tuple<string, string>(receivers[i], receivers[i + 1]));
        }
      }
      catch
      {
        // If fail to read, clear client SIDs
        Senders.Clear();
        Receivers.Clear();
        SetSpeckleClients(emailAddress, serverAddress);
      }
    }

    /// <summary>
    /// Writes sender and receiver streams associated with the account.
    /// </summary>
    /// <param name="emailAddress">User email address</param>
    /// <param name="serverAddress">Speckle server address</param>
    public static void SetSpeckleClients(string emailAddress, string serverAddress)
    {
      string key = emailAddress + "&" + serverAddress.Replace(':', '&');
			string res = Interfacer.GetSID();

			List<string[]> sids = Regex.Matches(res, @"(?<={).*?(?=})").Cast<Match>()
              .Select(m => m.Value.Split(new char[] { ':' }))
              .Where(s => s.Length == 2)
              .ToList();

      sids.RemoveAll(S => S[0] == "SpeckleSender&" + key || S[0] == "SpeckleReceiver&" + key);

      List<string> senderList = new List<string>();
      foreach (KeyValuePair<string, Tuple<string, string>> kvp in Senders)
      {
        senderList.Add(kvp.Key);
        senderList.Add(kvp.Value.Item1);
        senderList.Add(kvp.Value.Item2);
      }

      List<string> receiverList = new List<string>();
      foreach (Tuple<string, string> t in Receivers)
      {
        receiverList.Add(t.Item1);
        receiverList.Add(t.Item2);
      }

      sids.Add(new string[] { "SpeckleSender&" + key, string.Join("&", senderList) });
      sids.Add(new string[] { "SpeckleReceiver&" + key, string.Join("&", receiverList) });

      string sidRecord = "";
      foreach (string[] s in sids)
        sidRecord += "{" + s[0] + ":" + s[1] + "}";

			Interfacer.SetSID(sidRecord);
    }
    #endregion

    #region Document Properties

    /// <summary>
    /// Extracts the base properties of the Speckle stream.
    /// </summary>
    /// <returns>Base property dictionary</returns>
    public static Dictionary<string, object> GetBaseProperties()
    {
      var baseProps = new Dictionary<string, object>();

      baseProps["units"] = Settings.Units.LongUnitName();
      // TODO: Add other units

      var tolerances = Interfacer.GetTolerances();

			var lengthTolerances = new List<double>() {
								Convert.ToDouble(tolerances[3]), // edge
                Convert.ToDouble(tolerances[5]), // leg_length
                Convert.ToDouble(tolerances[7])  // memb_cl_dist
            };

      var angleTolerances = new List<double>(){
                Convert.ToDouble(tolerances[4]), // angle
                Convert.ToDouble(tolerances[6]), // meemb_orient
            };

      baseProps["tolerance"] = lengthTolerances.Max().ConvertUnit("m", Settings.Units);
      baseProps["angleTolerance"] = angleTolerances.Max().ToRadians();

      return baseProps;
    }

    #endregion

    #region Views

    /// <summary>
    /// Update GSA case and task links. This should be called at the end of changes.
    /// </summary>
    public static void UpdateCasesAndTasks()
    {
			Interfacer.UpdateCasesAndTasks();
    }
    #endregion
  }
}
