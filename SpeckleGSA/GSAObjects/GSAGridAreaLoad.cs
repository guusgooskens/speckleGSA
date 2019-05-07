﻿using Interop.Gsa_10_0;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Windows.Media.Media3D;
using SpeckleStructuresClasses;
using System.Reflection;

namespace SpeckleGSA
{
    [GSAObject("LOAD_GRID_AREA.2", new string[] { "POLYLINE.1", "GRID_SURFACE.1", "GRID_PLANE.4", "AXIS" }, "elements", true, true, new Type[] { }, new Type[] {  typeof(GSALoadCase) })]
    public class GSAGridAreaLoad : Structural2DLoadPanel, IGSAObject
    {
        public string GWACommand { get; set; } = "";
        public List<string> SubGWACommand { get; set; } = new List<string>();

        #region Sending Functions
        public static bool GetObjects(Dictionary<Type, List<IGSAObject>> dict)
        {
            if (!dict.ContainsKey(MethodBase.GetCurrentMethod().DeclaringType))
                dict[MethodBase.GetCurrentMethod().DeclaringType] = new List<IGSAObject>();

            List<GSAGridAreaLoad> loads = new List<GSAGridAreaLoad>();

            string keyword = MethodBase.GetCurrentMethod().DeclaringType.GetGSAKeyword();
            string[] subKeywords = MethodBase.GetCurrentMethod().DeclaringType.GetSubGSAKeyword();

            string[] lines = GSA.GetGWARecords("GET_ALL," + keyword);
            List<string> deletedLines = GSA.GetDeletedGWARecords("GET_ALL," + keyword).ToList();
            foreach (string k in subKeywords)
                deletedLines.AddRange(GSA.GetDeletedGWARecords("GET_ALL," + k));

            // Remove deleted lines
            dict[typeof(GSAGridAreaLoad)].RemoveAll(l => deletedLines.Contains(l.GWACommand));
            foreach (KeyValuePair<Type, List<IGSAObject>> kvp in dict)
                kvp.Value.RemoveAll(l => l.SubGWACommand.Any(x => deletedLines.Contains(x)));

            // Filter only new lines
            string[] prevLines = dict[typeof(GSAGridAreaLoad)].Select(l => l.GWACommand).ToArray();
            string[] newLines = lines.Where(l => !prevLines.Contains(l)).ToArray();

            foreach (string p in newLines)
            {
                GSAGridAreaLoad load = ParseGWACommand(p);
                loads.Add(load);
            }

            dict[typeof(GSAGridAreaLoad)].AddRange(loads);

            if (loads.Count() > 0 || deletedLines.Count() > 0) return true;

            return false;
        }

        public static GSAGridAreaLoad ParseGWACommand(string command)
        {
            GSAGridAreaLoad ret = new GSAGridAreaLoad();

            ret.GWACommand = command;

            string[] pieces = command.ListSplit(",");

            int counter = 1; // Skip identifier
            ret.Name = pieces[counter++].Trim(new char[] { '"' });

            var gridPlaneRefRet = GetGridPlaneRef(Convert.ToInt32(pieces[counter++]));
            ret.SubGWACommand.Add(gridPlaneRefRet.Item2);
            var gridPlaneDataRet = GetGridPlaneData(gridPlaneRefRet.Item1);
            ret.SubGWACommand.Add(gridPlaneDataRet.Item3);

            string gwaRec = null;
            StructuralAxis axis = HelperFunctions.Parse0DAxis(gridPlaneDataRet.Item1, out gwaRec);
            if (gwaRec != null)
                ret.SubGWACommand.Add(gwaRec);
            double elevation = gridPlaneDataRet.Item2;

            string polylineDescription = "";

            switch (pieces[counter++])
            {
                case "PLANE":
                    // TODO: Do not handle for now
                    return null;
                case "POLYREF":
                    string polylineRef = pieces[counter++];
                    var polylineRet = GetPolylineDesc(Convert.ToInt32(polylineRef));
                    polylineDescription = polylineRet.Item1;
                    ret.SubGWACommand.Add(polylineRet.Item2);
                    break;
                case "POLYGON":
                    polylineDescription = pieces[counter++];
                    break;
            }
            double[] polyVals = ParsePolylineDesc(polylineDescription);

            for (int i = 2; i < polyVals.Length; i += 3)
                polyVals[i] = elevation;

            ret.Value = TransformPolyline(polyVals, axis).ToList();
            ret.Closed = true;

            ret.LoadCaseRef = pieces[counter++];

            int loadAxisId = 0;
            string loadAxisData = pieces[counter++];
            StructuralAxis loadAxis;
            if (loadAxisData == "LOCAL")
                loadAxis = axis;
            else
            {
                loadAxisId = loadAxisData == "GLOBAL" ? 0 : Convert.ToInt32(axis);
                loadAxis = HelperFunctions.Parse0DAxis(loadAxisId, out gwaRec);
                if (gwaRec != null)
                    ret.SubGWACommand.Add(gwaRec);
            }
            bool projected = pieces[counter++] == "YES";
            string direction = pieces[counter++];
            double value = Convert.ToDouble(pieces[counter++]);

            ret.Loading = new StructuralVectorThree(new double[3]);
            switch (direction.ToUpper())
            {
                case "X":
                    ret.Loading.Value[0] = value;
                    break;
                case "Y":
                    ret.Loading.Value[1] = value;
                    break;
                case "Z":
                    ret.Loading.Value[2] = value;
                    break;
                default:
                    // TODO: Error case maybe?
                    break;
            }
            ret.Loading.TransformOntoAxis(loadAxis);

            if (projected)
            {
                double scale = (ret.Loading.Value[0] * axis.Normal.Value[0] +
                    ret.Loading.Value[1] * axis.Normal.Value[1] +
                    ret.Loading.Value[2] * axis.Normal.Value[2]) /
                    (axis.Normal.Value[0] * axis.Normal.Value[0] +
                    axis.Normal.Value[1] * axis.Normal.Value[1] +
                    axis.Normal.Value[2] * axis.Normal.Value[2]);

                ret.Loading = new StructuralVectorThree(axis.Normal.Value[0], axis.Normal.Value[1], axis.Normal.Value[2]);
                ret.Loading.Scale(scale);
            }

            return ret;
        }
        #endregion

        #region Receiving Functions
        public static void SetObjects(Dictionary<Type, List<IStructural>> dict)
        {
            if (!dict.ContainsKey(typeof(Structural2DLoadPanel))) return;

            foreach (IStructural obj in dict[typeof(Structural2DLoadPanel)])
            {
                Set(obj as Structural2DLoadPanel);
            }
        }

        public static void Set(Structural2DLoadPanel load)
        {
            if (load == null)
                return;

            string keyword = MethodBase.GetCurrentMethod().DeclaringType.GetGSAKeyword();
            
            int polylineIndex = Indexer.ResolveIndex("POLYLINE.1", load);
            int gridSurfaceIndex = Indexer.ResolveIndex("GRID_SURFACE.1", load);
            int gridPlaneIndex = Indexer.ResolveIndex("GRID_PLANE.4", load);

            int loadCaseRef = 0;
            try
            {
                loadCaseRef = Indexer.LookupIndex(typeof(GSALoadCase), load.LoadCaseRef).Value;
            }
            catch { loadCaseRef = Indexer.ResolveIndex(typeof(GSALoadCase), load.LoadCaseRef); }

            List<string> ls = new List<string>();

            string[] direction = new string[3] { "X", "Y", "Z" };

            for (int i = 0; i < load.Loading.Value.Count(); i++)
            {
                if (load.Loading.Value[i] == 0) continue;

                ls.Clear();

                int index = Indexer.ResolveIndex(MethodBase.GetCurrentMethod().DeclaringType);

                ls.Add("SET_AT");
                ls.Add(index.ToString());
                ls.Add(keyword);
                ls.Add(load.Name == null || load.Name == "" ? " " : load.Name);
                ls.Add(gridSurfaceIndex.ToString());
                ls.Add("POLYREF");
                ls.Add(polylineIndex.ToString());
                ls.Add(loadCaseRef.ToString());
                ls.Add("GLOBAL");
                ls.Add("NO");
                ls.Add(direction[i]);
                ls.Add(load.Loading.Value[i].ToString());

                GSA.RunGWACommand(string.Join(",", ls));
            }

            StructuralAxis axis = HelperFunctions.Parse2DAxis(load.Value.ToArray());

            // Calculate elevation
            double elevation = (load.Value[0] * axis.Normal.Value[0] +
                load.Value[1] * axis.Normal.Value[1] +
                load.Value[2] * axis.Normal.Value[2]) /
                Math.Sqrt(axis.Normal.Value[0] * axis.Normal.Value[0] +
                    axis.Normal.Value[1] * axis.Normal.Value[1] +
                    axis.Normal.Value[2] * axis.Normal.Value[2]);

            // Transform coordinate to new axis
            double[] transformed = UntransformPolyline(load.Value.ToArray(), axis);

            ls.Clear();
            ls.Add("SET");
            ls.Add("POLYLINE.1");
            ls.Add(polylineIndex.ToString());
            ls.Add(load.Name == null || load.Name == "" ? " " : load.Name);
            ls.Add("NO_RGB");
            ls.Add("-1"); // Grid plane
            ls.Add("2");
            List<string> subLs = new List<string>();
            for(int i = 0; i < transformed.Count(); i+=3)
                subLs.Add("(" + transformed[i].ToString() + "," + transformed[i + 1].ToString() + ")");
            ls.Add(string.Join(" ", subLs));
            GSA.RunGWACommand(string.Join("\t", ls));
            
            ls.Clear();
            ls.Add("SET"); 
            ls.Add("GRID_SURFACE.1");
            ls.Add(gridSurfaceIndex.ToString());
            ls.Add(load.Name == null || load.Name == "" ? " " : load.Name);
            ls.Add(gridPlaneIndex.ToString());
            ls.Add("2"); // Dimension of elements to target
            ls.Add("all"); // List of elements to target
            ls.Add("0.01"); // Tolerance
            ls.Add("ONE"); // Span option
            ls.Add("0"); // Span angle
            GSA.RunGWACommand(string.Join(",", ls));

            ls.Clear();
            ls.Add("SET");
            ls.Add("GRID_PLANE.4");
            ls.Add(gridPlaneIndex.ToString());
            ls.Add(load.Name == null || load.Name == "" ? " " : load.Name);
            ls.Add("GENERAL"); // Type
            ls.Add(SetAxis(axis).ToString());
            ls.Add(elevation.ToString());
            ls.Add("0"); // Elevation above
            ls.Add("0"); // Elevation below
            GSA.RunGWACommand(string.Join(",", ls));
        }
        #endregion

        #region Helper Functions
        private static Tuple<string, string> GetPolylineDesc(int polylineRef)
        {
            string res = GSA.GetGWARecords("GET,POLYLINE.1," + polylineRef.ToString()).FirstOrDefault();
            string[] pieces = res.ListSplit(",");

            // TODO: commas are used to seperate both data and polyline coordinate values...
            return new Tuple<string, string>(string.Join(",", pieces.Skip(6)), res);
        }

        private static Tuple<int, string> GetGridPlaneRef(int gridSurfaceRef)
        {
            string res = GSA.GetGWARecords("GET,GRID_SURFACE.1," + gridSurfaceRef.ToString()).FirstOrDefault();
            string[] pieces = res.ListSplit(",");

            return new Tuple<int, string>(Convert.ToInt32(pieces[3]), res);
        }

        private static Tuple<int, double, string> GetGridPlaneData(int gridPlaneRef)
        {
            string res = GSA.GetGWARecords("GET,GRID_PLANE.4," + gridPlaneRef.ToString()).FirstOrDefault();
            string[] pieces = res.ListSplit(",");

            return new Tuple<int, double, string>(Convert.ToInt32(pieces[4]), Convert.ToDouble(pieces[5]), res);
        }

        private static double[] TransformPolyline(double[] values, StructuralAxis axis)
        {
            List<double> newVals = new List<double>();

            for (int i = 0; i < values.Length; i += 3)
            {
                List<double> coor = values.Skip(i).Take(3).ToList();

                double x = 0;
                double y = 0;
                double z = 0;

                x += axis.Xdir.Value[0] * coor[0];
                y += axis.Xdir.Value[1] * coor[0];
                z += axis.Xdir.Value[2] * coor[0];

                x += axis.Ydir.Value[0] * coor[1];
                y += axis.Ydir.Value[1] * coor[1];
                z += axis.Ydir.Value[2] * coor[1];

                x += axis.Normal.Value[0] * coor[2];
                y += axis.Normal.Value[1] * coor[2];
                z += axis.Normal.Value[2] * coor[2];

                newVals.Add(x);
                newVals.Add(y);
                newVals.Add(z);
            }

            return newVals.ToArray();
        }

        private static double[] UntransformPolyline(double[] values, StructuralAxis axis)
        {
            List<double> newVals = new List<double>();

            for (int i = 0; i < values.Length; i += 3)
            {
                List<double> coor = values.Skip(i).Take(3).ToList();

                double x = 0;
                double y = 0;
                double z = 0;

                x += axis.Xdir.Value[0] * coor[0];
                y += axis.Ydir.Value[0] * coor[0];
                z += axis.Normal.Value[0] * coor[0];

                x += axis.Xdir.Value[1] * coor[1];
                y += axis.Ydir.Value[1] * coor[1];
                z += axis.Normal.Value[1] * coor[1];

                x += axis.Xdir.Value[2] * coor[2];
                y += axis.Ydir.Value[2] * coor[2];
                z += axis.Normal.Value[2] * coor[2];

                newVals.Add(x);
                newVals.Add(y);
                newVals.Add(z);
            }

            return newVals.ToArray();
        }

        private static double[] ParsePolylineDesc(string desc)
        {
            List<double> coordinates = new List<double>();

            foreach (Match m in Regex.Matches(desc, @"(?<=\()(.+?)(?=\))" ))
            {
                string[] pieces = m.Value.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

                try
                {
                    coordinates.AddRange(pieces.Take(2).Select(p => Convert.ToDouble(p)));
                    coordinates.Add(0);
                }
                catch { }
            }
            return coordinates.ToArray();
        }

        private static int SetAxis(StructuralAxis axis)
        {
            if (axis.Xdir.Value.SequenceEqual(new double[] { 1, 0, 0 }) &&
                axis.Ydir.Value.SequenceEqual(new double[] { 0, 1, 0 }) &&
                axis.Normal.Value.SequenceEqual(new double[] { 0, 0, 1 }))
                return 0;

            List<string> ls = new List<string>();

            int res = (int)GSA.RunGWACommand("HIGHEST,AXIS");

            ls.Add("AXIS");
            ls.Add((res + 1).ToString());
            ls.Add("");
            ls.Add("CART");

            ls.Add("0");
            ls.Add("0");
            ls.Add("0");

            ls.Add(axis.Xdir.Value[0].ToString());
            ls.Add(axis.Xdir.Value[1].ToString());
            ls.Add(axis.Xdir.Value[2].ToString());

            ls.Add(axis.Ydir.Value[0].ToString());
            ls.Add(axis.Ydir.Value[1].ToString());
            ls.Add(axis.Ydir.Value[2].ToString());

            GSA.RunGWACommand(string.Join(",", ls));

            return res + 1;
        }
        #endregion
    }
}