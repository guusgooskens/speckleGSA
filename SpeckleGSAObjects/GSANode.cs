﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Media3D;

namespace SpeckleGSA
{
    public class GSANode : GSAObject
    {
        public Dictionary<string, object> Axis { get; set; }
        public Dictionary<string, object> Restraint { get; set; }
        public Dictionary<string, object> Stiffness { get; set; }
        public double Mass { get; set; }

        public GSANode()
        {
            Axis = new Dictionary<string, object>()
            {
                { "X", new Dictionary<string, object> { { "x", 1 }, { "y", 0 },{ "z", 0 }  } },
                { "Y", new Dictionary<string, object> { { "x", 0 }, { "y", 1 },{ "z", 0 }  } },
                { "Z", new Dictionary<string, object> { { "x", 0 }, { "y", 0 },{ "z", 1 }  } },
            };
            Restraint = new Dictionary<string, object>()
            {
                { "x", false },
                { "y", false },
                { "z", false },
                { "xx", false },
                { "yy", false },
                { "zz", false },
            };
            Stiffness = new Dictionary<string, object>()
            {
                { "x", 0.0 },
                { "y", 0.0 },
                { "z", 0.0 },
                { "xx", 0.0 },
                { "yy", 0.0 },
                { "zz", 0.0 },
            };
            Mass = 0;
        }

        #region GSAObject Functions
        public override void ParseGWACommand(string command, GSAObject[] children = null)
        {
            string[] pieces = command.ListSplit(",");

            int counter = 1; // Skip identifier
            Reference = Convert.ToInt32(pieces[counter++]);
            Name = pieces[counter++].Trim(new char[] { '"' });
            Color = pieces[counter++].ParseGSAColor();
            Coor = new List<double>();
            Coor.Add(Convert.ToDouble(pieces[counter++]));
            Coor.Add(Convert.ToDouble(pieces[counter++]));
            Coor.Add(Convert.ToDouble(pieces[counter++]));

            while (counter < pieces.Length)
            {
                string s = pieces[counter++];
                if (s == "GRID")
                {
                    counter++; // Grid place
                    counter++; // Datum
                    counter++; // Grid line A
                    counter++; // Grid line B
                }
                else if (s == "REST")
                {
                    Restraint["x"] = pieces[counter++] == "0" ? false : true;
                    Restraint["y"] = pieces[counter++] == "0" ? false : true;
                    Restraint["z"] = pieces[counter++] == "0" ? false : true;
                    Restraint["xx"] = pieces[counter++] == "0" ? false : true;
                    Restraint["yy"] = pieces[counter++] == "0" ? false : true;
                    Restraint["zz"] = pieces[counter++] == "0" ? false : true;
                }
                else if (s == "STIFF")
                {
                    Stiffness["x"] = Convert.ToDouble(pieces[counter++]);
                    Stiffness["y"] = Convert.ToDouble(pieces[counter++]);
                    Stiffness["z"] = Convert.ToDouble(pieces[counter++]);
                    Stiffness["xx"] = Convert.ToDouble(pieces[counter++]);
                    Stiffness["yy"] = Convert.ToDouble(pieces[counter++]);
                    Stiffness["zz"] = Convert.ToDouble(pieces[counter++]);
                }
                else if (s == "MESH")
                {
                    counter++; // Edge length
                    counter++; // Radius
                    counter++; // Tie to mesh
                    counter++; // Column rigidity
                    counter++; // Column prop
                    counter++; // Column node
                    counter++; // Column angle
                    counter++; // Column factor
                    counter++; // Column slab factor
                }
                else
                    Axis = ParseGSANodeAxis(Convert.ToInt32(pieces[counter++]), Coor.ToArray());
            }
            return;
        }

        public override string GetGWACommand(GSAObject[] children = null)
        {
            List<string> ls = new List<string>();

            ls.Add("SET");
            ls.Add("NODE.2");
            ls.Add(Reference.ToString());
            ls.Add(Name);
            if (Color == null)
                ls.Add("NO_RGB");
            else
                ls.Add(((int)Color).ToString());
            ls.Add(string.Join(",", Coor));
            
            ls.Add("NO_GRID");

            ls.Add(AddAxistoGSA(Axis).ToString());

            if (Restraint.Count == 0)
                ls.Add("NO_REST");
            else
            {
                ls.Add("REST");
                ls.Add(((bool)Restraint["x"]) ? "1" : "0");
                ls.Add(((bool)Restraint["y"]) ? "1" : "0");
                ls.Add(((bool)Restraint["z"]) ? "1" : "0");
                ls.Add(((bool)Restraint["xx"]) ? "1" : "0");
                ls.Add(((bool)Restraint["yy"]) ? "1" : "0");
                ls.Add(((bool)Restraint["zz"]) ? "1" : "0");
            }

            if (Stiffness.Count == 0)
                ls.Add("NO_STIFF");
            else
            {
                ls.Add("STIFF");
                ls.Add(Stiffness["x"].ToNumString());
                ls.Add(Stiffness["y"].ToNumString());
                ls.Add(Stiffness["z"].ToNumString());
                ls.Add(Stiffness["xx"].ToNumString());
                ls.Add(Stiffness["yy"].ToNumString());
                ls.Add(Stiffness["zz"].ToNumString());
            }
            
            ls.Add("NO_MESH");

            return string.Join(",", ls);
        }

        public override List<GSAObject> GetChildren()
        {
            return null;
        }

        public override void WriteDerivedObjectstoGSA(Dictionary<Type, object> dict)
        {
            List<GSA0DElement> e0Ds = Get0DElements();
            foreach (GSA0DElement e in e0Ds)
                e.WritetoGSA(dict);
        }
        #endregion

        #region 0D Element Operations
        public void Merge0DElement(GSA0DElement elem)
        {
            if (elem.Type == "MASS")
                Mass = GetGSAMass(elem);
        }

        private double GetGSAMass(GSA0DElement elem)
        {
            string res = (string)RunGWACommand("GET,PROP_MASS," + elem.Property.ToString());
            string[] pieces = res.ListSplit(",");

            return Convert.ToDouble(pieces[5]);
        }

        public List<GSA0DElement> Get0DElements()
        {
            List<GSA0DElement> elemList = new List<GSA0DElement>();

            if (Mass > 0)
            {
                GSA0DElement massElem = new GSA0DElement().AttachGSA(gsa);
                massElem.Type = "MASS";
                massElem.Connectivity = new List<int>() { Reference };
                massElem.Property = WriteMassProptoGSA(Mass);
                elemList.Add(massElem);
            }
            return elemList;
        }

        private int WriteMassProptoGSA(double mass)
        {
            List<string> ls = new List<string>();

            int res = (int)RunGWACommand("HIGHEST,PROP_MASS");

            ls.Add("SET");
            ls.Add("PROP_MASS.2");
            ls.Add((res + 1).ToString());
            ls.Add("");
            ls.Add("NO_RGB");
            ls.Add("GLOBAL");
            ls.Add(mass.ToString());
            ls.Add("0");
            ls.Add("0");
            ls.Add("0");
            ls.Add("0");
            ls.Add("0");
            ls.Add("0");

            ls.Add("MOD");
            ls.Add("100%");
            ls.Add("100%");
            ls.Add("100%");

            RunGWACommand(string.Join(",", ls));

            return res + 1;
        }

        public void Merge(GSANode mergeNode)
        {
            Dictionary<string, object> temp = new Dictionary<string, object>();

            foreach (string key in Restraint.Keys)
                temp[key] = (bool)Restraint[key] | (bool)mergeNode.Restraint[key];
            Restraint = temp;

            temp = new Dictionary<string, object>();
            foreach (string key in Stiffness.Keys)
                temp[key] = Math.Max((double)Stiffness[key], (double)mergeNode.Stiffness[key]);
            Stiffness = temp;

            Mass += mergeNode.Mass;
        }
        #endregion

        #region Axis
        private Dictionary<string, object> ParseGSANodeAxis(int axis, double[] evalAtCoor = null)
        {
            // Returns unit vector of each X, Y, Z axis
            Dictionary<string, object> axisVectors = new Dictionary<string, object>();

            Vector3D x;
            Vector3D y;
            Vector3D z;

            switch (axis)
            {
                case 0:
                    // Global
                    axisVectors["X"] = new Dictionary<string, object> { { "x", 1 }, { "y", 0 }, { "z", 0 } };
                    axisVectors["Y"] = new Dictionary<string, object> { { "x", 0 }, { "y", 1 }, { "z", 0 } };
                    axisVectors["Z"] = new Dictionary<string, object> { { "x", 0 }, { "y", 0 }, { "z", 1 } };
                    return axisVectors;
                case -11:
                    // X elevation
                    axisVectors["X"] = new Dictionary<string, object> { { "x", 0 }, { "y", -1 }, { "z", 0 } };
                    axisVectors["Y"] = new Dictionary<string, object> { { "x", 0 }, { "y", 0 }, { "z", 1 } };
                    axisVectors["Z"] = new Dictionary<string, object> { { "x", -1 }, { "y", 0 }, { "z", 0 } };
                    return axisVectors;
                case -12:
                    // Y elevation
                    axisVectors["X"] = new Dictionary<string, object> { { "x", 1 }, { "y", 0 }, { "z", 0 } };
                    axisVectors["Y"] = new Dictionary<string, object> { { "x", 0 }, { "y", 0 }, { "z", 1 } };
                    axisVectors["Z"] = new Dictionary<string, object> { { "x", 0 }, { "y", -1 }, { "z", 0 } };
                    return axisVectors;
                case -14:
                    // Vertical
                    axisVectors["X"] = new Dictionary<string, object> { { "x", 0 }, { "y", 0 }, { "z", 1 } };
                    axisVectors["Y"] = new Dictionary<string, object> { { "x", 1 }, { "y", 0 }, { "z", 0 } };
                    axisVectors["Z"] = new Dictionary<string, object> { { "x", 0 }, { "y", 1 }, { "z", 0 } };
                    return axisVectors;
                case -13:
                    // Global cylindrical
                    x = new Vector3D(evalAtCoor[0], evalAtCoor[1], 0);
                    x.Normalize();
                    z = new Vector3D(0, 0, 1);
                    y = Vector3D.CrossProduct(z, x);

                    axisVectors["X"] = new Dictionary<string, object> { { "x", x.X }, { "y", x.Y }, { "z", x.Z } };
                    axisVectors["Y"] = new Dictionary<string, object> { { "x", y.X }, { "y", y.Y }, { "z", y.Z } };
                    axisVectors["Z"] = new Dictionary<string, object> { { "x", z.X }, { "y", z.Y }, { "z", z.Z } };
                    return axisVectors;
                default:
                    string res = (string)RunGWACommand("GET,AXIS," + axis.ToString());
                    string[] pieces = res.Split(new char[] { ',' });
                    if (pieces.Length < 13)
                    {
                        axisVectors["X"] = new Dictionary<string, object> { { "x", 1 }, { "y", 0 }, { "z", 0 } };
                        axisVectors["Y"] = new Dictionary<string, object> { { "x", 0 }, { "y", 1 }, { "z", 0 } };
                        axisVectors["Z"] = new Dictionary<string, object> { { "x", 0 }, { "y", 0 }, { "z", 1 } };
                        return axisVectors;
                    }
                    Vector3D origin = new Vector3D(Convert.ToDouble(pieces[4]), Convert.ToDouble(pieces[5]), Convert.ToDouble(pieces[6]));

                    Vector3D X = new Vector3D(Convert.ToDouble(pieces[7]), Convert.ToDouble(pieces[8]), Convert.ToDouble(pieces[9]));
                    X.Normalize();


                    Vector3D Yp = new Vector3D(Convert.ToDouble(pieces[10]), Convert.ToDouble(pieces[11]), Convert.ToDouble(pieces[12]));
                    Vector3D Z = Vector3D.CrossProduct(X, Yp);
                    Z.Normalize();

                    Vector3D Y = Vector3D.CrossProduct(Z, X);

                    Vector3D pos = new Vector3D(0, 0, 0);

                    if (evalAtCoor == null)
                        pieces[3] = "CART";
                    else
                    {
                        pos = new Vector3D(evalAtCoor[0] - origin.X, evalAtCoor[1] - origin.Y, evalAtCoor[2] - origin.Z);
                        if (pos.Length == 0)
                            pieces[3] = "CART";
                    }

                    switch (pieces[3])
                    {
                        case "CART":
                            axisVectors["X"] = new Dictionary<string, object> { { "x", X.X }, { "y", X.Y }, { "z", X.Z } };
                            axisVectors["Y"] = new Dictionary<string, object> { { "x", Y.X }, { "y", Y.Y }, { "z", Y.Z } };
                            axisVectors["Z"] = new Dictionary<string, object> { { "x", Z.X }, { "y", Z.Y }, { "z", Z.Z } };
                            return axisVectors;
                        case "CYL":
                            x = new Vector3D(pos.X, pos.Y, 0);
                            x.Normalize();
                            z = Z;
                            y = Vector3D.CrossProduct(Z, x);
                            y.Normalize();

                            axisVectors["X"] = new Dictionary<string, object> { { "x", x.X }, { "y", x.Y }, { "z", x.Z } };
                            axisVectors["Y"] = new Dictionary<string, object> { { "x", y.X }, { "y", y.Y }, { "z", y.Z } };
                            axisVectors["Z"] = new Dictionary<string, object> { { "x", z.X }, { "y", z.Y }, { "z", z.Z } };
                            return axisVectors;
                        case "SPH":
                            x = pos;
                            x.Normalize();
                            z = Vector3D.CrossProduct(Z, x);
                            z.Normalize();
                            y = Vector3D.CrossProduct(z, x);
                            z.Normalize();

                            axisVectors["X"] = new Dictionary<string, object> { { "x", x.X }, { "y", x.Y }, { "z", x.Z } };
                            axisVectors["Y"] = new Dictionary<string, object> { { "x", y.X }, { "y", y.Y }, { "z", y.Z } };
                            axisVectors["Z"] = new Dictionary<string, object> { { "x", z.X }, { "y", z.Y }, { "z", z.Z } };
                            return axisVectors;
                        default:
                            axisVectors["X"] = new Dictionary<string, object> { { "x", 1 }, { "y", 0 }, { "z", 0 } };
                            axisVectors["Y"] = new Dictionary<string, object> { { "x", 0 }, { "y", 1 }, { "z", 0 } };
                            axisVectors["Z"] = new Dictionary<string, object> { { "x", 0 }, { "y", 0 }, { "z", 1 } };
                            return axisVectors;
                    }
            }
        }

        private int AddAxistoGSA(Dictionary<string,object> axis)
        {
            Dictionary<string, object> X = axis["X"] as Dictionary<string, object>;
            Dictionary<string, object> Y = axis["Y"] as Dictionary<string, object>;
            Dictionary<string, object> Z = axis["Z"] as Dictionary<string, object>;

            if (X["x"].Equal(1) & X["y"].Equal(0) & X["z"].Equal(0) &
                Y["x"].Equal(0) & Y["y"].Equal(1) & Y["z"].Equal(0) &
                Z["x"].Equal(0) & Z["y"].Equal(0) & Z["z"].Equal(1))
            {
                return 0;
            }

            List<string> ls = new List<string>();

            int res = (int)RunGWACommand("HIGHEST,AXIS");

            ls.Add("AXIS");
            ls.Add((res + 1).ToString());
            ls.Add("");
            ls.Add("CART");

            ls.Add("0");
            ls.Add("0");
            ls.Add("0");

            ls.Add(X["x"].ToNumString());
            ls.Add(X["y"].ToNumString());
            ls.Add(X["z"].ToNumString());

            ls.Add(Y["x"].ToNumString());
            ls.Add(Y["y"].ToNumString());
            ls.Add(Y["z"].ToNumString());

            RunGWACommand(string.Join(",", ls));

            return res + 1;
        }
        #endregion
    }
}