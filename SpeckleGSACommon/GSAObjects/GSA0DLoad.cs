﻿using Interop.Gsa_10_0;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Media3D;
using SpeckleStructures;
using System.Reflection;

namespace SpeckleGSA
{
    public class GSA0DLoad : Structural0DLoad
    {
        public static readonly string GSAKeyword = "LOAD_NODE";
        public static readonly string Stream = "loads";
        public static readonly int WritePriority = 9999;

        public static readonly Type[] ReadPrerequisite = new Type[1] { typeof(GSANode) };
        public static readonly Type[] WritePrerequisite = new Type[1] { typeof(GSANode) };
        
        public int Axis;

        public GSA0DLoad()
        {
            Axis = 0;
        }

        public GSA0DLoad(Structural0DLoad baseClass)
        {
            Axis = 0;

            foreach (FieldInfo f in baseClass.GetType().GetFields())
                f.SetValue(this, f.GetValue(baseClass));

            foreach (PropertyInfo p in baseClass.GetType().GetProperties())
                p.SetValue(this, p.GetValue(baseClass));
        }

        #region GSAObject Functions
        public static void GetObjects(Dictionary<Type, List<StructuralObject>> dict)
        {
            if (!dict.ContainsKey(typeof(GSANode))) return;

            List<StructuralObject> nodes = dict[typeof(GSANode)];
            List<int> nodeRefs = nodes.Select(n => n.Reference).ToList(); 

            List<StructuralObject> loads = new List<StructuralObject>();

            string res = (string)GSA.RunGWACommand("GET_ALL,LOAD_NODE");

            if (res == "")
                return;

            string[] pieces = res.Split(new string[] { "\n" }, StringSplitOptions.RemoveEmptyEntries);

            double counter = 1;
            foreach (string p in pieces)
            {
                List<GSA0DLoad> loadSubList = new List<GSA0DLoad>();

                // Placeholder load object to get list of nodes and load values
                // Need to transform to axis
                GSA0DLoad initLoad = new GSA0DLoad();
                initLoad.ParseGWACommand(p);

                // Only send those where the nodes actually exists
                List<int> nodesApplied = initLoad.Nodes
                    .Where(nRef => nodeRefs.Contains(nRef)).ToList();

                // Raise node flag to make sure it gets sent
                foreach(GSANode n in nodes.Where(n => nodesApplied.Contains(n.Reference)).Cast<GSANode>())
                    n.ForceSend = true;

                foreach (int nRef in nodesApplied)
                {
                    GSA0DLoad load = new GSA0DLoad();
                    load.Name = initLoad.Name;
                    load.LoadCase = initLoad.LoadCase;
                    
                    // Transform load to defined axis
                    GSANode node = nodes.Where(n => n.Reference == nRef).First() as GSANode;
                    Axis loadAxis = HelperFunctions.Parse0DAxis(initLoad.Axis, node.Coordinates.ToArray());
                    load.Loading = initLoad.Loading;
                    load.Loading.TransformOntoAxis(loadAxis);

                    // If the loading already exists, add node ref to list
                    List<GSA0DLoad> matches = loadSubList.Where(l => l.Loading == load.Loading).ToList();
                    if (matches.Count() > 0)
                        matches[0].Nodes.Add(nRef);
                    else
                    {
                        load.Nodes.Add(nRef);
                        loadSubList.Add(load);
                    }
                }

                loads.AddRange(loadSubList);

                Status.ChangeStatus("Reading 0D loads", counter++ / pieces.Length * 100);
            }

            dict[typeof(GSA0DLoad)] = loads;
        }

        public static void WriteObjects(Dictionary<Type, List<StructuralObject>> dict)
        {
            if (!dict.ContainsKey(typeof(GSA0DLoad))) return;

            List<StructuralObject> loads = dict[typeof(GSA0DLoad)] as List<StructuralObject>;

            double counter = 1;
            foreach (StructuralObject l in loads)
            {
                GSARefCounters.RefObject(l);
                
                List<string> commands = (l as GSA0DLoad).GetGWACommand();
                foreach (string c in commands)
                    GSA.RunGWACommand(c);

                Status.ChangeStatus("Writing 0D loads", counter++ / loads.Count() * 100);
            }
        }

        public void ParseGWACommand(string command, Dictionary<Type, List<StructuralObject>> dict = null)
        {
            string[] pieces = command.ListSplit(",");

            int counter = 1; // Skip identifier
            Name = pieces[counter++].Trim(new char[] { '"' });
            Nodes = pieces[counter++].ParseGSAList(GsaEntity.NODE).ToList();
            LoadCase = Convert.ToInt32(pieces[counter++]);

            string axis = pieces[counter++];
            Axis = axis == "GLOBAL" ? 0 : Convert.ToInt32(axis);

            string direction = pieces[counter++].ToLower();
            switch(direction.ToUpper())
            {
                case "X":
                    Loading.X = Convert.ToDouble(pieces[counter++]);
                    break;
                case "Y":
                    Loading.Y = Convert.ToDouble(pieces[counter++]);
                    break;
                case "Z":
                    Loading.Z = Convert.ToDouble(pieces[counter++]);
                    break;
                case "XX":
                    Loading.X = Convert.ToDouble(pieces[counter++]);
                    break;
                case "YY":
                    Loading.Y = Convert.ToDouble(pieces[counter++]);
                    break;
                case "ZZ":
                    Loading.Z = Convert.ToDouble(pieces[counter++]);
                    break;
                default:
                    // TODO: Error case maybe?
                    break;
            }
        }

        public List<string> GetGWACommand(Dictionary<Type, object> dict = null)
        {
            List<string> ls = new List<string>();

            double[] values = Loading.ToArray();
            string[] direction = new string[6] { "X", "Y", "Z", "X", "Y", "Z" };

            for(int i = 0; i < 6; i++)
            {
                List<string> subLs = new List<string>();
                
                if (values[i] == 0) continue;

                subLs.Add("SET");
                subLs.Add(GSAKeyword);
                subLs.Add(Name == "" ? " " : "");
                subLs.Add(string.Join(" ", Nodes));
                subLs.Add(LoadCase.ToString());
                subLs.Add("GLOBAL"); // Axis
                subLs.Add(direction[i]);
                subLs.Add(values[i].ToString());

                ls.Add(string.Join(",", subLs));

            }

            return ls;
        }
        #endregion
    }
}
