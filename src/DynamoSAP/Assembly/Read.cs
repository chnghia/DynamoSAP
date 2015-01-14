﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using DynamoSAP.Structure;
using DynamoSAP;

//DYNAMO
using Autodesk.DesignScript.Geometry;
using Autodesk.DesignScript.Runtime;

using SAPConnection;
using SAP2000v16;

namespace DynamoSAP.Assembly
{
    public class Read
    {
        //// DYNAMO NODES ////

        /// <summary>
        /// Read a SAP project from a filepath
        /// </summary>
        /// <param name="FilePath">Filepath of the project to read</param>
        /// <param name="read">Set Boolean to True to open and readthe project</param>
        /// <returns>Structural Model</returns>
        [MultiReturn("StructuralModel", "units")]
        public static Dictionary<string, object> SAPModel(string FilePath, bool read)
        {
            if (read)
            {
                StructuralModel Model = new StructuralModel();
                Model.StructuralElements = new List<Element>();
                cSapModel mySapModel = null;
                string units = string.Empty;
                // Open & instantiate SAP file
                Initialize.OpenSAPModel(FilePath, ref mySapModel, ref units);

                // Populate the model's elemets
                StructuralModelFromSapFile(ref mySapModel, ref Model, units);

                // Return outputs
                return new Dictionary<string, object>
                {
                    {"StructuralModel", Model},
                    {"units", units}
                };
            }
            else
            {
                throw new Exception("Set boolean True to read!");
            }

        }

        /// <summary>
        /// Read a SAP project from an open instance
        /// </summary>
        /// <param name="read">Set Boolean to True to read the open project</param>
        /// <returns>Structural Model</returns>
        [MultiReturn("StructuralModel", "units")]
        public static Dictionary<string, object> SAPModel(bool read)
        {
            if (read)
            {
                StructuralModel Model = new StructuralModel();

                cSapModel mySapModel = null;
                string units = string.Empty;

                // Open & instantiate SAP file
                Initialize.GrabOpenSAP(ref mySapModel, ref units);

                StructuralModelFromSapFile(ref mySapModel, ref Model, units);

                // Return outputs
                return new Dictionary<string, object>
                {
                    {"StructuralModel", Model},
                    {"units", units}
                };
            }
            else
            {
                throw new Exception("Set boolean True to read!");
            }

        }

        private Read() { }

        internal static void StructuralModelFromSapFile(ref cSapModel SapModel, ref StructuralModel model, string SapModelUnits)
        {
            model.StructuralElements = new List<Element>();
            if (SapModel != null)
            {

                // 1. GET LOAD PATTERNS

                string[] LoadPatternNames = null;
                string[] LoadPatternTypes = null;
                double[] LoadPatternMultipliers = null;

                StructureMapper.GetLoadPatterns(ref SapModel, ref LoadPatternNames, ref LoadPatternTypes, ref LoadPatternMultipliers);
                if (LoadPatternNames != null)
                {
                    model.LoadPatterns = new List<LoadPattern>();
                    foreach (string lpname in LoadPatternNames)
                    {
                        int pos = Array.IndexOf(LoadPatternNames, lpname);
                        model.LoadPatterns.Add(new LoadPattern(lpname, LoadPatternTypes[pos], LoadPatternMultipliers[pos]));
                    }
                }

                // 2. GET DYNAMO FRAMES ( get Loads and Releases that are assigned to that frame)

                //2.a GET LOADS that are Assigned to Frames

                Dictionary<int, string> DictFrm_PointLoads = new Dictionary<int, string>();
                Dictionary<int, string> DictFrm_DistLoads = new Dictionary<int, string>();

                //Get Point Loads
                string[] framesWithPointLoads = null;
                string[] PlPattern = null;
                int Pnumber = 0;
                int[] PmyType = null;
                string[] PCsys = null;
                int[] Pdir = null;
                double[] PRelDist = null;
                double[] PDist = null;
                double[] PVal = null;

                LoadMapper.GetPointLoads(ref SapModel, ref framesWithPointLoads, ref Pnumber, ref PlPattern, ref PmyType, ref PCsys, ref Pdir, ref PRelDist, ref PDist, ref PVal);
                if (framesWithPointLoads != null)
                {
                    for (int i = 0; i < framesWithPointLoads.Count(); i++)
                    {
                        DictFrm_PointLoads.Add(i, framesWithPointLoads[i]);
                    }
                }

                // Get Distributed Loads
                string[] framesWithDistributedLoads = null;
                string[] DlPattern = null;
                int Dnumber = 0;
                int[] DmyType = null;
                string[] DCsys = null;
                int[] Ddir = null;
                double[] DRD1 = null;
                double[] DRD2 = null;
                double[] DDist1 = null;
                double[] DDist2 = null;
                double[] DVal1 = null;
                double[] DVal2 = null;

                LoadMapper.GetDistributedLoads(ref SapModel, ref framesWithDistributedLoads, ref Dnumber, ref DlPattern, ref DmyType, ref DCsys, ref Ddir, ref DRD1, ref DRD2, ref DDist1, ref DDist2, ref DVal1, ref DVal2);
                if (framesWithDistributedLoads != null)
                {
                    for (int i = 0; i < framesWithDistributedLoads.Count(); i++)
                    {
                        DictFrm_DistLoads.Add(i, framesWithDistributedLoads[i]);
                    }
                }


                //2.b Get Frames

                // Calculate Length Scale Factor
                Double SF = Utilities.UnitConversion("m", SapModelUnits); // Dynamo API Lenght Unit is 'meter'

                string[] FrmIds = null;
                StructureMapper.GetFrameIds(ref FrmIds, ref SapModel);
                for (int i = 0; i < FrmIds.Length; i++)
                {
                    Point s = null;
                    Point e = null;
                    string matProp = "A992Fy50"; // default value
                    string secName = "W12X14"; // default value
                    string secCatalog = "AISC14"; // default value
                    string Just = "MiddleCenter"; // default value
                    double Rot = 0; // default value

                    StructureMapper.GetFrm(ref SapModel, FrmIds[i], ref s, ref e, ref matProp, ref secName, ref Just, ref Rot, ref secCatalog, SF);
                    SectionProp secProp = new SectionProp(secName, matProp, secCatalog);
                    Frame d_frm = new Frame(s, e, secProp, Just, Rot);
                    d_frm.Label = FrmIds[i];
                    model.StructuralElements.Add(d_frm);

                    //LOADS
                    // Frame might have multiple loads assigned to it...
                    d_frm.Loads = new List<Load>();
                    
                    //Check if the frame has distributed loads
                    var outindexes = from obj in DictFrm_DistLoads
                                     where obj.Value == d_frm.Label
                                     select obj.Key;

                    foreach(int index in outindexes)
                    {
                       LoadPattern Dlp = null;
                       foreach (LoadPattern loadp in model.LoadPatterns)
                        {
                            if (loadp.name == DlPattern[index])
                            {
                                Dlp = loadp;
                                break;
                            }
                        }
                        if (Dlp != null)
                        {
                            // using relDist as true, and using the relative distance values DRD1 and DRD2
                            bool relDist = true;
                            Load l = new Load(Dlp, DmyType[index], Ddir[index], DRD1[index], DRD2[index], DVal1[index], DVal2[index], DCsys[index], relDist);
                            l.LoadType = "DistributedLoad";
                            d_frm.Loads.Add(l);
                        }
                    }

                    //Check if the frame has Point Loads
                    var outindexesO = from obj in DictFrm_PointLoads
                                      where obj.Value == d_frm.Label
                                      select obj.Key;

                    foreach (int index in outindexesO)
                    {
                        LoadPattern Plp = null;
                        foreach (LoadPattern loadp in model.LoadPatterns)
                        {
                            if (loadp.name == PlPattern[index])
                            {
                                Plp = loadp;
                                break;
                            }
                        }

                        if (Plp != null)
                        {
                            bool relativedist = true;
                            Load l = new Load(Plp, PmyType[index], Pdir[index], PRelDist[index], PVal[index], PCsys[index], relativedist);
                            l.LoadType = "PointLoad";
                            d_frm.Loads.Add(l);
                        }
                    }

                    //RELEASES
                    bool[] ii = new bool[6];
                    bool[] jj = new bool[6];
                    ReleaseMapper.Get(ref SapModel, FrmIds[i], ref ii, ref jj);

                    // Populate if return releases
                    if (ii.Contains(true) || jj.Contains(true))
                    {
                        d_frm.Releases = Release.Set(ii[0], jj[0]
                                                    , ii[1], jj[1]
                                                    , ii[2], jj[2]
                                                    , ii[3], jj[3]
                                                    , ii[4], jj[4]
                                                    , ii[5], jj[5]);
                    }
                }

                // 3. GET RESTRAINTS
                int CountRes = RestraintMapper.Count(ref SapModel);
                if (CountRes > 0) 
                {
                    model.Restraints = new List<Restraint>();

                    List<string> PtIds = new List<string>();
                    RestraintMapper.GetSupportedPts(ref SapModel, ref PtIds);

                    // Populate Dynamo Restraints 
                    foreach (var PtId in PtIds)
                    {
                        Point Pti = null;
                        bool[] restraints = new bool[6];

                        RestraintMapper.Get(ref SapModel, PtId, ref Pti, ref restraints, SF);

                        // Populate on Structural Model restraint Ls
                        Restraint support = Restraint.SetRestraint(Pti, restraints[0], restraints[1],restraints[2], restraints[3], restraints[4], restraints[5]);
                        model.Restraints.Add(support);
                    }
                }
                
            }
            else
            {
                throw new Exception("Make sure SAP Model is open!");
            }
        }
    }
}
